using System.Text.Json;
using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.Services;

public sealed partial class GitHubService
{
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

    public async Task<CommandResult> EnablePagesFromActionsAsync(
        string sitePath,
        CancellationToken cancellationToken = default)
    {
        var info = await GetInfoAsync(sitePath, cancellationToken).ConfigureAwait(false);
        var target = !string.IsNullOrWhiteSpace(info.RemoteUrl)
            ? ParseRemoteTarget(info.RemoteUrl)
            : new GitHubRepositoryTarget { IsValid = false };
        if (target.IsValid && target.Provider != GitHostingProvider.GitHub)
        {
            await EnsureHostingWorkflowAsync(sitePath, target.Provider, cancellationToken).ConfigureAwait(false);
            if (StaticPagesDeployment.ShouldPublishOutputBranch(target))
                return await PublishStaticOutputBranchAsync(sitePath, target, progress: null, cancellationToken)
                    .ConfigureAwait(false);

            return new CommandResult
            {
                ExitCode = 0,
                IsPartialSuccess = true,
                StdOut = ProviderManualSetupMessage(target.Provider)
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
            return ProviderPagesStatus(target.IsValid
                ? target
                : new GitHubRepositoryTarget
                {
                    IsValid = false,
                    Provider = provider,
                    PagesUrl = null
                });
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

    private static GitHubPagesStatus ProviderPagesStatus(GitHubRepositoryTarget target)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(target.PagesUrl);
        return new GitHubPagesStatus
        {
            Enabled = hasUrl,
            HtmlUrl = target.PagesUrl,
            Status = hasUrl ? "configured" : "manual",
            BuildType = target.Provider switch
            {
                GitHostingProvider.GitLab => "gitlab-ci",
                GitHostingProvider.Codeberg => "pages-branch",
                GitHostingProvider.Bitbucket => target.IsUserOrOrganizationSite ? "static-root" : "git-push",
                _ => null
            },
            Message = hasUrl
                ? $"{target.PagesProductName} 建議網址：{target.PagesUrl}\n{ProviderManualSetupMessage(target.Provider)}"
                : ProviderManualSetupMessage(target.Provider)
        };
    }

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
}
