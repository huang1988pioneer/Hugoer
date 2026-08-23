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

var shell = MarkdownPreviewService.PreviewShellDocument();
AssertContains(shell, "hugoerSetPreview");
AssertContains(shell, "markdown-body");
AssertContains(shell, "開始輸入 Markdown，預覽會即時更新。");

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
