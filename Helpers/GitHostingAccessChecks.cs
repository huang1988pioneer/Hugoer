using Hugoer.Models;

namespace Hugoer.Helpers;

public static class GitHostingAccessChecks
{
    public static string LsRemoteHeadArguments(GitHubRepositoryTarget target) =>
        $"ls-remote --symref {Quote(target.CanonicalUrl!)} HEAD";

    public static string PushDryRunArguments(string remoteBranch, string remoteName = "origin") =>
        $"push --dry-run -u {remoteName} HEAD:\"{remoteBranch}\"";

    public static (bool HasAccess, string Message) FromLsRemoteResult(
        GitHubRepositoryTarget target,
        CommandResult result)
    {
        if (result.Succeeded)
        {
            return (true,
                $"已確認 {target.ProviderName} repository 可由本機 Git 存取；接著會在程式內安全 fetch、合併並推送。");
        }

        var hinted = GitHostingProcessErrors.WithRepositoryAccessHint(
            target.Provider,
            "存取",
            result);
        return (false, hinted.CombinedOutput);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
