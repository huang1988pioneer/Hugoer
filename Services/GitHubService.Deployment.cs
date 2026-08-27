using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.Services;

public sealed partial class GitHubService
{
    public async Task<CommandResult> CreateRepoAndPushAsync(
        string sitePath,
        string repoName,
        bool isPublic = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("初始化 Git…");
        var init = await InitRepoAsync(sitePath, cancellationToken).ConfigureAwait(false);
        if (!init.Succeeded) return init;

        progress?.Report("加入 GitHub Actions 工作流程…");
        await EnsureGitHubActionsWorkflowAsync(sitePath, cancellationToken).ConfigureAwait(false);

        var markerError = await PrepareDeploymentMarkerAsync(sitePath, progress, cancellationToken).ConfigureAwait(false);
        if (markerError is not null) return markerError;

        progress?.Report("提交檔案…");
        var commit = await CommitAllAsync(sitePath, "Initial commit via Hugoer", cancellationToken)
            .ConfigureAwait(false);
        if (!commit.Succeeded)
            return commit;

        var visibility = isPublic ? "public" : "private";
        progress?.Report($"建立 GitHub repository：{repoName}…");

        var remote = await ProcessRunner.RunAsync(
            "git", "remote get-url origin", sitePath, 10_000, cancellationToken).ConfigureAwait(false);

        if (remote.Succeeded)
        {
            var (owner, repo) = ParseGitHubRemote(remote.StdOut.Trim());
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return new CommandResult
                {
                    ExitCode = -1,
                    StdErr = $"本機 origin 已指向 {remote.StdOut.Trim()}，不是 GitHub repository。Hugoer 沒有修改 origin，也沒有建立新 repository。"
                };
            }

            if (!repo.Equals(repoName, StringComparison.OrdinalIgnoreCase))
            {
                return new CommandResult
                {
                    ExitCode = -1,
                    StdErr = $"本機 origin 已指向 {owner}/{repo}，與要建立的「{repoName}」不同。Hugoer 沒有修改 origin，也沒有建立新 repository。"
                };
            }

            progress?.Report("本機已有 origin，推送到既有 repository…");
            var push = await ProcessRunner.RunAsync(
                "git", "push -u origin HEAD", sitePath, 180_000, cancellationToken).ConfigureAwait(false);
            if (!push.Succeeded)
                return push;
        }
        else
        {
            var create = await ProcessRunner.RunAsync(
                "gh",
                ["repo", "create", repoName, "--source=.", "--remote=origin", $"--{visibility}", "--push"],
                workingDirectory: sitePath,
                timeoutMs: 180_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!create.Succeeded)
            {
                if (!LooksLikeNameExistsError(create))
                    return create;

                var info = await GetInfoAsync(sitePath, cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(info.GhUser))
                    return create;

                var target = ParseRepositoryTarget($"https://github.com/{info.GhUser}/{repoName}");
                if (!target.IsValid)
                    return create;

                progress?.Report($"GitHub 上已有 {info.GhUser}/{repoName}，改為安全連結既有 repository…");
                return await ConnectExistingRepositoryAndPushAsync(
                    sitePath,
                    target,
                    DefaultUpdateCommitMessage,
                    progress,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        progress?.Report("啟用 GitHub Pages（GitHub Actions）…");
        var pages = await EnablePagesFromActionsAsync(sitePath, cancellationToken).ConfigureAwait(false);
        return new CommandResult
        {
            ExitCode = pages.Succeeded ? 0 : pages.ExitCode,
            StdOut = $"Repo ready.\n{pages.CombinedOutput}",
            StdErr = pages.Succeeded ? string.Empty : pages.StdErr
        };
    }

    public async Task<CommandResult> ConnectExistingRepositoryAndPushAsync(
        string sitePath,
        GitHubRepositoryTarget target,
        string commitMessage,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!target.IsValid || string.IsNullOrWhiteSpace(target.CanonicalUrl))
            return new CommandResult { ExitCode = -1, StdErr = target.ErrorMessage };

        progress?.Report("初始化本機 Git repository…");
        var init = await InitRepoAsync(sitePath, cancellationToken).ConfigureAwait(false);
        if (!init.Succeeded) return init;

        var remote = await ProcessRunner.RunAsync(
            "git", "remote get-url origin", sitePath, 10_000, cancellationToken).ConfigureAwait(false);
        if (remote.Succeeded)
        {
            var existing = ParseRemoteTarget(remote.StdOut.Trim());
            if (!GitRemoteSafety.IsSameRepository(existing, target))
            {
                return new CommandResult
                {
                    ExitCode = -1,
                    StdErr = GitRemoteSafety.BuildMismatchMessage(remote.StdOut.Trim(), target)
                };
            }
        }
        else
        {
            progress?.Report($"連結 {target.ProviderName} origin：{target.Owner}/{target.Repository}…");
            var addRemote = await ProcessRunner.RunAsync(
                "git",
                ["remote", "add", "origin", target.CanonicalUrl],
                workingDirectory: sitePath,
                timeoutMs: 15_000,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!addRemote.Succeeded) return addRemote;
        }

        progress?.Report("抓取遠端預設分支…");
        var fetch = await ProcessRunner.RunAsync(
            "git", "fetch origin --prune", sitePath, 120_000, cancellationToken).ConfigureAwait(false);
        if (!fetch.Succeeded)
            return GitHostingProcessErrors.WithRepositoryAccessHint(target.Provider, "抓取遠端預設分支", fetch);

        var remoteHead = await ProcessRunner.RunAsync(
            "git", "ls-remote --symref origin HEAD", sitePath, 30_000, cancellationToken).ConfigureAwait(false);
        var branchMatch = RemoteHeadRegex().Match(remoteHead.StdOut);
        var remoteBranch = StaticPagesDeployment.ResolveSourceBranch(
            target,
            branchMatch.Success ? branchMatch.Groups["branch"].Value : null);
        if (!GitBranchRegex().IsMatch(remoteBranch))
            return new CommandResult { ExitCode = -1, StdErr = "遠端預設分支名稱格式不安全，已停止操作。" };

        var remoteRef = await ProcessRunner.RunAsync(
            "git",
            $"rev-parse --verify \"refs/remotes/origin/{remoteBranch}\"",
            sitePath,
            10_000,
            cancellationToken).ConfigureAwait(false);
        var remoteHasCommit = remoteRef.Succeeded || RemoteHeadCommitRegex().IsMatch(remoteHead.StdOut);
        if (target.Provider == GitHostingProvider.Codeberg
            && branchMatch.Success
            && branchMatch.Groups["branch"].Value.Equals(
                StaticPagesDeployment.CodebergPagesBranch, StringComparison.OrdinalIgnoreCase))
        {
            remoteHasCommit = remoteRef.Succeeded;
        }

        var localHead = await ProcessRunner.RunAsync(
            "git", "rev-parse --verify HEAD", sitePath, 10_000, cancellationToken).ConfigureAwait(false);
        if (StaticPagesDeployment.ShouldPushSourceBranch(target))
        {
            if (remoteHasCommit && !localHead.Succeeded)
            {
                progress?.Report($"以遠端 {remoteBranch} 為基準，保留本機未追蹤網站檔案…");
                var checkout = await ProcessRunner.RunAsync(
                    "git",
                    ["checkout", "-B", remoteBranch, "--track", $"origin/{remoteBranch}"],
                    workingDirectory: sitePath,
                    timeoutMs: 30_000,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!checkout.Succeeded)
                {
                    return new CommandResult
                    {
                        ExitCode = checkout.ExitCode,
                        StdErr = $"遠端檔案與本機未追蹤檔案衝突，已停止連結；沒有強制覆蓋。\n{checkout.CombinedOutput}"
                    };
                }
            }
            else if (remoteHasCommit && localHead.Succeeded)
            {
                var merge = await MergeOriginBranchForPushAsync(sitePath, remoteBranch, progress, cancellationToken)
                    .ConfigureAwait(false);
                if (!merge.Succeeded)
                    return new CommandResult
                    {
                        ExitCode = merge.ExitCode,
                        StdErr = $"遠端內容與本機內容發生合併衝突，已中止合併且未推送。\n{merge.CombinedOutput}"
                    };
            }
        }

        progress?.Report($"加入 {target.PagesProductName} 部署設定並提交網站…");
        await EnsureHostingWorkflowAsync(sitePath, target.Provider, cancellationToken).ConfigureAwait(false);
        var markerError = await PrepareDeploymentMarkerAsync(sitePath, progress, cancellationToken).ConfigureAwait(false);
        if (markerError is not null) return markerError;
        var commit = await CommitAllAsync(sitePath, commitMessage, cancellationToken).ConfigureAwait(false);
        if (!commit.Succeeded) return commit;

        if (StaticPagesDeployment.ShouldPushSourceBranch(target))
        {
            progress?.Report($"確認 {target.ProviderName} 推送權限…");
            var dryRun = await ProcessRunner.RunAsync(
                "git",
                GitHostingAccessChecks.PushDryRunArguments(remoteBranch),
                sitePath,
                60_000,
                cancellationToken).ConfigureAwait(false);
            if (!dryRun.Succeeded)
                return GitHostingProcessErrors.WithRepositoryAccessHint(target.Provider, "推送", dryRun);

            var push = await PushHeadToOriginBranchAsync(
                    sitePath,
                    remoteBranch,
                    $"推送到 {target.Owner}/{target.Repository}…",
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!push.Succeeded)
                return GitHostingProcessErrors.WithRepositoryAccessHint(target.Provider, "推送", push);
        }

        if (StaticPagesDeployment.ShouldPublishOutputBranch(target))
        {
            var published = await PublishStaticOutputBranchAsync(
                    sitePath,
                    target,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!published.Succeeded) return published;
            if (target.Provider != GitHostingProvider.GitHub)
                return published;
        }

        if (target.Provider == GitHostingProvider.GitHub)
        {
            progress?.Report("啟用 GitHub Pages（Actions）…");
            return await EnablePagesFromActionsAsync(sitePath, cancellationToken).ConfigureAwait(false);
        }

        return ProviderPushResult(target);
    }

    public async Task<CommandResult> PushAsync(
        string sitePath,
        string commitMessage = DefaultUpdateCommitMessage,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var info = await GetInfoAsync(sitePath, cancellationToken).ConfigureAwait(false);
        var target = !string.IsNullOrWhiteSpace(info.RemoteUrl)
            ? ParseRemoteTarget(info.RemoteUrl)
            : new GitHubRepositoryTarget { IsValid = false };
        var provider = target.IsValid ? target.Provider : info.Provider ?? GitHostingProvider.GitHub;

        progress?.Report("提交變更…");
        await EnsureHostingWorkflowAsync(sitePath, provider, cancellationToken).ConfigureAwait(false);
        var markerError = await PrepareDeploymentMarkerAsync(sitePath, progress, cancellationToken).ConfigureAwait(false);
        if (markerError is not null) return markerError;
        var commit = await CommitAllAsync(sitePath, commitMessage, cancellationToken).ConfigureAwait(false);
        progress?.Report(commit.CombinedOutput);
        if (!commit.Succeeded)
            return commit;

        var branch = target.IsValid
            ? StaticPagesDeployment.ResolveSourceBranch(target, info.Branch)
            : (string.IsNullOrWhiteSpace(info.Branch) ? "main" : info.Branch);
        if (!GitBranchRegex().IsMatch(branch))
            return new CommandResult { ExitCode = -1, StdErr = "目前分支名稱格式不安全，已停止推送。" };

        CommandResult sourcePush = new() { ExitCode = 0, StdOut = commit.CombinedOutput };
        if (!target.IsValid || StaticPagesDeployment.ShouldPushSourceBranch(target))
        {
            var sync = await SyncOriginBranchBeforePushAsync(sitePath, branch, progress, cancellationToken)
                .ConfigureAwait(false);
            if (!sync.Succeeded) return sync;

            sourcePush = await PushHeadToOriginBranchAsync(
                    sitePath,
                    branch,
                    "git push…",
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!sourcePush.Succeeded)
                return target.IsValid
                    ? GitHostingProcessErrors.WithRepositoryAccessHint(target.Provider, "推送", sourcePush)
                    : sourcePush;
        }

        if (target.IsValid && StaticPagesDeployment.ShouldPublishOutputBranch(target))
            return await PublishStaticOutputBranchAsync(sitePath, target, progress, cancellationToken)
                .ConfigureAwait(false);

        return sourcePush;
    }

    private async Task<CommandResult> PublishStaticOutputBranchAsync(
        string sitePath,
        GitHubRepositoryTarget target,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var outputBranch = StaticPagesDeployment.OutputBranchFor(target);
        if (string.IsNullOrWhiteSpace(outputBranch) || string.IsNullOrWhiteSpace(target.CanonicalUrl))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = $"這個 {target.ProviderName} repository 沒有可自動發布的靜態網站分支。"
            };
        }

