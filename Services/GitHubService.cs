using System.Globalization;
using System.Text.Json;
using System.Text;
using System.Text.RegularExpressions;
using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.Services;

public sealed partial class GitHubService
{
    public const string DefaultUpdateCommitMessage = GitProviderSettings.DefaultUpdateCommitMessage;

    private readonly DeploymentMonitorService _deploymentMonitor;
    private readonly HugoService? _hugo;
    private readonly TomlParamsService _params;

    public GitHubService(
        DeploymentMonitorService? deploymentMonitor = null,
        HugoService? hugo = null,
        TomlParamsService? paramsService = null)
    {
        _deploymentMonitor = deploymentMonitor ?? new DeploymentMonitorService();
        _hugo = hugo;
        _params = paramsService ?? new TomlParamsService();
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
        var updated = _params.UpsertSimpleRootKeys(original, new Dictionary<string, string>
        {
            ["baseURL"] = baseUrl
        });
        await AtomicFileWriter.WriteAllTextAsync(config, updated, cancellationToken).ConfigureAwait(false);
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
            if (string.IsNullOrWhiteSpace(target.CanonicalUrl))
                return (false, "找不到可檢查的 repository 網址。");

            var access = await ProcessRunner.RunAsync(
                "git",
                GitHostingAccessChecks.LsRemoteHeadArguments(target),
                timeoutMs: 60_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return GitHostingAccessChecks.FromLsRemoteResult(target, access);
        }

        var result = await ProcessRunner.RunAsync(
            "gh",
            $"api repos/{target.Owner}/{target.Repository} --jq .permissions.push",
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
        {
            var canPush = result.StdOut.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            if (canPush)
                return (true, $"已確認具有 {target.Owner}/{target.Repository} 的推送權限。");
        }

        // gh is convenient, but it is not the only supported GitHub
        // credential path. A user may be authenticated through Git
        // Credential Manager or SSH while gh is missing/expired. Probe the
        // repository with the same Git transport used by the actual push
        // before deciding that the remote route is unavailable.
        if (!string.IsNullOrWhiteSpace(target.CanonicalUrl))
        {
            var gitProbe = await ProcessRunner.RunAsync(
                "git",
                GitHostingAccessChecks.LsRemoteHeadArguments(target),
                timeoutMs: 60_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (gitProbe.Succeeded)
            {
                return (true,
                    $"gh 無法回報權限，但已確認 GitHub repository 可由本機 Git 存取；將直接使用 Git 推送（實際寫入仍由遠端 Git 驗證）。");
            }

            var ghMessage = result.Succeeded
                ? $"gh 回報目前登入帳號沒有 {target.Owner}/{target.Repository} 的推送權限。\n"
                : string.IsNullOrWhiteSpace(result.CombinedOutput)
                    ? string.Empty
                    : $"gh：{result.CombinedOutput}\n";
            var gitMessage = GitHostingProcessErrors.WithRepositoryAccessHint(
                GitHostingProvider.GitHub,
                "存取",
                gitProbe).CombinedOutput;
            return (false,
                $"無法確認 GitHub repository 權限。請確認 gh 已登入，或設定 Git Credential Manager／SSH。\n{ghMessage}{gitMessage}");
        }

        return (false,
            $"無法確認 repository 權限。請確認 gh 已登入，且 repository 存在或目前帳號可存取。\n{result.CombinedOutput}");
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
                    ["checkout", "-B", sourceBranch, "--track", $"origin/{sourceBranch}"],
                    workingDirectory: destination,
                    timeoutMs: 30_000,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
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
        var text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        var additions = new StringBuilder();
        if (!text.Contains("/public/", StringComparison.Ordinal))
            additions.AppendLine("/public/");
        if (!text.Contains("/resources/", StringComparison.Ordinal))
            additions.AppendLine("/resources/");

        if (string.IsNullOrEmpty(text))
        {
            AtomicFileWriter.WriteAllText(path, defaults);
            return;
        }

        if (additions.Length == 0)
            return;

        var separator = text.EndsWith('\n') ? string.Empty : Environment.NewLine;
        AtomicFileWriter.WriteAllText(path, text + separator + additions);
    }

    /// <summary>
    /// 產生帶日期與自動序號的提交訊息，格式：{baseMessage} YYYYMMDD-N。
    /// 序號依當天已有相同前綴的 commit 最大編號自動遞增。
    /// </summary>
    public async Task<string> NextDatedCommitMessageAsync(
        string sitePath,
        string baseMessage = DefaultUpdateCommitMessage,
        CancellationToken cancellationToken = default)
    {
        var normalizedBaseMessage = string.IsNullOrWhiteSpace(baseMessage)
            ? DefaultUpdateCommitMessage
            : baseMessage.Trim();
        var dateStr = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var prefix = $"{normalizedBaseMessage} {dateStr}-";

        var log = await ProcessRunner.RunAsync(
            "git",
            "log --format=%s --all",
            sitePath,
            30_000,
            cancellationToken).ConfigureAwait(false);

        long maxSequence = 0;
        if (log.Succeeded)
        {
            foreach (var subject in log.StdOut.Split(
                         ['\r', '\n'],
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!subject.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                var suffix = subject[prefix.Length..];
                if (long.TryParse(
                        suffix,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var sequence)
                    && sequence > maxSequence)
                {
                    maxSequence = sequence;
                }
            }
        }

        var nextSequence = maxSequence == long.MaxValue ? long.MaxValue : maxSequence + 1;
        return $"{prefix}{nextSequence.ToString(CultureInfo.InvariantCulture)}";
    }

    public static bool IsAutomaticCommitMessage(string? message) =>
        GitProviderSettings.IsAutomaticCommitMessage(message);

    public async Task<CommandResult> CommitAllAsync(
        string sitePath,
        string message,
        CancellationToken cancellationToken = default)
    {
        var add = await ProcessRunner.RunAsync("git", "add -A", sitePath, 60_000, cancellationToken)
            .ConfigureAwait(false);
        if (!add.Succeeded)
            return add;
        var status = await ProcessRunner.RunAsync("git", "status --porcelain", sitePath, 30_000, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(status.StdOut))
            return new CommandResult { ExitCode = 0, StdOut = "沒有需要提交的變更。" };

        var resolvedMessage = IsAutomaticCommitMessage(message)
            ? await NextDatedCommitMessageAsync(sitePath, cancellationToken: cancellationToken)
            : message.Trim();
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

        return await ProcessRunner.RunAsync(
            "git",
            ["commit", "-m", resolvedMessage],
            workingDirectory: sitePath,
            timeoutMs: 60_000,
            cancellationToken: cancellationToken,
            env: env).ConfigureAwait(false);
    }

    private async Task<CommandResult> CloneRepositoryAsync(
        GitHubRepositoryTarget target,
        string destination,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (target.Provider == GitHostingProvider.GitHub
            && await IsGhAvailableAsync(cancellationToken).ConfigureAwait(false))
        {
            var ghClone = await ProcessRunner.RunAsync(
                "gh",
                ["repo", "clone", $"{target.Owner}/{target.Repository}", destination, "--", "--recurse-submodules"],
                timeoutMs: 300_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (ghClone.Succeeded)
                return ghClone;

            progress?.Report("gh repo clone 失敗，改用 git clone…");
        }

        return await ProcessRunner.RunAsync(
            "git",
            ["clone", "--recurse-submodules", target.CanonicalUrl!, destination],
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
                ["checkout", "-B", sourceBranch, "--track", $"origin/{sourceBranch}"],
                workingDirectory: destination,
                timeoutMs: 30_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);
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
                ["ls-tree", "--name-only", "origin/" + branch],
                workingDirectory: sitePath,
                timeoutMs: 15_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);
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

    private static string ProviderManualSetupMessage(GitHostingProvider provider) => provider switch
    {
        GitHostingProvider.GitLab =>
            "已加入 .gitlab-ci.yml；GitLab 會透過 CI/CD 的 pages job 發布 public/。請到 GitLab Pipelines/Pages 查看部署結果。" +
            "若開啟網站出現 401/403，請到 Settings > General > Visibility, project features, permissions，將 Pages access control 設為 Everyone，或把專案/Pages 調整為可公開瀏覽。",
        GitHostingProvider.Codeberg =>
            "Hugoer 會把 Hugo 原始碼推到預設分支，並把靜態輸出推到 pages 分支。" +
            "請在 Codeberg repo Settings > Webhooks 新增 Forgejo webhook，Target URL 使用 Pages 網址，Branch filter 設為 pages。",
        GitHostingProvider.Bitbucket =>
            "Bitbucket Cloud 靜態網站必須使用 <workspace>.bitbucket.io repository；Hugoer 會建置 Hugo 並把 public/ 靜態檔推送到該 repository 根目錄。" +
            "其他 Bitbucket repository 會以一般 git push 保存 Hugo 原始碼。",
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
      # Keep tool versions pinned so a future runner image cannot silently
      # change the generated site.
      DART_SASS_VERSION: 1.102.0
      GO_VERSION: 1.26.5
      HUGO_VERSION: 0.165.0
      NODE_VERSION: 24.19.0
      TZ: Asia/Taipei
    steps:
      - name: Checkout
        uses: actions/checkout@v7
        with:
          submodules: recursive
          fetch-depth: 0
          lfs: false

      - name: Setup Pages
        id: pages
        uses: actions/configure-pages@v6

      - name: Create a local tools directory
        run: |
          mkdir -p "${HOME}/.local"

      - name: Install Go
        if: hashFiles('go.mod') != ''
        uses: actions/setup-go@v6
        with:
          go-version: ${{ env.GO_VERSION }}
          cache: false

      - name: Install Node.js
        if: hashFiles('package-lock.json') != ''
        uses: actions/setup-node@v6
        with:
          node-version: ${{ env.NODE_VERSION }}

      - name: Install Dart Sass
        run: |
          echo "Installing Dart Sass ${DART_SASS_VERSION}..."
          curl -sfL --output-dir "${{ runner.temp }}" -O "https://github.com/sass/dart-sass/releases/download/${DART_SASS_VERSION}/dart-sass-${DART_SASS_VERSION}-linux-x64.tar.gz"
          tar -C "${HOME}/.local" -xf "${{ runner.temp }}/dart-sass-${DART_SASS_VERSION}-linux-x64.tar.gz"
          echo "${HOME}/.local/dart-sass" >> "${GITHUB_PATH}"

      - name: Install Hugo Extended
        run: |
          echo "Installing Hugo Extended ${HUGO_VERSION}..."
          curl -sfL --output-dir "${{ runner.temp }}" -O "https://github.com/gohugoio/hugo/releases/download/v${HUGO_VERSION}/hugo_extended_${HUGO_VERSION}_linux-amd64.tar.gz"
          mkdir -p "${HOME}/.local/hugo"
          tar -C "${HOME}/.local/hugo" -xf "${{ runner.temp }}/hugo_extended_${HUGO_VERSION}_linux-amd64.tar.gz"
          echo "${HOME}/.local/hugo" >> "${GITHUB_PATH}"

      - name: Log tool versions
        run: |
          command -v sass &> /dev/null && echo "Dart Sass: $(sass --version)" || echo "Dart Sass: not installed"
          command -v go &> /dev/null && echo "Go: $(go version)" || echo "Go: not installed"
          command -v hugo &> /dev/null && echo "Hugo: $(hugo version)" || echo "Hugo: not installed"
          command -v node &> /dev/null && echo "Node.js: $(node --version)" || echo "Node.js: not installed"

      - name: Configure Git
        run: git config --global core.quotepath false

      - name: Fetch full Git history
        run: |
          if [[ $(git rev-parse --is-shallow-repository) == true ]]; then
            git fetch --unshallow
          fi

      - name: Initialize Git submodules
        run: |
          if [[ -f .gitmodules ]]; then
            git submodule update --init --recursive
          fi

      - name: Install Node.js dependencies
        run: |
          if [[ -f package-lock.json ]]; then
            npm ci
          fi

      - name: Cache restore
        id: cache-restore
        uses: actions/cache/restore@v6
        with:
          path: ${{ runner.temp }}/.cache/hugo
          key: hugo-${{ github.run_id }}
          restore-keys: hugo-

      - name: Build with Hugo
        env:
          HUGO_CACHEDIR: ${{ runner.temp }}/.cache/hugo
          HUGO_ENVIRONMENT: production
        run: |
          hugo build \
            --gc \
            --minify \
            --baseURL "${{ steps.pages.outputs.base_url }}/" \
            --cacheDir "${{ runner.temp }}/.cache/hugo"

      - name: Cache save
        uses: actions/cache/save@v6
        with:
          path: ${{ runner.temp }}/.cache/hugo
          key: ${{ steps.cache-restore.outputs.cache-primary-key }}

      - name: Upload artifact
        uses: actions/upload-pages-artifact@v5
        with:
          include-hidden-files: false
          path: ./public

  deploy:
    runs-on: ubuntu-latest
    needs: build
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    steps:
      - name: Deploy to GitHub Pages
        id: deployment
        uses: actions/deploy-pages@v5
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

Hugoer clones and pushes Hugo source to the default branch (`main`), then builds the site and publishes `public/` to the `pages` branch.

Codeberg Pages still needs one platform-side setup:

1. User / organization site: use a repository named `pages`, publish from a branch named `pages`, and set the Pages webhook target to `https://<user>.codeberg.page/`.
2. Repository site: publish from a branch named `pages`, and set the Pages webhook target to `https://<user>.codeberg.page/<repository>/`.
3. After the webhook or Forgejo Actions workflow is configured on Codeberg, push updates from Hugoer again.

If the remote HEAD is already `pages`, Hugoer keeps Hugo source on `main` and does not merge the static `pages` branch into the source tree.
""";

    private const string BitbucketPagesNotes = """
# Bitbucket static website deployment notes

Hugoer can clone and push this repository to Bitbucket.

Bitbucket Cloud static websites are workspace-level sites:

1. The repository that serves the website must be named `<workspace>.bitbucket.io`.
2. The live URL is `https://<workspace>.bitbucket.io/`.
3. Hugoer builds the Hugo site and publishes the generated `public/` output to the website repository root. Local Hugo source stays on this computer unless you also keep a separate source repository.

Other Bitbucket repositories receive a normal `git push` of the Hugo source. Bitbucket does not provide GitHub-style per-project Pages URLs.
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
