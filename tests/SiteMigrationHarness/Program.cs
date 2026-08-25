using Hugoer.Helpers;
using Hugoer.Services;

var root = Path.Combine(Path.GetTempPath(), "HugoerSiteMigrationTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var service = new SiteMigrationService();
    var frontMatter = new FrontMatterService();

    var convertedBody = MarkdownEngineConverter.Convert("""
        {{< figure src="/image/a.jpg" alt="Hero" caption="A cat" >}}
        {% more %}
        {% codeblock js %}
        console.log(1)
        {% endcodeblock %}
        {% highlight python %}
        print(1)
        {% endhighlight %}
        {{< youtube dQw4w9WgXcQ >}}
        {% img /image/b.jpg "Bee" %}
        {{< relref "post.md" >}}
        """);
    Assert(convertedBody.Contains("![Hero](/image/a.jpg)", StringComparison.Ordinal), "figure → markdown image");
    Assert(convertedBody.Contains("*A cat*", StringComparison.Ordinal), "figure caption");
    Assert(convertedBody.Contains("<!--more-->", StringComparison.Ordinal), "hexo more → hugo more");
    Assert(convertedBody.Contains("```js", StringComparison.Ordinal), "hexo codeblock → fence");
    Assert(convertedBody.Contains("```python", StringComparison.Ordinal), "jekyll highlight → fence");
    Assert(convertedBody.Contains("https://www.youtube.com/watch?v=dQw4w9WgXcQ", StringComparison.Ordinal), "youtube");
    Assert(convertedBody.Contains("![Bee](/image/b.jpg)", StringComparison.Ordinal), "hexo img");
    Assert(convertedBody.Contains("/post/", StringComparison.Ordinal), "relref");
    Assert(!convertedBody.Contains("{{<", StringComparison.Ordinal), "hugo shortcodes removed");

    var hugoPost = """
        +++
        title = "Hello World"
        date = "2026-08-21T10:00:00+08:00"
        draft = true
        slug = "hello-world"
        tags = ["demo", "migrate"]
        categories = ["news"]
        image = "/image/cover.jpg"
        description = "A sample"
        +++

        {{< figure src="/image/cover.jpg" alt="Cover" >}}

        Hello.
        """;
    var hexoDoc = service.ConvertDocument(hugoPost, "content/post/hello-world.md", StaticSiteKind.Hugo, StaticSiteKind.Hexo);
    Assert(hexoDoc.IsDraft, "hugo draft → hexo draft");
    Assert(hexoDoc.FileName == "hello-world.md", $"draft hexo filename {hexoDoc.FileName}");
    Assert(hexoDoc.RelativeDirectory == "source/_drafts", hexoDoc.RelativeDirectory);
    Assert(hexoDoc.Markdown.Contains("published: false", StringComparison.Ordinal), "hexo published false");
    Assert(hexoDoc.Markdown.Contains("cover:", StringComparison.Ordinal), "hexo cover from image");
    Assert(hexoDoc.Markdown.Contains("![Cover](/image/cover.jpg)", StringComparison.Ordinal), "shortcode converted in export");
    Assert(!hexoDoc.Markdown.Contains("draft:", StringComparison.OrdinalIgnoreCase), "hexo should not keep hugo draft key");

    var jekyllDoc = service.ConvertDocument(
        hugoPost.Replace("draft = true", "draft = false", StringComparison.Ordinal),
        "content/post/hello-world.md",
        StaticSiteKind.Hugo,
        StaticSiteKind.Jekyll);
    Assert(!jekyllDoc.IsDraft, "published hugo post");
    Assert(jekyllDoc.FileName == "2026-08-21-hello-world.md", jekyllDoc.FileName);
    Assert(jekyllDoc.RelativeDirectory == "_posts", jekyllDoc.RelativeDirectory);
    Assert(jekyllDoc.Markdown.Contains("layout: \"post\"", StringComparison.Ordinal)
           || jekyllDoc.Markdown.Contains("layout: post", StringComparison.Ordinal), "jekyll layout");
    Assert(jekyllDoc.Markdown.Contains("published: true", StringComparison.Ordinal), "jekyll published");

    var hexoSource = """
        ---
        title: From Hexo
        date: 2024-01-02 09:30:00
        categories:
          - tech
          - csharp
        tags:
          - hugo
        cover: /images/hexo.jpg
        published: false
        ---

        {% more %}

        Body from Hexo.
        """;
    var fromHexo = service.ConvertDocument(hexoSource, "source/_posts/2024-01-02-from-hexo.md", StaticSiteKind.Hexo, StaticSiteKind.Hugo);
    Assert(fromHexo.IsDraft, "hexo published false → hugo draft");
    Assert(fromHexo.FileName == "from-hexo.md", fromHexo.FileName);
    Assert(fromHexo.RelativeDirectory == "content/post", fromHexo.RelativeDirectory);
    Assert(fromHexo.Markdown.Contains("draft: true", StringComparison.Ordinal), "hugo draft true");
    Assert(fromHexo.Markdown.Contains("image:", StringComparison.Ordinal), "cover → image");
    Assert(fromHexo.Markdown.Contains("<!--more-->", StringComparison.Ordinal), "more converted");
    var parsedHugo = frontMatter.Parse(fromHexo.Markdown);
    Assert(parsedHugo.Fields["categories"].Contains("tech", StringComparison.Ordinal), "categories preserved");
    Assert(parsedHugo.Fields["tags"].Contains("hugo", StringComparison.Ordinal), "tags preserved");

    var jekyllSource = """
        ---
        title: From Jekyll
        date: 2023-05-06 12:00:00 +0800
        published: true
        tags: [alpha]
        excerpt: Hello excerpt
        ---

        {% highlight csharp %}
        var x = 1;
        {% endhighlight %}
        """;
    var fromJekyll = service.ConvertDocument(jekyllSource, "_posts/2023-05-06-from-jekyll.md", StaticSiteKind.Jekyll, StaticSiteKind.Hugo);
    Assert(!fromJekyll.IsDraft, "jekyll published");
    Assert(fromJekyll.FileName == "from-jekyll.md", fromJekyll.FileName);
    Assert(fromJekyll.Markdown.Contains("```csharp", StringComparison.Ordinal), "highlight converted");
    Assert(fromJekyll.Markdown.Contains("description:", StringComparison.Ordinal), "excerpt → description");

    var aboutPage = service.ConvertDocument("""
        ---
        title: About
        ---

        About the site.
        """, "source/about/index.md", StaticSiteKind.Hexo, StaticSiteKind.Hugo, isPost: false);
    Assert(aboutPage.RelativeDirectory == "content/about", aboutPage.RelativeDirectory);
    Assert(aboutPage.FileName == "index.md", aboutPage.FileName);
    Assert(!aboutPage.IsPost, "about is a page");

    var hugoSite = Path.Combine(root, "hugo-site");
    CreateHugoSite(hugoSite);
    Assert(service.Detect(hugoSite) == StaticSiteKind.Hugo, "detect hugo");

    var hexoSite = Path.Combine(root, "hexo-site");
    CreateHexoSite(hexoSite);
    Assert(service.Detect(hexoSite) == StaticSiteKind.Hexo, "detect hexo");

    var jekyllSite = Path.Combine(root, "jekyll-site");
    CreateJekyllSite(jekyllSite);
    Assert(service.Detect(jekyllSite) == StaticSiteKind.Jekyll, "detect jekyll");

    var hexoToHugo = Path.Combine(root, "hexo-to-hugo");
    var hexoResult = service.Migrate(hexoSite, hexoToHugo, StaticSiteKind.Hexo, StaticSiteKind.Hugo);
    Assert(hexoResult.Succeeded, hexoResult.Message);
    Assert(hexoResult.PostCount >= 1, "hexo posts migrated");
    Assert(File.Exists(Path.Combine(hexoToHugo, "hugo.toml")), "hugo.toml written");
    var migratedHexoPost = Directory.EnumerateFiles(Path.Combine(hexoToHugo, "content", "post"), "*.md").First();
    var migratedHexoText = File.ReadAllText(migratedHexoPost);
    Assert(migratedHexoText.Contains("title:", StringComparison.Ordinal), migratedHexoText);
    Assert(File.Exists(Path.Combine(hexoToHugo, "static", "images", "photo.png"))
           || File.Exists(Path.Combine(hexoToHugo, "static", "images", "photo-1.png")),
        "hexo source image copied to hugo static");

    var hugoToHexo = Path.Combine(root, "hugo-to-hexo");
    var toHexo = service.Migrate(hugoSite, hugoToHexo, StaticSiteKind.Hugo, StaticSiteKind.Hexo);
    Assert(toHexo.Succeeded, toHexo.Message);
    Assert(File.Exists(Path.Combine(hugoToHexo, "_config.yml")), "hexo config");
    Assert(Directory.Exists(Path.Combine(hugoToHexo, "source", "_posts"))
           || Directory.Exists(Path.Combine(hugoToHexo, "source", "_drafts")),
        "hexo post folders");
    var hexoOutFiles = Directory.EnumerateFiles(hugoToHexo, "*.md", SearchOption.AllDirectories).ToList();
    Assert(hexoOutFiles.Count >= 1, "hexo received markdown");
    Assert(hexoOutFiles.Any(path => File.ReadAllText(path).Contains("published:", StringComparison.Ordinal)),
        "hexo published field");
    Assert(File.Exists(Path.Combine(hugoToHexo, "source", "image", "cover.png")),
        "hugo static copied into hexo source");

    var hugoToJekyll = Path.Combine(root, "hugo-to-jekyll");
    var toJekyll = service.Migrate(hugoSite, hugoToJekyll, StaticSiteKind.Hugo, StaticSiteKind.Jekyll);
    Assert(toJekyll.Succeeded, toJekyll.Message);
    Assert(File.Exists(Path.Combine(hugoToJekyll, "_config.yml")), "jekyll config");
    Assert(toJekyll.PostCount >= 1, "jekyll posts");
    var jekyllPost = Directory.EnumerateFiles(Path.Combine(hugoToJekyll, "_posts"), "*.md").Concat(
        Directory.Exists(Path.Combine(hugoToJekyll, "_drafts"))
            ? Directory.EnumerateFiles(Path.Combine(hugoToJekyll, "_drafts"), "*.md")
            : []).First();
    var jekyllText = File.ReadAllText(jekyllPost);
    Assert(jekyllText.Contains("layout:", StringComparison.Ordinal), "jekyll layout");

    var jekyllToHugo = Path.Combine(root, "jekyll-to-hugo");
    var fromJekyllSite = service.Migrate(jekyllSite, jekyllToHugo, StaticSiteKind.Jekyll, StaticSiteKind.Hugo);
    Assert(fromJekyllSite.Succeeded, fromJekyllSite.Message);
    Assert(fromJekyllSite.PostCount >= 1, "jekyll post migrated");
    Assert(File.Exists(Path.Combine(jekyllToHugo, "hugo.toml")), "jekyll → hugo config");

    var exportDir = Path.Combine(root, "export-empty");
    Directory.CreateDirectory(exportDir);
    var export = service.ExportArticles(
        hugoSite,
        [
            new ArticleExportInput
            {
                FullPath = Path.Combine(hugoSite, "content", "post", "hello.md"),
                RelativePath = "post/hello.md"
            }
        ],
        StaticSiteKind.Jekyll,
        exportDir);
    Assert(export.Succeeded, export.Message);
    Assert(export.PostCount == 1, "exported one article");
    Assert(Directory.EnumerateFiles(exportDir, "*.md", SearchOption.AllDirectories).Any(), "export wrote markdown");

    var same = service.Migrate(hugoSite, hugoSite, StaticSiteKind.Hugo, StaticSiteKind.Hexo);
    Assert(!same.Succeeded, "refuse nested/same destination");

    Console.WriteLine("SITE_MIGRATION_HARNESS_OK");
}
finally
{
    try
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
    catch (IOException)
    {
        // Temp cleanup is best-effort.
    }
}

