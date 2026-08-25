using Hugoer.Models;

namespace Hugoer.Helpers;

public static class GitHostingProcessErrors
{
    public static CommandResult WithRepositoryAccessHint(
        GitHostingProvider provider,
        string operation,
        CommandResult result)
    {
        if (result.Succeeded || provider == GitHostingProvider.GitHub)
            return result;

        if (LooksLikePlanOrQuotaLimit(result.CombinedOutput))
        {
            return new CommandResult
            {
                ExitCode = result.ExitCode,
                StdOut = result.StdOut,
                StdErr =
                    $"{provider.DisplayName()} 儲存庫已被設為唯讀（HTTP 402）：帳號或 Workspace 已超過方案／使用者額度限制（例如 Bitbucket 免費版人數上限）。\n" +
                    $"請至 {provider.DisplayName()} 網站後台管理使用者權限、移除多餘成員或變更方案以恢復寫入權限。\n" +
                    result.CombinedOutput,
                IsPartialSuccess = result.IsPartialSuccess
            };
        }

        if (!LooksLikeAccessFailure(result.CombinedOutput))
            return result;

        return new CommandResult
        {
            ExitCode = result.ExitCode,
            StdOut = result.StdOut,
            StdErr =
                $"{provider.DisplayName()} 無法{operation}：Git 命令列沒有此 repository 的讀寫權限，或目前 Git 憑證不是可存取的帳號。\n" +
                CredentialHint(provider) + "\n" +
                result.CombinedOutput,
            IsPartialSuccess = result.IsPartialSuccess
        };
    }

    public static string CredentialHint(GitHostingProvider provider) => provider switch
    {
        GitHostingProvider.GitLab =>
            "瀏覽器登入不等於 Git 命令列已登入；請用 Git Credential Manager 登入 GitLab，或使用具有 read_repository / write_repository 權限的 Personal Access Token。",
        GitHostingProvider.Codeberg =>
            "請確認 Git Credential Manager / SSH 使用的是 Codeberg repository owner 或已被授權的 collaborator。若 HTTPS 使用過期 token，請清除 Windows 認證管理員中的 git:https://codeberg.org，再輸入 Codeberg 使用者名稱與 Access Token。",
        GitHostingProvider.Bitbucket =>
            "若 Bitbucket 畫面已顯示 Admin，請確認受邀使用者已接受邀請，並清除 Windows 認證管理員中的 git:https://bitbucket.org；push 需要本機 Git 使用 Bitbucket App password 或 SSH key。",
        _ => "請確認本機 Git 憑證具有此 repository 的寫入權限。"
    };

    private static bool LooksLikePlanOrQuotaLimit(string output) =>
        ContainsAny(
            output,
            "402",
            "exceeded its user limit",
            "restricted to read only access",
            "Change your plan to restore write access",
            "quota exceeded");

    private static bool LooksLikeAccessFailure(string output) =>
        ContainsAny(
            output,
            "could not be found or you don't have permission",
            "not found or you don't have permission",
            "repository not found",
            "Authentication failed",
            "HTTP Basic: Access denied",
            "Permission denied",
            "not allowed to push to branch",
            "pre-receive hook declined",
            "protected branch",
            "403",
            "401");

    private static bool ContainsAny(string value, params string[] needles) =>
        needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
