using Hugoer.Services;

var heading = RoundTrip("# 標題\n\n段落文字");
AssertContains(heading, "# 標題");
AssertContains(heading, "段落文字");

var emphasis = RoundTrip("這是 **粗體**、*斜體* 與 ~~刪除~~。");
AssertContains(emphasis, "**粗體**");
AssertContains(emphasis, "*斜體*");
AssertContains(emphasis, "~~刪除~~");

var lists = RoundTrip("- 蘋果\n- 香蕉\n\n1. 第一\n2. 第二");
AssertContains(lists, "- 蘋果");
AssertContains(lists, "1. 第一");

var tasks = RoundTrip("- [ ] 未完成\n- [x] 已完成");
AssertContains(tasks, "[ ]");
AssertContains(tasks, "[x]");

var link = RoundTrip("[Hugo](https://gohugo.io) 與 ![圖](cover.jpg)");
AssertContains(link, "[Hugo](https://gohugo.io)");
AssertContains(link, "![圖](cover.jpg)");

var media = RoundTrip("<audio controls src=\"/music/song.mp3\"></audio>\n\n<video controls src=\"/video/clip.mp4\"></video>");
AssertContains(media, "<audio controls src=\"/music/song.mp3\"></audio>");
AssertContains(media, "<video controls src=\"/video/clip.mp4\"></video>");

var code = RoundTrip("行內 `var x`\n\n```csharp\nConsole.WriteLine(1);\n```");
AssertContains(code, "`var x`");
AssertContains(code, "```csharp");
AssertContains(code, "Console.WriteLine(1);");

var quote = RoundTrip("> 引用段落");
AssertContains(quote, "> 引用段落");

var table = RoundTrip("| 欄位一 | 欄位二 |\n| --- | --- |\n| 內容 | 內容 |");
AssertContains(table, "| 欄位一 | 欄位二 |");
AssertContains(table, "| 內容 | 內容 |");

var rule = RoundTrip("上段\n\n---\n\n下段");
AssertContains(rule, "---");

var shortcode = RoundTrip("開頭\n\n{{< figure src=\"a.jpg\" >}}\n\n結尾");
AssertContains(shortcode, "{{< figure src=\"a.jpg\" >}}");

var frontMatter = """
---
title: Demo
draft: true
---

正文 **Hello**
""";
var html = MarkdownWysiwygConverter.ToEditableHtml(frontMatter);
Assert(!html.Contains("title: Demo", StringComparison.Ordinal), "WYSIWYG HTML must not include front matter.");
var body = MarkdownWysiwygConverter.FromEditableHtml(html);
AssertContains(body, "**Hello**");
Assert(!body.Contains("title: Demo", StringComparison.Ordinal), "Converted markdown must stay body-only.");

var document = new FrontMatterService().Parse(frontMatter);
document.Body = body;
var written = new FrontMatterService().Write(document);
AssertContains(written, "title:");
AssertContains(written, "**Hello**");

var preserved = new FrontMatterService().ReplaceBody(frontMatter, "新的 **正文**");
AssertContains(preserved, "title: Demo");
AssertContains(preserved, "draft: true");
AssertContains(preserved, "新的 **正文**");
Assert(!preserved.Contains("正文 **Hello**", StringComparison.Ordinal), "ReplaceBody must replace the previous body.");

var previewBody = MarkdownPreviewService.StripFrontMatter(frontMatter);
AssertContains(previewBody, "**Hello**");
Assert(!previewBody.Contains("title: Demo", StringComparison.Ordinal), "Preview body must not include front matter.");

var rendered = MarkdownPreviewService.ToHtmlFragment("這是 **粗體** 與 *斜體*。");
AssertContains(rendered, "<strong>粗體</strong>");
AssertContains(rendered, "<em>斜體</em>");

var videoShortcode = MarkdownPreviewService.ToHtmlFragment(
    "{{< embed-video src=\"/videos/a.mp4\" width=\"100%\" >}}");
AssertContains(videoShortcode, "<video controls width=\"100%\" src=\"/videos/a.mp4\"></video>");

var audioShortcode = MarkdownPreviewService.ToHtmlFragment("{{< embed-audio src=\"/music/a.mp3\" >}}");
AssertContains(audioShortcode, "<audio controls src=\"/music/a.mp3\"></audio>");