static void CreateHugoSite(string path)
{
    Directory.CreateDirectory(Path.Combine(path, "content", "post"));
    Directory.CreateDirectory(Path.Combine(path, "static", "image"));
    File.WriteAllText(Path.Combine(path, "hugo.toml"), """
        baseURL = "https://example.org/"
        title = "Hugo Demo"
        languageCode = "zh-tw"
        """);
    File.WriteAllText(Path.Combine(path, "content", "post", "hello.md"), """
        ---
        title: "Hello Hugo"
        date: 2026-08-21T10:00:00+08:00
        slug: "hello"
        draft: false
        tags: ["demo"]
        image: "/image/cover.png"
        ---

        {{< figure src="/image/cover.png" alt="Cover" >}}

        Hugo body.
        """);
    File.WriteAllText(Path.Combine(path, "static", "image", "cover.png"), "png");
}

static void CreateHexoSite(string path)
{
    Directory.CreateDirectory(Path.Combine(path, "source", "_posts"));
    Directory.CreateDirectory(Path.Combine(path, "source", "images"));
    File.WriteAllText(Path.Combine(path, "_config.yml"), """
        title: Hexo Demo
        url: https://hexo.example.org
        language: zh-TW
        """);
    File.WriteAllText(Path.Combine(path, "package.json"), """{ "dependencies": { "hexo": "7.0.0" } }""");
    File.WriteAllText(Path.Combine(path, "source", "_posts", "2024-03-01-hexo-hello.md"), """
        ---
        title: Hexo Hello
        date: 2024-03-01 08:00:00
        categories:
          - journal
        tags:
          - hexo
        cover: /images/photo.png
        ---

        {% more %}

        Hexo body.
        """);
    File.WriteAllText(Path.Combine(path, "source", "images", "photo.png"), "png");
}

static void CreateJekyllSite(string path)
{
    Directory.CreateDirectory(Path.Combine(path, "_posts"));
    Directory.CreateDirectory(Path.Combine(path, "assets", "images"));
    File.WriteAllText(Path.Combine(path, "_config.yml"), """
        title: Jekyll Demo
        url: https://jekyll.example.org
        baseurl: ""
        """);
    File.WriteAllText(Path.Combine(path, "Gemfile"), "gem \"jekyll\"");
    File.WriteAllText(Path.Combine(path, "_posts", "2022-04-05-jekyll-hello.md"), """
        ---
        title: Jekyll Hello
        date: 2022-04-05 11:00:00 +0800
        tags: [jekyll]
        ---

        {% highlight ruby %}
        puts "hi"
        {% endhighlight %}
        """);
    File.WriteAllText(Path.Combine(path, "assets", "images", "pic.png"), "png");
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