        progress?.Report($"建立 {target.PagesProductName} 靜態輸出…");
        var extraArgs = new List<string> { "--gc", "--minify" };
        if (!string.IsNullOrWhiteSpace(target.PagesUrl))
        {
            extraArgs.Add("--baseURL");
            extraArgs.Add(target.PagesUrl);
        }
        var build = await BuildSiteOutputAsync(sitePath, extraArgs, cancellationToken).ConfigureAwait(false);
        if (!build.Succeeded)
        {
            return new CommandResult
            {
                ExitCode = build.ExitCode,
                StdErr = $"{target.PagesProductName} 靜態輸出建置失敗。\n{build.CombinedOutput}"
            };
        }

        if (!StaticPagesDeployment.TryFindOutputDirectory(sitePath, out var publicDir, out var outputError))
            return new CommandResult { ExitCode = -1, StdErr = outputError };

        CopyDeploymentMarkerToOutput(sitePath, publicDir);

        var tempRoot = Path.Combine(Path.GetTempPath(), $"hugoer-{target.Provider.ToString().ToLowerInvariant()}-pages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var init = await ProcessRunner.RunAsync(
                "git",
                ["init", "-b", outputBranch],
                workingDirectory: tempRoot,
                timeoutMs: 30_000,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!init.Succeeded) return init;

