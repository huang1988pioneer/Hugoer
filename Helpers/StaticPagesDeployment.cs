using Hugoer.Models;

namespace Hugoer.Helpers;

public static class StaticPagesDeployment
{
    public const string CodebergPagesBranch = "pages";
    public const string BitbucketWebsiteBranch = "main";

    public static bool ShouldPublishOutputBranch(GitHubRepositoryTarget target) =>
        OutputBranchFor(target) is not null;

    public static bool ShouldPushSourceBranch(GitHubRepositoryTarget target)
    {
        var outputBranch = OutputBranchFor(target);
        if (outputBranch is null)
            return true;

        var sourceBranch = ResolveSourceBranch(target, remoteHeadBranch: null);
        return !outputBranch.Equals(sourceBranch, StringComparison.OrdinalIgnoreCase);
    }

    public static string? OutputBranchFor(GitHubRepositoryTarget target)
    {
        if (!target.IsValid)
            return null;

        return target.Provider switch
        {
            GitHostingProvider.Codeberg => CodebergPagesBranch,
            GitHostingProvider.Bitbucket when target.IsUserOrOrganizationSite => BitbucketWebsiteBranch,
            _ => null
        };
    }

    public static string ResolveSourceBranch(GitHubRepositoryTarget target, string? remoteHeadBranch)
    {
        var branch = string.IsNullOrWhiteSpace(remoteHeadBranch) ? "main" : remoteHeadBranch.Trim();
        if (target.Provider == GitHostingProvider.Codeberg
            && branch.Equals(CodebergPagesBranch, StringComparison.OrdinalIgnoreCase))
        {
            return "main";
        }

        return branch;
    }

    public static bool TryFindOutputDirectory(
        string projectPath,
        out string outputDirectory,
        out string message)
    {
        var path = Path.Combine(projectPath, "public");
        if (Directory.Exists(path)
            && File.Exists(Path.Combine(path, "index.html")))
        {
            outputDirectory = path;
            message = string.Empty;
            return true;
        }

        outputDirectory = string.Empty;
        message = "找不到可發佈的靜態輸出。請先完成 Hugo 建置，並確認已產生 public/index.html。";
        return false;
    }
}
