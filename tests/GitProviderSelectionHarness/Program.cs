using Hugoer.Helpers;
using Hugoer.Models;

Assert(
    !GitProviderSelectionPolicy.ShouldAdoptDetectedProvider(
        providerWasSelectedByUser: true,
        detectedProvider: GitHostingProvider.GitHub,
        activeProvider: GitHostingProvider.GitLab),
    "A manually selected GitLab provider must not be replaced by GitHub during refresh.");

Assert(
    GitProviderSelectionPolicy.ShouldAdoptDetectedProvider(
        providerWasSelectedByUser: false,
        detectedProvider: GitHostingProvider.GitLab,
        activeProvider: GitHostingProvider.GitHub),
    "Initial refresh may adopt the detected provider when the user has not selected one.");

Assert(
    !GitProviderSelectionPolicy.ShouldAdoptDetectedProvider(
        providerWasSelectedByUser: false,
        detectedProvider: null,
        activeProvider: GitHostingProvider.GitHub),
    "A refresh without a detected provider must keep the active provider.");

var summary = GitProviderStatusFormatter.BuildRemoteSummary(
    new GitRemoteInfo
    {
        Provider = GitHostingProvider.GitHub,
        Owner = "fengtusama",
        Repo = "fengtusama.github.io",
        RemoteUrl = "https://github.com/fengtusama/fengtusama.github.io.git",
        Branch = "main",
        GhAuthenticated = true,
        GhUser = "huang1988pioneer"
    },
    activeProvider: GitHostingProvider.GitLab,
    providerWasSelectedByUser: true,
    selectedTarget: new GitHubRepositoryTarget
    {
        IsValid = true,
        Provider = GitHostingProvider.GitLab,
        Owner = "group5923835",
        Repository = "fengtusama.gitlab.io",
        CanonicalUrl = "https://gitlab.com/group5923835/fengtusama.gitlab.io.git",
        PagesUrl = "https://group5923835.gitlab.io/fengtusama.gitlab.io/"
    },
    providerAccount: "group5923835");

Assert(summary.Contains("平台：GitLab", StringComparison.Ordinal), "Manual GitLab selection must be the displayed platform.");
Assert(!summary.Contains("平台：GitHub", StringComparison.Ordinal), "Manual GitLab selection must not display GitHub as the active platform.");
Assert(summary.Contains("Repository：group5923835/fengtusama.gitlab.io", StringComparison.Ordinal), "Manual GitLab target must be displayed.");

var existingGitLabOrigin = GitHubRepositoryParser.Parse("https://gitlab.com/group5923835/fengtusama.gitlab.io.git");
var pagesUrlWrongNamespace = GitHubRepositoryParser.Parse("https://fengtusama.gitlab.io/");
Assert(
    existingGitLabOrigin.IsValid && pagesUrlWrongNamespace.IsValid,
    "Both the existing GitLab origin and the pasted Pages URL must parse.");
Assert(
    !GitRemoteSafety.IsSameRepository(existingGitLabOrigin, pagesUrlWrongNamespace),
    "A GitLab Pages URL from a different namespace must not be treated as the same repository.");
var mismatch = GitRemoteSafety.BuildMismatchMessage(
    "https://gitlab.com/group5923835/fengtusama.gitlab.io.git",
    pagesUrlWrongNamespace);
Assert(
    mismatch.Contains("不會自動改寫 origin", StringComparison.Ordinal),
    "Origin mismatch must stop with an explicit no-auto-retarget message.");
Assert(
    mismatch.Contains("fengtusama/fengtusama.gitlab.io", StringComparison.Ordinal),
    "Mismatch message must show the unsafe target that would have caused HTTP 403.");

var fetchFirstOutput = """
To https://gitlab.com/group5923835/fengtusama.gitlab.io.git
 ! [rejected]        HEAD -> main (fetch first)
error: failed to push some refs to 'https://gitlab.com/group5923835/fengtusama.gitlab.io.git'
hint: Updates were rejected because the remote contains work that you do not
hint: have locally.
""";
Assert(
    GitPushFailureClassifier.IsNonFastForwardRejection(fetchFirstOutput),
    "GitLab fetch-first push rejection must be classified for automatic fetch/merge retry.");
Assert(
    GitPushFailureClassifier.ToUserMessage(fetchFirstOutput).Contains("安全合併遠端內容", StringComparison.Ordinal),
    "Fetch-first rejection must produce an actionable Hugoer message.");

var permissionOutput = """
remote: The project you were looking for could not be found or you don't have permission to view it.
fatal: repository 'https://gitlab.com/group5923835/fengtusama.gitlab.io.git/' not found
""";
Assert(
    GitPushFailureClassifier.LooksLikeMissingPushPermission(permissionOutput),
    "GitLab permission/not-found push failure must be classified separately from fetch-first.");

var oldGitLabWorkflow = """
default:
  image: "hugomods/hugo:exts"

create-pages:
  script:
    - hugo --gc --minify
""";
Assert(
    GitLabPagesWorkflowPolicy.ShouldRewrite(oldGitLabWorkflow),
    "GitLab CI using the floating hugomods/hugo image must be rewritten to the pinned Hugo workflow.");

var pinnedGitLabWorkflow = """
image: alpine:latest

variables:
  HUGO_VERSION: "0.164.0"

pages:
  script:
    - hugo --gc --minify --baseURL "${CI_PAGES_URL}/"
""";
Assert(
    !GitLabPagesWorkflowPolicy.ShouldRewrite(pinnedGitLabWorkflow),
    "Pinned GitLab CI workflow for Hugo 0.164.0 must be kept.");

Console.WriteLine("GIT_PROVIDER_SELECTION_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
