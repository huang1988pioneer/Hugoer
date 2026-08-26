using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Hugoer.Services;

/// <summary>
/// Best-effort conversion of common Hugo shortcodes and plain Markdown links that
/// reference non-image media (video / audio / PDF) into real HTML so the live
/// preview can actually play or display them instead of showing raw shortcode
/// text or a broken image icon. This only affects the preview HTML; the saved
/// Markdown content is untouched.
/// </summary>
public static partial class MarkdownPreviewService
{
    /// <summary>
    /// Rewrites self-closing Hugo shortcodes such as
    /// <c>{{&lt; embed-video src="/videos/a.mp4" width="100%" &gt;}}</c> into raw HTML
    /// before the Markdown is handed to Markdig, so the tag survives untouched and
    /// renders (Markdig passes raw HTML blocks straight through).
    /// Shortcodes that aren't recognised as video / audio / pdf / figure media are
    /// left exactly as-is.
    /// </summary>
    private static string ConvertMediaShortcodesToHtml(string markdown) =>
        MediaShortcodeRegex().Replace(markdown, match =>
        {
            var name = match.Groups["name"].Value;
            var attrs = ParseShortcodeAttributes(match.Groups["attrs"].Value);
            if (!attrs.TryGetValue("src", out var src) || string.IsNullOrWhiteSpace(src))
                return match.Value;

            if (name.Contains("video", StringComparison.OrdinalIgnoreCase))
                return BuildVideoHtml(src, attrs);
            if (name.Contains("audio", StringComparison.OrdinalIgnoreCase)
                || name.Contains("voice", StringComparison.OrdinalIgnoreCase)
                || name.Contains("sound", StringComparison.OrdinalIgnoreCase))
                return BuildAudioHtml(src);
            if (name.Contains("pdf", StringComparison.OrdinalIgnoreCase))
                return BuildPdfEmbedHtml(src);
            if (name.Equals("figure", StringComparison.OrdinalIgnoreCase))
                return BuildFigureHtml(src, attrs);

            return match.Value;
        });

    /// <summary>
    /// After Markdig has produced HTML, upgrade plain links/images that point at
    /// video, audio, or PDF files into an actual playable/embedded element so the
    /// preview matches what a reader would experience, while keeping a fallback
    /// link for opening the original file.
    /// </summary>
    private static string EmbedMediaLinks(string html)
    {
        html = MediaImgTagRegex().Replace(html, match =>
        {
            var attrs = ParseShortcodeAttributes(match.Groups["attrs"].Value);
            if (!attrs.TryGetValue("src", out var src) || string.IsNullOrWhiteSpace(src))
                return match.Value;

            return MediaAssetService.Classify(src) switch
            {
                MediaKind.Video => BuildVideoHtml(src, attrs),
                MediaKind.Music or MediaKind.Voice => BuildAudioHtml(src),
                MediaKind.Pdf => BuildPdfEmbedHtml(src),
                _ => match.Value
            };
        });

        html = StandalonePdfLinkRegex().Replace(html, match =>
        {
            var attrs = ParseShortcodeAttributes(match.Groups["attrs"].Value);
            if (!attrs.TryGetValue("href", out var href) || MediaAssetService.Classify(href) != MediaKind.Pdf)
                return match.Value;

            return match.Value + BuildPdfEmbedHtml(href);
        });

        return html;
    }

    private static string BuildVideoHtml(string src, IReadOnlyDictionary<string, string> attrs)
    {
        var sb = new StringBuilder("<video controls");
        AppendSizeAttributes(sb, attrs);
        sb.Append(" src=\"").Append(WebUtility.HtmlEncode(src)).Append("\"></video>");
        return sb.ToString();
    }

    private static string BuildAudioHtml(string src) =>
        $"<audio controls src=\"{WebUtility.HtmlEncode(src)}\"></audio>";

    private static string BuildFigureHtml(string src, IReadOnlyDictionary<string, string> attrs)
    {
        var alt = attrs.TryGetValue("alt", out var a) ? a : attrs.GetValueOrDefault("title", string.Empty);
        var img = $"<img src=\"{WebUtility.HtmlEncode(src)}\" alt=\"{WebUtility.HtmlEncode(alt)}\"/>";
        return attrs.TryGetValue("caption", out var caption) && !string.IsNullOrWhiteSpace(caption)
            ? $"<figure>{img}<figcaption>{WebUtility.HtmlEncode(caption)}</figcaption></figure>"
            : img;
    }

    private static string BuildPdfEmbedHtml(string src)
    {
        var encodedSrc = WebUtility.HtmlEncode(src);
        var fileName = WebUtility.HtmlEncode(PdfFileNameFor(src));
        return "<div class=\"hugoer-pdf-embed\">"
            + $"<iframe src=\"{encodedSrc}\" loading=\"lazy\"></iframe>"
            + $"<p class=\"hugoer-pdf-fallback\"><a href=\"{encodedSrc}\" target=\"_blank\" rel=\"noopener\">在新視窗開啟 PDF：{fileName}</a></p>"
            + "</div>";
    }

    private static string PdfFileNameFor(string src)
    {
        var withoutQuery = src.Split('?', '#')[0];
        var slash = withoutQuery.LastIndexOf('/');
        return slash >= 0 ? withoutQuery[(slash + 1)..] : withoutQuery;
    }

    private static void AppendSizeAttributes(StringBuilder sb, IReadOnlyDictionary<string, string> attrs)
    {
        if (attrs.TryGetValue("width", out var width) && !string.IsNullOrWhiteSpace(width))
            sb.Append(" width=\"").Append(WebUtility.HtmlEncode(width)).Append('"');
        if (attrs.TryGetValue("height", out var height) && !string.IsNullOrWhiteSpace(height))
            sb.Append(" height=\"").Append(WebUtility.HtmlEncode(height)).Append('"');
    }

    private static Dictionary<string, string> ParseShortcodeAttributes(string attrs)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ShortcodeAttributeRegex().Matches(attrs))
        {
            var value = match.Groups["dq"].Success ? match.Groups["dq"].Value : match.Groups["sq"].Value;
            map[match.Groups["key"].Value] = WebUtility.HtmlDecode(value);
        }

        return map;
    }

    // Matches self-closing Hugo shortcodes, e.g. {{< embed-video src="/a.mp4" width="100%" >}}
    [GeneratedRegex(@"\{\{[<%]\s*(?<name>[\w-]+)(?<attrs>(?:\s+[\w:-]+\s*=\s*(?:""[^""]*""|'[^']*'))*)\s*/?\s*[%>]\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex MediaShortcodeRegex();

    [GeneratedRegex(@"(?<key>[\w:-]+)\s*=\s*(?:""(?<dq>[^""]*)""|'(?<sq>[^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex ShortcodeAttributeRegex();

    [GeneratedRegex(@"<img\b(?<attrs>[^>]*)/?>", RegexOptions.IgnoreCase)]
    private static partial Regex MediaImgTagRegex();

    [GeneratedRegex(@"<p>\s*<a\b(?<attrs>[^>]*)>(?<text>.*?)</a>\s*</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StandalonePdfLinkRegex();
}
