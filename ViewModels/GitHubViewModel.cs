using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.ViewModels;

public partial class GitHubViewModel : PageViewModelBase, IDisposable
{
    private static readonly TimeSpan DeploymentCheckInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan[] PostPushDeploymentCheckDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(20)
    ];
    private readonly SemaphoreSlim _deploymentCheckGate = new(1, 1);
    private CancellationTokenSource? _deploymentMonitorCts;
    private DeploymentVersionState? _lastDeploymentState;
    private string? _lastExpectedDeploymentId;
    private GitHostingProvider _lastDeploymentProvider = GitHostingProvider.GitHub;
    private string? _lastDeploymentPagesUrl;
    private GitHostingProvider _activeProvider = GitHostingProvider.GitHub;
    private bool _switchingProviderSettings;
    private bool _providerWasSelectedByUser;

    public IReadOnlyList<GitHostingProviderOption> ProviderOptions { get; } =
    [
        new()
        {
            Provider = GitHostingProvider.GitHub,
            DisplayName = "GitHub",
            Hint = "使用 gh CLI 或 Git credential manager；可自動設定 GitHub Pages。"
        },
        new()
        {
            Provider = GitHostingProvider.GitLab,
            DisplayName = "GitLab",
            Hint = "使用 Git 憑證；推送時會加入 GitLab Pages CI（.gitlab-ci.yml）。"
        },
        new()
        {
            Provider = GitHostingProvider.Codeberg,
            DisplayName = "Codeberg",
            Hint = "使用 Git 憑證；原始碼推到預設分支，靜態輸出推到 pages 分支。"
        },
        new()
        {
            Provider = GitHostingProvider.Bitbucket,
            DisplayName = "Bitbucket",
            Hint = "使用 Git 憑證；workspace.bitbucket.io 發布靜態網站，其他 repo 推送 Hugo 原始碼。"
        }
    ];

    public GitHubViewModel()
    {
        Title = "Git 部署";
        var path = Services.CurrentSitePath;
        HasLocalSite = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        SelectedProvider = ProviderOptions[0];
        LoadProviderSettings(GitHostingProvider.GitHub);
        ReloadRecentRepositories();
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
    public partial GitHostingProviderOption? SelectedProvider { get; set; }

    [ObservableProperty]
    public partial string ProviderAccount { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProviderPagesUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProviderHint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProviderSettingsStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNonGitHubProvider))]
    public partial bool IsGitHubProvider { get; set; } = true;

    public bool IsNonGitHubProvider => !IsGitHubProvider;

    [ObservableProperty]
    public partial string RepositoryTargetSummary { get; set; } = "貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository / Pages 網址後，Hugoer 會先顯示目標與建議網址。";

    [ObservableProperty]
    public partial bool CanConnectRepository { get; set; }

    [ObservableProperty]
    public partial bool HasLocalSite { get; set; }

    [ObservableProperty]
    public partial string CloneParent { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [ObservableProperty]
    public partial bool CanCloneToLocal { get; set; }

    [ObservableProperty]
    public partial bool HasPagesRepositories { get; set; }

    [ObservableProperty]
    public partial GitHubPagesRepositoryItem? SelectedPagesRepository { get; set; }

    public ObservableCollection<GitHubPagesRepositoryItem> PagesRepositories { get; } = [];

    [ObservableProperty]
    public partial bool HasRecentRepositories { get; set; }

    [ObservableProperty]
    public partial RecentRepositoryEntry? SelectedRecentRepository { get; set; }

    public ObservableCollection<RecentRepositoryEntry> RecentRepositories { get; } = [];

    [ObservableProperty]
    public partial bool SyncRecommendedBaseUrl { get; set; } = true;

    [ObservableProperty]
    public partial bool IsPublicRepo { get; set; } = true;

    [ObservableProperty]
    public partial string CommitMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DeploymentMonitorTitle { get; set; } = "等待第一次部署";

    [ObservableProperty]
    public partial string DeploymentMonitorSummary { get; set; } = "推送網站後，Hugoer 會辨識線上網站是否已更新。";

    [ObservableProperty]
    public partial string DeploymentMonitorSchedule { get; set; } = "每 5 分鐘自動檢查";

    [ObservableProperty]
    public partial bool IsCheckingDeployment { get; set; }

    partial void OnRepositoryUrlChanged(string value)
    {
        MaybeAdoptProviderFromUrl(value);
        UpdateRepositoryTarget(value);
        PersistProviderSettings();
    }

    partial void OnSelectedProviderChanged(GitHostingProviderOption? value)
    {
        if (value is null || _switchingProviderSettings || value.Provider == _activeProvider)
            return;

        _providerWasSelectedByUser = true;
        PersistProviderSettings();
        LoadProviderSettings(value.Provider);
    }

    partial void OnProviderAccountChanged(string value) => PersistProviderSettings();
    partial void OnProviderPagesUrlChanged(string value) => PersistProviderSettings();
    partial void OnSyncRecommendedBaseUrlChanged(bool value) => PersistProviderSettings();
    partial void OnCommitMessageChanged(string value) => PersistProviderSettings();

    partial void OnCloneParentChanged(string value)
    {
        UpdateCloneAvailability();
        if (!string.IsNullOrWhiteSpace(RepositoryUrl))
            UpdateRepositoryTarget(RepositoryUrl);
    }

    partial void OnHasLocalSiteChanged(bool value)
    {
        UpdateCloneAvailability();
        UpdateRepositoryTarget(RepositoryUrl);
    }

    partial void OnSelectedPagesRepositoryChanged(GitHubPagesRepositoryItem? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.HtmlUrl))
            return;
        if (!string.Equals(RepositoryUrl, value.HtmlUrl, StringComparison.Ordinal))
            RepositoryUrl = value.HtmlUrl;
    }

    partial void OnSelectedRecentRepositoryChanged(RecentRepositoryEntry? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.CanonicalUrl))
            return;
        if (!string.Equals(RepositoryUrl, value.CanonicalUrl, StringComparison.Ordinal))
            RepositoryUrl = value.CanonicalUrl;
    }

    protected override void OnBusyChanged(bool isBusy) => UpdateCloneAvailability();

    public override async Task OnNavigatedToAsync()
    {
        await RefreshAsync();
        EnsureDeploymentMonitorStarted();
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

            HasLocalSite = !string.IsNullOrWhiteSpace(Services.CurrentSitePath)
                           && Directory.Exists(Services.CurrentSitePath);
            if (!HasLocalSite)
            {
                RemoteSummary = "尚未選擇本機網站。若 Git 平台已有 Hugo 原始碼 repository，可貼上網址複製到本機。";
                PagesSummary = "尚未選擇本機網站";
                UpdateCloneAvailability();
                return;
            }

            var site = Services.CurrentSitePath!;

            if (string.IsNullOrWhiteSpace(RepoName))
                RepoName = Path.GetFileName(site.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            var info = await Services.GitHub.GetInfoAsync(site);
            if (GitProviderSelectionPolicy.ShouldAdoptDetectedProvider(
                    _providerWasSelectedByUser,
                    info.Provider,
                    _activeProvider))
            {
                var detectedProvider = info.Provider!.Value;
                LoadProviderSettings(detectedProvider);
            }
            if (string.IsNullOrWhiteSpace(RepositoryUrl) && !string.IsNullOrWhiteSpace(info.RemoteUrl))
            {
                var originTarget = Hugoer.Services.GitHubService.ParseRepositoryTarget(info.RemoteUrl);
                if (!originTarget.IsValid || originTarget.Provider == _activeProvider)
                    RepositoryUrl = info.RemoteUrl;
            }
            RemoteSummary = GitProviderStatusFormatter.BuildRemoteSummary(
                info,
                _activeProvider,
                _providerWasSelectedByUser,
                GetActiveRepositoryTarget(),
                ProviderAccount);

            await RefreshPagesStatusAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BrowseCloneParentAsync()
    {
        var folder = await DialogHelper.PickFolderAsync("選擇複製到本機的父資料夾");
        if (!string.IsNullOrWhiteSpace(folder))
            CloneParent = folder;
    }

    [RelayCommand]
    private async Task ListPagesRepositoriesAsync()
    {
        IsBusy = true;
        try
        {
            StatusMessage = "正在列出 GitHub Pages 網站…";
            var list = await Services.GitHub.ListPagesRepositoriesAsync();
            ApplyPagesRepositories(list);
            StatusMessage = list.Message;
            AppendLog(list.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CloneSiteToLocalAsync()
    {
        var target = Hugoer.Services.GitHubService.ParseRepositoryTarget(RepositoryUrl);
        if (!target.IsValid)
        {
            StatusMessage = target.ErrorMessage;
            AppendLog(StatusMessage);
            return;
        }
        MaybeAdoptProviderFromUrl(RepositoryUrl);
        if (target.Provider != _activeProvider)
        {
            StatusMessage = $"請先切換到 {target.ProviderName} 設定，再複製此 repository。";
            return;
        }

        if (string.IsNullOrWhiteSpace(CloneParent))
        {
            StatusMessage = "請選擇本機存放資料夾。";
            return;
        }

        IsBusy = true;
        try
        {
            var progress = new Progress<string>(message =>
            {
                AppendLog(message);
                StatusMessage = message;
            });
            var result = await Services.GitHub.CloneSiteFromGitHubAsync(RepositoryUrl, CloneParent, progress);
            if (!string.IsNullOrWhiteSpace(result.CombinedOutput))
                AppendLog(result.CombinedOutput);
            StatusMessage = result.Message;
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.SitePath))
                return;

            Services.SetSite(result.SitePath);
            RepoName = result.Target?.Repository ?? RepoName;
            RecordRecentRepository(result.Target, result.SitePath);
            await RefreshAsync();
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
        MaybeAdoptProviderFromUrl(RepositoryUrl);
        if (target.Provider != _activeProvider)
        {
            StatusMessage = $"請先切換到 {target.ProviderName} 設定，再連結此 repository。";
            return;
        }

        IsBusy = true;
        try
        {
            StatusMessage = $"正在確認 {target.ProviderName} repository 推送方式…";
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
            var autoMsg = string.IsNullOrWhiteSpace(CommitMessage)
                ? await Services.GitHub.NextDatedCommitMessageAsync(site, "Publish site via Hugoer")
                : CommitMessage.Trim();
            var result = await Services.GitHub.ConnectExistingRepositoryAndPushAsync(
                site,
                target,
                autoMsg,
                progress);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.IsPartialSuccess
                ? $"{target.ProviderName} 推送成功；請依平台提示完成 Pages/靜態網站設定"
                : result.Succeeded
                    ? $"已連結 {target.ProviderName} repository 並推送網站"
                    : "連結或部署失敗；請查看操作日誌";
            if (result.Succeeded || result.IsPartialSuccess)
                RecordRecentRepository(target, site);
            await RefreshAsync();
            if (result.Succeeded)
                await CheckDeploymentVersionAfterPushAsync(CancellationToken.None);
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

        var requestedName = RepoName.Trim();
        var reuseExisting = false;
        IsBusy = true;
        try
        {
            var info = await Services.GitHub.GetInfoAsync(site);
            if (!string.IsNullOrWhiteSpace(info.Owner) && !string.IsNullOrWhiteSpace(info.Repo))
            {
                if (!info.Repo.Equals(requestedName, StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage =
                        $"本機已連結 {info.Owner}/{info.Repo}，與要建立的「{requestedName}」不同。Hugoer 不會改指向新 repository。" +
                        $"若這就是既有的 Hugo 網站，請把名稱改成 {info.Repo}，或用上方「連結既有 Repository」。";
                    AppendLog(StatusMessage);
                    return;
                }

                RepositoryUrl = $"https://github.com/{info.Owner}/{info.Repo}";
                AppendLog($"本機已連結 {info.Owner}/{info.Repo}，改用安全連結既有 repository 流程。");
                reuseExisting = true;
            }
            else
            {
                StatusMessage = $"正在確認 GitHub 上是否已有 {requestedName}…";
                var lookup = await Services.GitHub.LookupOwnedRepositoryAsync(requestedName);
                if (!lookup.CheckSucceeded)
                {
                    StatusMessage = lookup.Message;
                    AppendLog(lookup.Message);
                    return;
                }

                if (lookup.Exists)
                {
                    AppendLog(lookup.Message);
                    if (!lookup.CanReuse || lookup.Target is not { IsValid: true })
                    {
                        StatusMessage = lookup.Message;
                        return;
                    }

                    RepositoryUrl = lookup.Target.CanonicalUrl ?? RepositoryUrl;
                    reuseExisting = true;
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        if (reuseExisting)
        {
            await ConnectExistingRepositoryAsync();
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
                site, requestedName, IsPublicRepo, progress);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.IsPartialSuccess
                ? "推送成功；請由 Repository 擁有者在 GitHub Pages 設定中選擇 GitHub Actions 來源"
                : result.Succeeded
                    ? "已推送並嘗試啟用 GitHub Pages"
                    : "部署過程有錯誤，請查看日誌";
            await RefreshAsync();
            if (result.Succeeded || result.IsPartialSuccess)
                RecordRecentRepository(GetActiveRepositoryTarget(), site);
            if (result.Succeeded)
                await CheckDeploymentVersionAfterPushAsync(CancellationToken.None);
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

            StatusMessage = "尚未連結 repository。請先在上方貼上完整 Repository URL，再按「連結、推送並設定 Pages」。";
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
            var pushMsg = string.IsNullOrWhiteSpace(CommitMessage)
                ? await Services.GitHub.NextDatedCommitMessageAsync(site)
                : CommitMessage.Trim();
            var result = await Services.GitHub.PushAsync(site, pushMsg, progress);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Succeeded ? "推送完成" : "推送失敗";
            if (result.Succeeded)
                RecordRecentRepository(Hugoer.Services.GitHubService.ParseRepositoryTarget(info.RemoteUrl), site);
            await RefreshPagesStatusAsync();
            if (result.Succeeded)
                await CheckDeploymentVersionAfterPushAsync(CancellationToken.None);
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
            await Services.GitHub.EnsureHostingWorkflowAsync(site, _activeProvider);
            var result = await Services.GitHub.EnablePagesFromActionsAsync(site);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.IsPartialSuccess
                ? $"{_activeProvider.PagesProductName()} 已推送；請依平台提示完成設定"
                : result.Succeeded
                    ? $"已請求啟用 {_activeProvider.PagesProductName()}"
                    : "啟用失敗";
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

        var status = await Services.GitHub.GetPagesStatusAsync(site, GetActiveRepositoryTarget());
        PagesUrl = !string.IsNullOrWhiteSpace(ProviderPagesUrl) ? ProviderPagesUrl : status.HtmlUrl;
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

}
