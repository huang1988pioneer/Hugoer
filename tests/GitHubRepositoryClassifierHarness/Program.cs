using Hugoer.Helpers;

Assert(GitHubRepositoryClassifier.LooksLikeHugo(["hugo.toml", "content", "themes"]),
    "hugo.toml must be recognized as a Hugo repository.");
Assert(GitHubRepositoryClassifier.LooksLikeHugo(["config.yaml", "archetypes"]),
    "config.yaml must be recognized as a Hugo repository.");
Assert(GitHubRepositoryClassifier.LooksLikeHugo(["content", "layouts", "static"]),
    "content + layouts must be recognized as a Hugo repository.");
Assert(!GitHubRepositoryClassifier.LooksLikeHugo(["README.md", "LICENSE"]),
    "A README starter must not look like Hugo.");
Assert(!GitHubRepositoryClassifier.LooksLikeHugo(["package.json", "src"]),
    "A Node project must not look like Hugo.");

Assert(GitHubRepositoryClassifier.CanReuseExisting(["hugo.toml", "content"]),
    "An existing Hugo repository may be reused by Create new repo.");
Assert(GitHubRepositoryClassifier.CanReuseExisting([]),
    "An empty repository may be reused.");
Assert(GitHubRepositoryClassifier.CanReuseExisting(["README.md", "LICENSE", ".gitignore"]),
    "GitHub's default starter files may be reused.");
Assert(GitHubRepositoryClassifier.CanReuseExisting(["README", ".github"]),
    "README without extension and .github may be reused.");
Assert(!GitHubRepositoryClassifier.CanReuseExisting(["package.json", "src"]),
    "An unrelated existing repository must not be auto-reused.");
Assert(!GitHubRepositoryClassifier.CanReuseExisting(["README.md", "app.py"]),
    "A mixed non-Hugo repository must not be auto-reused.");

Console.WriteLine("GITHUB_REPOSITORY_CLASSIFIER_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
