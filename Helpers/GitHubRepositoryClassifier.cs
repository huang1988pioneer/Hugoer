namespace Hugoer.Helpers;

public static class GitHubRepositoryClassifier
{
    private static readonly HashSet<string> HugoConfigFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "hugo.toml", "hugo.yaml", "hugo.yml", "hugo.json",
        "config.toml", "config.yaml", "config.yml", "config.json"
    };

    private static readonly HashSet<string> InitialRepoFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "readme", "readme.md", "readme.txt", "readme.rst",
        "license", "license.md", "license.txt", "licence", "licence.md",
        "copying", "copying.md",
        ".gitignore", ".gitattributes", ".gitmodules",
        "code_of_conduct.md", "contributing.md", "security.md"
    };

    public static bool LooksLikeHugo(IEnumerable<string> rootNames)
    {
        var names = Normalize(rootNames);
        if (names.Any(name => HugoConfigFiles.Contains(name)))
            return true;

        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        return set.Contains("content")
               && (set.Contains("archetypes")
                   || set.Contains("layouts")
                   || set.Contains("themes")
                   || set.Contains("config")
                   || set.Contains("static"));
    }

    /// <summary>
    /// Existing GitHub repos that "Create new repo" may safely reuse:
    /// a Hugo site, an empty repo, or GitHub's default README/license starter.
    /// </summary>
    public static bool CanReuseExisting(IEnumerable<string> rootNames)
    {
        var names = Normalize(rootNames);
        if (LooksLikeHugo(names)) return true;
        if (names.Count == 0) return true;

        return names.All(name =>
            InitialRepoFiles.Contains(name)
            || InitialRepoFiles.Contains(Path.GetFileNameWithoutExtension(name))
            || name.Equals(".github", StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> Normalize(IEnumerable<string> rootNames) =>
        rootNames
            .Select(name => name.Trim().TrimEnd('/', '\\'))
            .Where(name => name.Length > 0)
            .ToList();
}
