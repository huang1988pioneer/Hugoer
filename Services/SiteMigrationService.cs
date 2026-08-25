using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hugoer.Helpers;

namespace Hugoer.Services;

public sealed class ArticleExportInput
{
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public string? Markdown { get; init; }
}

public sealed class ConvertedDocument
{
    public required string FileName { get; init; }
    public required string RelativeDirectory { get; init; }
    public required string Markdown { get; init; }
    public required bool IsDraft { get; init; }
    public required bool IsPost { get; init; }
    public required string Slug { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class SiteMigrationResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = string.Empty;
    public string DestinationPath { get; init; } = string.Empty;
    public int PostCount { get; init; }
    public int PageCount { get; init; }
    public int AssetCount { get; init; }
    public int SkippedCount { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Log { get; init; } = [];
}

public sealed class SiteIdentity
{
    public string Title { get; init; } = "Migrated site";
    public string Description { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://example.org/";
    public string Language { get; init; } = "zh-tw";
    public string TimeZone { get; init; } = "Asia/Taipei";
    public string Author { get; init; } = string.Empty;
}

public sealed partial class SiteMigrationService
{
    private static readonly HashSet<string> SkipDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".github", ".vs", ".vscode", ".idea", ".bundle",
        "node_modules", "public", "_site", "resources", "themes", "layouts",
        "archetypes", "bin", "obj", "vendor", "scaffolds", "_layouts",
        "_includes", "_sass", "_data", "_posts", "_drafts"
    };

    private static readonly HashSet<string> SkipFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "_config.yml", "_config.yaml", "_config.toml", "config.toml", "config.yaml",
        "config.yml", "hugo.toml", "hugo.yaml", "hugo.yml", "hugo.json",
        "package.json", "package-lock.json", "Gemfile", "Gemfile.lock",
        "README.md", "README", "LICENSE", "LICENSE.md", ".gitignore",
        ".gitattributes", ".editorconfig", "Thumbs.db", ".DS_Store"
    };

    private static readonly HashSet<string> DeniedFrontMatterKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "draft", "published", "lastmod", "updated", "last_modified_at",
        "excerpt", "summary", "cover", "thumbnail", "photos", "photo",
        "banner", "og_image", "feature", "featured_image", "category", "tag",
        "permalink", "url", "aliases", "redirect_from", "layout", "type",
        "cascade", "resources", "outputs", "markup", "weight", "slug"
    };

    private static readonly string[] HugoPostSections = ["post", "posts", "blog"];

    private readonly FrontMatterService _frontMatter = new();

    public StaticSiteKind Detect(string? path) => StaticSiteDetector.Detect(path);

    public ConvertedDocument ConvertDocument(
        string markdown,
        string originalPath,
        StaticSiteKind source,
        StaticSiteKind target,
        bool isPost = true,
        bool isDraftFolder = false,
        string? assetUrlPrefix = null,
        bool usePageBundle = false)
    {
        var document = _frontMatter.Parse(markdown ?? string.Empty);
        var isDraft = IsDraft(document.Fields, isDraftFolder);
        var date = ResolveDate(document.Fields, originalPath) ?? DateTimeOffset.Now;
        var slug = ResolveSlug(document.Fields, originalPath, document.Fields.GetValueOrDefault("title"));
        var body = MarkdownEngineConverter.Convert(document.Body, assetUrlPrefix);
        var warnings = MarkdownEngineConverter.CollectWarnings(body).ToList();
        var fields = MapFields(document.Fields, source, target, isDraft, isPost, date, slug);
        var relativeDirectory = DestinationDirectory(
            target,
            isPost,
            isDraft,
            slug,
            usePageBundle && target == StaticSiteKind.Hugo,
            originalPath);
        var destFileName = DestinationFileName(target, isPost, isDraft, date, slug, relativeDirectory, originalPath);
        return new ConvertedDocument
        {
            FileName = destFileName,
            RelativeDirectory = relativeDirectory,
            Markdown = WriteMarkdown(fields, body, target),
            IsDraft = isDraft,
            IsPost = isPost,
            Slug = slug,
            Warnings = warnings
        };
    }

