using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Hugoer.ViewModels;

public partial class GitHubViewModel : PageViewModelBase
{
    public GitHubViewModel()
    {
        Title = "GitHub Pages";
    }

    [ObservableProperty]
    public partial string GitStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GhStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RemoteSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string PagesSummary { get; set; } = "尚未查詢";

    [ObservableProperty]
    public partial string? PagesUrl { get; set; }

    [ObservableProperty]
    public partial string RepoName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RepositoryUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RepositoryTargetSummary { get; set; } = "貼上既有 GitHub repository 網址後，Hugoer 會先顯示目標與 Pages 網址。";

    [ObservableProperty]
    public partial bool CanConnectRepository { get; set; }

    [ObservableProperty]
    public partial bool SyncRecommendedBaseUrl { get; set; } = true;

    [ObservableProperty]
    public partial bool IsPublicRepo { get; set; } = true;

    [ObservableProperty]
    public partial string CommitMessage { get; set; } = "Update site via Hugoer";

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    partial void OnRepositoryUrlChanged(string value)
    {
        var target = Hugoer.Services.GitHubService.ParseRepositoryTarget(value);
        CanConnectRepository = target.IsValid;
        if (!target.IsValid)
        {
            RepositoryTargetSummary = string.IsNullOrWhiteSpace(value)
                ? "貼上既有 GitHub repository 網址後，Hugoer 會先顯示目標與 Pages 網址。"
                : target.ErrorMessage;
            return;
        }

        RepoName = target.Repository!;
        RepositoryTargetSummary =
            $"Repository：{target.Owner}/{target.Repository}\n" +
            $"網站類型：{(target.IsUserOrOrganizationSite ? "使用者／組織網站" : "專案網站")}\n" +
            $"建議 Pages 網址：{target.PagesUrl}";
    }

    public override async Task OnNavigatedToAsync()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var gitOk = await Services.GitHub.IsGitAvailableAsync();
            var ghOk = await Services.GitHub.IsGhAvailableAsync();
            GitStatus = gitOk ? "Git：已安裝" : "Git：未找到（請安裝 Git for Windows）";
            GhStatus = ghOk ? "GitHub CLI (gh)：已安裝" : "GitHub CLI：未找到（請安裝 gh）";

            if (!RequireSite(out var site))
            {
                RemoteSummary = "尚未選擇網站";
                return;
            }

