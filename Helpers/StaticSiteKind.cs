namespace Hugoer.Helpers;

public enum StaticSiteKind
{
    Unknown,
    Hugo,
    Hexo,
    Jekyll
}

public static class StaticSiteDetector
{
    public static StaticSiteKind Detect(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return StaticSiteKind.Unknown;

        var hexoPosts = Path.Combine(path, "source", "_posts");
        var jekyllPosts = Path.Combine(path, "_posts");
        var hasHexoPosts = Directory.Exists(hexoPosts) && HasMarkdown(hexoPosts);
        var hasJekyllPosts = Directory.Exists(jekyllPosts) && HasMarkdown(jekyllPosts);
        var hasHugo = PathHelper.LooksLikeHugoSite(path);

        if (hasHugo && !hasHexoPosts && !hasJekyllPosts)
            return StaticSiteKind.Hugo;
        if (hasHexoPosts && !hasHugo)
            return StaticSiteKind.Hexo;
        if (hasJekyllPosts && !hasHugo)
            return StaticSiteKind.Jekyll;
        if (hasHugo)
            return StaticSiteKind.Hugo;
        if (hasHexoPosts)
            return StaticSiteKind.Hexo;
        if (hasJekyllPosts)
            return StaticSiteKind.Jekyll;

        var hasConfigYaml = File.Exists(Path.Combine(path, "_config.yml"))
                            || File.Exists(Path.Combine(path, "_config.yaml"));
        if (hasConfigYaml && Directory.Exists(Path.Combine(path, "source")) && MentionsHexo(path))
            return StaticSiteKind.Hexo;
        if (hasConfigYaml && MentionsJekyll(path))
            return StaticSiteKind.Jekyll;

        return StaticSiteKind.Unknown;
    }

    public static string DisplayName(StaticSiteKind kind) => kind switch
    {
        StaticSiteKind.Hugo => "Hugo",
        StaticSiteKind.Hexo => "Hexo",
        StaticSiteKind.Jekyll => "Jekyll",
        _ => "未知"
    };

    public static StaticSiteKind Parse(string? text) => (text ?? string.Empty).Trim() switch
    {
        "Hugo" => StaticSiteKind.Hugo,
        "Hexo" => StaticSiteKind.Hexo,
        "Jekyll" => StaticSiteKind.Jekyll,
        _ => StaticSiteKind.Unknown
    };

    public static string PostsDirectory(string sitePath, StaticSiteKind kind, bool drafts) => kind switch
    {
        StaticSiteKind.Hexo => Path.Combine(sitePath, "source", drafts ? "_drafts" : "_posts"),
        StaticSiteKind.Jekyll => Path.Combine(sitePath, drafts ? "_drafts" : "_posts"),
        StaticSiteKind.Hugo => Path.Combine(PathHelper.ContentDir(sitePath), "post"),
        _ => Path.Combine(sitePath, drafts ? "_drafts" : "_posts")
    };

    public static string PagesDirectory(string sitePath, StaticSiteKind kind) => kind switch
    {
        StaticSiteKind.Hexo => Path.Combine(sitePath, "source"),
        StaticSiteKind.Jekyll => sitePath,
        StaticSiteKind.Hugo => PathHelper.ContentDir(sitePath),
        _ => sitePath
    };

    public static string AssetDirectory(string sitePath, StaticSiteKind kind, string slug) => kind switch
    {
        StaticSiteKind.Hugo => Path.Combine(sitePath, "static", "images", slug),
        StaticSiteKind.Hexo => Path.Combine(sitePath, "source", "images", slug),
        StaticSiteKind.Jekyll => Path.Combine(sitePath, "assets", "images", slug),
        _ => Path.Combine(sitePath, "images", slug)
    };

    public static string AssetUrlPrefix(StaticSiteKind kind, string slug) => kind switch
    {
        StaticSiteKind.Jekyll => $"/assets/images/{slug}",
        _ => $"/images/{slug}"
    };

    private static bool HasMarkdown(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
                .Any(file => file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                             || file.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool MentionsHexo(string sitePath)
    {
        var packageJson = Path.Combine(sitePath, "package.json");
        if (!File.Exists(packageJson))
            return Directory.Exists(Path.Combine(sitePath, "scaffolds"));

        try
        {
            return File.ReadAllText(packageJson).Contains("\"hexo\"", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool MentionsJekyll(string sitePath)
    {
        var gemfile = Path.Combine(sitePath, "Gemfile");
        if (!File.Exists(gemfile))
            return false;

        try
        {
            return File.ReadAllText(gemfile).Contains("jekyll", StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
