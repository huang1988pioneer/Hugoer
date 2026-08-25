using System.Text;
using System.Text.RegularExpressions;

namespace Hugoer.Services;

/// <summary>
/// Converts engine-specific Markdown shortcodes / Liquid tags into CommonMark
/// that Hugo, Hexo, and Jekyll can all consume.
/// </summary>
public static partial class MarkdownEngineConverter
{
    public static string Convert(string body, string? assetUrlPrefix = null)
    {
        var text = body ?? string.Empty;
        text = HighlightBlockRegex().Replace(text, match => ToFence(match.Groups["lang"].Value, match.Groups["code"].Value));
        text = JekyllHighlightRegex().Replace(text, match => ToFence(match.Groups["lang"].Value, match.Groups["code"].Value));
        text = HexoCodeblockRegex().Replace(text, match => ToFence(ParseCodeblockLang(match.Groups["meta"].Value), match.Groups["code"].Value));
        text = NoticeBlockRegex().Replace(text, match => ToBlockquote(match.Groups["body"].Value));
        text = HexoBlockquoteRegex().Replace(text, match => ToBlockquote(match.Groups["body"].Value, match.Groups["author"].Value));
        text = FigureRegex().Replace(text, match => KeepIfEmpty(match.Value, ConvertFigure(match.Groups["attrs"].Value)));
        text = HugoImageRegex().Replace(text, match => KeepIfEmpty(match.Value, ConvertHugoImage(match.Groups["attrs"].Value)));
        text = HexoImgRegex().Replace(text, match => KeepIfEmpty(match.Value, ConvertHexoImg(match.Groups["body"].Value, assetUrlPrefix)));
        text = AssetImgRegex().Replace(text, match => KeepIfEmpty(match.Value, ConvertAssetImg(match.Groups["file"].Value, match.Groups["alt"].Value, assetUrlPrefix)));
        text = YoutubeShortcodeRegex().Replace(text, match => KeepIfEmpty(match.Value, ToVideoLink("YouTube", "https://www.youtube.com/watch?v=", match.Groups["id"].Value)));
        text = HexoYoutubeRegex().Replace(text, match => KeepIfEmpty(match.Value, ToVideoLink("YouTube", "https://www.youtube.com/watch?v=", match.Groups["id"].Value)));
        text = VimeoShortcodeRegex().Replace(text, match => KeepIfEmpty(match.Value, ToVideoLink("Vimeo", "https://vimeo.com/", match.Groups["id"].Value)));
        text = GistShortcodeRegex().Replace(text, match =>
        {
            var user = match.Groups["user"].Value.Trim();
            var id = match.Groups["id"].Value.Trim();
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(id))
                return match.Value;
            return $"[Gist](https://gist.github.com/{user}/{id})";
        });
        text = RelrefRegex().Replace(text, match => ToRelLink(match.Groups["path"].Value));
        text = IncludeTagRegex().Replace(text, string.Empty);
        text = RawBlockRegex().Replace(text, match => match.Groups["body"].Value);
        text = SiteBaseUrlRegex().Replace(text, string.Empty);
        text = SiteUrlRegex().Replace(text, string.Empty);
        text = MoreTagRegex().Replace(text, "<!--more-->");
        text = HtmlMoreRegex().Replace(text, "<!--more-->");
        if (!string.IsNullOrWhiteSpace(assetUrlPrefix))
            text = MarkdownImageRegex().Replace(text, match => RewriteRelativeImage(match, assetUrlPrefix));
        return text.TrimStart('\r', '\n');
    }

    public static IReadOnlyList<string> CollectWarnings(string body)
    {
        var text = body ?? string.Empty;
        var warnings = new List<string>();
        if (HexoOrLiquidTagRegex().IsMatch(text))
            warnings.Add("正文仍含 Hexo / Jekyll 標籤，請手動檢查。");
        if (HugoShortcodeRegex().IsMatch(text))
            warnings.Add("正文仍含 Hugo shortcode，請手動檢查。");
        if (LiquidOutputRegex().IsMatch(text))
            warnings.Add("正文仍含 Liquid 輸出，Hugo 可能無法解析。");
        return warnings;
    }

    private static string KeepIfEmpty(string original, string converted) =>
        string.IsNullOrWhiteSpace(converted) ? original : converted;

    private static string ConvertFigure(string attrs)
    {
        var map = ParseAttrs(attrs);
        if (!map.TryGetValue("src", out var src) && !map.TryGetValue("link", out src))
            return string.Empty;

        map.TryGetValue("alt", out var alt);
        if (string.IsNullOrWhiteSpace(alt))
            map.TryGetValue("title", out alt);
        var image = $"![{alt ?? string.Empty}]({src})";
        if (map.TryGetValue("caption", out var caption) && !string.IsNullOrWhiteSpace(caption))
            return $"{image}\n*{caption.Trim()}*";
        return image;
    }

    private static string ConvertHugoImage(string attrs)
    {
        var map = ParseAttrs(attrs);
        if (!map.TryGetValue("src", out var src) && !map.TryGetValue("srcset", out src))
            return string.Empty;
        map.TryGetValue("alt", out var alt);
        return $"![{alt ?? string.Empty}]({src})";
    }

    private static string ConvertHexoImg(string body, string? assetUrlPrefix)
    {
        var tokens = Tokenize(body);
        if (tokens.Count == 0)
            return string.Empty;

        string? src = null;
        string? alt = null;
        foreach (var token in tokens)
        {
            if (src is null && LooksLikePath(token))
            {
                src = token;
                continue;
            }

            if (LooksLikePath(token) || IsNumeric(token) || IsCssClass(token))
                continue;

            alt = token;
        }

        if (string.IsNullOrWhiteSpace(src))
            return string.Empty;

        src = QualifyAsset(src, assetUrlPrefix);
        return $"![{alt ?? string.Empty}]({src})";
    }

    private static string ConvertAssetImg(string file, string alt, string? assetUrlPrefix)
    {
        if (string.IsNullOrWhiteSpace(file))
            return string.Empty;

        var src = QualifyAsset(file.Trim(), assetUrlPrefix);
        return $"![{UnquoteToken(alt)}]({src})";
    }

    private static string RewriteRelativeImage(Match match, string assetUrlPrefix)
    {
        var url = match.Groups["url"].Value;
        if (string.IsNullOrWhiteSpace(url) || IsAbsoluteOrRooted(url))
            return match.Value;

        var alt = match.Groups["alt"].Value;
        return $"![{alt}]({QualifyAsset(url, assetUrlPrefix)})";
    }

    private static string QualifyAsset(string src, string? assetUrlPrefix)
    {
        src = src.Trim().Trim('"').Trim('\'');
        if (string.IsNullOrWhiteSpace(assetUrlPrefix) || IsAbsoluteOrRooted(src))
            return src;

        var file = src.Replace('\\', '/').TrimStart('.', '/');
        return $"{assetUrlPrefix.TrimEnd('/')}/{file}";
    }

    private static string ToFence(string lang, string code)
    {
        code = (code ?? string.Empty).Trim('\r', '\n');
        var fence = "```";
        while (code.Contains(fence, StringComparison.Ordinal))
            fence += "`";
        lang = (lang ?? string.Empty).Trim().Trim('"', '\'');
        return $"\n{fence}{lang}\n{code}\n{fence}\n";
    }

    private static string ToBlockquote(string body, string? author = null)
    {
        var lines = (body ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim()
            .Split('\n')
            .Select(line => "> " + line.TrimEnd());
        var text = string.Join('\n', lines);
        if (!string.IsNullOrWhiteSpace(author))
            text += $"\n>\n> — {UnquoteToken(author)}";
        return "\n" + text + "\n";
    }

    private static string ToVideoLink(string label, string prefix, string id)
    {
        id = UnquoteToken(id);
        if (string.IsNullOrWhiteSpace(id))
            return string.Empty;
        return $"[{label}]({prefix}{id})";
    }

    private static string ToRelLink(string path)
    {
        path = UnquoteToken(path).Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(path))
            return "/";
        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase))
            path = path[..path.LastIndexOf('.')];
        if (!path.StartsWith('/'))
            path = "/" + path;
        return path.TrimEnd('/') + "/";
    }

    private static string ParseCodeblockLang(string meta)
    {
        meta = (meta ?? string.Empty).Trim();
        if (meta.Length == 0)
            return string.Empty;

        var lang = LangMetaRegex().Match(meta);
        if (lang.Success)
            return lang.Groups[1].Value;

        var token = meta.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(token)
            || token.Equals("line_number", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("first_line", StringComparison.OrdinalIgnoreCase)
            || token.Equals("wrap", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return UnquoteToken(token);
    }

    private static Dictionary<string, string> ParseAttrs(string attrs)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttrRegex().Matches(attrs ?? string.Empty))
            map[match.Groups["key"].Value] = match.Groups["value"].Value;
        return map;
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        foreach (Match match in TokenRegex().Matches(text ?? string.Empty))
            tokens.Add(UnquoteToken(match.Value));
        return tokens;
    }

    private static string UnquoteToken(string value)
    {
        value = (value ?? string.Empty).Trim().Trim('"').Trim('\'');
        return value;
    }

    private static bool LooksLikePath(string token) =>
        token.Contains('/', StringComparison.Ordinal)
        || token.Contains('.', StringComparison.Ordinal)
        || token.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumeric(string token) =>
        token.Length > 0 && token.All(c => char.IsDigit(c) || c == '.');

    private static bool IsCssClass(string token) =>
        token.Equals("l", StringComparison.OrdinalIgnoreCase)
        || token.Equals("r", StringComparison.OrdinalIgnoreCase)
        || token.Equals("left", StringComparison.OrdinalIgnoreCase)
        || token.Equals("right", StringComparison.OrdinalIgnoreCase)
        || token.Equals("center", StringComparison.OrdinalIgnoreCase);

    private static bool IsAbsoluteOrRooted(string url) =>
        url.StartsWith('/')
        || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith('#');

    [GeneratedRegex(@"\{\{[<%]\s*highlight\s+(?<lang>[^\s%}]+).*?[%>]\}\}(?<code>.*?)\{\{[<%]\s*/highlight\s*[%>]\}\}", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HighlightBlockRegex();

    [GeneratedRegex(@"\{%\s*highlight\s+(?<lang>[^\s%]+).*?%\}(?<code>.*?)\{%\s*endhighlight\s*%\}", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex JekyllHighlightRegex();

    [GeneratedRegex(@"\{%\s*codeblock(?<meta>[^%]*)%\}(?<code>.*?)\{%\s*endcodeblock\s*%\}", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HexoCodeblockRegex();

    [GeneratedRegex(@"\{\{[<%]\s*(?:notice|admonition)\s+(?<kind>[^\s%>]*)[^%>]*[%>]\}\}(?<body>.*?)\{\{[<%]\s*/(?:notice|admonition)\s*[%>]\}\}", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex NoticeBlockRegex();

    [GeneratedRegex(@"\{%\s*blockquote(?<author>[^%]*)%\}(?<body>.*?)\{%\s*endblockquote\s*%\}", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HexoBlockquoteRegex();

    [GeneratedRegex(@"\{\{[<%]\s*figure\s+(?<attrs>.*?)\s*[%>]\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex FigureRegex();

    [GeneratedRegex(@"\{\{[<%]\s*img(?:age)?\s+(?<attrs>.*?)\s*[%>]\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex HugoImageRegex();

    [GeneratedRegex(@"\{%\s*img\s+(?<body>.*?)%\}", RegexOptions.IgnoreCase)]
    private static partial Regex HexoImgRegex();

    [GeneratedRegex(@"\{%\s*asset_img\s+(?<file>[^\s%]+)(?:\s+(?<alt>.*?))?\s*%\}", RegexOptions.IgnoreCase)]
    private static partial Regex AssetImgRegex();

    [GeneratedRegex(@"\{\{[<%]\s*youtube\s+(?:id=(?:[""'](?<id>[^""']+)[""']|(?<id>[^\s%>]+))|(?<id>[^\s%>]+))\s*[%>]\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex YoutubeShortcodeRegex();

    [GeneratedRegex(@"\{%\s*youtube\s+(?<id>[^\s%]+)\s*%\}", RegexOptions.IgnoreCase)]
    private static partial Regex HexoYoutubeRegex();

    [GeneratedRegex(@"\{\{[<%]\s*vimeo\s+(?:id=(?:[""'](?<id>[^""']+)[""']|(?<id>[^\s%>]+))|(?<id>[^\s%>]+))\s*[%>]\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex VimeoShortcodeRegex();

    [GeneratedRegex(@"\{\{[<%]\s*gist\s+(?<user>[^\s/>]+)\s+(?<id>[^\s%>]+)\s*[%>]\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex GistShortcodeRegex();

    [GeneratedRegex(@"\{\{[<%]\s*(?:relref|ref)\s+[""'](?<path>[^""']+)[""']\s*[%>]\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex RelrefRegex();

    [GeneratedRegex(@"\{%\s*include\s+[^%]+%\}", RegexOptions.IgnoreCase)]
    private static partial Regex IncludeTagRegex();

    [GeneratedRegex(@"\{%\s*raw\s*%\}(?<body>.*?)\{%\s*endraw\s*%\}", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RawBlockRegex();

    [GeneratedRegex(@"\{\{\s*site\.baseurl\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex SiteBaseUrlRegex();

    [GeneratedRegex(@"\{\{\s*site\.url\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex SiteUrlRegex();

    [GeneratedRegex(@"\{%\s*more\s*%\}", RegexOptions.IgnoreCase)]
    private static partial Regex MoreTagRegex();

    [GeneratedRegex(@"<!--\s*more\s*-->", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlMoreRegex();

    [GeneratedRegex(@"!\[(?<alt>[^\]]*)\]\((?<url>[^)\s]+)\)")]
    private static partial Regex MarkdownImageRegex();

    [GeneratedRegex(@"lang:([A-Za-z0-9_+\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex LangMetaRegex();

    [GeneratedRegex(@"(?<key>[A-Za-z0-9_-]+)\s*=\s*(?:[""'](?<value>[^""']*)[""']|(?<value>[^\s]+))")]
    private static partial Regex AttrRegex();

    [GeneratedRegex(@"[""'][^""']*[""']|[^\s]+")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"\{%\s*(?!more\s*%})[^%]+%\}")]
    private static partial Regex HexoOrLiquidTagRegex();

    [GeneratedRegex(@"\{\{[<%]")]
    private static partial Regex HugoShortcodeRegex();

    [GeneratedRegex(@"\{\{\s*[^<%>][^}]*\}\}")]
    private static partial Regex LiquidOutputRegex();
}
