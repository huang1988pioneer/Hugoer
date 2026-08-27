using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hugoer.Helpers;
using Hugoer.Models;
using Tomlyn;
using Tomlyn.Model;

namespace Hugoer.Services;

public sealed partial class MenuService
{
    private static readonly string[] DedicatedMenuNames =
    [
        "menu.toml", "menus.toml", "menu.yaml", "menu.yml", "menus.yaml", "menus.yml"
    ];

    public SiteMenuDocument Load(string sitePath)
    {
        var configPath = FindMenuConfigFile(sitePath);
        var dedicated = IsDedicatedMenuFile(configPath);
        var rootKey = "menu";
        var configEntries = new List<MenuEntry>();

        if (!string.IsNullOrWhiteSpace(configPath) && File.Exists(configPath)
            && configPath.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
        {
            var text = File.ReadAllText(configPath);
            rootKey = DetectMenuRootKey(text, dedicated);
            configEntries.AddRange(ParseTomlMenus(text, dedicated, configPath));
        }

        var frontMatterFiles = new List<string>();
        var frontMatterEntries = new List<MenuEntry>();
        var contentRoot = PathHelper.ContentDir(sitePath);
        if (Directory.Exists(contentRoot))
        {
            foreach (var file in Directory.EnumerateFiles(contentRoot, "*.*", SearchOption.AllDirectories)
                         .Where(IsMarkdown))
            {
                var relative = Path.GetRelativePath(contentRoot, file).Replace('\\', '/');
                var markdown = File.ReadAllText(file);
                var parsed = ParseFrontMatterMenus(markdown, relative);
                if (parsed.Count == 0) continue;
                frontMatterFiles.Add(file);
                foreach (var entry in parsed)
                    FillEntryFromContent(entry, markdown, relative);
                frontMatterEntries.AddRange(parsed);
            }
        }

        var merged = MergeEntries(configEntries, frontMatterEntries);
        return new SiteMenuDocument
        {
            ConfigPath = configPath ?? Path.Combine(sitePath, "hugo.toml"),
            IsDedicatedMenuFile = dedicated,
            MenuRootKey = rootKey,
            Entries = merged,
            FrontMatterFiles = frontMatterFiles,
            ImportedFromFrontMatter = frontMatterEntries.Count
        };
    }

    public void Save(string sitePath, SiteMenuDocument document, IReadOnlyList<MenuEntry> entries)
    {
        var configPath = string.IsNullOrWhiteSpace(document.ConfigPath)
            ? Path.Combine(sitePath, "hugo.toml")
            : document.ConfigPath;
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var rendered = RenderMenuToml(entries, document.MenuRootKey, document.IsDedicatedMenuFile);
        if (document.IsDedicatedMenuFile || IsDedicatedMenuFile(configPath))
        {
            AtomicFileWriter.WriteAllText(configPath, rendered.EndsWith('\n') ? rendered : rendered + "\n");
        }
        else
        {
            var original = File.Exists(configPath) ? File.ReadAllText(configPath) : string.Empty;
            var next = ReplaceMenuSpan(original, rendered);
            AtomicFileWriter.WriteAllText(configPath, next);
        }

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in document.FrontMatterFiles)
            files.Add(file);

        var contentRoot = PathHelper.ContentDir(sitePath);
        if (Directory.Exists(contentRoot))
        {
            foreach (var file in Directory.EnumerateFiles(contentRoot, "*.*", SearchOption.AllDirectories)
                         .Where(IsMarkdown))
            {
                files.Add(file);
            }
        }

        foreach (var file in files)
        {
            if (!File.Exists(file)) continue;
            var markdown = File.ReadAllText(file);
            var stripped = RemoveMenuFromFrontMatter(markdown);
            if (!string.Equals(stripped, markdown, StringComparison.Ordinal))
                AtomicFileWriter.WriteAllText(file, stripped);
        }
    }

    public IReadOnlyList<string> ListThemeIcons(string sitePath)
    {
        var icons = new SortedSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "home", "archives", "search", "user", "link", "brand-github", "brand-twitter",
            "rss", "tag", "categories", "hash"
        };

