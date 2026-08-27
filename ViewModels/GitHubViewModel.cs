using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Helpers;
using Hugoer.Models;
using Hugoer.Services;

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

    /// <summary>
    /// Publishing is remote-first by default, matching the repository-backed
    /// workflow used by Hugoer Mobile.  Local output remains an explicit
    /// offline/diagnostic route.
    /// </summary>
    public IReadOnlyList<DeploymentModeOption> DeploymentModeOptions { get; } =
    [
        new()
        {
            Mode = DeploymentMode.GitHubPages,
            DisplayName = DeploymentMode.GitHubPages.DisplayName(),
            Description = DeploymentMode.GitHubPages.Description()
        },
        new()
        {
            Mode = DeploymentMode.Local,
            DisplayName = DeploymentMode.Local.DisplayName(),
            Description = DeploymentMode.Local.Description()
        }
    ];

    public GitHubViewModel()
        : this(AppServices.Instance)
    {
    }

    public GitHubViewModel(AppServices services)
        : base(services)
    {
        Title = "Git 部署";
        var path = Services.CurrentSitePath;
        HasLocalSite = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        SelectedProvider = ProviderOptions[0];
        LoadProviderSettings(GitHostingProvider.GitHub);
        SelectedDeploymentMode = DeploymentModeOptions.FirstOrDefault(option =>
            option.Mode == Services.Settings.GetDeploymentMode()) ?? DeploymentModeOptions[0];
        AllowLocalDeploymentFallback = Services.Settings.GetAllowLocalDeploymentFallback();
        RefreshPublishModePresentation();
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRemoteDeploymentMode))]
    [NotifyPropertyChangedFor(nameof(IsLocalDeploymentMode))]
    [NotifyPropertyChangedFor(nameof(PublishButtonText))]
    public partial DeploymentModeOption? SelectedDeploymentMode { get; set; }

    [ObservableProperty]
    public partial bool AllowLocalDeploymentFallback { get; set; } = true;

    [ObservableProperty]
    public partial string DeploymentModeSummary { get; set; } = DeploymentMode.GitHubPages.Description();

    [ObservableProperty]
    public partial string PublishButtonText { get; set; } = "直接推送至 GitHub Pages";

    public bool IsRemoteDeploymentMode => SelectedDeploymentMode?.Mode == DeploymentMode.GitHubPages;
    public bool IsLocalDeploymentMode => SelectedDeploymentMode?.Mode == DeploymentMode.Local;

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

    partial void OnSelectedDeploymentModeChanged(DeploymentModeOption? value)
    {
        var mode = value?.Mode ?? DeploymentMode.GitHubPages;
        Services.Settings.SetDeploymentMode(mode);
        RefreshPublishModePresentation();
    }

    partial void OnAllowLocalDeploymentFallbackChanged(bool value) =>
        Services.Settings.SetAllowLocalDeploymentFallback(value);

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

    private void RefreshPublishModePresentation()
    {
        var mode = SelectedDeploymentMode?.Mode ?? DeploymentMode.GitHubPages;
        DeploymentModeSummary = mode.Description();
        PublishButtonText = mode == DeploymentMode.GitHubPages
            ? "直接推送至 GitHub Pages"
            : "執行本機部署備援";
    }

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
            StatusMessage = $"正在以遠端優先方式連結 {target.ProviderName} repository…";

            if (SyncRecommendedBaseUrl && !string.IsNullOrWhiteSpace(target.PagesUrl))
            {
                await Services.GitHub.UpdateBaseUrlAsync(site, target.PagesUrl);
                AppendLog($"已將 baseURL 設為 {target.PagesUrl}");
            }

            var progress = new Progress<string>(message =>
            {
                AppendLog(message);
                StatusMessage = message;
            });
            var result = await Services.Publishing.ConnectAndPublishAsync(
                site,
                target,
                CommitMessage,
                allowLocalFallback: AllowLocalDeploymentFallback,
                progress: progress);
            AppendPublishResult(result);
            StatusMessage = result.Message;
            if (result.RemotePushSucceeded)
                RecordRecentRepository(target, site);
            await RefreshAsync();
            StatusMessage = result.Message;
            if (result.RemotePushSucceeded)
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
            var result = await Services.Publishing.CreateAndPublishAsync(
                site,
                requestedName,
                IsPublicRepo,
                commitMessage: CommitMessage,
                allowLocalFallback: AllowLocalDeploymentFallback,
                progress: progress);
            AppendPublishResult(result);
            StatusMessage = result.Message;
            await RefreshAsync();
            StatusMessage = result.Message;
            if (result.RemotePushSucceeded || result.UsedLocalFallback)
                RecordRecentRepository(GetActiveRepositoryTarget(), site);
            if (result.RemotePushSucceeded)
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

        var requestedMode = SelectedDeploymentMode?.Mode ?? DeploymentMode.GitHubPages;
        if (requestedMode == DeploymentMode.Local)
        {
            // Local mode is intentionally independent of Git credentials and
            // an origin. The primary button must keep the selected mode's
            // promise instead of stopping at the remote precondition.
            await DeployLocallyAsync();
            return;
        }

        var info = await Services.GitHub.GetInfoAsync(site);
        if (string.IsNullOrWhiteSpace(info.RemoteUrl))
        {
            var candidate = !string.IsNullOrWhiteSpace(RepositoryUrl) ? RepositoryUrl : RepoName;
            var candidateTarget = Hugoer.Services.GitHubService.ParseRepositoryTarget(candidate);
            if (candidateTarget.IsValid)
            {
                RepositoryUrl = candidate;
                AppendLog($"尚未設定 origin；改用安全連結流程：{candidateTarget.Owner}/{candidateTarget.Repository}");
                await ConnectExistingRepositoryAsync();
                return;
            }

            // A missing origin is a remote-route failure. Keep the default
            // remote-first policy observable and use its configured local
            // fallback rather than silently doing nothing.
            IsBusy = true;
            try
            {
                var progress = new Progress<string>(message =>
                {
                    AppendLog(message);
                    StatusMessage = message;
                });
                var result = await Services.Publishing.PublishAsync(
                    site,
                    requestedMode,
                    target: null,
                    CommitMessage,
                    allowLocalFallback: AllowLocalDeploymentFallback,
                    progress: progress);
                AppendPublishResult(result);
                StatusMessage = result.Message;
            }
            finally
            {
                IsBusy = false;
            }
            return;
        }

        var target = Hugoer.Services.GitHubService.ParseRepositoryTarget(info.RemoteUrl);
        if (!target.IsValid)
        {
            StatusMessage = "目前 origin 不是可辨識的 Git 平台 repository；為避免推錯位置，已停止發佈。";
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
            var result = await Services.Publishing.PublishAsync(
                site,
                requestedMode,
                target,
                CommitMessage,
                allowLocalFallback: AllowLocalDeploymentFallback,
                progress: progress);
            AppendPublishResult(result);
            StatusMessage = result.Message;
            if (result.RemotePushSucceeded)
                RecordRecentRepository(target, site);
            if (result.RemotePushSucceeded)
                await RefreshPagesStatusAsync();
            StatusMessage = result.Message;
            if (result.RemotePushSucceeded)
                await CheckDeploymentVersionAfterPushAsync(CancellationToken.None);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeployLocallyAsync()
    {
        if (!RequireSite(out var site)) return;

        IsBusy = true;
        try
        {
            var progress = new Progress<string>(message =>
            {
                AppendLog(message);
                StatusMessage = message;
            });
            var result = await Services.Publishing.DeployLocallyAsync(site, progress);
            AppendPublishResult(result);
            StatusMessage = result.Message;
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
