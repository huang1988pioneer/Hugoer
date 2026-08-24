namespace Hugoer.Helpers;

public static class GitPushFailureClassifier
{
    public static bool IsNonFastForwardRejection(string output) =>
        ContainsAny(
            output,
            "fetch first",
            "Updates were rejected because the remote contains work that you do not have locally",
            "non-fast-forward",
            "failed to push some refs");

    public static bool LooksLikeMissingPushPermission(string output) =>
        ContainsAny(
            output,
            "The project you were looking for could not be found",
            "you don't have permission",
            "not have permission to view",
            "repository not found",
            "authentication failed",
            "access denied");

    public static string ToUserMessage(string output) =>
        IsNonFastForwardRejection(output)
            ? "遠端 repository 有本機尚未合併的更新。Hugoer 會先抓取並安全合併遠端內容，再重新推送。"
            : LooksLikeMissingPushPermission(output)
                ? "GitLab/Git 平台拒絕推送。請確認目前 Windows Git 憑證具有此 repository 的 Developer/Maintainer 寫入權限，或重新登入 Git Credential Manager。"
                : output;

    private static bool ContainsAny(string text, params string[] tokens) =>
        tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
}