            var addRemote = await ProcessRunner.RunAsync(
                "git",
                ["remote", "add", "origin", target.CanonicalUrl],
                workingDirectory: tempRoot,
                timeoutMs: 15_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!addRemote.Succeeded) return addRemote;

            var fetch = await ProcessRunner.RunAsync(
                "git",
                ["fetch", "origin", outputBranch],
                workingDirectory: tempRoot,
                timeoutMs: 60_000,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (fetch.Succeeded)
            {
                var checkout = await ProcessRunner.RunAsync(
                    "git",
                    ["checkout", "-B", outputBranch, "FETCH_HEAD"],
                    workingDirectory: tempRoot,
                    timeoutMs: 30_000,
                    cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (!checkout.Succeeded) return checkout;
            }

            ClearDirectoryExceptGit(tempRoot);
            CopyDirectory(publicDir, tempRoot);

            progress?.Report($"提交 {target.PagesProductName} 靜態檔到 {outputBranch} 分支…");
            var commit = await CommitAllAsync(
                tempRoot,
                $"Publish {target.PagesProductName} via Hugoer",
                cancellationToken).ConfigureAwait(false);
            if (!commit.Succeeded) return commit;

            progress?.Report($"確認 {target.ProviderName} {outputBranch} 分支推送權限…");
            var dryRun = await ProcessRunner.RunAsync(
                "git",
                GitHostingAccessChecks.PushDryRunArguments(outputBranch),
                tempRoot,
                60_000,
                cancellationToken).ConfigureAwait(false);
            if (!dryRun.Succeeded)
                return GitHostingProcessErrors.WithRepositoryAccessHint(
                    target.Provider, $"推送 {outputBranch} 分支", dryRun);

            progress?.Report($"推送 {target.PagesProductName} 靜態檔到 {outputBranch} 分支…");
            var push = await ProcessRunner.RunAsync(
                "git",
                ["push", "-u", "origin", $"HEAD:{outputBranch}"],
                workingDirectory: tempRoot,
                timeoutMs: 180_000,
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!push.Succeeded)
                return GitHostingProcessErrors.WithRepositoryAccessHint(
                    target.Provider, $"推送 {outputBranch} 分支", push);

            return new CommandResult
            {
                ExitCode = 0,
                StdOut = BuildStaticPublishMessage(target, outputBranch)
            };
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private async Task<CommandResult> BuildSiteOutputAsync(
        string sitePath,
        IReadOnlyList<string> extraArgs,
        CancellationToken cancellationToken)
    {
        if (_hugo is not null)
            return await _hugo.BuildWithArgumentsAsync(sitePath, extraArgs, cancellationToken).ConfigureAwait(false);

        return await ProcessRunner.RunAsync(
            "hugo",
            new[] { "build" }.Concat(extraArgs),
            sitePath,
            180_000,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandResult> SyncOriginBranchBeforePushAsync(
        string sitePath,
        string branch,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report($"抓取遠端 {branch} 更新…");
        var fetch = await ProcessRunner.RunAsync(
            "git", "fetch origin --prune", sitePath, 120_000, cancellationToken).ConfigureAwait(false);
        if (!fetch.Succeeded) return WithGitPushHint(fetch);

        var localHead = await ProcessRunner.RunAsync(
            "git", "rev-parse --verify HEAD", sitePath, 10_000, cancellationToken).ConfigureAwait(false);
        if (!localHead.Succeeded)
            return new CommandResult { ExitCode = 0, StdOut = "本機尚無 commit，略過合併遠端更新。" };

        var remoteCommit = await ProcessRunner.RunAsync(
            "git",
            ["rev-parse", "--verify", $"origin/{branch}"],
            workingDirectory: sitePath,
            timeoutMs: 10_000,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!remoteCommit.Succeeded)
            return new CommandResult { ExitCode = 0, StdOut = $"遠端尚無 {branch} 分支，將建立新分支。" };

        var mergeBase = await ProcessRunner.RunAsync(
            "git",
            ["merge-base", "--is-ancestor", $"origin/{branch}", "HEAD"],
            workingDirectory: sitePath,
            timeoutMs: 10_000,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (mergeBase.Succeeded)
            return new CommandResult { ExitCode = 0, StdOut = $"本機已包含 origin/{branch}。" };

        return await MergeOriginBranchForPushAsync(sitePath, branch, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<CommandResult> MergeOriginBranchForPushAsync(
        string sitePath,
        string branch,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report($"合併遠端 {branch}（保留本機 Hugo 網站內容，不 force push）…");
        var merge = await ProcessRunner.RunAsync(
            "git",
            ["merge", $"origin/{branch}", "--allow-unrelated-histories", "--no-edit", "-X", "ours"],
            workingDirectory: sitePath,
            timeoutMs: 60_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (merge.Succeeded)
            return merge;

        await ProcessRunner.RunAsync("git", "merge --abort", sitePath, 15_000, cancellationToken)
            .ConfigureAwait(false);
        return new CommandResult
        {
            ExitCode = merge.ExitCode,
            StdErr =
                "遠端內容與本機網站發生合併衝突，Hugoer 已中止合併且沒有推送。\n" +
                "請先處理衝突後再推送；Hugoer 不會 force push 覆蓋遠端。\n" +
                merge.CombinedOutput
        };
    }

    private async Task<CommandResult> PushHeadToOriginBranchAsync(
        string sitePath,
        string branch,
        string message,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(message);
        var push = await ProcessRunner.RunAsync(
            "git",
            ["push", "-u", "origin", $"HEAD:{branch}"],
            workingDirectory: sitePath,
            timeoutMs: 180_000,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (push.Succeeded)
            return push;

        if (!GitPushFailureClassifier.IsNonFastForwardRejection(push.CombinedOutput))
            return WithGitPushHint(push);

        progress?.Report(GitPushFailureClassifier.ToUserMessage(push.CombinedOutput));
        var sync = await SyncOriginBranchBeforePushAsync(sitePath, branch, progress, cancellationToken)
            .ConfigureAwait(false);
        if (!sync.Succeeded) return sync;

        var retry = await ProcessRunner.RunAsync(
            "git",
            ["push", "-u", "origin", $"HEAD:{branch}"],
            workingDirectory: sitePath,
            timeoutMs: 180_000,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return retry.Succeeded ? retry : WithGitPushHint(retry);
    }

    private async Task<CommandResult?> PrepareDeploymentMarkerAsync(
        string sitePath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var marker = await _deploymentMonitor.PrepareDeploymentAsync(sitePath, cancellationToken)
                .ConfigureAwait(false);
            progress?.Report($"建立部署版本標記：{marker.DeploymentId}");
            return null;
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = $"無法建立部署版本標記，已停止提交與推送：{ex.Message}"
            };
        }
    }

    private static CommandResult WithGitPushHint(CommandResult result, GitHostingProvider? provider = null)
    {
        var hint = GitPushFailureClassifier.ToUserMessage(result.CombinedOutput, provider);
        if (string.Equals(hint, result.CombinedOutput, StringComparison.Ordinal))
            return result;

        return new CommandResult
        {
            ExitCode = result.ExitCode,
            StdOut = result.StdOut,
            StdErr = $"{hint}\n{result.StdErr}".Trim(),
            IsPartialSuccess = result.IsPartialSuccess
        };
    }

    private static void CopyDeploymentMarkerToOutput(string sitePath, string outputDirectory)
    {
        var source = Path.Combine(sitePath, "static", DeploymentMonitorService.MarkerFileName);
        if (!File.Exists(source))
            return;

        Directory.CreateDirectory(outputDirectory);
        File.Copy(source, Path.Combine(outputDirectory, DeploymentMonitorService.MarkerFileName), overwrite: true);
    }

    private static string BuildStaticPublishMessage(GitHubRepositoryTarget target, string outputBranch)
    {
        var url = string.IsNullOrWhiteSpace(target.PagesUrl) ? "（請依平台後台確認）" : target.PagesUrl;
        return target.Provider switch
        {
            GitHostingProvider.Codeberg =>
                $"已推送到 Codeberg：{target.NameWithOwner}\n" +
                $"已將 Hugo 靜態輸出推送到 Codeberg Pages 的 {outputBranch} 分支。\n" +
                $"網站網址：{url}\n" +
                "若尚未設定 Webhook，請在 Codeberg repo Settings > Webhooks 新增 Forgejo webhook，Target URL 使用 Pages 網址，Branch filter 設為 pages。",
            GitHostingProvider.Bitbucket =>
                $"已推送 Bitbucket 靜態網站：{target.NameWithOwner}\n" +
                $"網站網址：{url}\n" +
                "Bitbucket Cloud 會直接從 repository 根目錄提供 index.html；更新可能因快取最多延遲約 15 分鐘。",
            _ => $"已推送 {target.ProviderName} 靜態輸出到 {outputBranch} 分支。\n網站網址：{url}"
        };
    }

    private static CommandResult ProviderPushResult(GitHubRepositoryTarget target) => new()
    {
        ExitCode = 0,
        IsPartialSuccess = target.Provider is GitHostingProvider.GitLab
                           or GitHostingProvider.Codeberg
                           or GitHostingProvider.Bitbucket,
        StdOut =
            $"已推送到 {target.ProviderName}：{target.NameWithOwner}\n" +
            ProviderManualSetupMessage(target.Provider)
    };

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(targetDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file);
            var target = Path.Combine(targetDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void ClearDirectoryExceptGit(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
            File.Delete(file);

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (Path.GetFileName(child).Equals(".git", StringComparison.OrdinalIgnoreCase))
                continue;
            Directory.Delete(child, recursive: true);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Temporary publish worktrees are best-effort cleanup.
        }
    }
}
