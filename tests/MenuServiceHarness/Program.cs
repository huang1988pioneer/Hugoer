using Hugoer.Models;
using Hugoer.Services;

var toml = """
theme = "Stack"
baseURL = "https://example.org/"
title = "Demo"

[[menu.main]]
identifier = "home"
name = "Home"
url = "/"
weight = 1

[menu.main.params]
icon = "home"

[[menu.main]]
identifier = "archives"
name = "Archives"
url = "/archives/"
weight = 2

[params]
mainSections = ["post"]
""";

var parsed = MenuService.ParseTomlMenus(toml, dedicatedFile: false);
Assert(parsed.Count == 2, $"Expected 2 config menus, got {parsed.Count}");
Assert(parsed[0].Identifier == "home", "home identifier");
Assert(parsed[0].Icon == "home", "home icon from params");
Assert(parsed[1].Name == "Archives", "archives name");

var frontMatter = """
---
title: "Archives"
layout: "archives"
slug: "archives"
menu:
  main:
    weight: 2
    params:
      icon: archives
---
""";

var fromPage = MenuService.ParseFrontMatterMenus(frontMatter, "archives/_index.md");
Assert(fromPage.Count == 1, "front matter should yield one menu entry");
Assert(fromPage[0].MenuName == "main", "front matter menu name");
Assert(fromPage[0].Weight == 2, "front matter weight");
Assert(fromPage[0].Icon == "archives", "front matter icon");

fromPage[0].Identifier = "archives";
fromPage[0].Url = MenuService.UrlFromContentPath("archives/_index.md");
var merged = MenuService.MergeEntries(parsed, fromPage);
Assert(merged.Count == 2, $"merged should dedupe archives, got {merged.Count}");
var archives = merged.Single(item => item.Identifier == "archives");
Assert(archives.Icon == "archives", "merged archives should keep front matter icon");
Assert(archives.Source == MenuEntrySource.Config, "config entry should win");

var stripped = MenuService.RemoveMenuFromFrontMatter(frontMatter);
Assert(!stripped.Contains("menu:", StringComparison.OrdinalIgnoreCase), "menu block must be removed");
Assert(stripped.Contains("layout: \"archives\"", StringComparison.Ordinal), "other front matter must remain");

var rendered = MenuService.RenderMenuToml(merged, "menu", dedicatedFile: false);
var replaced = MenuService.ReplaceMenuSpan(toml, rendered);
Assert(replaced.Contains("theme = \"Stack\"", StringComparison.Ordinal), "theme must be preserved");
Assert(replaced.Contains("[params]", StringComparison.Ordinal), "params must be preserved");
Assert(replaced.Contains("icon = \"archives\"", StringComparison.Ordinal), "merged icon must be written");
Assert(!replaced.Contains("[menu]\n", StringComparison.Ordinal), "bare [menu] table is optional");

Assert(MenuService.UrlFromContentPath("about/index.md") == "/about/", "about url");
Assert(MenuService.UrlFromContentPath("search/index.md") == "/search/", "search url");
Assert(MenuService.UrlFromContentPath("post/hello-world.md") == "/post/hello-world/", "post url");

var sections = new[] { "post", "posts" };
Assert(ContentService.IsArticle("post/hello-world.md", sections), "post file is an article");
Assert(ContentService.IsArticle("post/hello/index.md", sections), "page bundle is an article");
Assert(!ContentService.IsArticle("post/_index.md", sections), "section index is not an article");
Assert(!ContentService.IsArticle("about/index.md", sections), "about is not an article");
Assert(!ContentService.IsArticle("archives/_index.md", sections), "archives is not an article");
Assert(!ContentService.IsArticle("search/index.md", sections), "search is not an article");

var temp = Path.Combine(Path.GetTempPath(), "HugoerMenuHarness-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(temp, "content", "post"));
Directory.CreateDirectory(Path.Combine(temp, "content", "archives"));
Directory.CreateDirectory(Path.Combine(temp, "content", "search"));
File.WriteAllText(Path.Combine(temp, "hugo.toml"), toml);
File.WriteAllText(Path.Combine(temp, "content", "post", "hello-world.md"), """
---
title: "Hello"
date: 2026-08-21
draft: false
---

Hi
""");
File.WriteAllText(Path.Combine(temp, "content", "archives", "_index.md"), frontMatter);
File.WriteAllText(Path.Combine(temp, "content", "search", "index.md"), """
---
title: "Search"
layout: "search"
menu:
  main:
    weight: 3
    params:
      icon: search
---
""");

var service = new MenuService();
var loaded = service.Load(temp);
Assert(loaded.Entries.Count == 3, $"load should merge home/archives/search, got {loaded.Entries.Count}");
Assert(loaded.ImportedFromFrontMatter >= 1, "should see front matter menus");
var contentService = new ContentService();
Assert(contentService.ListArticles(temp).Count == 1, "only hello-world is an article");
Assert(contentService.ListSitePages(temp).Count == 2, "archives and search are pages");
Assert(contentService.ListContent(temp, "../outside").Count == 0, "content browser must reject traversal");

var escapedTitlePath = Path.Combine(temp, "content", "post", "safe.md");
await contentService.CreateMarkdownAsync(
    temp,
    "post/safe.md",
    "Title\nInjected",
    slug: "safe\nslug");
var escapedTitleBody = File.ReadAllText(escapedTitlePath);
Assert(escapedTitleBody.Contains("title: \"Title Injected\"", StringComparison.Ordinal), "front matter title must stay on one line");
Assert(escapedTitleBody.Contains("slug: \"safe slug\"", StringComparison.Ordinal), "front matter slug must stay on one line");
File.Delete(escapedTitlePath);

var traversalRejected = false;
try
{
    await contentService.CreateMarkdownAsync(temp, "../escape.md", "Should not be created");
}
catch (ArgumentException)
{
    traversalRejected = true;
}
Assert(traversalRejected, "article creation must reject paths outside content/");

service.Save(temp, loaded, loaded.Entries);
var savedConfig = File.ReadAllText(Path.Combine(temp, "hugo.toml"));
Assert(savedConfig.Contains("identifier = \"search\"", StringComparison.Ordinal), "search should be written to config");
var savedArchives = File.ReadAllText(Path.Combine(temp, "content", "archives", "_index.md"));
Assert(!savedArchives.Contains("menu:", StringComparison.OrdinalIgnoreCase), "archives front matter menu must be stripped on save");
var savedSearch = File.ReadAllText(Path.Combine(temp, "content", "search", "index.md"));
Assert(!savedSearch.Contains("menu:", StringComparison.OrdinalIgnoreCase), "search front matter menu must be stripped on save");
Assert(savedSearch.Contains("layout: \"search\"", StringComparison.Ordinal), "search layout must remain");

Directory.Delete(temp, recursive: true);
Console.WriteLine("MENU_SERVICE_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
