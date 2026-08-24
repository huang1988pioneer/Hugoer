using Hugoer.Helpers;
using Hugoer.Models;
using Tomlyn;
using Tomlyn.Model;

namespace Hugoer.Services;

public sealed class ContentService
{
    public IReadOnlyList<ContentItem> ListContent(string sitePath, string? relativeDir = null)
    {
        var contentRoot = PathHelper.ContentDir(sitePath);
        if (!Directory.Exists(contentRoot))
            return [];

        var dir = string.IsNullOrWhiteSpace(relativeDir)
            ? contentRoot
            : Path.GetFullPath(Path.Combine(contentRoot, relativeDir));

        if (!dir.StartsWith(Path.GetFullPath(contentRoot), StringComparison.OrdinalIgnoreCase))
            return [];

        if (!Directory.Exists(dir))
            return [];

        var items = new List<ContentItem>();

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

        return items;
    }

    public IReadOnlyList<ContentItem> ListAllMarkdown(string sitePath)
    {
        var contentRoot = PathHelper.ContentDir(sitePath);
        if (!Directory.Exists(contentRoot))
            return [];

        return Directory.EnumerateFiles(contentRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                        || f.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f =>
            {
                var metadata = ReadArticleMetadata(f);
                return new ContentItem
                {
                    FullPath = f,
                    RelativePath = Path.GetRelativePath(contentRoot, f).Replace('\\', '/'),
                    Name = Path.GetFileName(f),
                    IsDirectory = false,
                    LastWriteTime = File.GetLastWriteTime(f),
                    ArticleDate = metadata.Date,
                    ArticleTitle = metadata.Title,
                    IsDraft = metadata.IsDraft
                };
            })
            .ToList();
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

    private static ArticleMetadata ReadArticleMetadata(string fullPath)
    {
        try
        {
            var document = new FrontMatterService().Parse(File.ReadAllText(fullPath));
            var title = document.Fields.TryGetValue("title", out var parsedTitle) ? parsedTitle : string.Empty;
            var isDraft = document.Fields.TryGetValue("draft", out var parsedDraft)
                          && bool.TryParse(parsedDraft, out var draft)
                          && draft;
            var date = document.Fields.TryGetValue("date", out var parsedDate)
                       && DateTimeOffset.TryParse(parsedDate, out var articleDate)
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

        return new ArticleMetadata(string.Empty, null, false);
    }

    private readonly record struct ArticleMetadata(string Title, DateTimeOffset? Date, bool IsDraft);

    public async Task<string> ReadAsync(string fullPath, CancellationToken cancellationToken = default)
    {
        return await File.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(string fullPath, string content, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(fullPath, content, cancellationToken).ConfigureAwait(false);
    }

    public async Task CreateMarkdownAsync(
        string sitePath,
        string relativePath,
        string title,
        CancellationToken cancellationToken = default,
        string? slug = null)
    {
        var contentRoot = PathHelper.ContentDir(sitePath);
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            normalized += ".md";

        var full = Path.Combine(contentRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        if (File.Exists(full))
            throw new InvalidOperationException($"檔案已存在：{normalized}");

        var date = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK");
        var code = string.IsNullOrWhiteSpace(slug)
            ? Path.GetFileNameWithoutExtension(normalized)
            : slug.Trim();
        var body = $"""
---
title: "{title.Replace("\"", "\\\"")}"
date: {date}
slug: "{code.Replace("\"", "\\\"")}"
draft: true
---

開始寫作吧。
""";
        await File.WriteAllTextAsync(full, body, cancellationToken).ConfigureAwait(false);
    }

    public void Delete(string fullPath)
    {
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        else if (Directory.Exists(fullPath))
            Directory.Delete(fullPath, recursive: true);
    }
}
