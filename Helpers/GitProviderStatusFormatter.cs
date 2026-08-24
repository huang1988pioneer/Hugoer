using Hugoer.Models;

namespace Hugoer.Helpers;

public static class GitProviderStatusFormatter
{
    public static string BuildRemoteSummary(
        GitRemoteInfo info,
        GitHostingProvider activeProvider,
        bool providerWasSelectedByUser,
        GitHubRepositoryTarget? selectedTarget,
        string? providerAccount)
    {
        if (providerWasSelectedByUser
            && selectedTarget is { IsValid: true }
            && selectedTarget.Provider == activeProvider)
        {
            var account = string.IsNullOrWhiteSpace(providerAccount)
                ? selectedTarget.Owner
                : providerAccount.Trim();
            var originNote = BuildOriginNote(info, selectedTarget);

            return
                $"平台：{activeProvider.DisplayName()}\n" +
                $"帳號 / Workspace：{account ?? "—"}\n" +
                $"分支：{info.Branch ?? "—"}\n" +
                $"Repository：{selectedTarget.NameWithOwner}\n" +
                $"Remote：{selectedTarget.CanonicalUrl ?? "—"}\n" +
                $"本機 origin：{info.RemoteUrl ?? "（無 origin）"}" +
                originNote;
        }

        return
            $"平台：{info.ProviderName}\n" +
            $"GitHub 使用者：{info.GhUser ?? "（僅 GitHub 顯示）"}\n" +
            $"GitHub 驗證：{(info.GhAuthenticated ? "已登入" : "未登入或非 GitHub remote")}\n" +
            $"分支：{info.Branch ?? "—"}\n" +
            $"Remote：{info.RemoteUrl ?? "（無 origin）"}\n" +
            $"Repo：{(info.Owner is null ? "—" : $"{info.Owner}/{info.Repo}")}";
    }

    private static string BuildOriginNote(GitRemoteInfo info, GitHubRepositoryTarget selectedTarget)
    {
        if (string.IsNullOrWhiteSpace(info.RemoteUrl))
            return "\n狀態：尚未連結本機 origin；按「連結、推送並設定 Pages」會連到目前選擇的平台。";

        if (info.Provider == selectedTarget.Provider
            && string.Equals(info.Owner, selectedTarget.Owner, StringComparison.OrdinalIgnoreCase)
            && string.Equals(info.Repo, selectedTarget.Repository, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return $"\n狀態：本機 origin 仍指向 {info.ProviderName}；按「連結、推送並設定 Pages」會改連到目前選擇的 repository。";
    }
}
