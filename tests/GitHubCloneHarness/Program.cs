using Hugoer.Helpers;
using Hugoer.Models;

var github = GitHubRepositoryParser.Parse("https://github.com/octocat/hello-world");
Assert(github.IsValid, "github.com repository URL must parse.");
Assert(github.Owner == "octocat" && github.Repository == "hello-world", "owner/repo must be preserved.");
Assert(github.CanonicalUrl == "https://github.com/octocat/hello-world.git", "canonical git URL must be set.");
Assert(github.PagesUrl == "https://octocat.github.io/hello-world/", "project Pages URL must be derived.");
Assert(!github.IsUserOrOrganizationSite, "hello-world is not a user site.");

var gitSuffix = GitHubRepositoryParser.Parse("https://github.com/octocat/hello-world.git");
Assert(gitSuffix.IsValid && gitSuffix.Repository == "hello-world", ".git suffix must be stripped.");

var shortForm = GitHubRepositoryParser.Parse("octocat/hello-world");
Assert(shortForm.IsValid && shortForm.Owner == "octocat" && shortForm.Repository == "hello-world",
    "owner/repo shorthand must parse.");

var userSite = GitHubRepositoryParser.Parse("https://github.com/octocat/octocat.github.io");
Assert(userSite.IsValid && userSite.IsUserOrOrganizationSite, "owner.github.io repo is a user site.");
Assert(userSite.PagesUrl == "https://octocat.github.io/", "user site Pages URL has no extra path.");

var pagesUser = GitHubRepositoryParser.Parse("https://octocat.github.io/");
Assert(pagesUser.IsValid, "user GitHub Pages URL must parse.");
Assert(pagesUser.Owner == "octocat" && pagesUser.Repository == "octocat.github.io",
    "user Pages URL maps to owner/owner.github.io.");
Assert(pagesUser.IsUserOrOrganizationSite, "user Pages URL is a user/organization site.");

var pagesProject = GitHubRepositoryParser.Parse("https://octocat.github.io/hello-world/");
Assert(pagesProject.IsValid && pagesProject.Repository == "hello-world",
    "project Pages URL maps to owner/repo.");
Assert(pagesProject.PagesUrl == "https://octocat.github.io/hello-world/",
    "project Pages URL must be preserved.");

var pagesNoScheme = GitHubRepositoryParser.Parse("octocat.github.io/hello-world");
Assert(pagesNoScheme.IsValid && pagesNoScheme.Repository == "hello-world",
    "Pages host without scheme must parse.");

var pagesArticle = GitHubRepositoryParser.Parse("https://octocat.github.io/hello-world/posts/welcome/");
Assert(pagesArticle.IsValid && pagesArticle.Repository == "hello-world",
    "Pages article path still maps to the project repository.");

var httpPages = GitHubRepositoryParser.Parse("http://octocat.github.io/hello-world");
Assert(httpPages.IsValid && httpPages.CanonicalUrl == "https://github.com/octocat/hello-world.git",
    "http Pages URLs must upgrade to https clone targets.");

var empty = GitHubRepositoryParser.Parse(" ");
Assert(!empty.IsValid && empty.ErrorMessage.Contains("請貼上"), "empty input must be rejected.");

var gitlab = GitHubRepositoryParser.Parse("https://gitlab.com/octocat/hello-world");
Assert(gitlab.IsValid && gitlab.Provider == GitHostingProvider.GitLab, "gitlab.com repository URL must parse.");
Assert(gitlab.CanonicalUrl == "https://gitlab.com/octocat/hello-world.git",
    "GitLab canonical git URL must be set.");
Assert(gitlab.PagesUrl == "https://octocat.gitlab.io/hello-world/",
    "GitLab project Pages URL must be derived.");

var gitlabSubgroup = GitHubRepositoryParser.Parse("https://gitlab.com/engineering/docs/workflows");
Assert(gitlabSubgroup.IsValid && gitlabSubgroup.Owner == "engineering/docs" && gitlabSubgroup.Repository == "workflows",
    "GitLab subgroup repository URL must preserve namespace path.");
Assert(gitlabSubgroup.PagesUrl == "https://engineering.gitlab.io/docs/workflows/",
    "GitLab subgroup Pages URL must include subgroup path.");

var gitlabPages = GitHubRepositoryParser.Parse("https://engineering.gitlab.io/docs/workflows/");
Assert(gitlabPages.IsValid && gitlabPages.Provider == GitHostingProvider.GitLab,
    "GitLab Pages URL must parse.");
Assert(gitlabPages.Owner == "engineering/docs" && gitlabPages.Repository == "workflows",
    "GitLab Pages URL must map back to namespace/repository.");

var codeberg = GitHubRepositoryParser.Parse("https://codeberg.org/octocat/hello-world");
Assert(codeberg.IsValid && codeberg.Provider == GitHostingProvider.Codeberg,
    "Codeberg repository URL must parse.");
