using System.Text.Json;
using System.Text.RegularExpressions;
using Hugoer.Models;

namespace Hugoer.Services;

public sealed partial class GitHubService
{
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
            var (owner, repo) = ParseGitHubRemote(data.RemoteUrl);
            data.Owner = owner;
            data.Repo = repo;
        }

        var auth = await ProcessRunner.RunAsync(
            "gh", "auth status", sitePath, 15_000, cancellationToken).ConfigureAwait(false);
        data.GhAuthenticated = auth.Succeeded
            || auth.CombinedOutput.Contains("Logged in", StringComparison.OrdinalIgnoreCase);

        var user = await ProcessRunner.RunAsync(
            "gh", "api user --jq .login", sitePath, 15_000, cancellationToken).ConfigureAwait(false);
        if (user.Succeeded)
            data.GhUser = user.StdOut.Trim();

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

        progress?.Report("提交檔案…");
        await CommitAllAsync(sitePath, "Initial commit via Hugoer", cancellationToken).ConfigureAwait(false);

        var visibility = isPublic ? "public" : "private";
        progress?.Report($"建立 GitHub repository：{repoName}…");

        var remote = await ProcessRunner.RunAsync(
            "git", "remote get-url origin", sitePath, 10_000, cancellationToken).ConfigureAwait(false);

        if (!remote.Succeeded)
        {
            var create = await ProcessRunner.RunAsync(
                "gh",
                $"repo create \"{repoName}\" --source=. --remote=origin --{visibility} --push",
                sitePath,
                180_000,
                cancellationToken).ConfigureAwait(false);

            if (!create.Succeeded)
                return create;
        }
        else
        {
            progress?.Report("推送到 origin…");
            var push = await ProcessRunner.RunAsync(
                "git", "push -u origin HEAD", sitePath, 180_000, cancellationToken).ConfigureAwait(false);
            if (!push.Succeeded)
                return push;
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

    public async Task<CommandResult> PushAsync(
        string sitePath,
        string commitMessage = "Update site via Hugoer",
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("提交變更…");
        await EnsureGitHubActionsWorkflowAsync(sitePath, cancellationToken).ConfigureAwait(false);
        var commit = await CommitAllAsync(sitePath, commitMessage, cancellationToken).ConfigureAwait(false);
        progress?.Report(commit.CombinedOutput);

        progress?.Report("git push…");
        return await ProcessRunner.RunAsync(
            "git", "push", sitePath, 180_000, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandResult> EnablePagesFromActionsAsync(
        string sitePath,
        CancellationToken cancellationToken = default)
    {
        var info = await GetInfoAsync(sitePath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(info.Owner) || string.IsNullOrWhiteSpace(info.Repo))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "找不到 GitHub remote（origin）。請先建立或連結 repository。"
            };
        }

        var create = await ProcessRunner.RunAsync(
            "gh",
            $"api -X POST repos/{info.Owner}/{info.Repo}/pages -f build_type=workflow",
            sitePath,
            60_000,
            cancellationToken).ConfigureAwait(false);

        if (create.Succeeded)
            return create;

        // Already enabled or needs update — try PUT
        var put = await ProcessRunner.RunAsync(
            "gh",
            $"api -X PUT repos/{info.Owner}/{info.Repo}/pages -f build_type=workflow",
            sitePath,
            60_000,
            cancellationToken).ConfigureAwait(false);

        if (put.Succeeded
            || create.CombinedOutput.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            || create.CombinedOutput.Contains("409", StringComparison.OrdinalIgnoreCase))
        {
            return put.Succeeded
                ? put
                : new CommandResult { ExitCode = 0, StdOut = "GitHub Pages 可能已啟用，請重新查詢狀態。" };
        }

        return put.ExitCode != 0 ? create : put;
    }

    public async Task<GitHubPagesStatus> GetPagesStatusAsync(
        string sitePath,
        CancellationToken cancellationToken = default)
    {
        var info = await GetInfoAsync(sitePath, cancellationToken).ConfigureAwait(false);
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

            return new GitHubPagesStatus
            {
                Enabled = true,
                Status = status,
                HtmlUrl = htmlUrl,
                SourceBranch = branch,
                SourcePath = path,
                BuildType = buildType,
                Cname = cname,
                Message = status switch
                {
                    "built" => "網站已成功建置並上線。",
                    "building" => "正在建置中…",
                    "errored" => "建置發生錯誤，請檢查 Actions 日誌。",
                    null => "GitHub Pages 已啟用。",
                    _ => $"狀態：{status}"
                }
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

    public async Task<CommandResult> OpenGhAuthLoginAsync(CancellationToken cancellationToken = default)
    {
        return await ProcessRunner.RunAsync(
            "gh",
            "auth login --web --git-protocol https",
            timeoutMs: 300_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static (string? Owner, string? Repo) ParseGitHubRemote(string url)
    {
        var m = GitHubRemoteRegex().Match(url);
        if (!m.Success) return (null, null);
        return (m.Groups["owner"].Value, m.Groups["repo"].Value);
    }

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
      HUGO_VERSION: 0.147.8
    steps:
      - name: Checkout
        uses: actions/checkout@v4
        with:
          submodules: recursive
          fetch-depth: 0

      - name: Setup Hugo
        uses: peaceiris/actions-hugo@v3
        with:
          hugo-version: ${{ env.HUGO_VERSION }}
          extended: true

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

    [GeneratedRegex(@"github\.com[:/](?<owner>[^/]+)/(?<repo>[^/.]+)(?:\.git)?", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubRemoteRegex();
}
