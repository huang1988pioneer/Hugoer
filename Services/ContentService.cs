using System.Globalization;
using System.Text;
using Hugoer.Helpers;
using Hugoer.Models;
using Tomlyn;
using Tomlyn.Model;

namespace Hugoer.Services;

public sealed class ContentService
{
    private readonly FrontMatterService _frontMatter;

    public ContentService(FrontMatterService? frontMatter = null)
    {
        _frontMatter = frontMatter ?? new FrontMatterService();
    }

    public IReadOnlyList<ContentItem> ListContent(string sitePath, string? relativeDir = null)
    {
        var contentRoot = PathHelper.ContentDir(sitePath);
        if (!Directory.Exists(contentRoot))
            return [];

        var dir = string.IsNullOrWhiteSpace(relativeDir)
            ? Path.GetFullPath(contentRoot)
            : PathHelper.TryResolveUnder(contentRoot, relativeDir, out var resolved)
                ? resolved
                : string.Empty;

        if (dir.Length == 0)
            return [];

        if (!Directory.Exists(dir))
            return [];

        var items = new List<ContentItem>();

        try
        {
            foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                items.Add(new ContentItem
                {
                    FullPath = d,
                    RelativePath = Path.GetRelativePath(contentRoot, d).Replace('\\', '/'),
                    Name = Path.GetFileName(d),
                    IsDirectory = true,
                    LastWriteTime = Directory.GetLastWriteTime(d)
                });
            }

            foreach (var f in Directory.GetFiles(dir)
                         .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                                     || f.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase)
                                     || f.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                items.Add(new ContentItem
                {
                    FullPath = f,
                    RelativePath = Path.GetRelativePath(contentRoot, f).Replace('\\', '/'),
                    Name = Path.GetFileName(f),
                    IsDirectory = false,
                    LastWriteTime = File.GetLastWriteTime(f)
                });
            }
        }
        catch (IOException)
        {
            // A site can be changed or removed while the content browser is open.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep the entries already read when a protected child is encountered.
        }

        return items;
    }

    public IReadOnlyList<ContentItem> ListAllMarkdown(string sitePath)
    {
        var contentRoot = PathHelper.ContentDir(sitePath);
        if (!Directory.Exists(contentRoot))
            return [];

        var items = new List<ContentItem>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(contentRoot, "*.*", SearchOption.AllDirectories)
                         .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                                     || f.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var metadata = ReadArticleMetadata(f);
                items.Add(new ContentItem
                {
                    FullPath = f,
                    RelativePath = Path.GetRelativePath(contentRoot, f).Replace('\\', '/'),
                    Name = Path.GetFileName(f),
                    IsDirectory = false,
                    LastWriteTime = File.GetLastWriteTime(f),
                    ArticleDate = metadata.Date,
                    ArticleTitle = metadata.Title,
                    IsDraft = metadata.IsDraft
                });
            }
        }
        catch (IOException)
        {
            // Return the entries collected before a directory disappeared or became locked.
        }
        catch (UnauthorizedAccessException)
        {
            // Return readable content even when another directory is protected.
        }

        return items;
    }

    public IReadOnlyList<ContentItem> ListArticles(string sitePath)
    {
        var sections = GetMainSections(sitePath);
        return ListAllMarkdown(sitePath)
            .Where(item => IsArticle(item.RelativePath, sections))
            .ToList();
    }

    public IReadOnlyList<ContentItem> ListSitePages(string sitePath)
    {
        var sections = GetMainSections(sitePath);
        return ListAllMarkdown(sitePath)
            .Where(item => !IsArticle(item.RelativePath, sections))
            .ToList();
    }

    public IReadOnlyList<string> GetMainSections(string sitePath)
    {
        var sections = new List<string>();
        foreach (var file in EnumerateSiteTomlFiles(sitePath))
        {
            try
            {
                var text = File.ReadAllText(file);
                var root = TomlSerializer.Deserialize<TomlTable>(text);
                if (root is null) continue;
                var fileName = Path.GetFileName(file);
                TomlTable? table = root;
                if (!fileName.StartsWith("params.", StringComparison.OrdinalIgnoreCase))
                {
                    if (!root.TryGetValue("params", out var paramsObj) || paramsObj is not TomlTable paramsTable)
                        continue;
                    table = paramsTable;
                }

                if (!table.TryGetValue("mainSections", out var value) || value is null)
                    continue;
                switch (value)
                {
                    case TomlArray array:
                        sections.AddRange(array.OfType<string>().Where(item => !string.IsNullOrWhiteSpace(item)));
                        break;
                    case string textValue when !string.IsNullOrWhiteSpace(textValue):
                        sections.AddRange(textValue.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                        break;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (Exception)
            {
                // Invalid TOML is ignored; callers still get the default section names.
            }

            if (sections.Count > 0)
                break;
        }

        if (sections.Count == 0)
            sections.AddRange(["post", "posts"]);

        return sections
            .Select(section => section.Trim().Trim('/'))
            .Where(section => section.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool IsArticle(string relativePath, IReadOnlyList<string> mainSections)
    {
        var normalized = (relativePath ?? string.Empty).Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        if (!mainSections.Any(candidate => candidate.Equals(parts[0], StringComparison.OrdinalIgnoreCase)))
            return false;

        var fileName = parts[^1];
        return !fileName.Equals("_index.md", StringComparison.OrdinalIgnoreCase)
               && !fileName.Equals("_index.markdown", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateSiteTomlFiles(string sitePath)
    {
        var rootConfig = PathHelper.FindConfigFile(sitePath);
        if (rootConfig is not null)
            yield return rootConfig;

        var configDir = Path.Combine(sitePath, "config");
        if (!Directory.Exists(configDir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(configDir, "*.toml", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (rootConfig is not null && file.Equals(rootConfig, StringComparison.OrdinalIgnoreCase))
                continue;
            yield return file;
        }
    }

    private ArticleMetadata ReadArticleMetadata(string fullPath)
    {
        try
        {
            var document = _frontMatter.Parse(File.ReadAllText(fullPath));
            var title = document.Fields.TryGetValue("title", out var parsedTitle) ? parsedTitle : string.Empty;
            var isDraft = document.Fields.TryGetValue("draft", out var parsedDraft)
                          && bool.TryParse(parsedDraft, out var draft)
                          && draft;
            var date = document.Fields.TryGetValue("date", out var parsedDate)
                       && DateTimeOffset.TryParse(
                           parsedDate,
                           CultureInfo.InvariantCulture,
                           DateTimeStyles.AllowWhiteSpaces,
                           out var articleDate)
                ? articleDate
                : (DateTimeOffset?)null;
            return new ArticleMetadata(title, date, isDraft);
        }
        catch (IOException)
        {
            // A temporarily locked article remains manageable; it simply has no sortable article date.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep the article in the list even when its metadata cannot be read.
        }
        catch (Exception)
        {
            // Malformed front matter must not hide every other article from the browser.
        }

        return new ArticleMetadata(string.Empty, null, false);
    }

    private readonly record struct ArticleMetadata(string Title, DateTimeOffset? Date, bool IsDraft);

    public async Task<string> ReadAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        return await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(string fullPath, string content, CancellationToken cancellationToken = default)
    {
        await AtomicFileWriter.WriteAllTextAsync(fullPath, content, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task CreateMarkdownAsync(
        string sitePath,
        string relativePath,
        string title,
        CancellationToken cancellationToken = default,
        string? slug = null)
    {
        var contentRoot = PathHelper.ContentDir(sitePath);
        var normalized = (relativePath ?? string.Empty).Replace('\\', '/').Trim();
        normalized = normalized.TrimStart('/');
        if (normalized.Length == 0)
            throw new ArgumentException("文章路徑不可為空。", nameof(relativePath));
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            normalized += ".md";

        if (!PathHelper.TryResolveUnder(
                contentRoot,
                normalized.Replace('/', Path.DirectorySeparatorChar),
                out var full,
                allowRoot: false))
        {
            throw new ArgumentException("文章路徑必須位於 content/ 內。", nameof(relativePath));
        }

        var directory = Path.GetDirectoryName(full)
            ?? throw new ArgumentException("文章路徑格式無效。", nameof(relativePath));
        Directory.CreateDirectory(directory);

        var safeTitle = SanitizeFrontMatterValue(title);
        var code = string.IsNullOrWhiteSpace(slug)
            ? Path.GetFileNameWithoutExtension(normalized)
            : SanitizeFrontMatterValue(slug);
        if (code.Length == 0)
            code = Path.GetFileNameWithoutExtension(normalized);

        var date = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var body = $"""
---
title: "{EscapeFrontMatterValue(safeTitle)}"
date: {date}
slug: "{EscapeFrontMatterValue(code)}"
draft: true
---

開始寫作吧。
""";
        try
        {
            await using var stream = new FileStream(
                full,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            await writer.WriteAsync(body.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException) when (File.Exists(full))
        {
            throw new InvalidOperationException($"檔案已存在：{normalized}");
        }
    }

    public void Delete(string fullPath)
    {
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        else if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);
    }

    private static string SanitizeFrontMatterValue(string? value) =>
        (value ?? string.Empty).ReplaceLineEndings(" ").Trim();

    private static string EscapeFrontMatterValue(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}