var pdfShortcode = MarkdownPreviewService.ToHtmlFragment("{{< embed-pdf src=\"/pdf/a.pdf\" >}}");
AssertContains(pdfShortcode, "hugoer-pdf-embed");
AssertContains(pdfShortcode, "<iframe src=\"/pdf/a.pdf\" loading=\"lazy\"></iframe>");

var unrelatedShortcode = MarkdownPreviewService.ToHtmlFragment("{{< youtube id=\"abc123\" >}}");
AssertContains(unrelatedShortcode, "youtube");
AssertContains(unrelatedShortcode, "abc123");
Assert(!unrelatedShortcode.Contains("<video", StringComparison.Ordinal), "Unrelated shortcodes must not turn into a video tag.");

var imageAsVideo = MarkdownPreviewService.ToHtmlFragment("![影片](/videos/a.mp4)");
AssertContains(imageAsVideo, "<video");
AssertContains(imageAsVideo, "/videos/a.mp4");
Assert(!imageAsVideo.Contains("<img", StringComparison.Ordinal), imageAsVideo);

var imageAsPdf = MarkdownPreviewService.ToHtmlFragment("![文件](/pdf/a.pdf)");
AssertContains(imageAsPdf, "hugoer-pdf-embed");

var linkToPdf = MarkdownPreviewService.ToHtmlFragment("[說明文件](/pdf/manual.pdf)");
AssertContains(linkToPdf, "<a href=\"/pdf/manual.pdf\">說明文件</a>");
AssertContains(linkToPdf, "hugoer-pdf-embed");

var shell = MarkdownPreviewService.PreviewShellDocument();
AssertContains(shell, "hugoerSetPreview");
AssertContains(shell, "hugoerApplyMedia");
AssertContains(shell, "markdown-body");
AssertContains(shell, "開始輸入 Markdown，預覽會即時更新。");

var imageHtml = """<p><img src="data:image/png;base64,aaaa" alt="cat" data-hugoer-src="/image/cat.png"></p>""";
var restored = MarkdownWysiwygConverter.FromEditableHtml(imageHtml);
AssertContains(restored, "![cat](/image/cat.png)");
Assert(!restored.Contains("data:image", StringComparison.Ordinal), restored);

var plainImage = RoundTrip("![貓](/image/cat.png)");
AssertContains(plainImage, "![貓](/image/cat.png)");
Assert(!plainImage.Contains("<img", StringComparison.Ordinal), plainImage);

var sizedHtml = """<p><img src="/image/cat.png" alt="貓" width="320" data-hugoer-align="center" style="max-width:100%;height:auto;width:320px;display:block;margin:0.5em auto"></p>""";
var sized = MarkdownWysiwygConverter.FromEditableHtml(sizedHtml);
AssertContains(sized, "<img src=\"/image/cat.png\"");
AssertContains(sized, "alt=\"貓\"");
AssertContains(sized, "width=\"320\"");
AssertContains(sized, "data-hugoer-align=\"center\"");
AssertContains(sized, "margin:0.5em auto");
var sizedRound = RoundTrip(sized);
AssertContains(sizedRound, "width=\"320\"");
AssertContains(sizedRound, "data-hugoer-align=\"center\"");

var wrapHtml = """<p><img src="/image/cat.png" alt="cat" width="240" data-hugoer-align="wrap-left"></p>""";
var wrap = MarkdownWysiwygConverter.FromEditableHtml(wrapHtml);
AssertContains(wrap, "data-hugoer-align=\"wrap-left\"");
AssertContains(wrap, "float:left");
AssertContains(wrap, "width=\"240\"");

Console.WriteLine("MARKDOWN_WYSIWYG_HARNESS_OK");
Console.WriteLine(heading);
Console.WriteLine("---");
Console.WriteLine(tasks);
Console.WriteLine("---");
Console.WriteLine(code);
Console.WriteLine("---");
Console.WriteLine(shortcode);

static string RoundTrip(string markdown)
{
    var html = MarkdownWysiwygConverter.ToEditableHtml(markdown);
    return MarkdownWysiwygConverter.FromEditableHtml(html);
}

static void AssertContains(string actual, string expected)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
        throw new InvalidOperationException($"Expected to contain `{expected}` but got:\n{actual}");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
