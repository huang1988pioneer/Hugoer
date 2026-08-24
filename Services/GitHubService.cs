using System.Text.Json;
using System.Text.RegularExpressions;
using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.Services;

public sealed partial class GitHubService
{
    private readonly DeploymentMonitorService _deploymentMonitor;

    public GitHubService(DeploymentMonitorService? deploymentMonitor = null)
    {
        _deploymentMonitor = deploymentMonitor ?? new DeploymentMonitorService();
    }

    public static GitHubRepositoryTarget ParseRepositoryTarget(string? input) =>
        GitHubRepositoryParser.Parse(input);

    public async Task UpdateBaseUrlAsync(
        string sitePath,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        var config = PathHelper.FindConfigFile(sitePath)
                     ?? Path.Combine(sitePath, "hugo.toml");
        var original = File.Exists(config)
            ? await File.ReadAllTextAsync(config, cancellationToken).ConfigureAwait(false)
            : string.Empty;
        var updated = new TomlParamsService().UpsertSimpleRootKeys(original, new Dictionary<string, string>
        {
            ["baseURL"] = baseUrl
        });
        await File.WriteAllTextAsync(config, updated, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GitHubRepositoryLookup> LookupOwnedRepositoryAsync(
        string repoName,
        CancellationToken cancellationToken = default)
    {
        var name = repoName.Trim();
        if (!GitHubRepositoryParser.IsValidRepositoryName(name))
            return GitHubRepositoryLookup.Fail("Repository 名稱格式無效。");

        var user = await ProcessRunner.RunAsync(
            "gh", "api user --jq .login", timeoutMs: 15_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!user.Succeeded || string.IsNullOrWhiteSpace(user.StdOut))
            return GitHubRepositoryLookup.Fail("無法取得目前 GitHub 帳號。請先執行 gh auth login。");

        var owner = user.StdOut.Trim();
        var target = ParseRepositoryTarget($"https://github.com/{owner}/{name}");
        if (!target.IsValid)
            return GitHubRepositoryLookup.Fail(target.ErrorMessage);

        var meta = await ProcessRunner.RunAsync(
            "gh",
            $"api repos/{owner}/{name}",
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!meta.Succeeded)
        {
            return IsNotFound(meta)
                ? GitHubRepositoryLookup.Missing()
                : GitHubRepositoryLookup.Fail(
                    $"無法確認 GitHub 上是否已有 {owner}/{name}。\n{meta.CombinedOutput}");
        }

        var contents = await ProcessRunner.RunAsync(
            "gh",
            $"api repos/{owner}/{name}/contents",
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string> names;
        if (!contents.Succeeded)
        {
            if (IsEmptyRepository(contents) || IsNotFound(contents))
                names = [];
            else
                return GitHubRepositoryLookup.Fail(
                    $"無法讀取 {owner}/{name} 的檔案清單。\n{contents.CombinedOutput}");
        }
        else
        {
            names = ParseRepositoryContentNames(contents.StdOut);
        }

        var looksLikeHugo = GitHubRepositoryClassifier.LooksLikeHugo(names);
        var canReuse = GitHubRepositoryClassifier.CanReuseExisting(names);
        var message = looksLikeHugo
            ? $"GitHub 上已有 Hugo repository {owner}/{name}，改用安全連結流程（會保留遠端內容，衝突時停止，不會 force push）。"
            : canReuse
                ? $"GitHub 上已有 {owner}/{name}（空的或僅有 README 等初始檔），改用安全連結流程。"
                : $"GitHub 上已有同名 repository {owner}/{name}，但看起來不是 Hugo 網站。請改用其他名稱，或到上方「連結既有 GitHub Repository」貼上網址確認後再連。";

        return new GitHubRepositoryLookup
        {
            CheckSucceeded = true,
            Exists = true,
            CanReuse = canReuse,
            LooksLikeHugo = looksLikeHugo,
            Target = target,
            Message = message
        };
    }

    public async Task<(bool HasAccess, string Message)> CheckPushAccessAsync(
        GitHubRepositoryTarget target,
        CancellationToken cancellationToken = default)
    {
        if (!target.IsValid || string.IsNullOrWhiteSpace(target.Owner) || string.IsNullOrWhiteSpace(target.Repository))
            return (false, target.ErrorMessage);

        if (target.Provider != GitHostingProvider.GitHub)
        {
            return (true,
                $"{target.ProviderName} 不使用 GitHub CLI 驗證權限；Hugoer 會以一般 git push 測試目前憑證是否可推送。");
        }

        var result = await ProcessRunner.RunAsync(
            "gh",
            $"api repos/{target.Owner}/{target.Repository} --jq .permissions.push",
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return (false,
                $"無法確認 repository 權限。請確認 gh 已登入，且 repository 存在或目前帳號可存取。\n{result.CombinedOutput}");
        }

        var canPush = result.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        return canPush
            ? (true, $"已確認具有 {target.Owner}/{target.Repository} 的推送權限。")
            : (false, $"目前 GitHub 登入帳號沒有 {target.Owner}/{target.Repository} 的推送權限。請由 owner 加入 collaborator，或改用有權限的帳號執行 gh auth login。");
    }

    public async Task<GitHubPagesRepositoryList> ListPagesRepositoriesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await ProcessRunner.RunAsync(
            "gh",
            "api --paginate user/repos?per_page=100&affiliation=owner,collaborator&sort=updated",
            timeoutMs: 45_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return new GitHubPagesRepositoryList
            {
                Succeeded = false,
                Message = "無法列出 GitHub Pages 網站。請先執行 gh auth login，並確認網路連線。"
                    + (string.IsNullOrWhiteSpace(result.CombinedOutput)
                        ? string.Empty
                        : $"\n{result.CombinedOutput}")
            };
        }

        var repositories = GitHubRepositoryParser.ParsePagesEnabledRepositories(result.StdOut);
        return new GitHubPagesRepositoryList
        {
            Succeeded = true,
            Repositories = repositories,
            Message = repositories.Count == 0
                ? "目前帳號沒有已啟用 GitHub Pages 的 repository。仍可貼上 repository 或 Pages 網址複製。"
                : $"找到 {repositories.Count} 個已啟用 GitHub Pages 的 repository。"
        };
    }

    public async Task<CloneSiteResult> CloneSiteFromGitHubAsync(
        string? input,
        string parentDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var target = ParseRepositoryTarget(input);
        if (!target.IsValid || string.IsNullOrWhiteSpace(target.Owner) || string.IsNullOrWhiteSpace(target.Repository)
            || string.IsNullOrWhiteSpace(target.CanonicalUrl))
        {
            return CloneSiteResult.Fail(target.ErrorMessage, target);
        }

        var destination = GitHubClonePath.TryGetDestination(parentDirectory, target.Repository, out var pathError);
        if (destination is null)
            return CloneSiteResult.Fail(pathError, target);

        if (!await IsGitAvailableAsync(cancellationToken).ConfigureAwait(false))
            return CloneSiteResult.Fail("未找到 Git。請先安裝 Git for Windows。", target);

        if (!GitHubClonePath.IsVacantDirectory(destination))
        {
            var existing = await TryOpenExistingCloneAsync(destination, target, progress, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
                return existing;

            return CloneSiteResult.Fail($"目標資料夾已存在且不是空的：{destination}", target);
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? parentDirectory);
        }
        catch (Exception ex)
        {
            return CloneSiteResult.Fail($"無法建立本機資料夾：{ex.Message}", target);
        }

        progress?.Report($"正在從 {target.ProviderName} 複製 {target.Owner}/{target.Repository}…");
        var clone = await CloneRepositoryAsync(target, destination, progress, cancellationToken).ConfigureAwait(false);
        if (!clone.Succeeded)
        {
            return CloneSiteResult.Fail(
                $"複製失敗。若是私人 repository，請先確認此平台的 Git 憑證或 SSH key。\n{clone.CombinedOutput}",
                target,
                clone.CombinedOutput);
        }

        progress?.Report("更新 git submodules…");
        var submodules = await ProcessRunner.RunAsync(
            "git",
            "submodule update --init --recursive",
            destination,
            180_000,
            cancellationToken).ConfigureAwait(false);
        if (!submodules.Succeeded && !string.IsNullOrWhiteSpace(submodules.CombinedOutput))
            progress?.Report(submodules.CombinedOutput);

        if (!PathHelper.LooksLikeHugoSite(destination))
        {
            var sourceBranch = await FindHugoSourceBranchAsync(destination, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(sourceBranch))
            {
                progress?.Report($"預設分支不是 Hugo 網站，改切換到 {sourceBranch}…");
                var checkout = await ProcessRunner.RunAsync(
                    "git",
                    $"checkout -B \"{sourceBranch}\" --track \"origin/{sourceBranch}\"",
                    destination,
                    30_000,
                    cancellationToken).ConfigureAwait(false);
                if (!checkout.Succeeded)
                {
                    return CloneSiteResult.Fail(
                        $"已複製 repository，但切換到 Hugo 來源分支失敗。\n{checkout.CombinedOutput}",
                        target,
                        checkout.CombinedOutput);
                }
            }
        }

        if (!PathHelper.LooksLikeHugoSite(destination))
        {
            var hint = GitHubClonePath.LooksLikeStaticPagesOutput(destination)
                ? $"這看起來是 {target.PagesProductName} 的靜態輸出，不是 Hugo 原始碼。"
                : "複製完成，但找不到 Hugo 設定或 content。";
            return CloneSiteResult.Fail(
                $"{hint} 請改貼來源 repository 網址，而不是只含建置結果的分支。",
                target);
        }

        var message = $"已從 {target.ProviderName} 複製並開啟：{destination}";
        progress?.Report(message);
        return CloneSiteResult.Ok(destination, target, message, output: clone.CombinedOutput);
    }

    public async Task<bool> IsGitAvailableAsync(CancellationToken cancellationToken = default)
    {
        var r = await ProcessRunner.RunAsync("git", "--version", timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return r.Succeeded;
    }

    public async Task<bool> IsGhAvailableAsync(CancellationToken cancellationToken = default)
    {
        var r = await ProcessRunner.RunAsync("gh", "--version", timeoutMs: 10_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return r.Succeeded;
    }

    public async Task<GitRemoteInfo> GetInfoAsync(string sitePath, CancellationToken cancellationToken = default)
    {
        var data = new GitRemoteInfo();

        var branch = await ProcessRunner.RunAsync(
            "git", "branch --show-current", sitePath, 10_000, cancellationToken).ConfigureAwait(false);
        if (branch.Succeeded)
            data.Branch = branch.StdOut.Trim();

        var remote = await ProcessRunner.RunAsync(
            "git", "remote get-url origin", sitePath, 10_000, cancellationToken).ConfigureAwait(false);
        if (remote.Succeeded)
        {
            data.RemoteUrl = remote.StdOut.Trim();
            var target = ParseRemoteTarget(data.RemoteUrl);
            data.Owner = target.Owner;
            data.Repo = target.Repository;
            data.Provider = target.IsValid ? target.Provider : null;
        }

        if (data.Provider is null or GitHostingProvider.GitHub)
        {
            var auth = await ProcessRunner.RunAsync(
                "gh", "auth status", sitePath, 15_000, cancellationToken).ConfigureAwait(false);
            data.GhAuthenticated = auth.Succeeded
                || auth.CombinedOutput.Contains("Logged in", StringComparison.OrdinalIgnoreCase);

            var user = await ProcessRunner.RunAsync(
                "gh", "api user --jq .login", sitePath, 15_000, cancellationToken).ConfigureAwait(false);
            if (user.Succeeded)
                data.GhUser = user.StdOut.Trim();
        }

        return data;
    }

    public async Task<CommandResult> InitRepoAsync(string sitePath, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Path.Combine(sitePath, ".git")))
        {
            var init = await ProcessRunner.RunAsync("git", "init -b main", sitePath, 30_000, cancellationToken)
                .ConfigureAwait(false);
            if (!init.Succeeded)
                return init;
        }

        EnsureGitignore(sitePath);
        return new CommandResult { ExitCode = 0, StdOut = "Git repository ready." };
    }

    public void EnsureGitignore(string sitePath)
    {
        var path = Path.Combine(sitePath, ".gitignore");
        const string defaults = """
# Hugo
/public/
/resources/
/.hugo_build.lock
.DS_Store
Thumbs.db
""";
        if (!File.Exists(path))
        {
            File.WriteAllText(path, defaults);
            return;
        }

        var text = File.ReadAllText(path);
        if (!text.Contains("/public/", StringComparison.Ordinal))
            File.AppendAllText(path, "\n/public/\n");
        if (!text.Contains("/resources/", StringComparison.Ordinal))
            File.AppendAllText(path, "\n/resources/\n");
    }

    public async Task EnsureGitHubActionsWorkflowAsync(
        string sitePath,
        CancellationToken cancellationToken = default)
    {
        var dir = Path.Combine(sitePath, ".github", "workflows");
        Directory.CreateDirectory(dir);
        var workflow = Path.Combine(dir, "hugo.yml");

        if (!File.Exists(workflow))
        {
            await File.WriteAllTextAsync(workflow, DefaultHugoPagesWorkflow, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task EnsureHostingWorkflowAsync(
        string sitePath,
        GitHostingProvider provider,
        CancellationToken cancellationToken = default)
    {
        switch (provider)
        {
            case GitHostingProvider.GitHub:
                await EnsureGitHubActionsWorkflowAsync(sitePath, cancellationToken).ConfigureAwait(false);
                break;
            case GitHostingProvider.GitLab:
                await EnsureGitLabPagesWorkflowAsync(sitePath, cancellationToken).ConfigureAwait(false);
                break;
            case GitHostingProvider.Codeberg:
            case GitHostingProvider.Bitbucket:
                EnsureHostingNotes(sitePath, provider);
                break;
        }
    }

    private static async Task EnsureGitLabPagesWorkflowAsync(
        string sitePath,
        CancellationToken cancellationToken)
    {
        var workflow = Path.Combine(sitePath, ".gitlab-ci.yml");
        if (!File.Exists(workflow))
        {
            await File.WriteAllTextAsync(workflow, DefaultGitLabPagesWorkflow, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var current = await File.ReadAllTextAsync(workflow, cancellationToken).ConfigureAwait(false);
        if (!GitLabPagesWorkflowPolicy.ShouldRewrite(current))
            return;

        var backup = workflow + ".hugoer.bak";
        if (!File.Exists(backup))
            File.Copy(workflow, backup);
        await File.WriteAllTextAsync(workflow, DefaultGitLabPagesWorkflow, cancellationToken)
            .ConfigureAwait(false);
    }

    private static void EnsureHostingNotes(string sitePath, GitHostingProvider provider)
    {
        var dir = Path.Combine(sitePath, "docs");
        Directory.CreateDirectory(dir);
        var file = provider == GitHostingProvider.Codeberg
            ? Path.Combine(dir, "codeberg-pages.md")
            : Path.Combine(dir, "bitbucket-static-website.md");
        if (File.Exists(file))
            return;

        File.WriteAllText(file, provider == GitHostingProvider.Codeberg
            ? CodebergPagesNotes
            : BitbucketPagesNotes);
    }

    public async Task<CommandResult> CommitAllAsync(
        string sitePath,
        string message,
        CancellationToken cancellationToken = default)
    {
        await ProcessRunner.RunAsync("git", "add -A", sitePath, 60_000, cancellationToken).ConfigureAwait(false);
        var status = await ProcessRunner.RunAsync("git", "status --porcelain", sitePath, 30_000, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(status.StdOut))
            return new CommandResult { ExitCode = 0, StdOut = "沒有需要提交的變更。" };

        var env = new Dictionary<string, string?>();
        var email = await ProcessRunner.RunAsync("git", "config user.email", sitePath, 10_000, cancellationToken)
            .ConfigureAwait(false);
        if (!email.Succeeded || string.IsNullOrWhiteSpace(email.StdOut))
        {
            env["GIT_AUTHOR_NAME"] = "Hugoer";
            env["GIT_AUTHOR_EMAIL"] = "hugoer@local";
            env["GIT_COMMITTER_NAME"] = "Hugoer";
            env["GIT_COMMITTER_EMAIL"] = "hugoer@local";
        }

        var msg = message.Replace("\"", "'");
        return await ProcessRunner.RunAsync(
            "git",
            $"commit -m \"{msg}\"",
            sitePath,
            60_000,
            cancellationToken,
            env).ConfigureAwait(false);
    }

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
        await CommitAllAsync(sitePath, "Initial commit via Hugoer", cancellationToken).ConfigureAwait(false);

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
                $"repo create \"{repoName}\" --source=. --remote=origin --{visibility} --push",
                sitePath,
                180_000,
                cancellationToken).ConfigureAwait(false);

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
                    "Publish site via Hugoer",
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

        if (target.Provider == GitHostingProvider.Bitbucket)
            return await PublishBitbucketStaticWebsiteAsync(sitePath, target, progress, cancellationToken)
                .ConfigureAwait(false);

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
                "git", $"remote add origin \"{target.CanonicalUrl}\"", sitePath, 15_000, cancellationToken)
                .ConfigureAwait(false);
            if (!addRemote.Succeeded) return addRemote;
        }

        progress?.Report("抓取遠端預設分支…");
        var fetch = await ProcessRunner.RunAsync(
            "git", "fetch origin --prune", sitePath, 120_000, cancellationToken).ConfigureAwait(false);
        if (!fetch.Succeeded) return fetch;

        var remoteHead = await ProcessRunner.RunAsync(
            "git", "ls-remote --symref origin HEAD", sitePath, 30_000, cancellationToken).ConfigureAwait(false);
        var branchMatch = RemoteHeadRegex().Match(remoteHead.StdOut);
        var remoteBranch = branchMatch.Success ? branchMatch.Groups["branch"].Value : "main";
        if (!GitBranchRegex().IsMatch(remoteBranch))
            return new CommandResult { ExitCode = -1, StdErr = "遠端預設分支名稱格式不安全，已停止操作。" };
        var remoteHasCommit = RemoteHeadCommitRegex().IsMatch(remoteHead.StdOut);

        var localHead = await ProcessRunner.RunAsync(
            "git", "rev-parse --verify HEAD", sitePath, 10_000, cancellationToken).ConfigureAwait(false);
        if (remoteHasCommit && !localHead.Succeeded)
        {
            progress?.Report($"以遠端 {remoteBranch} 為基準，保留本機未追蹤網站檔案…");
            var checkout = await ProcessRunner.RunAsync(
                "git", $"checkout -B \"{remoteBranch}\" --track \"origin/{remoteBranch}\"",
                sitePath, 30_000, cancellationToken).ConfigureAwait(false);
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

        progress?.Report($"加入 {target.PagesProductName} 部署設定並提交網站…");
        await EnsureHostingWorkflowAsync(sitePath, target.Provider, cancellationToken).ConfigureAwait(false);
        var markerError = await PrepareDeploymentMarkerAsync(sitePath, progress, cancellationToken).ConfigureAwait(false);
        if (markerError is not null) return markerError;
        var commit = await CommitAllAsync(sitePath, commitMessage, cancellationToken).ConfigureAwait(false);
        if (!commit.Succeeded) return commit;

        var push = await PushHeadToOriginBranchAsync(
                sitePath,
                remoteBranch,
                $"推送到 {target.Owner}/{target.Repository}…",
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        if (!push.Succeeded) return push;

        if (target.Provider == GitHostingProvider.GitHub)
        {
            progress?.Report("啟用 GitHub Pages（Actions）…");
            return await EnablePagesFromActionsAsync(sitePath, cancellationToken).ConfigureAwait(false);
        }

        if (target.Provider == GitHostingProvider.Codeberg)
            return await PublishCodebergPagesBranchAsync(sitePath, target, progress, cancellationToken)
                .ConfigureAwait(false);

        return ProviderPushResult(target);
    }

    public async Task<CommandResult> PushAsync(
        string sitePath,
        string commitMessage = "Update site via Hugoer",
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var info = await GetInfoAsync(sitePath, cancellationToken).ConfigureAwait(false);
        var provider = info.Provider ?? GitHostingProvider.GitHub;

        if (provider == GitHostingProvider.Bitbucket
            && !string.IsNullOrWhiteSpace(info.RemoteUrl))
        {
            var target = ParseRemoteTarget(info.RemoteUrl);
            if (target.IsValid)
                return await PublishBitbucketStaticWebsiteAsync(sitePath, target, progress, cancellationToken)
                    .ConfigureAwait(false);
        }

        progress?.Report("提交變更…");
        await EnsureHostingWorkflowAsync(sitePath, provider, cancellationToken).ConfigureAwait(false);
        var markerError = await PrepareDeploymentMarkerAsync(sitePath, progress, cancellationToken).ConfigureAwait(false);
        if (markerError is not null) return markerError;
        var commit = await CommitAllAsync(sitePath, commitMessage, cancellationToken).ConfigureAwait(false);
        progress?.Report(commit.CombinedOutput);

        var branch = string.IsNullOrWhiteSpace(info.Branch) ? "main" : info.Branch;
        if (!GitBranchRegex().IsMatch(branch))
            return new CommandResult { ExitCode = -1, StdErr = "目前分支名稱格式不安全，已停止推送。" };

        var sync = await SyncOriginBranchBeforePushAsync(sitePath, branch, progress, cancellationToken)
            .ConfigureAwait(false);
        if (!sync.Succeeded) return sync;

        var push = await PushHeadToOriginBranchAsync(
                sitePath,
                branch,
                "git push…",
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        if (!push.Succeeded)
            return push;

        if (provider == GitHostingProvider.Codeberg
            && !string.IsNullOrWhiteSpace(info.RemoteUrl))
        {
            var target = ParseRemoteTarget(info.RemoteUrl);
            if (target.IsValid)
                return await PublishCodebergPagesBranchAsync(sitePath, target, progress, cancellationToken)
                    .ConfigureAwait(false);
        }

        return push;
    }

    private async Task<CommandResult> PublishBitbucketStaticWebsiteAsync(
        string sitePath,
        GitHubRepositoryTarget target,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!target.IsUserOrOrganizationSite)
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "Bitbucket Cloud 靜態網站 repository 必須命名為 <workspace>.bitbucket.io。請改用正確的 Bitbucket Pages repository。"
            };
        }

        var pagesUrl = target.PagesUrl;
        if (string.IsNullOrWhiteSpace(pagesUrl))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "無法推算 Bitbucket 靜態網站網址，請確認 repository 是否為 <workspace>.bitbucket.io。"
            };
        }

        progress?.Report("建立 Bitbucket 靜態網站輸出…");
        var build = await ProcessRunner.RunAsync(
            "hugo",
            $"--gc --minify --baseURL {QuoteArg(pagesUrl)}",
            sitePath,
            180_000,
            cancellationToken).ConfigureAwait(false);
        if (!build.Succeeded)
        {
            return new CommandResult
            {
                ExitCode = build.ExitCode,
                StdErr = $"Bitbucket 靜態網站輸出建置失敗。\n{build.CombinedOutput}"
            };
        }

        var publicDir = Path.Combine(sitePath, "public");
        if (!File.Exists(Path.Combine(publicDir, "index.html")))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "Bitbucket 靜態網站需要 public/index.html，但 Hugo build 後沒有找到這個檔案。"
            };
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"hugoer-bitbucket-pages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var init = await ProcessRunner.RunAsync(
                "git", "init -b main", tempRoot, 30_000, cancellationToken).ConfigureAwait(false);
            if (!init.Succeeded) return init;