        var themesDir = PathHelper.ThemesDir(sitePath);
        if (!Directory.Exists(themesDir))
            return icons.ToList();

        foreach (var themeDir in Directory.EnumerateDirectories(themesDir))
        {
            var iconDir = Path.Combine(themeDir, "assets", "icons");
            if (!Directory.Exists(iconDir)) continue;
            foreach (var svg in Directory.EnumerateFiles(iconDir, "*.svg"))
                icons.Add(Path.GetFileNameWithoutExtension(svg)!);
        }

        return icons.ToList();
    }

    public static string UrlFromContentPath(string relativePath)
    {
        var path = (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            path = path[..^3];
        else if (path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            path = path[..^9];

        if (path.EndsWith("/index", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/_index", StringComparison.OrdinalIgnoreCase))
            path = path[..path.LastIndexOf('/')];
        else if (path.Equals("index", StringComparison.OrdinalIgnoreCase)
                 || path.Equals("_index", StringComparison.OrdinalIgnoreCase))
            return "/";

        return string.IsNullOrWhiteSpace(path) ? "/" : "/" + path.Trim('/') + "/";
    }

    public static List<MenuEntry> ParseTomlMenus(string tomlText, bool dedicatedFile, string? sourcePath = null)
    {
        var entries = new List<MenuEntry>();
        if (string.IsNullOrWhiteSpace(tomlText))
            return entries;

        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(tomlText) ?? new TomlTable();
        }
        catch
        {
            return entries;
        }

        if (dedicatedFile)
        {
            foreach (var (key, value) in root)
                ReadMenuValue(entries, key, value, sourcePath);

            return entries;
        }

        foreach (var rootKey in new[] { "menu", "menus" })
        {
            if (!root.TryGetValue(rootKey, out var container)) continue;
            if (container is TomlTable table)
            {
                foreach (var (key, value) in table)
                    ReadMenuValue(entries, key, value, sourcePath);
            }
            else
            {
                ReadMenuValue(entries, rootKey, container, sourcePath);
            }
        }

        return entries;
    }

    public static List<MenuEntry> ParseFrontMatterMenus(string markdown, string sourcePath)
    {
        var entries = new List<MenuEntry>();
        if (string.IsNullOrWhiteSpace(markdown))
            return entries;

        var match = FrontMatterBlockRegex().Match(markdown);
        if (!match.Success)
            return entries;

        var delimiter = match.Groups["delimiter"].Value;
        var frontMatter = match.Groups["frontMatter"].Value;
        if (delimiter == "+++")
        {
            foreach (var entry in ParseTomlMenus(frontMatter, dedicatedFile: false, sourcePath))
            {
                entry.Source = MenuEntrySource.FrontMatter;
                entry.SourcePath = sourcePath;
                entries.Add(entry);
            }

            return entries;
        }

        ParseYamlMenuBlock(frontMatter, sourcePath, entries);
        return entries;
    }

    public static string RemoveMenuFromFrontMatter(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return markdown ?? string.Empty;

        var match = FrontMatterBlockRegex().Match(markdown);
        if (!match.Success)
            return markdown;

        var delimiter = match.Groups["delimiter"].Value;
        var frontMatter = match.Groups["frontMatter"].Value;
        var cleaned = delimiter == "+++"
            ? StripTomlMenu(frontMatter)
            : StripYamlMenu(frontMatter);

        cleaned = cleaned.Trim('\r', '\n');
        var body = markdown[match.Length..].TrimStart('\r', '\n');
        if (string.IsNullOrWhiteSpace(cleaned))
            return string.IsNullOrEmpty(body) ? $"{delimiter}\n{delimiter}\n" : $"{delimiter}\n{delimiter}\n\n{body}";

        return $"{delimiter}\n{cleaned}\n{delimiter}\n\n{body}";
    }

    public static string RenderMenuToml(IEnumerable<MenuEntry> entries, string rootKey, bool dedicatedFile)
    {
        var sb = new StringBuilder();
        var groups = entries
            .Select(entry =>
            {
                var clone = entry.Clone();
                clone.MenuName = string.IsNullOrWhiteSpace(clone.MenuName) ? "main" : clone.MenuName.Trim();
                return clone;
            })
            .GroupBy(entry => entry.MenuName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key.Equals("main", StringComparison.OrdinalIgnoreCase) ? 0
                : group.Key.Equals("social", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase);

        var key = string.IsNullOrWhiteSpace(rootKey) ? "menu" : rootKey.Trim();

        foreach (var group in groups)
        {
            var tableName = dedicatedFile ? group.Key : $"{key}.{group.Key}";
            foreach (var entry in group.OrderBy(item => item.Weight).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (sb.Length > 0)
                    sb.AppendLine();

                sb.AppendLine($"[[{tableName}]]");
                WriteString(sb, "identifier", entry.Identifier);
                WriteString(sb, "name", entry.Name);
                WriteString(sb, "pageRef", entry.PageRef);
                WriteString(sb, "url", entry.Url);
                WriteString(sb, "parent", entry.Parent);
                sb.AppendLine($"weight = {entry.Weight}");

                if (!string.IsNullOrWhiteSpace(entry.Icon) || entry.NewTab)
                {
                    sb.AppendLine();
                    sb.AppendLine($"[{tableName}.params]");
                    WriteString(sb, "icon", entry.Icon);
                    if (entry.NewTab)
                        sb.AppendLine("newTab = true");
                }
            }
        }

        return sb.ToString();
    }

    public static string ReplaceMenuSpan(string tomlText, string renderedMenus)
    {
        tomlText ??= string.Empty;
        renderedMenus = (renderedMenus ?? string.Empty).TrimEnd() + "\n";
        var span = FindMenuSpan(tomlText);
        if (span.Start < 0)
        {
            if (string.IsNullOrWhiteSpace(tomlText))
                return renderedMenus;
            return tomlText.TrimEnd() + "\n\n" + renderedMenus;
        }

        var before = tomlText[..span.Start].TrimEnd();
        var after = span.End < tomlText.Length ? tomlText[span.End..].TrimStart('\r', '\n') : string.Empty;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(before))
            parts.Add(before);
        if (!string.IsNullOrWhiteSpace(renderedMenus))
            parts.Add(renderedMenus.TrimEnd());
        if (!string.IsNullOrWhiteSpace(after))
            parts.Add(after);
        var joined = string.Join("\n\n", parts);
        return joined.EndsWith('\n') ? joined : joined + "\n";
    }

    public static List<MenuEntry> MergeEntries(IEnumerable<MenuEntry> configEntries, IEnumerable<MenuEntry> frontMatterEntries)
    {
        var result = new List<MenuEntry>();
        var index = new Dictionary<string, MenuEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in configEntries)
        {
            var clone = entry.Clone();
            clone.Source = MenuEntrySource.Config;
            result.Add(clone);
            index[MergeKey(clone)] = clone;
        }

        foreach (var incoming in frontMatterEntries)
        {
            var key = MergeKey(incoming);
            if (index.TryGetValue(key, out var existing))
            {
                if (string.IsNullOrWhiteSpace(existing.Icon))
                    existing.Icon = incoming.Icon;
                if (string.IsNullOrWhiteSpace(existing.Name))
                    existing.Name = incoming.Name;
                if (string.IsNullOrWhiteSpace(existing.Identifier))
                    existing.Identifier = incoming.Identifier;
                if (string.IsNullOrWhiteSpace(existing.Url))
                    existing.Url = incoming.Url;
                if (string.IsNullOrWhiteSpace(existing.PageRef))
                    existing.PageRef = incoming.PageRef;
                if (!existing.NewTab)
                    existing.NewTab = incoming.NewTab;
                continue;
            }

            var clone = incoming.Clone();
            clone.Source = MenuEntrySource.FrontMatter;
            result.Add(clone);
            index[key] = clone;
        }

        return result;
    }

    public static string? FindMenuConfigFile(string sitePath)
    {
        foreach (var folder in new[]
                 {
                     Path.Combine(sitePath, "config", "_default"),
                     Path.Combine(sitePath, "config")
                 })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var name in DedicatedMenuNames)
            {
                var path = Path.Combine(folder, name);
                if (File.Exists(path))
                    return path;
            }
        }

        return PathHelper.FindConfigFile(sitePath);
    }

    private static bool IsDedicatedMenuFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var name = Path.GetFileName(path);
        return DedicatedMenuNames.Any(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static string DetectMenuRootKey(string tomlText, bool dedicatedFile)
    {
        if (dedicatedFile) return "menu";
        if (tomlText.Contains("[[menus.", StringComparison.OrdinalIgnoreCase)
            || tomlText.Contains("[menus.", StringComparison.OrdinalIgnoreCase)
            || tomlText.Contains("[menus]", StringComparison.OrdinalIgnoreCase))
            return "menus";
        return "menu";
    }

    private static void ReadMenuValue(List<MenuEntry> entries, string menuName, object? value, string? sourcePath)
    {
        switch (value)
        {
            case TomlTableArray tableArray:
                ReadEntryArray(entries, menuName, tableArray, sourcePath);
                break;
            case TomlArray array:
                ReadEntryArray(entries, menuName, array.OfType<TomlTable>(), sourcePath);
                break;
            case TomlTable single:
                ReadEntryArray(entries, menuName, [single], sourcePath);
                break;
        }
    }

    private static void ReadEntryArray(List<MenuEntry> entries, string menuName, IEnumerable<TomlTable> tables, string? sourcePath)
    {
        foreach (var table in tables)
        {
            var entry = new MenuEntry
            {
                MenuName = menuName,
                Identifier = GetString(table, "identifier"),
                Name = GetString(table, "name"),
                Url = GetString(table, "url"),
                PageRef = GetString(table, "pageRef", "page_ref"),
                Parent = GetString(table, "parent"),
                Weight = GetInt(table, "weight"),
                Source = MenuEntrySource.Config,
                SourcePath = sourcePath
            };

            if (table.TryGetValue("params", out var paramsObj) && paramsObj is TomlTable paramsTable)
            {
                entry.Icon = GetString(paramsTable, "icon");
                entry.NewTab = GetBool(paramsTable, "newTab", "newtab");
            }

            entries.Add(entry);
        }
    }

    private static void ParseYamlMenuBlock(string frontMatter, string sourcePath, List<MenuEntry> entries)
    {
        var lines = frontMatter.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('#')) continue;

            var arrayMatch = YamlMenuArrayRegex().Match(lines[i]);
            if (arrayMatch.Success)
            {
                foreach (var part in arrayMatch.Groups["items"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    entries.Add(new MenuEntry
                    {
                        MenuName = Unquote(part),
                        Source = MenuEntrySource.FrontMatter,
                        SourcePath = sourcePath
                    });
                }
                continue;
            }

            var scalarMatch = YamlMenuScalarRegex().Match(lines[i]);
            if (scalarMatch.Success)
            {
                entries.Add(new MenuEntry
                {
                    MenuName = Unquote(scalarMatch.Groups["name"].Value),
                    Source = MenuEntrySource.FrontMatter,
                    SourcePath = sourcePath
                });
                continue;
            }

            if (!YamlMenuHeaderRegex().IsMatch(lines[i]))
                continue;

            var headerIndent = IndentOf(lines[i]);
            i++;
            while (i < lines.Length)
            {
                if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].TrimStart().StartsWith('#'))
                {
                    i++;
                    continue;
                }

                var indent = IndentOf(lines[i]);
                if (indent <= headerIndent)
                {
                    i--;
                    break;
                }

                var (key, value) = SplitYaml(lines[i]);
                if (string.IsNullOrWhiteSpace(value))
                {
                    var entry = new MenuEntry
                    {
                        MenuName = key,
                        Source = MenuEntrySource.FrontMatter,
                        SourcePath = sourcePath
                    };
                    i = ReadYamlEntryFields(lines, i + 1, indent, entry);
                    entries.Add(entry);
                    continue;
                }

                entries.Add(new MenuEntry
                {
                    MenuName = Unquote(value),
                    Identifier = key,
                    Source = MenuEntrySource.FrontMatter,
                    SourcePath = sourcePath
                });
                i++;
            }
        }
    }

    private static int ReadYamlEntryFields(string[] lines, int start, int parentIndent, MenuEntry entry)
    {
        var i = start;
        while (i < lines.Length)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].TrimStart().StartsWith('#'))
            {
                i++;
                continue;
            }

            var indent = IndentOf(lines[i]);
            if (indent <= parentIndent)
                return i;

            var (key, value) = SplitYaml(lines[i]);
            if (key.Equals("params", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(value))
            {
                i++;
                while (i < lines.Length)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]) || lines[i].TrimStart().StartsWith('#'))
                    {
                        i++;
                        continue;
                    }

                    var paramsIndent = IndentOf(lines[i]);
                    if (paramsIndent <= indent)
                        break;

                    var (paramsKey, paramsValue) = SplitYaml(lines[i]);
                    if (paramsKey.Equals("icon", StringComparison.OrdinalIgnoreCase))
                        entry.Icon = Unquote(paramsValue);
                    else if (paramsKey.Equals("newTab", StringComparison.OrdinalIgnoreCase)
                             || paramsKey.Equals("newtab", StringComparison.OrdinalIgnoreCase))
                        entry.NewTab = ParseBool(paramsValue);
                    i++;
                }

                continue;
            }

            if (key.Equals("identifier", StringComparison.OrdinalIgnoreCase))
                entry.Identifier = Unquote(value);
            else if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                entry.Name = Unquote(value);
            else if (key.Equals("url", StringComparison.OrdinalIgnoreCase))
                entry.Url = Unquote(value);
            else if (key.Equals("pageRef", StringComparison.OrdinalIgnoreCase)
                     || key.Equals("page_ref", StringComparison.OrdinalIgnoreCase))
                entry.PageRef = Unquote(value);
            else if (key.Equals("parent", StringComparison.OrdinalIgnoreCase))
                entry.Parent = Unquote(value);
            else if (key.Equals("weight", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(Unquote(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight))
                entry.Weight = weight;

            i++;
        }

        return i;
    }

    private static void FillEntryFromContent(MenuEntry entry, string markdown, string relativePath)
    {
        var document = new FrontMatterService().Parse(markdown);
        if (string.IsNullOrWhiteSpace(entry.Name))
            entry.Name = document.Fields.TryGetValue("title", out var title) ? title : Path.GetFileNameWithoutExtension(relativePath);
        if (string.IsNullOrWhiteSpace(entry.Identifier))
        {
            if (document.Fields.TryGetValue("slug", out var slug) && !string.IsNullOrWhiteSpace(slug))
                entry.Identifier = slug;
            else
                entry.Identifier = Path.GetFileName(Path.GetDirectoryName(Path.Combine("x", relativePath.Replace('/', Path.DirectorySeparatorChar))) ?? relativePath);
        }

        if (string.IsNullOrWhiteSpace(entry.Url) && string.IsNullOrWhiteSpace(entry.PageRef))
            entry.Url = UrlFromContentPath(relativePath);
    }

    private static string StripYamlMenu(string frontMatter)
    {
        var lines = frontMatter.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (YamlMenuHeaderRegex().IsMatch(lines[i])
                || YamlMenuScalarRegex().IsMatch(lines[i])
                || YamlMenuArrayRegex().IsMatch(lines[i]))
            {
                var headerIndent = IndentOf(lines[i]);
                if (YamlMenuHeaderRegex().IsMatch(lines[i]))
                {
                    i++;
                    while (i < lines.Length)
                    {
                        if (string.IsNullOrWhiteSpace(lines[i]))
                        {
                            i++;
                            continue;
                        }

                        if (IndentOf(lines[i]) > headerIndent)
                        {
                            i++;
                            continue;
                        }

                        i--;
                        break;
                    }
                }

                continue;
            }

            kept.Add(lines[i]);
        }

        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
            kept.RemoveAt(kept.Count - 1);
        return string.Join("\n", kept);
    }

    private static string StripTomlMenu(string frontMatter)
    {
        try
        {
            var table = TomlSerializer.Deserialize<TomlTable>(frontMatter) ?? new TomlTable();
            table.Remove("menu");
            table.Remove("menus");
            return TomlSerializer.Serialize(table).TrimEnd();
        }
        catch
        {
            return frontMatter;
        }
    }

    private static (int Start, int End) FindMenuSpan(string text)
    {
        var start = -1;
        var end = -1;
        var inMenu = false;
        var offset = 0;
        while (offset < text.Length)
        {
            var newline = text.IndexOf('\n', offset);
            var lineEnd = newline < 0 ? text.Length : newline + 1;
            var line = text[offset..lineEnd].TrimEnd('\r', '\n');
            var trimmed = line.Trim();

            if (IsTableHeader(trimmed))
            {
                if (IsMenuHeader(trimmed))
                {
                    if (start < 0) start = offset;
                    inMenu = true;
                    end = lineEnd;
                }
                else if (inMenu)
                {
                    break;
                }
            }
            else if (inMenu)
            {
                end = lineEnd;
            }

            offset = lineEnd;
        }

        return (start, end);
    }

    private static bool IsTableHeader(string trimmed) =>
        trimmed.StartsWith('[') && trimmed.EndsWith(']');

    private static bool IsMenuHeader(string trimmed)
    {
        var inner = trimmed.Trim('[', ']');
        return inner.Equals("menu", StringComparison.OrdinalIgnoreCase)
               || inner.Equals("menus", StringComparison.OrdinalIgnoreCase)
               || inner.StartsWith("menu.", StringComparison.OrdinalIgnoreCase)
               || inner.StartsWith("menus.", StringComparison.OrdinalIgnoreCase);
    }

    private static string MergeKey(MenuEntry entry)
    {
        var menu = string.IsNullOrWhiteSpace(entry.MenuName) ? "main" : entry.MenuName.Trim();
        if (!string.IsNullOrWhiteSpace(entry.Identifier))
            return menu + "|id|" + entry.Identifier.Trim();
        if (!string.IsNullOrWhiteSpace(entry.Url))
            return menu + "|url|" + NormalizeUrl(entry.Url);
        if (!string.IsNullOrWhiteSpace(entry.PageRef))
            return menu + "|ref|" + entry.PageRef.Trim().Trim('/');
        return menu + "|name|" + (entry.Name ?? string.Empty).Trim();
    }

    private static string NormalizeUrl(string url)
    {
        var value = url.Trim();
        if (value == "/") return "/";
        return "/" + value.Trim('/') + "/";
    }

    private static void WriteString(StringBuilder builder, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        builder.Append(key).Append(" = ").Append(Quote(value.Trim())).Append('\n');
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string GetString(TomlTable table, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (table.TryGetValue(key, out var value) && value is not null)
                return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static int GetInt(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var value) || value is null)
            return 0;
        return value switch
        {
            int number => number,
            long number => (int)number,
            double number => (int)number,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0
        };
    }

    private static bool GetBool(TomlTable table, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!table.TryGetValue(key, out var value) || value is null) continue;
            if (value is bool flag) return flag;
            if (value is string text) return ParseBool(text);
        }

        return false;
    }

    private static bool ParseBool(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value == "1";

    private static (string Key, string Value) SplitYaml(string line)
    {
        var trimmed = line.Trim();
        var index = trimmed.IndexOf(':');
        if (index <= 0) return (trimmed, string.Empty);
        return (trimmed[..index].Trim(), trimmed[(index + 1)..].Trim());
    }

    private static int IndentOf(string line)
    {
        var count = 0;
        foreach (var character in line)
        {
            if (character == ' ') count++;
            else if (character == '\t') count += 2;
            else break;
        }

        return count;
    }

    private static string Unquote(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }

    private static bool IsMarkdown(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\A(?<delimiter>---|\+\+\+)(?:\s*\r?\n(?<frontMatter>.*?)\r?\n\k<delimiter>|\s+(?<frontMatter>.*?)\s+\k<delimiter>)\s*(?:\r?\n|\z)", RegexOptions.Singleline)]
    private static partial Regex FrontMatterBlockRegex();

    [GeneratedRegex(@"^menu:\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex YamlMenuHeaderRegex();

    [GeneratedRegex(@"^menu:\s*(?<name>[^\[#][^\r\n]*?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex YamlMenuScalarRegex();

    [GeneratedRegex(@"^menu:\s*\[(?<items>[^\]]*)\]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex YamlMenuArrayRegex();
}