            if (string.IsNullOrWhiteSpace(RepoName))
                RepoName = Path.GetFileName(site.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var info = await Services.GitHub.GetInfoAsync(site);
            if (string.IsNullOrWhiteSpace(RepositoryUrl) && !string.IsNullOrWhiteSpace(info.RemoteUrl))
                RepositoryUrl = info.RemoteUrl;
            RemoteSummary =
                $"使用者：{info.GhUser ?? "（未登入）"}\n" +
                $"驗證：{(info.GhAuthenticated ? "已登入" : "未登入")}\n" +
                $"分支：{info.Branch ?? "—"}\n" +
                $"Remote：{info.RemoteUrl ?? "（無 origin）"}\n" +
                $"Repo：{(info.Owner is null ? "—" : $"{info.Owner}/{info.Repo}")}";

            await RefreshPagesStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConnectExistingRepositoryAsync()
    {
        if (!RequireSite(out var site)) return;
        var target = Hugoer.Services.GitHubService.ParseRepositoryTarget(RepositoryUrl);
        if (!target.IsValid)
        {
            StatusMessage = target.ErrorMessage;
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "正在確認 GitHub repository 推送權限…";
            var access = await Services.GitHub.CheckPushAccessAsync(target);
            AppendLog(access.Message);
            if (!access.HasAccess)
            {
                StatusMessage = access.Message;
                return;
            }

            if (SyncRecommendedBaseUrl && !string.IsNullOrWhiteSpace(target.PagesUrl))
            {
                await Services.GitHub.UpdateBaseUrlAsync(site, target.PagesUrl);
                AppendLog($"已將 baseURL 設為 {target.PagesUrl}");
            }

            StatusMessage = "正在以 production 設定驗證 Hugo 網站…";
            AppendLog("hugo build…");
            var build = await Services.Hugo.BuildAsync(site);
            AppendLog(build.CombinedOutput);
            if (!build.Succeeded)
            {
                StatusMessage = "建置失敗；尚未連結或推送 repository。";
                return;
            }

            var progress = new Progress<string>(message =>
            {
                AppendLog(message);
                StatusMessage = message;
            });
            var result = await Services.GitHub.ConnectExistingRepositoryAndPushAsync(
                site,
                target,
                string.IsNullOrWhiteSpace(CommitMessage) ? "Publish site via Hugoer" : CommitMessage.Trim(),
                progress);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Succeeded
                ? "已連結 repository、推送網站並啟用 GitHub Pages"
                : "連結或部署失敗；請查看操作日誌";
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsBusy = true;
        try
        {
            AppendLog("啟動 gh auth login…");
            StatusMessage = "請在瀏覽器完成 GitHub 登入";
            var result = await Services.GitHub.OpenGhAuthLoginAsync();
            AppendLog(result.CombinedOutput);
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateAndDeployAsync()
    {
        if (!RequireSite(out var site)) return;
        if (string.IsNullOrWhiteSpace(RepoName))
        {
            StatusMessage = "請輸入 repository 名稱";
            return;
        }

        var existingTarget = Hugoer.Services.GitHubService.ParseRepositoryTarget(RepoName);
        if (existingTarget.IsValid)
        {
            RepositoryUrl = RepoName.Trim();
            AppendLog($"偵測到既有 repository 網址，改用安全連結流程：{existingTarget.Owner}/{existingTarget.Repository}");
            await ConnectExistingRepositoryAsync();
            return;
        }

        if (RepoName.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            || RepoName.Contains('/')
            || RepoName.Contains('\\'))
        {
            StatusMessage = $"Repository 網址格式無效：{existingTarget.ErrorMessage}";
            AppendLog(StatusMessage);
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new Progress<string>(m =>
            {
                AppendLog(m);
                StatusMessage = m;
            });
            var result = await Services.GitHub.CreateRepoAndPushAsync(
                site, RepoName.Trim(), IsPublicRepo, progress);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Succeeded
                ? "已推送並嘗試啟用 GitHub Pages"
                : "部署過程有錯誤，請查看日誌";
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PushAsync()
    {
        if (!RequireSite(out var site)) return;

        var info = await Services.GitHub.GetInfoAsync(site);
        if (string.IsNullOrWhiteSpace(info.RemoteUrl))
        {
            var candidate = !string.IsNullOrWhiteSpace(RepositoryUrl) ? RepositoryUrl : RepoName;
            var target = Hugoer.Services.GitHubService.ParseRepositoryTarget(candidate);
            if (target.IsValid)
            {
                RepositoryUrl = candidate;
                AppendLog($"尚未設定 origin；改用安全連結流程：{target.Owner}/{target.Repository}");
                await ConnectExistingRepositoryAsync();
                return;
            }

            StatusMessage = "尚未連結 GitHub repository。請先在上方貼上完整 Repository URL，再按「連結、推送並啟用 Pages」。";
            AppendLog(StatusMessage);
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = "正在以 production 設定建置 Hugo 網站…";
            AppendLog("hugo build…");
            var build = await Services.Hugo.BuildAsync(site);
            AppendLog(build.CombinedOutput);
            if (!build.Succeeded)
            {
                StatusMessage = "建置失敗；已停止提交與推送。請依日誌修正網站內容。";
                return;
            }

            var progress = new Progress<string>(m =>
            {
                AppendLog(m);
                StatusMessage = m;
            });
            var result = await Services.GitHub.PushAsync(site, CommitMessage, progress);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Succeeded ? "推送完成" : "推送失敗";
            await RefreshPagesStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EnablePagesAsync()
    {
        if (!RequireSite(out var site)) return;
        IsBusy = true;
        try
        {
            await Services.GitHub.EnsureGitHubActionsWorkflowAsync(site);
            var result = await Services.GitHub.EnablePagesFromActionsAsync(site);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Succeeded ? "已請求啟用 GitHub Pages" : "啟用失敗";
            await RefreshPagesStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshPagesStatusAsync()
    {
        if (!RequireSite(out var site)) return;

        var status = await Services.GitHub.GetPagesStatusAsync(site);
        PagesUrl = status.HtmlUrl;
        PagesSummary =
            $"啟用：{(status.Enabled ? "是" : "否")}\n" +
            $"狀態：{status.Status ?? "—"}\n" +
            $"建置類型：{status.BuildType ?? "—"}\n" +
            $"來源分支：{status.SourceBranch ?? "—"}\n" +
            $"網址：{status.HtmlUrl ?? "—"}\n" +
            $"CNAME：{status.Cname ?? "—"}\n" +
            $"{status.Message}";
        StatusMessage = status.Message;
    }

    [RelayCommand]
    private void OpenPagesUrl()
    {
        if (string.IsNullOrWhiteSpace(PagesUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = PagesUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AddWorkflowOnlyAsync()
    {
        if (!RequireSite(out var site)) return;
        await Services.GitHub.EnsureGitHubActionsWorkflowAsync(site);
        StatusMessage = "已寫入 .github/workflows/hugo.yml";
        AppendLog(StatusMessage);
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var line = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";
        Log = string.IsNullOrEmpty(Log) ? line : Log + Environment.NewLine + line;
    }
}
