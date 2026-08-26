using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace Hugoer.Services;

public static partial class MarkdownPreviewService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseEmojiAndSmiley()
        // AdvancedExtensions enables the CommonMark/GFM extensions shipped by
        // Markdig (tables, task lists, footnotes, alerts, definition lists,
        // attributes, math, media links and more). These two opt-in extensions
        // complete the authoring experience without changing Markdown's normal
        // soft-line-break semantics.
        .UseSmartyPants()
        // Keep definition lists explicit: their parser is intentionally not
        // enabled by every Markdig AdvancedExtensions profile.
        .UseDefinitionLists()
        .Build();

    public static string StripFrontMatter(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        var body = markdown;
        while (FrontMatterRegex().Match(body) is { Success: true } match)
            body = body[match.Length..].TrimStart('\r', '\n');
        return body;
    }

    public static string ExtractFrontMatter(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        var m = FrontMatterRegex().Match(markdown);
        return m.Success ? m.Groups["frontMatter"].Value.Trim() : string.Empty;
    }

    public static string ToHtmlFragment(string markdown)
    {
        var bodyMd = StripFrontMatter(markdown);
        if (string.IsNullOrWhiteSpace(bodyMd))
            return string.Empty;
        bodyMd = ConvertMediaShortcodesToHtml(bodyMd);
        var html = Markdown.ToHtml(bodyMd, Pipeline).Trim();
        return EmbedMediaLinks(html);
    }

    public static string ToHtmlDocument(string markdown, string? title = null)
    {
        var bodyMd = ConvertMediaShortcodesToHtml(StripFrontMatter(markdown));
        var bodyHtml = EmbedMediaLinks(Markdown.ToHtml(bodyMd, Pipeline));
        var pageTitle = string.IsNullOrWhiteSpace(title) ? "Preview" : title;

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-Hant\"><head><meta charset=\"utf-8\"/>");
        sb.AppendLine($"<title>{System.Net.WebUtility.HtmlEncode(pageTitle)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(DarkPreviewCss);
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<article class=\"markdown-body\">");
        sb.AppendLine(bodyHtml);
        sb.AppendLine("</article></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Empty WebView shell for live preview. Call <c>hugoerSetPreview(html)</c> to replace the article.
    /// </summary>
    public static string PreviewShellDocument()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"zh-Hant\"><head><meta charset=\"utf-8\"/>");
        sb.AppendLine("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; img-src data: https: http: file: blob:; media-src data: https: http: file: blob:;\"/>");
        sb.AppendLine("<title>Preview</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(DarkPreviewCss);
        sb.AppendLine("#placeholder { display:block; color:#6b7785; font-style:italic; padding:20px 24px; }");
        sb.AppendLine("#content { display:none; }");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<p id=\"placeholder\">開始輸入 Markdown，預覽會即時更新。</p>");
        sb.AppendLine("<article id=\"content\" class=\"markdown-body\"></article>");
        sb.AppendLine("<script>");
        sb.AppendLine("window.hugoerMedia = {};");
        sb.AppendLine("function hugoerLookupMedia(src) {");
        sb.AppendLine("  if (!src) return null;");
        sb.AppendLine("  if (window.hugoerMedia[src]) return window.hugoerMedia[src];");
        sb.AppendLine("  try {");
        sb.AppendLine("    var decoded = decodeURI(src);");
        sb.AppendLine("    if (decoded !== src && window.hugoerMedia[decoded]) return window.hugoerMedia[decoded];");
        sb.AppendLine("  } catch (e) {}");
        sb.AppendLine("  return null;");
        sb.AppendLine("}");
        sb.AppendLine("function hugoerApplyMedia(root) {");
        sb.AppendLine("  if (!root) return;");
        sb.AppendLine("  root.querySelectorAll('img[src], audio[src], video[src], source[src], iframe[src], embed[src], object[data], a[href]').forEach(function (el) {");
        sb.AppendLine("    var attr = el.hasAttribute('src') ? 'src' : (el.hasAttribute('data') ? 'data' : 'href');");
        sb.AppendLine("    var raw = el.getAttribute(attr);");
        sb.AppendLine("    if (!raw || /^(data:|blob:|https?:|file:|#)/i.test(raw)) return;");
        sb.AppendLine("    var mapped = hugoerLookupMedia(raw);");
        sb.AppendLine("    if (!mapped) return;");
        sb.AppendLine("    el.setAttribute(attr, mapped);");
        sb.AppendLine("  });");
        sb.AppendLine("}");
        sb.AppendLine("window.hugoerSetPreview = function (html, media) {");
        sb.AppendLine("  if (media) {");
        sb.AppendLine("    var keys = Object.keys(media);");
        sb.AppendLine("    for (var i = 0; i < keys.length; i++) window.hugoerMedia[keys[i]] = media[keys[i]];");
        sb.AppendLine("  }");
        sb.AppendLine("  var content = document.getElementById('content');");
        sb.AppendLine("  var placeholder = document.getElementById('placeholder');");
        sb.AppendLine("  var scroller = document.scrollingElement || document.documentElement;");
        sb.AppendLine("  var top = scroller ? scroller.scrollTop : 0;");
        sb.AppendLine("  var empty = !html;");
        sb.AppendLine("  placeholder.style.display = empty ? 'block' : 'none';");
        sb.AppendLine("  content.style.display = empty ? 'none' : 'block';");
        sb.AppendLine("  var wrap = document.createElement('div');");
        sb.AppendLine("  wrap.innerHTML = html || '';");
        sb.AppendLine("  hugoerApplyMedia(wrap);");
        sb.AppendLine("  content.innerHTML = wrap.innerHTML;");
        sb.AppendLine("  if (scroller) scroller.scrollTop = top;");
        sb.AppendLine("};");
        sb.AppendLine("document.addEventListener('click', function (event) {");
        sb.AppendLine("  var link = event.target && event.target.closest ? event.target.closest('a[href]') : null;");
        sb.AppendLine("  if (link) event.preventDefault();");
        sb.AppendLine("});");
        sb.AppendLine("</script></body></html>");
        return sb.ToString();
    }

    public static string ToPlainPreviewHint(string markdown)
    {
        var body = StripFrontMatter(markdown).Trim();
        if (body.Length == 0)
            return "（空白預覽）";
        return body.Length > 2000 ? body[..2000] + "\n…" : body;
    }

    private const string DarkPreviewCss = """
:root { color-scheme: dark; }
html, body {
  margin: 0; padding: 0;
  background: #0d1218;
  color: #e6edf3;
  font-family: "Segoe UI", "Microsoft JhengHei", sans-serif;
  font-size: 15px;
  line-height: 1.65;
}
.markdown-body { padding: 20px 24px 40px; max-width: 820px; }
h1, h2, h3, h4, h5, h6 { color: #7cdaf9; margin-top: 1.4em; margin-bottom: 0.5em; font-weight: 650; }
h1 { font-size: 1.9em; border-bottom: 1px solid #2a3648; padding-bottom: 0.25em; }
h2 { font-size: 1.5em; border-bottom: 1px solid #243041; padding-bottom: 0.2em; }
h3 { font-size: 1.25em; }
p { margin: 0.75em 0; }
a { color: #5ec8f0; text-decoration: none; }
a:hover { text-decoration: underline; }
code {
  font-family: Consolas, "Cascadia Mono", monospace;
  background: #1a2330;
  padding: 0.15em 0.4em;
  border-radius: 4px;
  font-size: 0.92em;
}
pre {
  background: #121a24;
  border: 1px solid #2a3648;
  border-radius: 8px;
  padding: 12px 14px;
  overflow: auto;
}
pre code { background: transparent; padding: 0; }
blockquote {
  margin: 1em 0;
  padding: 0.4em 1em;
  border: 1px solid #28677a;
  border-radius: 6px;
  background: #151c26;
  color: #c5d0dc;
}
table { border-collapse: collapse; width: 100%; margin: 1em 0; }
th, td { border: 1px solid #2a3648; padding: 8px 10px; }
th { background: #1a2330; }
img { max-width: 100%; height: auto; border-radius: 6px; }
.markdown-body::after { content: ""; display: table; clear: both; }
audio, video { width: 100%; max-width: 640px; margin: 1em 0; }
hr { border: none; border-top: 1px solid #2a3648; margin: 1.5em 0; }
ul, ol { padding-left: 1.4em; }
li { margin: 0.25em 0; }
li > input[type="checkbox"] { margin-right: 0.45em; }
strong { color: #fff; }
del, s { color: #9aa8b7; }
ins { color: #a7e6c2; text-decoration: underline; text-decoration-color: #4f9f72; }
mark { background: #735b1e; color: #fff4c2; padding: 0.05em 0.2em; border-radius: 3px; }
sub, sup { color: #b9d9e8; font-size: 0.78em; }
kbd, samp { font-family: Consolas, "Cascadia Mono", monospace; background: #1a2330; border: 1px solid #3b4b61; border-radius: 4px; padding: 0.08em 0.35em; font-size: 0.88em; }
dl { margin: 1em 0; }
dt { color: #e6edf3; font-weight: 650; margin-top: 0.8em; }
dd { margin: 0.25em 0 0.7em 1.4em; color: #c5d0dc; }
figure { margin: 1em 0; }
figcaption { color: #9aa8b7; font-size: 0.9em; margin-top: 0.35em; }
details { border: 1px solid #2a3648; border-radius: 8px; padding: 0.65em 0.9em; margin: 1em 0; background: #121a24; }
summary { cursor: pointer; color: #7cdaf9; font-weight: 650; }
.math, .math-inline { color: #d8c7ff; font-family: "Cambria Math", "STIX Two Math", serif; }
.math-block { overflow-x: auto; padding: 0.7em 1em; border: 1px solid #2a3648; border-radius: 8px; background: #121a24; }
.markdown-alert { margin: 1em 0; padding: 0.7em 1em; border: 1px solid #2a3648; border-radius: 8px; background: #151c26; }
.markdown-alert-title { margin: 0 0 0.35em; color: #7cdaf9; font-weight: 650; }
.markdown-alert-note { border-color: #28677a; }
.markdown-alert-tip { border-color: #3d805d; }
.markdown-alert-important { border-color: #735ca8; }
.markdown-alert-warning { border-color: #8a6b2d; }
.markdown-alert-caution { border-color: #9a4d57; }
.hugoer-pdf-embed { margin: 1em 0; }
.hugoer-pdf-embed iframe { width: 100%; height: 640px; border: 1px solid #2a3648; border-radius: 8px; background: #fff; }
.hugoer-pdf-fallback { margin-top: 0.4em; font-size: 0.85em; color: #9aa8b7; }
.hugoer-pdf-fallback a { color: #5ec8f0; }
.footnotes { border-top: 1px solid #2a3648; margin-top: 1.5em; padding-top: 0.75em; color: #b7c4d0; font-size: 0.9em; }
.footnote-ref, .footnote-backref { color: #5ec8f0; }
pre code { display: block; white-space: pre; }
""";

    [GeneratedRegex(@"\A(?<delimiter>---|\+\+\+)(?:\s*\r?\n(?<frontMatter>.*?)\r?\n\k<delimiter>|\s+(?<frontMatter>.*?)\s+\k<delimiter>)\s*(?:\r?\n|\z)", RegexOptions.Singleline)]
    private static partial Regex FrontMatterRegex();
}
