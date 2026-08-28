using Hugoer.Models;

namespace Hugoer.Services;

/// <summary>
/// Coordinates the user-facing publish policy.  The default route is the
/// repository/GitHub Pages workflow: Hugoer commits and pushes source files and
/// lets GitHub Actions build the published site.  A local Hugo build is kept as
/// an explicit, observable fallback for offline or unavailable remotes.
/// </summary>
public sealed class PublishingService
{
    private readonly GitHubService _gitHub;
    private readonly HugoService _hugo;

    public PublishingService(GitHubService gitHub, HugoService hugo)
    {
        _gitHub = gitHub ?? throw new ArgumentNullException(nameof(gitHub));
        _hugo = hugo ?? throw new ArgumentNullException(nameof(hugo));
    }

    public async Task<PublishResult> PublishAsync(
        string sitePath,
        DeploymentMode requestedMode,
        GitHubRepositoryTarget? target,
        string commitMessage,
        bool allowLocalFallback = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sitePath);

        // Treat an unknown value (for example a hand-edited settings file)
        // as the safe repository-backed default.  Never silently reinterpret
        // it as a local-only deployment.
        if (!Enum.IsDefined(requestedMode))
            requestedMode = DeploymentMode.GitHubPages;

        if (requestedMode == DeploymentMode.GitHubPages)
        {
            if (target is not { IsValid: true })
            {
                const string reason = "尚未設定有效的 GitHub Pages repository。";
                progress?.Report(reason);
                return await FallbackOrFailAsync(
                    sitePath,
                    requestedMode,
                    reason,
                    allowLocalFallback: allowLocalFallback,
                    remoteResult: null,
                    remoteAttempted: false,
                    progress: progress,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            return await RunRemoteFirstAsync(
                    sitePath,
                    requestedMode,
                    remoteOperation: () => _gitHub.PushAsync(
                        sitePath,
                        commitMessage,
                        progress,
                        cancellationToken),
                    successMessage: target.Provider == GitHostingProvider.GitHub
                        ? "已直接推送到 GitHub；GitHub Actions 將建置並發布 Pages。"
                        : $"已直接推送到 {target.ProviderName}；平台將建置並發布網站。",
                    allowLocalFallback: allowLocalFallback,
                    progress: progress,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return await BuildLocallyAsync(
                sitePath,
                requestedMode,
                reason: "已選擇本機部署備援。",
                progress: progress,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Runs only the local fallback route on demand.</summary>
    public Task<PublishResult> DeployLocallyAsync(
        string sitePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        BuildLocallyAsync(
            sitePath,
            DeploymentMode.Local,
            reason: "正在執行本機部署備援…",
            progress: progress,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Creates a new GitHub repository and publishes the local source through
    /// the same remote-first seam. If GitHub CLI/authentication is unavailable,
    /// the configured local fallback still produces a usable public/ output.
    /// </summary>
    public Task<PublishResult> CreateAndPublishAsync(
        string sitePath,
        string repositoryName,
        bool isPublic,
        string? commitMessage = null,
        bool allowLocalFallback = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        RunRemoteFirstAsync(
            sitePath,
            DeploymentMode.GitHubPages,
            remoteOperation: () => _gitHub.CreateRepoAndPushAsync(
                sitePath,
                repositoryName,
                isPublic,
                progress,
                cancellationToken,
                commitMessage),
            successMessage: "已建立並推送到 GitHub；GitHub Actions 將建置並發布 Pages。",
            allowLocalFallback: allowLocalFallback,
            progress: progress,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Connects a local site to an existing repository and publishes it.  The
    /// access check and repository operation stay behind the same policy seam
    /// as ordinary updates, so a remote failure can never be mistaken for a
    /// successful local deployment.
    /// </summary>
    public async Task<PublishResult> ConnectAndPublishAsync(
        string sitePath,
        GitHubRepositoryTarget target,
        string commitMessage,
        bool allowLocalFallback = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sitePath);

        if (target is not { IsValid: true })
        {
            const string reason = "尚未設定有效的 repository。";
            progress?.Report(reason);
            return await FallbackOrFailAsync(
                sitePath,
                DeploymentMode.GitHubPages,
                reason,
                allowLocalFallback: allowLocalFallback,
                remoteResult: null,
                remoteAttempted: false,
                progress: progress,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        progress?.Report($"正在確認 {target.ProviderName} repository 推送權限…");
        (bool HasAccess, string Message) access;
        try
        {
            access = await _gitHub.CheckPushAccessAsync(target, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var accessFailure = FailureResult("確認遠端推送權限", ex);
            return await FallbackOrFailAsync(
                sitePath,
                DeploymentMode.GitHubPages,
                accessFailure.StdErr,
                allowLocalFallback,
                accessFailure,
                remoteAttempted: true,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        if (!access.HasAccess)
        {
            progress?.Report(access.Message);
            return await FallbackOrFailAsync(
                sitePath,
                DeploymentMode.GitHubPages,
                access.Message,
                allowLocalFallback: allowLocalFallback,
                remoteResult: null,
                remoteAttempted: true,
                progress: progress,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        progress?.Report(access.Message);
        return await RunRemoteFirstAsync(
                sitePath,
                DeploymentMode.GitHubPages,
                remoteOperation: () => _gitHub.ConnectExistingRepositoryAndPushAsync(
                    sitePath,
                    target,
                    commitMessage,
                    progress,
                    cancellationToken),
                successMessage: target.Provider == GitHostingProvider.GitHub
                    ? "已直接推送到 GitHub；GitHub Actions 將建置並發布 Pages。"
                    : $"已直接推送到 {target.ProviderName}；請依平台狀態完成網站發布。",
                allowLocalFallback: allowLocalFallback,
                progress: progress,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<PublishResult> RunRemoteFirstAsync(
        string sitePath,
        DeploymentMode requestedMode,
        Func<Task<CommandResult>> remoteOperation,
        string successMessage,
        bool allowLocalFallback,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("遠端優先：提交 repository，交由平台工作流程建置…");
        var remote = await ExecuteSafelyAsync(
                remoteOperation,
                "遠端發佈",
                cancellationToken)
            .ConfigureAwait(false);
        if (remote.Succeeded)
        {
            var completedMessage = remote.IsPartialSuccess
                && !string.IsNullOrWhiteSpace(remote.StdErr)
                ? $"{successMessage}\n{remote.StdErr.Trim()}"
                : successMessage;
            return new PublishResult
            {
                RequestedMode = requestedMode,
                Outcome = PublishOutcome.GitHubPages,
                RemoteAttempted = true,
                RemotePushSucceeded = true,
                RemoteResult = remote,
                Message = completedMessage
            };
        }

        var remoteReason = string.IsNullOrWhiteSpace(remote.CombinedOutput)
            ? "遠端推送失敗。"
            : $"遠端推送失敗：{remote.CombinedOutput}";
        progress?.Report(remoteReason);
        return await FallbackOrFailAsync(
            sitePath,
            requestedMode,
            remoteReason,
            allowLocalFallback: allowLocalFallback,
            remoteResult: remote,
            remoteAttempted: true,
            progress: progress,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<PublishResult> FallbackOrFailAsync(
        string sitePath,
        DeploymentMode requestedMode,
        string reason,
        bool allowLocalFallback,
        CommandResult? remoteResult,
        bool remoteAttempted,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!allowLocalFallback)
        {
            return new PublishResult
            {
                RequestedMode = requestedMode,
                Outcome = PublishOutcome.Failed,
                RemoteAttempted = remoteAttempted,
                RemotePushSucceeded = false,
                RemoteResult = remoteResult,
                Message = reason
            };
        }

        progress?.Report("遠端服務暫時不可用，改用本機部署備援…");
        var local = await ExecuteSafelyAsync(
                () => _hugo.BuildAsync(sitePath, cancellationToken),
                "本機 Hugo 建置",
                cancellationToken)
            .ConfigureAwait(false);
        if (!local.Succeeded)
        {
            var localError = string.IsNullOrWhiteSpace(local.CombinedOutput)
                ? "本機 Hugo 建置也失敗。"
                : $"本機 Hugo 建置也失敗：{local.CombinedOutput}";
            return new PublishResult
            {
                RequestedMode = requestedMode,
                Outcome = PublishOutcome.Failed,
                RemoteAttempted = remoteAttempted,
                RemotePushSucceeded = false,
                RemoteResult = remoteResult,
                LocalResult = local,
                Message = $"{reason}\n{localError}"
            };
        }

        return new PublishResult
        {
            RequestedMode = requestedMode,
            Outcome = PublishOutcome.LocalFallback,
            RemoteAttempted = remoteAttempted,
            RemotePushSucceeded = false,
            RemoteResult = remoteResult,
            LocalResult = local,
            Message = $"{reason}\n已完成本機部署備援（public/）。遠端恢復後可再次推送。"
        };
    }

    private async Task<PublishResult> BuildLocallyAsync(
        string sitePath,
        DeploymentMode requestedMode,
        string reason,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(reason);
        var local = await ExecuteSafelyAsync(
                () => _hugo.BuildAsync(sitePath, cancellationToken),
                "本機 Hugo 建置",
                cancellationToken)
            .ConfigureAwait(false);
        return new PublishResult
        {
            RequestedMode = requestedMode,
            Outcome = local.Succeeded ? PublishOutcome.Local : PublishOutcome.Failed,
            LocalResult = local,
            Message = local.Succeeded
                ? "本機部署完成（public/）。"
                : $"本機部署失敗：{(string.IsNullOrWhiteSpace(local.CombinedOutput) ? "請查看 Hugo 操作日誌。" : local.CombinedOutput)}"
        };
    }

    private static async Task<CommandResult> ExecuteSafelyAsync(
        Func<Task<CommandResult>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await operation().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FailureResult(operationName, ex);
        }
    }

    private static CommandResult FailureResult(string operationName, Exception exception) =>
        new()
        {
            ExitCode = -1,
            StdErr = $"{operationName}發生錯誤：{exception.Message}"
        };
}
