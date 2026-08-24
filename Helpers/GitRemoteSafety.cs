using Hugoer.Models;

namespace Hugoer.Helpers;

public static class GitRemoteSafety
{
    public static bool IsSameRepository(GitHubRepositoryTarget existing, GitHubRepositoryTarget target) =>
        existing.IsValid
        && target.IsValid
        && existing.Provider == target.Provider
        && string.Equals(existing.Owner, target.Owner, StringComparison.OrdinalIgnoreCase)
        && string.Equals(existing.Repository, target.Repository, StringComparison.OrdinalIgnoreCase);

    public static string BuildMismatchMessage(string existingRemote, GitHubRepositoryTarget target)
    {
        var targetText = target.IsValid
            ? $"{target.ProviderName}：{target.Owner}/{target.Repository}"
            : target.ErrorMessage;

        return
            $"本機 origin 已指向其他 repository：{existingRemote}。\n" +
            $"你目前選擇的是 {targetText}。\n" +
            "為避免推送到錯誤或沒有權限的 GitLab/Git repository，Hugoer 不會自動改寫 origin。請確認貼上的 repository URL 是否正確；若要換遠端，請先在 Git 中手動修改 origin。";
    }
}
