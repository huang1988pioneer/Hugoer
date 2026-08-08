using Hugoer.Helpers;
using Hugoer.Models;

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
            .Select(f => new ContentItem
            {
                FullPath = f,
                RelativePath = Path.GetRelativePath(contentRoot, f).Replace('\\', '/'),
                Name = Path.GetFileName(f),
                IsDirectory = false,
                LastWriteTime = File.GetLastWriteTime(f)
            })
            .ToList();
    }

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
        CancellationToken cancellationToken = default)
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
        var body = $"""
---
title: "{title.Replace("\"", "\\\"")}"
date: {date}
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