    public SiteMigrationResult ExportArticles(
        string? sourceSitePath,
        IReadOnlyList<ArticleExportInput> articles,
        StaticSiteKind target,
        string destinationDirectory)
    {
        if (target is not StaticSiteKind.Hexo and not StaticSiteKind.Jekyll)
        {
            return Fail("文章匯出目標必須是 Hexo 或 Jekyll。");
        }

        if (string.IsNullOrWhiteSpace(destinationDirectory))
            return Fail("請選擇匯出資料夾。");

        if (articles.Count == 0)
            return Fail("沒有可匯出的文章。");

        var dest = PrepareExportRoot(destinationDirectory, target);
        var sourceKind = string.IsNullOrWhiteSpace(sourceSitePath)
            ? StaticSiteKind.Hugo
            : Detect(sourceSitePath);
        if (sourceKind == StaticSiteKind.Unknown)
            sourceKind = StaticSiteKind.Hugo;

        Directory.CreateDirectory(dest);
        EnsureScaffold(dest, target, ReadSiteIdentity(sourceSitePath, sourceKind), overwriteConfig: false);

        var log = new List<string>();
        var warnings = new List<string>();
        var posts = 0;
        var assets = 0;
        var skipped = 0;

        foreach (var article in articles)
        {
            try
            {
                var markdown = article.Markdown ?? File.ReadAllText(article.FullPath);
                var slug = ResolveSlug(
                    _frontMatter.Parse(markdown).Fields,
                    Path.GetFileName(article.FullPath),
                    null);
                var siblingAssets = ListSiblingAssets(article.FullPath).ToList();
                var prefix = siblingAssets.Count > 0
                    ? StaticSiteDetector.AssetUrlPrefix(target, slug)
                    : null;
                var converted = ConvertDocument(
                    markdown,
                    article.RelativePath,
                    sourceKind,
                    target,
                    isPost: true,
                    isDraftFolder: false,
                    assetUrlPrefix: prefix);
                WriteConverted(dest, converted);
                posts++;
                assets += CopySiblingAssets(siblingAssets, dest, target, converted.Slug);
                assets += CopyReferencedStatic(sourceSitePath, sourceKind, markdown, dest, target);
                log.Add($"文章 {article.RelativePath} → {converted.RelativeDirectory}/{converted.FileName}");
                warnings.AddRange(converted.Warnings.Select(item => $"{article.RelativePath}：{item}"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                skipped++;
                warnings.Add($"{article.RelativePath}：{ex.Message}");
            }
        }

        WriteReport(dest, sourceSitePath ?? "", sourceKind, dest, target, posts, 0, assets, skipped, warnings, log, export: true);
        return new SiteMigrationResult
        {
            Succeeded = posts > 0,
            Message = posts > 0
                ? $"已匯出 {posts} 篇文章至 {dest}（{StaticSiteDetector.DisplayName(target)} 相容格式）"
                : "沒有成功匯出的文章。",
            DestinationPath = dest,
            PostCount = posts,
            AssetCount = assets,
            SkippedCount = skipped,
            Warnings = warnings,
            Log = log
        };
    }

    public SiteMigrationResult Migrate(
        string sourcePath,
        string destinationPath,
        StaticSiteKind sourceKind,
        StaticSiteKind targetKind)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !Directory.Exists(sourcePath))
            return Fail("找不到來源網站資料夾。");
        if (string.IsNullOrWhiteSpace(destinationPath))
            return Fail("請指定目標資料夾。");
        if (sourceKind is StaticSiteKind.Unknown)
            return Fail("無法辨識來源引擎，請改為手動選擇 Hugo、Hexo 或 Jekyll。");
        if (targetKind is StaticSiteKind.Unknown)
            return Fail("請選擇目標引擎。");
        if (sourceKind == targetKind)
            return Fail("來源與目標引擎相同，無需遷移。");
        if (IsSameOrNested(sourcePath, destinationPath))
            return Fail("目標資料夾不可與來源相同，也不可位於來源之內或之外層。");

        if (Directory.Exists(destinationPath)
            && Directory.EnumerateFileSystemEntries(destinationPath).Any())
        {
            return Fail("目標資料夾不是空的。請改用空白資料夾，以免覆蓋現有網站。");
        }

        Directory.CreateDirectory(destinationPath);
        var identity = ReadSiteIdentity(sourcePath, sourceKind);
        EnsureScaffold(destinationPath, targetKind, identity, overwriteConfig: true);

        var log = new List<string>();
        var warnings = new List<string>();
        var posts = 0;
        var pages = 0;
        var assets = 0;
        var skipped = 0;

        foreach (var file in ListSourceFiles(sourcePath, sourceKind))
        {
            try
            {
                var markdown = File.ReadAllText(file.FullPath);
                var parsed = _frontMatter.Parse(markdown);
                var slug = ResolveSlug(parsed.Fields, Path.GetFileName(file.FullPath), parsed.Fields.GetValueOrDefault("title"));
                var siblingAssets = ListSiblingAssets(file.FullPath).ToList();
                var useHugoBundle = targetKind == StaticSiteKind.Hugo && siblingAssets.Count > 0 && file.IsPost;
                var prefix = siblingAssets.Count > 0 && !useHugoBundle
                    ? StaticSiteDetector.AssetUrlPrefix(targetKind, slug)
                    : null;
                var converted = ConvertDocument(
                    markdown,
                    file.RelativePath,
                    sourceKind,
                    targetKind,
                    isPost: file.IsPost,
                    isDraftFolder: file.IsDraftFolder,
                    assetUrlPrefix: prefix,
                    usePageBundle: useHugoBundle);
                WriteConverted(destinationPath, converted);
                if (file.IsPost)
                    posts++;
                else
                    pages++;

                if (useHugoBundle)
                    assets += CopyFiles(siblingAssets, Path.Combine(destinationPath, converted.RelativeDirectory));
                else
                    assets += CopySiblingAssets(siblingAssets, destinationPath, targetKind, converted.Slug);

                log.Add($"{(file.IsPost ? "文章" : "頁面")} {file.RelativePath} → {converted.RelativeDirectory}/{converted.FileName}");
                warnings.AddRange(converted.Warnings.Select(item => $"{file.RelativePath}：{item}"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                skipped++;
                warnings.Add($"{file.RelativePath}：{ex.Message}");
            }
        }

        assets += CopySiteAssets(sourcePath, sourceKind, destinationPath, targetKind, log);
        WriteReport(destinationPath, sourcePath, sourceKind, destinationPath, targetKind, posts, pages, assets, skipped, warnings, log, export: false);

        var engineFrom = StaticSiteDetector.DisplayName(sourceKind);
        var engineTo = StaticSiteDetector.DisplayName(targetKind);
        return new SiteMigrationResult
        {
            Succeeded = true,
            Message = $"已將 {engineFrom} 遷移為 {engineTo}：{posts} 篇文章、{pages} 個頁面、{assets} 個靜態檔。主題與範本無法轉換。",
            DestinationPath = destinationPath,
            PostCount = posts,
            PageCount = pages,
            AssetCount = assets,
            SkippedCount = skipped,
            Warnings = warnings,
            Log = log
        };
    }

    public SiteIdentity ReadSiteIdentity(string? sitePath, StaticSiteKind kind)
    {
        var fields = ReadSimpleConfig(FindConfigPath(sitePath, kind));
        var url = FirstValue(fields, "baseURL", "baseurl", "url") ?? "https://example.org/";
        var root = FirstValue(fields, "root") ?? "/";
        if (!url.Contains("://", StringComparison.Ordinal) && kind != StaticSiteKind.Hugo)
            url = "https://example.org/";
        if (kind != StaticSiteKind.Hugo)
        {
            var trimmedRoot = root.Trim();
            if (trimmedRoot.Length > 1)
            {
                url = url.TrimEnd('/') + (trimmedRoot.StartsWith('/') ? trimmedRoot : "/" + trimmedRoot);
            }
        }

        if (!url.EndsWith('/'))
            url += "/";

        return new SiteIdentity
        {
            Title = FirstValue(fields, "title") ?? Path.GetFileName((sitePath ?? "").TrimEnd('\\', '/')) ?? "Migrated site",
            Description = FirstValue(fields, "description", "subtitle") ?? string.Empty,
            BaseUrl = url,
            Language = FirstValue(fields, "languageCode", "locale", "language", "lang") ?? "zh-tw",
            TimeZone = FirstValue(fields, "timeZone", "timezone") ?? "Asia/Taipei",
            Author = FirstValue(fields, "author") ?? string.Empty
        };
    }

    public string MigrationPlan(StaticSiteKind source, StaticSiteKind target) => (source, target) switch
    {
        (StaticSiteKind.Hexo, StaticSiteKind.Hugo) =>
            "Hexo → Hugo：文章寫入 content/post，source 下的靜態檔寫入 static，並產生 hugo.toml。主題、EJS 範本與 Hexo 外掛無法轉換。",
        (StaticSiteKind.Jekyll, StaticSiteKind.Hugo) =>
            "Jekyll → Hugo：_posts 寫入 content/post，assets 寫入 static，並產生 hugo.toml。Liquid 佈局與外掛無法轉換。",
        (StaticSiteKind.Hugo, StaticSiteKind.Hexo) =>
            "Hugo → Hexo：文章寫入 source/_posts（草稿到 _drafts），static 寫入 source，並產生 _config.yml。Hugo 主題無法轉換；shortcode 會盡量改成 Markdown。",
        (StaticSiteKind.Hugo, StaticSiteKind.Jekyll) =>
            "Hugo → Jekyll：文章寫入 _posts（草稿到 _drafts），static 放到網站根目錄，並產生 _config.yml。Hugo 主題無法轉換；shortcode 會盡量改成 Markdown。",
        (StaticSiteKind.Hexo, StaticSiteKind.Jekyll) =>
            "Hexo → Jekyll：文章寫入 _posts，source 靜態檔放到網站根目錄。主題與外掛無法轉換。",
        (StaticSiteKind.Jekyll, StaticSiteKind.Hexo) =>
            "Jekyll → Hexo：文章寫入 source/_posts，assets 寫入 source。主題與外掛無法轉換。",
        _ => "請選擇不同的來源與目標引擎。"
    };

    private void WriteConverted(string destRoot, ConvertedDocument converted)
    {
        var dir = Path.Combine(destRoot, converted.RelativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        var path = UniquePath(Path.Combine(dir, converted.FileName));
        File.WriteAllText(path, converted.Markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string DestinationDirectory(
        StaticSiteKind target,
        bool isPost,
        bool isDraft,
        string slug,
        bool hugoPageBundle,
        string originalPath)
    {
        if (isPost)
        {
            if (target == StaticSiteKind.Hugo)
                return hugoPageBundle ? $"content/post/{slug}" : "content/post";
            if (target == StaticSiteKind.Hexo)
                return isDraft ? "source/_drafts" : "source/_posts";
            return isDraft ? "_drafts" : "_posts";
        }

        var pageDir = PageRelativeDirectory(originalPath);
        return target switch
        {
            StaticSiteKind.Hugo => string.IsNullOrEmpty(pageDir) ? "content" : "content/" + pageDir,
            StaticSiteKind.Hexo => string.IsNullOrEmpty(pageDir) ? "source" : "source/" + pageDir,
            _ => pageDir
        };
    }

    private static string PageRelativeDirectory(string originalPath)
    {
        var relative = originalPath.Replace('\\', '/').Trim('/');
        foreach (var prefix in new[] { "content/", "source/" })
        {
            var index = relative.IndexOf("/" + prefix, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
                relative = relative[(index + 1 + prefix.Length)..];
            else if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                relative = relative[prefix.Length..];
        }

        var dir = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;
        if (dir.Contains("/_posts/", StringComparison.OrdinalIgnoreCase)
            || dir.EndsWith("/_posts", StringComparison.OrdinalIgnoreCase)
            || dir.Contains("/_drafts/", StringComparison.OrdinalIgnoreCase)
            || dir.EndsWith("/_drafts", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return dir.Trim('/');
    }

    private static string DestinationFileName(
        StaticSiteKind target,
        bool isPost,
        bool isDraft,
        DateTimeOffset date,
        string slug,
        string relativeDirectory,
        string originalPath)
    {
        var originalName = Path.GetFileName(originalPath);
        var originalIsIndex = originalName.Equals("index.md", StringComparison.OrdinalIgnoreCase)
                              || originalName.Equals("index.markdown", StringComparison.OrdinalIgnoreCase);

        if (target == StaticSiteKind.Hugo)
        {
            if (relativeDirectory.EndsWith("/" + slug, StringComparison.OrdinalIgnoreCase)
                || relativeDirectory.Equals("content/post/" + slug, StringComparison.OrdinalIgnoreCase))
                return "index.md";
            return slug + ".md";
        }

        if (!isPost)
            return originalIsIndex ? "index.md" : slug + ".md";

        if (isDraft)
            return slug + ".md";

        var prefix = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (slug.StartsWith(prefix + "-", StringComparison.Ordinal))
            return slug + ".md";
        return $"{prefix}-{slug}.md";
    }

    private Dictionary<string, string> MapFields(
        IReadOnlyDictionary<string, string> source,
        StaticSiteKind from,
        StaticSiteKind to,
        bool isDraft,
        bool isPost,
        DateTimeOffset date,
        string slug)
    {
        var dest = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Copy(dest, source, "title");
        if (!dest.ContainsKey("title") || string.IsNullOrWhiteSpace(dest["title"]))
            dest["title"] = slug;

        dest["date"] = FormatDate(date, to);

        if (TryGet(source, out var lastmod, "lastmod", "updated", "last_modified_at")
            && ParseDate(lastmod) is { } lastmodDate)
        {
            if (to == StaticSiteKind.Hexo)
                dest["updated"] = FormatDate(lastmodDate, to);
            else if (to == StaticSiteKind.Jekyll)
                dest["last_modified_at"] = FormatDate(lastmodDate, to);
            else
                dest["lastmod"] = FormatDate(lastmodDate, to);
        }

        if (to == StaticSiteKind.Hugo)
            dest["draft"] = isDraft ? "true" : "false";
        else
            dest["published"] = isDraft ? "false" : "true";

        dest["slug"] = slug;

        var categories = JoinPresent(source, "categories", "category");
        if (!string.IsNullOrWhiteSpace(categories))
            dest["categories"] = categories;

        var tags = JoinPresent(source, "tags", "tag");
        if (!string.IsNullOrWhiteSpace(tags))
            dest["tags"] = tags;

        if (TryGet(source, out var description, "description", "excerpt", "summary"))
            dest["description"] = CollapseWhitespace(description);

        if (TryGet(source, out var image, "image", "cover", "thumbnail", "banner", "photos", "photo", "featured_image"))
        {
            var first = SplitList(image).FirstOrDefault() ?? image;
            dest["image"] = first;
            if (to == StaticSiteKind.Hexo)
                dest["cover"] = first;
        }

        if (TryGet(source, out var permalink, "url", "permalink"))
        {
            if (to == StaticSiteKind.Hugo)
                dest["url"] = permalink;
            else
                dest["permalink"] = permalink;
        }

        if (TryGet(source, out var aliases, "aliases", "redirect_from"))
        {
            if (to == StaticSiteKind.Jekyll)
                dest["redirect_from"] = aliases;
            else if (to == StaticSiteKind.Hugo)
                dest["aliases"] = aliases;
        }

        if (to is StaticSiteKind.Hexo or StaticSiteKind.Jekyll)
            dest["layout"] = isPost ? "post" : "page";

        foreach (var (key, value) in source)
        {
            if (DeniedFrontMatterKeys.Contains(key) || dest.ContainsKey(key) || string.IsNullOrWhiteSpace(value))
                continue;
            dest[key] = value;
        }

        _ = from;
        return dest;
    }

    private static string WriteMarkdown(
        IReadOnlyDictionary<string, string> fields,
        string body,
        StaticSiteKind target)
    {
        var output = new StringBuilder("---\n");
        var ordered = target switch
        {
            StaticSiteKind.Hexo => new[]
            {
                "title", "date", "updated", "slug", "categories", "tags", "cover", "image",
                "description", "published", "permalink", "layout"
            },
            StaticSiteKind.Jekyll => new[]
            {
                "title", "date", "last_modified_at", "slug", "categories", "tags", "image",
                "description", "published", "permalink", "layout", "redirect_from"
            },
            _ => new[]
            {
                "title", "date", "lastmod", "slug", "categories", "tags", "image",
                "description", "draft", "url", "aliases"
            }
        };

        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in ordered)
            AppendYamlField(output, fields, key, emitted);
        foreach (var key in fields.Keys.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
            AppendYamlField(output, fields, key, emitted);

        output.Append("---\n\n");
        output.Append((body ?? string.Empty).TrimStart('\r', '\n'));
        if (output.Length > 0 && output[^1] != '\n')
            output.Append('\n');
        return output.ToString();
    }

    private static void AppendYamlField(
        StringBuilder output,
        IReadOnlyDictionary<string, string> fields,
        string key,
        ISet<string> emitted)
    {
        if (!fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value) || !emitted.Add(key))
            return;

        if (key.Equals("categories", StringComparison.OrdinalIgnoreCase)
            || key.Equals("tags", StringComparison.OrdinalIgnoreCase)
            || key.Equals("aliases", StringComparison.OrdinalIgnoreCase)
            || key.Equals("redirect_from", StringComparison.OrdinalIgnoreCase))
        {
            var items = SplitList(value);
            if (items.Count == 0)
                return;
            output.Append(key).Append(":\n");
            foreach (var item in items)
                output.Append("  - ").Append(QuoteYaml(item)).Append('\n');
            return;
        }

        if (key.Equals("draft", StringComparison.OrdinalIgnoreCase)
            || key.Equals("published", StringComparison.OrdinalIgnoreCase)
            || key.Equals("comments", StringComparison.OrdinalIgnoreCase))
        {
            output.Append(key).Append(": ").Append(value.ToLowerInvariant()).Append('\n');
            return;
        }

        if (key.Equals("date", StringComparison.OrdinalIgnoreCase)
            || key.Equals("updated", StringComparison.OrdinalIgnoreCase)
            || key.Equals("lastmod", StringComparison.OrdinalIgnoreCase)
            || key.Equals("last_modified_at", StringComparison.OrdinalIgnoreCase))
        {
            output.Append(key).Append(": ").Append(value).Append('\n');
            return;
        }

        output.Append(key).Append(": ").Append(QuoteYaml(CollapseWhitespace(value))).Append('\n');
    }

    private static string QuoteYaml(string value)
    {
        value ??= string.Empty;
        if (value.StartsWith('[') && value.EndsWith(']'))
            return value;
        return "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private List<SourceContentFile> ListSourceFiles(string sourcePath, StaticSiteKind kind)
    {
        var files = new List<SourceContentFile>();
        switch (kind)
        {
            case StaticSiteKind.Hexo:
                AddMarkdownTree(files, Path.Combine(sourcePath, "source", "_posts"), sourcePath, isPost: true, isDraftFolder: false);
                AddMarkdownTree(files, Path.Combine(sourcePath, "source", "_drafts"), sourcePath, isPost: true, isDraftFolder: true);
                AddMarkdownTree(files, Path.Combine(sourcePath, "source"), sourcePath, isPost: false, isDraftFolder: false, skipNames: ["_posts", "_drafts", "_data"]);
                break;
            case StaticSiteKind.Jekyll:
                AddMarkdownTree(files, Path.Combine(sourcePath, "_posts"), sourcePath, isPost: true, isDraftFolder: false);
                AddMarkdownTree(files, Path.Combine(sourcePath, "_drafts"), sourcePath, isPost: true, isDraftFolder: true);
                AddMarkdownTree(files, sourcePath, sourcePath, isPost: false, isDraftFolder: false, skipNames: ["_posts", "_drafts", "_layouts", "_includes", "_sass", "_site", "_data", "vendor"]);
                break;
            default:
                var content = PathHelper.ContentDir(sourcePath);
                AddMarkdownTree(files, content, sourcePath, isPost: false, isDraftFolder: false);
                foreach (var file in files.ToList())
                {
                    var relative = file.RelativePath.Replace('\\', '/');
                    if (relative.StartsWith("content/", StringComparison.OrdinalIgnoreCase))
                        relative = relative["content/".Length..];
                    var isPost = HugoPostSections.Any(section =>
                        relative.StartsWith(section + "/", StringComparison.OrdinalIgnoreCase));
                    files.Remove(file);
                    files.Add(file with { IsPost = isPost });
                }
                break;
        }

        return files
            .Where(file => !Path.GetFileName(file.FullPath).Equals("_index.md", StringComparison.OrdinalIgnoreCase)
                           && !Path.GetFileName(file.FullPath).Equals("_index.markdown", StringComparison.OrdinalIgnoreCase))
            .GroupBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddMarkdownTree(
        List<SourceContentFile> files,
        string directory,
        string sourcePath,
        bool isPost,
        bool isDraftFolder,
        IReadOnlyList<string>? skipNames = null)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                && !file.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
                continue;

            var name = Path.GetFileName(file);
            if (name.Equals("README.md", StringComparison.OrdinalIgnoreCase)
                || name.Equals("LICENSE.md", StringComparison.OrdinalIgnoreCase)
                || name.Equals("CONTRIBUTING.md", StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = Path.GetRelativePath(directory, file).Replace('\\', '/');
            var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (skipNames is not null && parts.Any(part => skipNames.Contains(part, StringComparer.OrdinalIgnoreCase)))
                continue;
            if (!isPost && parts.Any(part => part.StartsWith('_')))
                continue;
            if (!isPost
                && parts.Length == 1
                && Path.GetFileNameWithoutExtension(file).Equals("index", StringComparison.OrdinalIgnoreCase))
                continue;

            files.Add(new SourceContentFile(
                file,
                Path.GetRelativePath(sourcePath, file).Replace('\\', '/'),
                isPost,
                isDraftFolder));
        }
    }

    private static IEnumerable<string> ListSiblingAssets(string markdownPath)
    {
        var dir = Path.GetDirectoryName(markdownPath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            yield break;

        var stem = Path.GetFileNameWithoutExtension(markdownPath);
        var isIndex = stem.Equals("index", StringComparison.OrdinalIgnoreCase);

        if (isIndex)
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                if (IsMarkdown(file))
                    continue;
                yield return file;
            }

            yield break;
        }

        var assetDir = Path.Combine(dir, stem);
        if (!Directory.Exists(assetDir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(assetDir, "*", SearchOption.AllDirectories))
        {
            if (IsMarkdown(file))
                continue;
            yield return file;
        }
    }

    private static int CopySiblingAssets(
        IReadOnlyList<string> assets,
        string destRoot,
        StaticSiteKind target,
        string slug)
    {
        if (assets.Count == 0)
            return 0;
        var destDir = StaticSiteDetector.AssetDirectory(destRoot, target, slug);
        return CopyFiles(assets, destDir);
    }

    private static int CopyReferencedStatic(
        string? sourceSitePath,
        StaticSiteKind sourceKind,
        string markdown,
        string destRoot,
        StaticSiteKind target)
    {
        if (string.IsNullOrWhiteSpace(sourceSitePath))
            return 0;

        var count = 0;
        foreach (Match match in MarkdownImageRefRegex().Matches(markdown))
        {
            var url = match.Groups["url"].Value.Trim();
            if (!url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal))
                continue;

            var relative = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var candidates = SourceStaticCandidates(sourceSitePath, sourceKind, relative);
            var found = candidates.FirstOrDefault(File.Exists);
            if (found is null)
                continue;

            var dest = target switch
            {
                StaticSiteKind.Hugo => Path.Combine(destRoot, "static", relative),
                StaticSiteKind.Hexo => Path.Combine(destRoot, "source", relative),
                _ => Path.Combine(destRoot, relative)
            };
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            var unique = UniquePath(dest);
            File.Copy(found, unique, overwrite: false);
            count++;
        }

        return count;
    }

    private static IEnumerable<string> SourceStaticCandidates(string sitePath, StaticSiteKind kind, string relative) =>
        kind switch
        {
            StaticSiteKind.Hugo => [Path.Combine(sitePath, "static", relative)],
            StaticSiteKind.Hexo => [Path.Combine(sitePath, "source", relative)],
            _ =>
            [
                Path.Combine(sitePath, relative),
                Path.Combine(sitePath, "assets", relative)
            ]
        };

    private static int CopySiteAssets(
        string sourcePath,
        StaticSiteKind source,
        string destPath,
        StaticSiteKind target,
        List<string> log)
    {
        var count = 0;
        if (source == StaticSiteKind.Hugo)
        {
            var staticDir = PathHelper.StaticDir(sourcePath);
            var dest = target == StaticSiteKind.Hexo
                ? Path.Combine(destPath, "source")
                : destPath;
            if (target == StaticSiteKind.Hugo)
                dest = PathHelper.StaticDir(destPath);
            count += CopyTree(staticDir, dest, log);
        }
        else if (source == StaticSiteKind.Hexo)
        {
            var dest = target == StaticSiteKind.Hugo
                ? PathHelper.StaticDir(destPath)
                : target == StaticSiteKind.Hexo
                    ? Path.Combine(destPath, "source")
                    : destPath;
            count += CopyTree(Path.Combine(sourcePath, "source"), dest, log, extraSkip: ["_posts", "_drafts"]);
        }
        else if (source == StaticSiteKind.Jekyll)
        {
            var dest = target == StaticSiteKind.Hugo
                ? PathHelper.StaticDir(destPath)
                : target == StaticSiteKind.Hexo
                    ? Path.Combine(destPath, "source")
                    : destPath;
            foreach (var folder in new[] { "assets", "images", "files", "media", "uploads", "static" })
                count += CopyTree(Path.Combine(sourcePath, folder), Path.Combine(dest, folder == "static" && target == StaticSiteKind.Hugo ? "" : folder), log);
        }

        log.Add($"靜態檔複製 {count} 個。");
        return count;
    }

    private static int CopyTree(string sourceDir, string destDir, List<string> log, IReadOnlyList<string>? extraSkip = null)
    {
        if (!Directory.Exists(sourceDir))
            return 0;

        var count = 0;
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(part => SkipDirectoryNames.Contains(part)))
                continue;
            if (extraSkip is not null && parts.Any(part => extraSkip.Contains(part, StringComparer.OrdinalIgnoreCase)))
                continue;
            if (SkipFileNames.Contains(Path.GetFileName(file)))
                continue;
            if (IsMarkdown(file))
                continue;

            var dest = string.IsNullOrWhiteSpace(destDir)
                ? file
                : Path.Combine(destDir, relative);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, UniquePath(dest), overwrite: false);
                count++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Add($"略過靜態檔 {relative}：{ex.Message}");
            }
        }

        return count;
    }

    private static int CopyFiles(IReadOnlyList<string> files, string destDir)
    {
        if (files.Count == 0)
            return 0;
        Directory.CreateDirectory(destDir);
        var count = 0;
        foreach (var file in files)
        {
            var dest = UniquePath(Path.Combine(destDir, Path.GetFileName(file)));
            File.Copy(file, dest, overwrite: false);
            count++;
        }

        return count;
    }

    private static void EnsureScaffold(string dest, StaticSiteKind target, SiteIdentity identity, bool overwriteConfig)
    {
        switch (target)
        {
            case StaticSiteKind.Hugo:
                Directory.CreateDirectory(Path.Combine(dest, "content", "post"));
                Directory.CreateDirectory(Path.Combine(dest, "static"));
                WriteIfMissing(Path.Combine(dest, "hugo.toml"), BuildHugoConfig(identity), overwriteConfig);
                break;
            case StaticSiteKind.Hexo:
                Directory.CreateDirectory(Path.Combine(dest, "source", "_posts"));
                Directory.CreateDirectory(Path.Combine(dest, "source", "_drafts"));
                Directory.CreateDirectory(Path.Combine(dest, "source", "images"));
                WriteIfMissing(Path.Combine(dest, "_config.yml"), BuildHexoConfig(identity), overwriteConfig);
                break;
            default:
                Directory.CreateDirectory(Path.Combine(dest, "_posts"));
                Directory.CreateDirectory(Path.Combine(dest, "_drafts"));
                Directory.CreateDirectory(Path.Combine(dest, "assets", "images"));
                WriteIfMissing(Path.Combine(dest, "_config.yml"), BuildJekyllConfig(identity), overwriteConfig);
                break;
        }
    }

    private static string BuildHugoConfig(SiteIdentity identity) =>
        $"""
        baseURL = "{EscapeToml(identity.BaseUrl)}"
        languageCode = "{EscapeToml(identity.Language)}"
        title = "{EscapeToml(identity.Title)}"
        hasCJKLanguage = true
        timeZone = "{EscapeToml(identity.TimeZone)}"

        [pagination]
        pagerSize = 10

        [params]
        description = "{EscapeToml(identity.Description)}"
        mainSections = ["post"]
        """ + (string.IsNullOrWhiteSpace(identity.Author) ? Environment.NewLine : $"""

        [params.author]
        name = "{EscapeToml(identity.Author)}"
        """);

    private static string BuildHexoConfig(SiteIdentity identity)
    {
        var url = identity.BaseUrl.TrimEnd('/');
        return $"""
            title: {QuoteYaml(identity.Title)}
            subtitle: {QuoteYaml(identity.Description)}
            description: {QuoteYaml(identity.Description)}
            author: {QuoteYaml(identity.Author)}
            language: {QuoteYaml(identity.Language)}
            timezone: {QuoteYaml(identity.TimeZone)}
            url: {QuoteYaml(url)}
            root: "/"
            permalink: :year/:month/:day/:title/
            source_dir: source
            public_dir: public
            """ + "\n";
    }

    private static string BuildJekyllConfig(SiteIdentity identity)
    {
        var uri = TryParseBase(identity.BaseUrl);
        return $"""
            title: {QuoteYaml(identity.Title)}
            description: {QuoteYaml(identity.Description)}
            url: {QuoteYaml(uri.url)}
            baseurl: {QuoteYaml(uri.baseUrl)}
            markdown: kramdown
            highlighter: rouge
            permalink: /:year/:month/:day/:title/
            timezone: {QuoteYaml(identity.TimeZone)}
            """ + "\n";
    }

    private static (string url, string baseUrl) TryParseBase(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return (baseUrl.TrimEnd('/'), "");
        var path = uri.AbsolutePath.TrimEnd('/');
        return (uri.GetLeftPart(UriPartial.Authority), string.IsNullOrEmpty(path) ? "" : path);
    }

    private static void WriteIfMissing(string path, string content, bool overwrite)
    {
        if (!overwrite && File.Exists(path))
            return;
        File.WriteAllText(path, content.Replace("\r\n", "\n", StringComparison.Ordinal) + (content.EndsWith('\n') ? "" : "\n"),
            new UTF8Encoding(false));
    }

    private static void WriteReport(
        string dest,
        string sourcePath,
        StaticSiteKind sourceKind,
        string destinationPath,
        StaticSiteKind targetKind,
        int posts,
        int pages,
        int assets,
        int skipped,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> log,
        bool export)
    {
        var sb = new StringBuilder();
        sb.AppendLine(export ? "Hugoer 文章匯出報告" : "Hugoer 網站遷移報告");
        sb.AppendLine($"時間：{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"來源：{sourcePath}（{StaticSiteDetector.DisplayName(sourceKind)}）");
        sb.AppendLine($"目標：{destinationPath}（{StaticSiteDetector.DisplayName(targetKind)}）");
        sb.AppendLine($"文章：{posts}");
        if (!export)
            sb.AppendLine($"頁面：{pages}");
        sb.AppendLine($"靜態檔：{assets}");
        sb.AppendLine($"略過：{skipped}");
        sb.AppendLine();
        if (warnings.Count > 0)
        {
            sb.AppendLine("警告：");
            foreach (var warning in warnings)
                sb.AppendLine("- " + warning);
            sb.AppendLine();
        }

        sb.AppendLine("日誌：");
        foreach (var line in log)
            sb.AppendLine("- " + line);

        File.WriteAllText(
            Path.Combine(dest, export ? "hugoer-export-report.txt" : "hugoer-migration-report.txt"),
            sb.ToString(),
            new UTF8Encoding(false));
    }

    private static string PrepareExportRoot(string destinationDirectory, StaticSiteKind target)
    {
        var dest = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(dest);
        var detected = StaticSiteDetector.Detect(dest);
        if (detected == target)
            return dest;

        var hasEntries = Directory.EnumerateFileSystemEntries(dest).Any();
        if (!hasEntries)
            return dest;

        var folder = target == StaticSiteKind.Hexo ? "hexo-export" : "jekyll-export";
        dest = Path.Combine(dest, folder);
        Directory.CreateDirectory(dest);
        return dest;
    }

    private static string? FindConfigPath(string? sitePath, StaticSiteKind kind)
    {
        if (string.IsNullOrWhiteSpace(sitePath))
            return null;

        if (kind == StaticSiteKind.Hugo)
            return PathHelper.FindConfigFile(sitePath);

        foreach (var name in new[] { "_config.yml", "_config.yaml", "_config.toml" })
        {
            var path = Path.Combine(sitePath, name);
            if (File.Exists(path))
                return path;
        }

        return PathHelper.FindConfigFile(sitePath);
    }

    private static Dictionary<string, string> ReadSimpleConfig(string? path)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return fields;

        try
        {
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('['))
                    continue;
                var match = SimpleConfigLineRegex().Match(line);
                if (!match.Success)
                    continue;
                var value = UnquoteConfig(match.Groups["value"].Value);
                fields.TryAdd(match.Groups["key"].Value, value);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return fields;
    }

    private static string UnquoteConfig(string value)
    {
        value = value.Trim();
        var hash = value.IndexOf('#');
        if (hash > 0 && (hash == 0 || char.IsWhiteSpace(value[hash - 1])))
        {
            var inQuotes = value.StartsWith('"') || value.StartsWith('\'');
            if (!inQuotes)
                value = value[..hash].Trim();
        }

        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value.Trim(',');
    }

    private static bool IsDraft(IReadOnlyDictionary<string, string> fields, bool isDraftFolder)
    {
        if (isDraftFolder)
            return true;
        if (TryGet(fields, out var draft, "draft") && IsTrue(draft))
            return true;
        if (TryGet(fields, out var published, "published") && IsFalse(published))
            return true;
        return false;
    }

    private static DateTimeOffset? ResolveDate(IReadOnlyDictionary<string, string> fields, string fileName)
    {
        if (TryGet(fields, out var raw, "date") && ParseDate(raw) is { } parsed)
            return parsed;
        return DateFromFileName(fileName);
    }

    private static string ResolveSlug(IReadOnlyDictionary<string, string> fields, string fileName, string? title)
    {
        if (TryGet(fields, out var slug, "slug") && !string.IsNullOrWhiteSpace(slug))
            return SanitizeSlug(TrimPermalinkToSlug(slug));

        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem.Equals("index", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("_index", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(fileName) ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(parent))
                return SanitizeSlug(parent);
        }

        var withoutDate = DatePrefixRegex().Replace(stem, "${rest}");
        if (!string.IsNullOrWhiteSpace(withoutDate) && withoutDate != stem)
            return SanitizeSlug(withoutDate);
        if (!string.IsNullOrWhiteSpace(stem) && !stem.Equals("index", StringComparison.OrdinalIgnoreCase))
            return SanitizeSlug(stem);
        if (!string.IsNullOrWhiteSpace(title))
            return SanitizeSlug(SlugFromTitle(title));
        return "post";
    }

    private static string SanitizeSlug(string slug)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = slug.Trim().Select(c => invalid.Contains(c) || c is ' ' or '/' or '\\' ? '-' : c).ToArray();
        var text = new string(chars);
        while (text.Contains("--", StringComparison.Ordinal))
            text = text.Replace("--", "-", StringComparison.Ordinal);
        text = text.Trim('-', '.');
        return string.IsNullOrWhiteSpace(text) ? "post" : text;
    }

    private static string SlugFromTitle(string title)
    {
        var sb = new StringBuilder();
        foreach (var c in title.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                sb.Append(c);
            else if (char.IsWhiteSpace(c))
                sb.Append('-');
            else if (c >= 0x4E00)
                sb.Append(c);
        }

        return SanitizeSlug(sb.ToString());
    }

    private static string TrimPermalinkToSlug(string value)
    {
        var text = value.Trim().Replace('\\', '/').Trim('/');
        if (text.Length == 0)
            return "post";
        var last = text.Split('/')[^1];
        if (last.Contains('.'))
            last = last[..last.LastIndexOf('.')];
        return string.IsNullOrWhiteSpace(last) ? "post" : last;
    }

    private static DateTimeOffset? DateFromFileName(string fileName)
    {
        var match = DatePrefixRegex().Match(Path.GetFileNameWithoutExtension(fileName));
        if (!match.Success)
            return null;

        if (DateTime.TryParse(
                match.Groups["date"].Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var date))
            return new DateTimeOffset(date);
        return null;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
            return dto;
        if (DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dto))
            return dto;
        return null;
    }

    private static string FormatDate(DateTimeOffset date, StaticSiteKind target) => target switch
    {
        StaticSiteKind.Hexo => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        StaticSiteKind.Jekyll => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                                 + " " + date.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", "", StringComparison.Ordinal),
        _ => date.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture)
    };

    private static bool IsTrue(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || value.Equals("1", StringComparison.Ordinal);

    private static bool IsFalse(string value) =>
        value.Equals("false", StringComparison.OrdinalIgnoreCase)
        || value.Equals("no", StringComparison.OrdinalIgnoreCase)
        || value.Equals("0", StringComparison.Ordinal);

    private static bool TryGet(IReadOnlyDictionary<string, string> fields, out string value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found))
            {
                value = found;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static void Copy(IDictionary<string, string> dest, IReadOnlyDictionary<string, string> source, string key)
    {
        if (source.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            dest[key] = value;
    }

    private static string JoinPresent(IReadOnlyDictionary<string, string> source, params string[] keys)
    {
        var items = new List<string>();
        foreach (var key in keys)
        {
            if (!source.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
                continue;
            items.AddRange(SplitList(value));
        }

        return string.Join(", ", items.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static List<string> SplitList(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim().Trim('"').Trim('\''))
            .Where(item => item.Length > 0)
            .ToList();

    private static string? FirstValue(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (fields.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string CollapseWhitespace(string value) =>
        WhitespaceRegex().Replace(value ?? string.Empty, " ").Trim();

    private static string EscapeToml(string value) =>
        (value ?? string.Empty).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static bool IsMarkdown(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrNested(string a, string b)
    {
        var fa = AppendSep(Path.GetFullPath(a));
        var fb = AppendSep(Path.GetFullPath(b));
        return fa.Equals(fb, StringComparison.OrdinalIgnoreCase)
               || fa.StartsWith(fb, StringComparison.OrdinalIgnoreCase)
               || fb.StartsWith(fa, StringComparison.OrdinalIgnoreCase);
    }

    private static string AppendSep(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var dir = Path.GetDirectoryName(path) ?? "";
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}-{i}{ext}");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException($"無法配置不衝突的檔名：{path}");
    }

    private static SiteMigrationResult Fail(string message) =>
        new() { Succeeded = false, Message = message };

    private static readonly Regex DatePrefixRegexValue = new(
        @"^(?<date>\d{4}-\d{2}-\d{2})-(?<rest>.+)$",
        RegexOptions.CultureInvariant);

    private static Regex DatePrefixRegex() => DatePrefixRegexValue;

    [GeneratedRegex(@"^(?<key>[A-Za-z0-9_-]+)\s*[:=]\s*(?<value>.+)$")]
    private static partial Regex SimpleConfigLineRegex();

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)\)")]
    private static partial Regex MarkdownImageRefRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    private readonly record struct SourceContentFile(
        string FullPath,
        string RelativePath,
        bool IsPost,
        bool IsDraftFolder);
}
