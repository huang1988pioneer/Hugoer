using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Hugoer.Services;

public enum MediaKind
{
    Image,
    Music,
    Voice,
    Video,
    Pdf,
    Document,
    File
}

public sealed record MediaAsset(
    MediaKind Kind,
    string Folder,
    string DestinationPath,
    string PublicUrl,
    string DisplayName,
    string Markdown,
    string PreviewHtml);

/// <summary>
/// Copies media into the Hugo site <c>static/</c> tree so public URLs match
/// <c>/image</c>, <c>/music</c>, <c>/voice</c>, <c>/video</c>, <c>/pdf</c>, and similar folders.
/// </summary>
public static partial class MediaAssetService
{
    public const string StaticDirectoryName = "static";

    public static string FolderName(MediaKind kind) => kind switch
    {
        MediaKind.Image => "image",
        MediaKind.Music => "music",
        MediaKind.Voice => "voice",
        MediaKind.Video => "video",
        MediaKind.Pdf => "pdf",
        MediaKind.Document => "doc",
        _ => "file"
    };

    public static MediaKind Classify(string path, MediaKind? forced = null)
    {
        if (forced is { } kind && kind != MediaKind.File)
            return kind;

        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" or ".jfif" or ".png" or ".gif" or ".webp" or ".svg"
                or ".avif" or ".bmp" or ".ico" or ".heic" or ".heif" or ".tif" or ".tiff"
                => MediaKind.Image,
            ".mp4" or ".webm" or ".mov" or ".mkv" or ".avi" or ".m4v" or ".ogv"
                => MediaKind.Video,
            ".pdf" => MediaKind.Pdf,
            ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg" or ".wma" or ".aiff" or ".aif"
                => MediaKind.Music,
            ".m4a" or ".opus" or ".weba" or ".caf" or ".amr"
                => MediaKind.Voice,
            ".doc" or ".docx" or ".odt" or ".rtf" or ".xls" or ".xlsx" or ".ods"
                or ".ppt" or ".pptx" or ".odp" or ".csv"
                => MediaKind.Document,
            _ => MediaKind.File
        };
    }

    public static IReadOnlyList<MediaAsset> ImportMany(
        string sitePath,
        IEnumerable<string> sourcePaths,
        MediaKind? forcedKind = null)
    {
        if (string.IsNullOrWhiteSpace(sitePath))
            throw new ArgumentException("網站路徑不可為空。", nameof(sitePath));

        var results = new List<MediaAsset>();
        foreach (var source in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                continue;
            results.Add(Import(sitePath, source, forcedKind));
        }

        return results;
    }

    public static MediaAsset Import(string sitePath, string sourcePath, MediaKind? forcedKind = null)
    {
        if (string.IsNullOrWhiteSpace(sitePath))
            throw new ArgumentException("網站路徑不可為空。", nameof(sitePath));
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("找不到要上傳的檔案。", sourcePath);

        var kind = Classify(sourcePath, forcedKind);
        var folder = FolderName(kind);
        var staticDir = Path.GetFullPath(Path.Combine(sitePath, StaticDirectoryName));
        var destDir = Path.GetFullPath(Path.Combine(staticDir, folder));
        if (!destDir.StartsWith(staticDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("媒體資料夾必須位於 static/ 內。");

        Directory.CreateDirectory(destDir);

        var sourceFull = Path.GetFullPath(sourcePath);
        var safeName = SanitizeFileName(Path.GetFileName(sourceFull));
        var destPath = Path.Combine(destDir, safeName);

        if (PathsEqual(sourceFull, destPath))
            return CreateAsset(kind, folder, destPath, staticDir, Path.GetFileName(sourceFull));

        if (TryReuseExistingStaticFile(sourceFull, staticDir, destDir, kind, folder, out var reused))
            return reused;

        destPath = UniquePath(destDir, safeName);
        File.Copy(sourceFull, destPath, overwrite: false);
        return CreateAsset(kind, folder, destPath, staticDir, Path.GetFileName(sourceFull));
    }

    public static string JoinMarkdown(IReadOnlyList<MediaAsset> assets) =>
        string.Join("\n\n", assets.Select(asset => asset.Markdown));

    public static string JoinPreviewHtml(IReadOnlyList<MediaAsset> assets) =>
        string.Join("", assets.Select(asset => asset.PreviewHtml));

    public static string ToPreviewHtml(string html, string? sitePath)
    {
        if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(sitePath))
            return html;

        var staticDir = Path.GetFullPath(Path.Combine(sitePath, StaticDirectoryName));
        return SrcHrefRegex().Replace(html, match =>
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (!TryMapSiteUrlToFile(url, staticDir, out var fileUrl))
                return match.Value;
            return $"{match.Groups["attr"].Value}={match.Groups["quote"].Value}{WebUtility.HtmlEncode(fileUrl)}{match.Groups["quote"].Value}";
        });
    }

    public static string FromPreviewHtml(string html, string? sitePath)
    {
        if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(sitePath))
            return html;

        var staticDir = Path.GetFullPath(Path.Combine(sitePath, StaticDirectoryName));
        return SrcHrefRegex().Replace(html, match =>
        {
            var url = WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (!TryMapFileUrlToSite(url, staticDir, out var siteUrl))
                return match.Value;
            return $"{match.Groups["attr"].Value}={match.Groups["quote"].Value}{WebUtility.HtmlEncode(siteUrl)}{match.Groups["quote"].Value}";
        });
    }

    public static string BuildMarkdown(MediaKind kind, string publicUrl, string displayName)
    {
        var label = string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileName(publicUrl)
            : displayName;
        return kind switch
        {
            MediaKind.Image => $"![{EscapeMarkdownLabel(Path.GetFileNameWithoutExtension(label))}]({publicUrl})",
            MediaKind.Music or MediaKind.Voice =>
                $"<audio controls src=\"{publicUrl}\"></audio>",
            MediaKind.Video => $"<video controls src=\"{publicUrl}\"></video>",
            _ => $"[{EscapeMarkdownLabel(label)}]({publicUrl})"
        };
    }

    public static string BuildPreviewHtml(MediaKind kind, string previewUrl, string displayName)
    {
        var encodedUrl = WebUtility.HtmlEncode(previewUrl);
        var encodedAlt = WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(displayName)
            ? Path.GetFileName(previewUrl)
            : displayName);
        return kind switch
        {
            MediaKind.Image => $"<img src=\"{encodedUrl}\" alt=\"{WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(displayName))}\"/>",
            MediaKind.Music or MediaKind.Voice =>
                $"<audio controls src=\"{encodedUrl}\"></audio>",
            MediaKind.Video => $"<video controls src=\"{encodedUrl}\"></video>",
            _ => $"<a href=\"{encodedUrl}\">{encodedAlt}</a>"
        };
    }

    public static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "file";

        var name = fileName.Replace("..", "-", StringComparison.Ordinal);
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (ch < 32 || invalid.Contains(ch) || ch is '/' or '\\' or ':')
                builder.Append('-');
            else
                builder.Append(ch);
        }

        var cleaned = builder.ToString().Trim(' ', '.');
        return string.IsNullOrWhiteSpace(cleaned) ? "file" : cleaned;
    }

    private static MediaAsset CreateAsset(
        MediaKind kind,
        string folder,
        string destinationPath,
        string staticDir,
        string displayName)
    {
        var publicUrl = ToPublicUrl(staticDir, destinationPath);
        var previewUrl = new Uri(destinationPath).AbsoluteUri;
        return new MediaAsset(
            kind,
            folder,
            destinationPath,
            publicUrl,
            displayName,
            BuildMarkdown(kind, publicUrl, displayName),
            BuildPreviewHtml(kind, previewUrl, displayName));
    }

    private static bool TryReuseExistingStaticFile(
        string sourceFull,
        string staticDir,
        string destDir,
        MediaKind kind,
        string folder,
        out MediaAsset asset)
    {
        asset = null!;
        if (!IsUnder(sourceFull, staticDir))
            return false;

        if (IsUnder(sourceFull, destDir))
        {
            asset = CreateAsset(kind, folder, sourceFull, staticDir, Path.GetFileName(sourceFull));
            return true;
        }

        return false;
    }

    private static string UniquePath(string directory, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var dest = Path.Combine(directory, fileName);
        var index = 1;
        while (File.Exists(dest))
        {
            dest = Path.Combine(directory, $"{stem}-{index}{ext}");
            index++;
        }

        return dest;
    }

    private static string ToPublicUrl(string staticDir, string destinationPath)
    {
        var relative = Path.GetRelativePath(staticDir, destinationPath).Replace('\\', '/');
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);
        return "/" + string.Join('/', parts);
    }

    public static bool TryMapSiteUrlToFile(string url, string staticDir, out string fileUrl)
    {
        fileUrl = string.Empty;
        if (!TryGetSiteRelativePath(url, out var relative))
            return false;

        var combined = Path.GetFullPath(Path.Combine(staticDir, relative));
        if (!IsUnder(combined, staticDir) && !PathsEqual(combined, staticDir))
            return false;

        fileUrl = new Uri(combined).AbsoluteUri;
        return true;
    }

    public static bool TryMapFileUrlToSite(string url, string staticDir, out string siteUrl)
    {
        siteUrl = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeFile)
            return false;

        var local = Path.GetFullPath(uri.LocalPath);
        if (!IsUnder(local, staticDir))
            return false;

        siteUrl = ToPublicUrl(staticDir, local);
        return true;
    }

    private static bool TryGetSiteRelativePath(string url, out string relative)
    {
        relative = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || url.Contains("://", StringComparison.Ordinal))
            return false;

        var trimmed = url.Trim();
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!trimmed.StartsWith('/'))
            return false;

        var cut = trimmed.IndexOfAny(['?', '#']);
        if (cut >= 0)
            trimmed = trimmed[..cut];

        relative = Uri.UnescapeDataString(trimmed.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar);
        return relative.Length > 0;
    }

    private static bool IsUnder(string path, string root)
    {
        var full = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string EscapeMarkdownLabel(string label) =>
        label.Replace("]", "\\]", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    [GeneratedRegex(@"(?<attr>\b(?:src|href))\s*=\s*(?<quote>['""])(?<url>[^'""]+)\k<quote>", RegexOptions.IgnoreCase)]
    private static partial Regex SrcHrefRegex();
}