            var addRemote = await ProcessRunner.RunAsync(
                "git", $"remote add origin {QuoteArg(target.CanonicalUrl!)}",
                tempRoot,
                15_000,
                cancellationToken).ConfigureAwait(false);
            if (!addRemote.Succeeded) return addRemote;

            var fetchMain = await ProcessRunner.RunAsync(
                "git", "fetch origin main", tempRoot, 60_000, cancellationToken).ConfigureAwait(false);
            if (fetchMain.Succeeded)
            {
                var checkout = await ProcessRunner.RunAsync(
                    "git", "checkout -B main FETCH_HEAD", tempRoot, 30_000, cancellationToken)
                    .ConfigureAwait(false);
                if (!checkout.Succeeded) return checkout;
            }

            ClearDirectoryExceptGit(tempRoot);
            CopyDirectory(publicDir, tempRoot);

            progress?.Report("提交 Bitbucket 靜態網站檔案…");
            var commit = await CommitAllAsync(
                tempRoot,
                "Publish Bitbucket static website via Hugoer",
                cancellationToken).ConfigureAwait(false);
            if (!commit.Succeeded) return commit;

            progress?.Report("推送 Bitbucket 靜態網站到 main 分支…");
            var push = await ProcessRunner.RunAsync(
                "git", "push -u origin HEAD:main", tempRoot, 180_000, cancellationToken)
                .ConfigureAwait(false);
            if (!push.Succeeded)
                return WithGitPushHint(push);