Assert(codeberg.CanonicalUrl == "https://codeberg.org/octocat/hello-world.git",
    "Codeberg canonical git URL must be set.");
Assert(codeberg.PagesUrl == "https://octocat.codeberg.page/hello-world/",
    "Codeberg repository Pages URL must be derived.");

var codebergUserPages = GitHubRepositoryParser.Parse("https://octocat.codeberg.page/");
Assert(codebergUserPages.IsValid && codebergUserPages.Repository == "pages",
    "Codeberg user Pages URL maps to owner/pages.");

var bitbucket = GitHubRepositoryParser.Parse("https://bitbucket.org/octocat/octocat.bitbucket.io");
Assert(bitbucket.IsValid && bitbucket.Provider == GitHostingProvider.Bitbucket,
    "Bitbucket repository URL must parse.");
Assert(bitbucket.IsUserOrOrganizationSite && bitbucket.PagesUrl == "https://octocat.bitbucket.io/",
    "Bitbucket workspace static site URL must be derived for workspace.bitbucket.io repository.");

var bitbucketSourcePage = GitHubRepositoryParser.Parse(
    "https://bitbucket.org/fengtusama/fengtusama.bitbucket.io/src/main/");
Assert(bitbucketSourcePage.IsValid && bitbucketSourcePage.Provider == GitHostingProvider.Bitbucket,
    "Bitbucket source page URL must normalize to its repository.");
Assert(bitbucketSourcePage.Owner == "fengtusama"
       && bitbucketSourcePage.Repository == "fengtusama.bitbucket.io",
    "Bitbucket source page URL must ignore the source branch path.");
Assert(bitbucketSourcePage.CanonicalUrl == "https://bitbucket.org/fengtusama/fengtusama.bitbucket.io.git",
    "Bitbucket source page URL must produce the canonical Git remote.");

var issues = GitHubRepositoryParser.Parse("https://github.com/octocat/hello-world/issues");
Assert(!issues.IsValid, "repository subpaths must be rejected.");

var pagesJson = """
[
  {"full_name":"octocat/blog","html_url":"https://github.com/octocat/blog","has_pages":true},
  {"full_name":"octocat/other","html_url":"https://github.com/octocat/other","has_pages":false}
]
[
  {"full_name":"octocat/blog","html_url":"https://github.com/octocat/blog","has_pages":true},
  {"full_name":"octocat/site","html_url":"https://github.com/octocat/site","has_pages":true}
]
""";
var listed = GitHubRepositoryParser.ParsePagesEnabledRepositories(pagesJson);
Assert(listed.Count == 2, "paginated GitHub repo JSON must keep Pages-enabled repos and drop duplicates.");
Assert(listed[0].NameWithOwner == "octocat/blog" && listed[1].NameWithOwner == "octocat/site",
    "Pages-enabled repository order must follow the API payload.");

var parent = Path.Combine(Path.GetTempPath(), "hugoer-clone-harness-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(parent);
try
{
    var destination = GitHubClonePath.TryGetDestination(parent, "hello-world", out var destError);
    Assert(destination is not null && string.IsNullOrEmpty(destError), "a normal destination must resolve.");
    Assert(destination == Path.GetFullPath(Path.Combine(parent, "hello-world")),
        "destination must be parent/repository.");
    Assert(GitHubClonePath.IsVacantDirectory(destination!), "missing destination is vacant.");

    Directory.CreateDirectory(destination!);
    Assert(GitHubClonePath.IsVacantDirectory(destination!), "empty destination is vacant.");
    File.WriteAllText(Path.Combine(destination!, "readme.txt"), "no");
    Assert(!GitHubClonePath.IsVacantDirectory(destination!), "non-empty destination is not vacant.");

    File.WriteAllText(Path.Combine(destination!, "index.html"), "<html></html>");
    Assert(GitHubClonePath.LooksLikeStaticPagesOutput(destination!),
        "index.html without Hugo config/content is static Pages output.");
    File.WriteAllText(Path.Combine(destination!, "hugo.toml"), "baseURL = '/'");
    Assert(!GitHubClonePath.LooksLikeStaticPagesOutput(destination!),
        "Hugo config must not be treated as static Pages output.");

    var missingParent = GitHubClonePath.TryGetDestination(" ", "hello-world", out var missingError);
    Assert(missingParent is null && missingError.Contains("請選擇"), "empty parent must be rejected.");

    var escape = GitHubClonePath.TryGetDestination(parent, "..", out var escapeError);
    Assert(escape is null && escapeError.Contains("超出"), "path traversal out of the parent folder must be rejected.");
}
finally
{
    try { Directory.Delete(parent, true); } catch { /* ignore */ }
}

Console.WriteLine("GITHUB_CLONE_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