            return new CommandResult
            {
                ExitCode = 0,
                StdOut =
                    $"已推送 Bitbucket 靜態網站：{target.NameWithOwner}\n" +
                    $"網站網址：{pagesUrl}\n" +
                    "Bitbucket Cloud 會直接從 repository 根目錄提供 index.html；更新可能因快取最多延遲約 15 分鐘。"
            };
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
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
            "git", $"rev-parse --verify \"origin/{branch}\"", sitePath, 10_000, cancellationToken)
            .ConfigureAwait(false);
        if (!remoteCommit.Succeeded)
            return new CommandResult { ExitCode = 0, StdOut = $"遠端尚無 {branch} 分支，將建立新分支。" };

        var mergeBase = await ProcessRunner.RunAsync(
            "git", $"merge-base --is-ancestor \"origin/{branch}\" HEAD", sitePath, 10_000, cancellationToken)
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
            "git", $"merge \"origin/{branch}\" --allow-unrelated-histories --no-edit -X ours",
            sitePath,
            60_000,
            cancellationToken).ConfigureAwait(false);
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
            "git", $"push -u origin HEAD:\"{branch}\"", sitePath, 180_000, cancellationToken)
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
            "git", $"push -u origin HEAD:\"{branch}\"", sitePath, 180_000, cancellationToken)
            .ConfigureAwait(false);
        return retry.Succeeded ? retry : WithGitPushHint(retry);
    }

    private async Task<CommandResult> PublishCodebergPagesBranchAsync(
        string sitePath,
        GitHubRepositoryTarget target,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report("建立 Codeberg Pages 靜態輸出…");
        var build = await ProcessRunner.RunAsync(
            "hugo",
            "build",
            sitePath,
            180_000,
            cancellationToken).ConfigureAwait(false);
        if (!build.Succeeded)
        {
            return new CommandResult
            {
                ExitCode = build.ExitCode,
                StdErr = $"Codeberg Pages 靜態輸出建置失敗。\n{build.CombinedOutput}"
            };
        }

        var publicDir = Path.Combine(sitePath, "public");
        if (!File.Exists(Path.Combine(publicDir, "index.html")))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "Codeberg Pages 發布需要 public/index.html，但 Hugo build 後沒有找到這個檔案。"
            };
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"hugoer-codeberg-pages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var init = await ProcessRunner.RunAsync(
                "git", "init -b pages", tempRoot, 30_000, cancellationToken).ConfigureAwait(false);
            if (!init.Succeeded) return init;

            var addRemote = await ProcessRunner.RunAsync(
                "git", $"remote add origin {QuoteArg(target.CanonicalUrl!)}",
                tempRoot,
                15_000,
                cancellationToken).ConfigureAwait(false);
            if (!addRemote.Succeeded) return addRemote;

            var fetchPages = await ProcessRunner.RunAsync(
                "git", "fetch origin pages", tempRoot, 60_000, cancellationToken).ConfigureAwait(false);
            if (fetchPages.Succeeded)
            {
                var checkout = await ProcessRunner.RunAsync(
                    "git", "checkout -B pages FETCH_HEAD", tempRoot, 30_000, cancellationToken)
                    .ConfigureAwait(false);
                if (!checkout.Succeeded) return checkout;
            }

            ClearDirectoryExceptGit(tempRoot);
            CopyDirectory(publicDir, tempRoot);

            progress?.Report("提交 Codeberg Pages 靜態檔到 pages 分支…");
            var commit = await CommitAllAsync(
                tempRoot,
                "Publish Codeberg Pages via Hugoer",
                cancellationToken).ConfigureAwait(false);
            if (!commit.Succeeded) return commit;

            progress?.Report("推送 Codeberg Pages 靜態檔到 pages 分支…");
            var push = await ProcessRunner.RunAsync(
                "git", "push -u origin HEAD:pages", tempRoot, 180_000, cancellationToken)
                .ConfigureAwait(false);
            if (!push.Succeeded)
                return WithGitPushHint(push);

            return new CommandResult
            {
                ExitCode = 0,
                StdOut =
                    $"已推送到 Codeberg：{target.NameWithOwner}\n" +
                    "已將 Hugo 靜態輸出推送到 Codeberg Pages 的 pages 分支。\n" +
                    "若尚未設定 Webhook，請在 Codeberg repo Settings > Webhooks 新增 Forgejo webhook，Target URL 使用 Pages 網址，Branch filter 設為 pages。"
            };
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }
    }

    private static CommandResult WithGitPushHint(CommandResult result)
    {
        var hint = GitPushFailureClassifier.ToUserMessage(result.CombinedOutput);
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

    public async Task<CommandResult> EnablePagesFromActionsAsync(
        string sitePath,
        CancellationToken cancellationToken = default)
    {
        var info = await GetInfoAsync(sitePath, cancellationToken).ConfigureAwait(false);
        if (info.Provider is { } provider && provider != GitHostingProvider.GitHub)
        {
            return new CommandResult
            {
                ExitCode = 0,
                IsPartialSuccess = true,
                StdOut = $"{provider.PagesProductName()} 不能用 GitHub CLI 自動啟用。{ProviderManualSetupMessage(provider)}"
            };
        }

        if (string.IsNullOrWhiteSpace(info.Owner) || string.IsNullOrWhiteSpace(info.Repo))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "找不到 GitHub remote（origin）。請先建立或連結 repository。"
            };
        }

        var permission = await ProcessRunner.RunAsync(
            "gh",
            $"api repos/{info.Owner}/{info.Repo} --jq .permissions.admin",
            sitePath,
            30_000,
            cancellationToken).ConfigureAwait(false);

        if (!permission.Succeeded)
        {
            return new CommandResult
            {
                ExitCode = permission.ExitCode,
                StdErr = $"無法確認 GitHub Pages 管理權限。請確認 gh 已登入且 repository 可存取。\n{permission.CombinedOutput}"
            };
        }

        var canManagePages = permission.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        if (!canManagePages)
        {
            return new CommandResult
            {
                ExitCode = 0,
                IsPartialSuccess = true,
                StdOut = "網站檔案與 GitHub Actions workflow 已成功推送。",
                StdErr = $"目前登入帳號具有 {info.Owner}/{info.Repo} 的推送權限，但沒有管理 GitHub Pages 設定所需的 admin 權限。\n" +
                         "請 Repository 擁有者開啟 Settings > Pages，在 Build and deployment 的 Source 選擇 GitHub Actions；完成後回到 Hugoer 按「查詢 Pages 狀態」。"
            };
        }

        var current = await ProcessRunner.RunAsync(
            "gh",
            $"api repos/{info.Owner}/{info.Repo}/pages",
            sitePath,
            30_000,
            cancellationToken).ConfigureAwait(false);

        var pagesExist = current.Succeeded;
        if (!pagesExist
            && !current.CombinedOutput.Contains("404", StringComparison.OrdinalIgnoreCase)
            && !current.CombinedOutput.Contains("Not Found", StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        var method = pagesExist ? "PUT" : "POST";
        var update = await ProcessRunner.RunAsync(
            "gh",
            $"api -X {method} repos/{info.Owner}/{info.Repo}/pages -f build_type=workflow",
            sitePath,
            60_000,
            cancellationToken).ConfigureAwait(false);

        if (update.Succeeded)
        {
            return new CommandResult
            {
                ExitCode = 0,
                StdOut = pagesExist
                    ? "已將 GitHub Pages 建置來源更新為 GitHub Actions。"
                    : "已啟用 GitHub Pages（GitHub Actions）。"
            };
        }

        return new CommandResult
        {
            ExitCode = update.ExitCode,
            StdErr = $"無法將 GitHub Pages 設為 GitHub Actions。\n{update.CombinedOutput}"
        };
    }

    public async Task<GitHubPagesStatus> GetPagesStatusAsync(
        string sitePath,
        CancellationToken cancellationToken = default)
    {
        var info = await GetInfoAsync(sitePath, cancellationToken).ConfigureAwait(false);
        if (info.Provider is { } provider && provider != GitHostingProvider.GitHub)
        {
            var target = !string.IsNullOrWhiteSpace(info.RemoteUrl)
                ? ParseRemoteTarget(info.RemoteUrl)
                : new GitHubRepositoryTarget { IsValid = false };
            return new GitHubPagesStatus
            {
                Enabled = false,
                HtmlUrl = target.PagesUrl,
                Message = $"{provider.PagesProductName()} 狀態查詢尚未接入平台 API。{ProviderManualSetupMessage(provider)}"
            };
        }

        if (string.IsNullOrWhiteSpace(info.Owner) || string.IsNullOrWhiteSpace(info.Repo))
        {
            return new GitHubPagesStatus
            {
                Enabled = false,
                Message = "尚未連結 GitHub repository。"
            };
        }

        var result = await ProcessRunner.RunAsync(
            "gh",
            $"api repos/{info.Owner}/{info.Repo}/pages",
            sitePath,
            30_000,
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            if (result.CombinedOutput.Contains("404", StringComparison.OrdinalIgnoreCase)
                || result.CombinedOutput.Contains("Not Found", StringComparison.OrdinalIgnoreCase))
            {
                return new GitHubPagesStatus
                {
                    Enabled = false,
                    Message = "GitHub Pages 尚未啟用。"
                };
            }

            return new GitHubPagesStatus
            {
                Enabled = false,
                Message = result.CombinedOutput
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            var root = doc.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
            var htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
            var cname = root.TryGetProperty("cname", out var c) && c.ValueKind != JsonValueKind.Null
                ? c.GetString()
                : null;
            var buildType = root.TryGetProperty("build_type", out var b) ? b.GetString() : null;

            string? branch = null;
            string? path = null;
            if (root.TryGetProperty("source", out var source) && source.ValueKind == JsonValueKind.Object)
            {
                branch = source.TryGetProperty("branch", out var br) ? br.GetString() : null;
                path = source.TryGetProperty("path", out var p) ? p.GetString() : null;
            }

            var message = status switch
            {
                "built" => "網站已成功建置並上線。",
                "building" => "正在建置中…",
                "errored" => "建置發生錯誤，請檢查 Actions 日誌。",
                null => "GitHub Pages 已啟用。",
                _ => $"狀態：{status}"
            };
            if (buildType?.Equals("workflow", StringComparison.OrdinalIgnoreCase) == true)
            {
                var actionsMessage = await GetLatestWorkflowRunMessageAsync(sitePath, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(actionsMessage))
                    message = actionsMessage;
            }

            return new GitHubPagesStatus
            {
                Enabled = true,
                Status = status,
                HtmlUrl = htmlUrl,
                SourceBranch = branch,
                SourcePath = path,
                BuildType = buildType,
                Cname = cname,
                Message = message
            };
        }
        catch (Exception ex)
        {
            return new GitHubPagesStatus
            {
                Enabled = true,
                Message = $"無法解析 Pages 回應：{ex.Message}\n{result.StdOut}"
            };
        }
    }

    public async Task<GitHubPagesStatus> GetPagesStatusAsync(
        string sitePath,
        GitHubRepositoryTarget? selectedTarget,
        CancellationToken cancellationToken = default)
    {
        if (selectedTarget is not { IsValid: true })
            return await GetPagesStatusAsync(sitePath, cancellationToken).ConfigureAwait(false);

        if (selectedTarget.Provider != GitHostingProvider.GitHub)
            return ProviderPagesStatus(selectedTarget);

        return await GetPagesStatusAsync(sitePath, cancellationToken).ConfigureAwait(false);
    }

    private static GitHubPagesStatus ProviderPagesStatus(GitHubRepositoryTarget target) => new()
    {
        Enabled = false,
        HtmlUrl = target.PagesUrl,
        Message = $"{target.PagesProductName} 狀態查詢尚未接入平台 API。{ProviderManualSetupMessage(target.Provider)}"
    };

    public async Task<CommandResult> OpenGhAuthLoginAsync(CancellationToken cancellationToken = default)
    {
        return await ProcessRunner.RunAsync(
            "gh",
            "auth login --web --git-protocol https",
            timeoutMs: 300_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string?> GetLatestWorkflowRunMessageAsync(
        string sitePath,
        CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(
            "gh",
            "run list --workflow hugo.yml --limit 1 --json status,conclusion,updatedAt,url,displayTitle",
            sitePath,
            30_000,
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StdOut))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(result.StdOut);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
                return "GitHub Pages 已設定為 Actions，但尚未找到 hugo.yml 部署紀錄。";

            var run = root[0];
            var runStatus = run.TryGetProperty("status", out var s) ? s.GetString() : null;
            var conclusion = run.TryGetProperty("conclusion", out var c) ? c.GetString() : null;
            var updatedAt = run.TryGetProperty("updatedAt", out var u) ? u.GetString() : null;
            var url = run.TryGetProperty("url", out var link) ? link.GetString() : null;
            var timeText = DateTimeOffset.TryParse(updatedAt, out var parsedUpdatedAt)
                ? parsedUpdatedAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm")
                : updatedAt;

            var message = runStatus switch
            {
                "queued" => "GitHub Actions 最新部署正在排隊。",
                "in_progress" => "GitHub Actions 最新部署正在執行。",
                "completed" when conclusion == "success" => $"GitHub Actions 最新部署成功（{timeText}）。",
                "completed" when conclusion == "failure" => $"GitHub Actions 最新部署失敗（{timeText}）。",
                "completed" when conclusion == "cancelled" => $"GitHub Actions 最新部署已取消（{timeText}）。",
                "completed" => $"GitHub Actions 最新部署已完成：{conclusion ?? "未知結果"}（{timeText}）。",
                _ => $"GitHub Actions 最新部署狀態：{runStatus ?? "未知"}。"
            };

            return string.IsNullOrWhiteSpace(url) ? message : $"{message}\nActions：{url}";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<CommandResult> CloneRepositoryAsync(
        GitHubRepositoryTarget target,
        string destination,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var quotedDestination = QuoteArg(destination);
        if (target.Provider == GitHostingProvider.GitHub
            && await IsGhAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            var ghClone = await ProcessRunner.RunAsync(
                "gh",
                $"repo clone {target.Owner}/{target.Repository} {quotedDestination} -- --recurse-submodules",
                timeoutMs: 300_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (ghClone.Succeeded)
                return ghClone;

            progress?.Report("gh repo clone 失敗，改用 git clone…");
        }

        return await ProcessRunner.RunAsync(
            "git",
            $"clone --recurse-submodules {QuoteArg(target.CanonicalUrl!)} {quotedDestination}",
            timeoutMs: 300_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<CloneSiteResult?> TryOpenExistingCloneAsync(
        string destination,
        GitHubRepositoryTarget target,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(destination, ".git")))
            return null;

        var info = await GetInfoAsync(destination, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(info.Owner) || string.IsNullOrWhiteSpace(info.Repo)
            || !info.Owner.Equals(target.Owner, StringComparison.OrdinalIgnoreCase)
            || !info.Repo.Equals(target.Repository, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!PathHelper.LooksLikeHugoSite(destination))
        {
            var sourceBranch = await FindHugoSourceBranchAsync(destination, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(sourceBranch))
                return null;

            progress?.Report($"本機已有此 repository，改切換到 Hugo 來源分支 {sourceBranch}…");
            var checkout = await ProcessRunner.RunAsync(
                "git",
                $"checkout -B \"{sourceBranch}\" --track \"origin/{sourceBranch}\"",
                destination,
                30_000,
                cancellationToken).ConfigureAwait(false);
            if (!checkout.Succeeded || !PathHelper.LooksLikeHugoSite(destination))
                return null;
        }

        var message = $"本機已有此網站，已直接開啟：{destination}";
        progress?.Report(message);
        return CloneSiteResult.Ok(destination, target, message, openedExisting: true);
    }

    private async Task<string?> FindHugoSourceBranchAsync(string sitePath, CancellationToken cancellationToken)
    {
        var listed = await ProcessRunner.RunAsync(
            "git", "branch -r", sitePath, 15_000, cancellationToken).ConfigureAwait(false);
        if (!listed.Succeeded)
            return null;

        var branches = listed.StdOut
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseRemoteBranchName)
            .Where(branch => !string.IsNullOrWhiteSpace(branch) && GitBranchRegex().IsMatch(branch!))
            .Select(branch => branch!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(RankSourceBranch)
            .ThenBy(branch => branch, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var hasNonPagesBranch = branches.Any(branch => RankSourceBranch(branch) < 100);
        foreach (var branch in branches)
        {
            if (hasNonPagesBranch && RankSourceBranch(branch) >= 100)
                continue;

            var tree = await ProcessRunner.RunAsync(
                "git",
                $"ls-tree --name-only {QuoteArg("origin/" + branch)}",
                sitePath,
                15_000,
                cancellationToken).ConfigureAwait(false);
            if (!tree.Succeeded)
                continue;

            var names = tree.StdOut.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (GitHubRepositoryClassifier.LooksLikeHugo(names))
                return branch;
        }

        return null;
    }

    private static string? ParseRemoteBranchName(string line)
    {
        var value = line.Trim();
        if (value.Contains("->", StringComparison.Ordinal) || value.Equals("origin/HEAD", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!value.StartsWith("origin/", StringComparison.OrdinalIgnoreCase))
            return null;
        return value["origin/".Length..];
    }

    private static int RankSourceBranch(string branch) =>
        branch.ToLowerInvariant() switch
        {
            "main" => 0,
            "master" => 1,
            "hugo" => 2,
            "source" => 3,
            "gh-pages" => 100,
            "pages" => 101,
            _ => 10
        };

    private static string QuoteArg(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static GitHubRepositoryTarget ParseRemoteTarget(string url)
    {
        var parsed = ParseRepositoryTarget(url);
        if (parsed.IsValid)
            return parsed;

        var ssh = KnownSshRemoteRegex().Match(url.Trim());
        if (!ssh.Success)
            return parsed;

        var host = ssh.Groups["host"].Value;
        var path = ssh.Groups["path"].Value.Trim('/');
        return ParseRepositoryTarget($"https://{host}/{path}");
    }

    private static (string? Owner, string? Repo) ParseGitHubRemote(string url)
    {
        var m = GitHubRemoteRegex().Match(url);
        if (!m.Success) return (null, null);
        return (m.Groups["owner"].Value, m.Groups["repo"].Value);
    }

    private static CommandResult ProviderPushResult(GitHubRepositoryTarget target) => new()
    {
        ExitCode = 0,
        IsPartialSuccess = target.Provider is GitHostingProvider.Codeberg or GitHostingProvider.Bitbucket,
        StdOut =
            $"已推送到 {target.ProviderName}：{target.NameWithOwner}\n" +
            ProviderManualSetupMessage(target.Provider)
    };

    private static string ProviderManualSetupMessage(GitHostingProvider provider) => provider switch
    {
        GitHostingProvider.GitLab =>
            "已加入 .gitlab-ci.yml；GitLab 會透過 CI/CD 的 pages job 發布 public/。請到 GitLab Pipelines/Pages 查看部署結果。" +
            "若開啟網站出現 401/403，請到 Settings > General > Visibility, project features, permissions，將 Pages access control 設為 Everyone，或把專案/Pages 調整為可公開瀏覽。",
        GitHostingProvider.Codeberg =>
            "Codeberg Pages 需要 pages branch 與 Webhook / Forgejo Actions 設定。Hugoer 已加入 docs/codeberg-pages.md 操作提示，請依 Codeberg Pages 後台完成一次設定。",
        GitHostingProvider.Bitbucket =>
            "Bitbucket Cloud 的靜態網站使用 <workspace>.bitbucket.io repository，Hugoer 會建置 Hugo 並把 public/ 靜態檔推送到 repository 根目錄。",
        _ => "請到平台後台查看部署結果。"
    };

    internal static List<string> ParseRepositoryContentNames(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var names = new List<string>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        names.Add(value);
                    continue;
                }

                if (element.ValueKind == JsonValueKind.Object
                    && element.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String)
                {
                    var value = name.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                        names.Add(value);
                }
            }

            return names;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool IsNotFound(CommandResult result) =>
        ContainsAny(result.CombinedOutput, "404", "Not Found");

    private static bool IsEmptyRepository(CommandResult result) =>
        ContainsAny(result.CombinedOutput, "This repository is empty", "Git Repository is empty");

    private static bool LooksLikeNameExistsError(CommandResult result) =>
        ContainsAny(
            result.CombinedOutput,
            "Name already exists on this account",
            "name already exists on this account",
            "already exists on this account");

    private static bool ContainsAny(string text, params string[] tokens) =>
        tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));

    private const string DefaultHugoPagesWorkflow = """
# Sample workflow for building and deploying a Hugo site to GitHub Pages
name: Deploy Hugo site to Pages

on:
  push:
    branches: ["main", "master"]
  workflow_dispatch:

permissions:
  contents: read
  pages: write
  id-token: write

concurrency:
  group: "pages"
  cancel-in-progress: false

defaults:
  run:
    shell: bash

jobs:
  build:
    runs-on: ubuntu-latest
    env:
      HUGO_VERSION: 0.164.0
    steps:
      - name: Checkout
        uses: actions/checkout@v4
        with:
          submodules: recursive
          fetch-depth: 0

      - name: Install Hugo CLI
        run: |
          wget -O ${{ runner.temp }}/hugo.tar.gz \
            https://github.com/gohugoio/hugo/releases/download/v${HUGO_VERSION}/hugo_extended_${HUGO_VERSION}_linux-amd64.tar.gz
          tar -xzf ${{ runner.temp }}/hugo.tar.gz -C ${{ runner.temp }}
          sudo mv ${{ runner.temp }}/hugo /usr/local/bin/hugo
          hugo version

      - name: Setup Pages
        id: pages
        uses: actions/configure-pages@v5

      - name: Build with Hugo
        env:
          HUGO_CACHEDIR: ${{ runner.temp }}/hugo_cache
          HUGO_ENVIRONMENT: production
          TZ: Asia/Taipei
        run: |
          hugo \
            --gc \
            --minify \
            --baseURL "${{ steps.pages.outputs.base_url }}/"

      - name: Upload artifact
        uses: actions/upload-pages-artifact@v3
        with:
          path: ./public

  deploy:
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    runs-on: ubuntu-latest
    needs: build
    steps:
      - name: Deploy to GitHub Pages
        id: deployment
        uses: actions/deploy-pages@v4
""";

private const string DefaultGitLabPagesWorkflow = """
# Build and deploy a Hugo site to GitLab Pages
image: debian:bookworm-slim

variables:
  HUGO_VERSION: "0.165.0"
  HUGO_ENVIRONMENT: production
  GIT_SUBMODULE_STRATEGY: recursive
  TZ: Asia/Taipei

pages:
  stage: deploy
  before_script:
    - apt-get update
    - apt-get install -y --no-install-recommends ca-certificates wget tar
    - wget -O /tmp/hugo.tar.gz "https://github.com/gohugoio/hugo/releases/download/v${HUGO_VERSION}/hugo_extended_${HUGO_VERSION}_linux-amd64.tar.gz"
    - tar -xzf /tmp/hugo.tar.gz -C /tmp
    - mv /tmp/hugo /usr/local/bin/hugo
    - hugo version
  script:
    - hugo --gc --minify --baseURL "${CI_PAGES_URL}/"
  artifacts:
    paths:
      - public
  rules:
    - if: $CI_COMMIT_BRANCH == $CI_DEFAULT_BRANCH
""";

    private const string CodebergPagesNotes = """
# Codeberg Pages deployment notes

Hugoer can clone and push this repository to Codeberg. When you deploy, Hugoer also builds the Hugo site and pushes the generated static files to the `pages` branch.

Codeberg Pages still needs one platform-side setup:

1. User / organization site: use a repository named `pages`, publish from a branch named `pages`, and set the Pages webhook target to `https://<user>.codeberg.page/`.
2. Repository site: publish from a branch named `pages`, and set the Pages webhook target to `https://<user>.codeberg.page/<repository>/`.
3. After the webhook or Forgejo Actions workflow is configured on Codeberg, push updates from Hugoer again.

Hugoer keeps Hugo source in the normal Git branch and publishes build output to the `pages` branch.
""";

    private const string BitbucketPagesNotes = """
# Bitbucket static website deployment notes

Hugoer can clone and push this repository to Bitbucket. Bitbucket Cloud static websites are workspace-level sites:

1. The repository that serves the website must be named `<workspace>.bitbucket.io`.
2. The live URL is `https://<workspace>.bitbucket.io/`.
3. Hugoer builds the Hugo site and publishes the generated `public/` output to the website repository root.

Bitbucket does not provide GitHub-style per-project Pages URLs.
""";

    [GeneratedRegex(@"github\.com[:/](?<owner>[^/]+)/(?<repo>[^/\s]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubRemoteRegex();

    [GeneratedRegex(@"^git@(?<host>github\.com|gitlab\.com|codeberg\.org|bitbucket\.org):(?<path>.+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
    private static partial Regex KnownSshRemoteRegex();

    [GeneratedRegex(@"ref:\s+refs/heads/(?<branch>[^\s]+)\s+HEAD")]
    private static partial Regex RemoteHeadRegex();

    [GeneratedRegex(@"(?m)^[0-9a-f]{40,64}\s+HEAD$")]
    private static partial Regex RemoteHeadCommitRegex();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._/-]*$")]
    private static partial Regex GitBranchRegex();
}
