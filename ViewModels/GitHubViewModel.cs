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
            Hint = "使用 Git 憑證；推送時可自動加入 GitLab Pages CI。"
        },
        new()
        {
            Provider = GitHostingProvider.Codeberg,
            DisplayName = "Codeberg",
            Hint = "使用 Git 憑證；推送後依 Codeberg Pages 設定提示完成部署。"
        },
        new()
        {
            Provider = GitHostingProvider.Bitbucket,
            DisplayName = "Bitbucket",
            Hint = "使用 Git 憑證；推送後依 Bitbucket 靜態網站設定提示完成部署。"
        }
    ];

    public GitHubViewModel()
    {
        Title = "Git 部署";
        var path = Services.CurrentSitePath;
        HasLocalSite = !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        SelectedProvider = ProviderOptions[0];
        LoadProviderSettings(GitHostingProvider.GitHub);
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
    public partial bool IsGitHubProvider { get; set; } = true;

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
    public partial bool SyncRecommendedBaseUrl { get; set; } = true;

    [ObservableProperty]
    public partial bool IsPublicRepo { get; set; } = true;

    [ObservableProperty]
    public partial string CommitMessage { get; set; } = "Update site via Hugoer";

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
                RepositoryUrl = info.RemoteUrl;
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
            var result = await Services.GitHub.ConnectExistingRepositoryAndPushAsync(
                site,
                target,
                string.IsNullOrWhiteSpace(CommitMessage) ? "Publish site via Hugoer" : CommitMessage.Trim(),
                progress);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.IsPartialSuccess
                ? $"{target.ProviderName} 推送成功；請依平台提示完成 Pages/靜態網站設定"
                : result.Succeeded
                    ? $"已連結 {target.ProviderName} repository 並推送網站"
                    : "連結或部署失敗；請查看操作日誌";
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
            var result = await Services.GitHub.PushAsync(site, CommitMessage, progress);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Succeeded ? "推送完成" : "推送失敗";
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
            if (_activeProvider != GitHostingProvider.GitHub)
            {
                await RefreshPagesStatusAsync();
                AppendLog(StatusMessage);
                return;
            }

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

        var status = await Services.GitHub.GetPagesStatusAsync(site, GetActiveRepositoryTarget());
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
    private Task CheckDeploymentNowAsync() =>
        CheckDeploymentVersionAsync(manual: true, CancellationToken.None);

    private void EnsureDeploymentMonitorStarted()
    {
        if (_deploymentMonitorCts is not null) return;
        _deploymentMonitorCts = new CancellationTokenSource();
        _ = MonitorDeploymentLoopAsync(_deploymentMonitorCts.Token);
    }

    private async Task MonitorDeploymentLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await CheckDeploymentVersionAsync(manual: false, cancellationToken);
                await Task.Delay(DeploymentCheckInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The application is closing.
        }
    }

    private async Task CheckDeploymentVersionAsync(bool manual, CancellationToken cancellationToken)
    {
        if (!await _deploymentCheckGate.WaitAsync(0, cancellationToken)) return;

        var providerAtStart = _activeProvider;
        var pagesUrlAtStart = PagesUrl;
        IsCheckingDeployment = true;
        DeploymentMonitorTitle = "正在檢查線上版本…";
        DeploymentMonitorSchedule = "每 5 分鐘自動檢查 · 正在連線";
        try
        {
            if (!HasLocalSite)
            {
                DeploymentMonitorTitle = "尚未選擇網站";
            DeploymentMonitorSummary = "請先在「環境」開啟、建立或從 Git 平台複製 Hugo 網站。";
                DeploymentMonitorSchedule = "選擇網站後開始每 5 分鐘檢查";
                return;
            }

            var site = Services.CurrentSitePath!;

            if (string.IsNullOrWhiteSpace(PagesUrl))
            {
                var pages = await Services.GitHub.GetPagesStatusAsync(site, GetActiveRepositoryTarget(), cancellationToken);
                PagesUrl = pages.HtmlUrl;
                pagesUrlAtStart = PagesUrl;
            }

            var result = await Services.DeploymentMonitor.CheckAsync(site, pagesUrlAtStart, cancellationToken);
            if (!IsCurrentDeploymentTarget(providerAtStart, pagesUrlAtStart))
                return;

            var stateChanged = result.State != _lastDeploymentState
                               || !string.Equals(result.ExpectedDeploymentId, _lastExpectedDeploymentId,
                                   StringComparison.Ordinal)
                               || _lastDeploymentProvider != providerAtStart
                               || !string.Equals(_lastDeploymentPagesUrl, pagesUrlAtStart, StringComparison.OrdinalIgnoreCase);

            DeploymentMonitorTitle = result.State switch
            {
                DeploymentVersionState.Latest => "線上網站已是最新版本",
                DeploymentVersionState.Previous => "線上網站仍是上一版本",
                DeploymentVersionState.Unavailable => "暫時無法檢查",
                _ => "等待下一次部署"
            };
            DeploymentMonitorSummary = result.Message;
            DeploymentMonitorSchedule =
                $"每 5 分鐘自動檢查 · 上次：{result.CheckedAt.LocalDateTime:yyyy/MM/dd HH:mm:ss}";

            if (manual || stateChanged)
                AppendLog($"線上版本監控：{result.Message}");

            if (result.State == DeploymentVersionState.Latest && stateChanged)
            {
                StatusMessage = "網站已更新：線上內容是最新版本。";
                Services.SetAppStatus("網站已更新為最新版本");
            }
            else if (result.State == DeploymentVersionState.Previous && stateChanged)
            {
                StatusMessage = "線上網站仍是上一版本，將在 5 分鐘後再次檢查。";
                Services.SetAppStatus("線上網站仍是上一版本 · 自動監控中");
            }
            else if (manual)
            {
                StatusMessage = result.Message;
            }

            _lastDeploymentState = result.State;
            _lastExpectedDeploymentId = result.ExpectedDeploymentId;
            _lastDeploymentProvider = providerAtStart;
            _lastDeploymentPagesUrl = pagesUrlAtStart;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The application is closing.
        }
        catch (Exception ex)
        {
            DeploymentMonitorTitle = "暫時無法檢查";
            DeploymentMonitorSummary = $"檢查線上版本時發生錯誤：{ex.Message}";
            DeploymentMonitorSchedule = "每 5 分鐘自動檢查 · 稍後重試";
            if (manual) AppendLog(DeploymentMonitorSummary);
        }
        finally
        {
            IsCheckingDeployment = false;
            _deploymentCheckGate.Release();
        }
    }

    private async Task CheckDeploymentVersionAfterPushAsync(CancellationToken cancellationToken)
    {
        await CheckDeploymentVersionAsync(manual: false, cancellationToken);
        if (_lastDeploymentState == DeploymentVersionState.Latest)
            return;

        for (var attempt = 0; attempt < PostPushDeploymentCheckDelays.Length; attempt++)
        {
            var delay = PostPushDeploymentCheckDelays[attempt];
            DeploymentMonitorTitle = "等待 Pages 發布最新內容…";
            DeploymentMonitorSummary = $"{_activeProvider.PagesProductName()} 可能還在更新快取；Hugoer 會在 {delay.TotalSeconds:0} 秒後再次檢查。";
            DeploymentMonitorSchedule = $"推送後快速檢查 · 第 {attempt + 2} 次";
            StatusMessage = "網站已推送，正在等待線上版本更新。";

            await Task.Delay(delay, cancellationToken);
            await CheckDeploymentVersionAsync(manual: true, cancellationToken);
            if (_lastDeploymentState == DeploymentVersionState.Latest)
                return;
        }
    }

    public void Dispose()
    {
        _deploymentMonitorCts?.Cancel();
        _deploymentMonitorCts?.Dispose();
        _deploymentMonitorCts = null;
        GC.SuppressFinalize(this);
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
        var provider = GetActiveRepositoryTarget()?.Provider ?? _activeProvider;
        await Services.GitHub.EnsureHostingWorkflowAsync(site, provider);
        StatusMessage = $"已寫入 {provider.PagesProductName()} 部署設定";
        AppendLog(StatusMessage);
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var line = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";
        Log = string.IsNullOrEmpty(Log) ? line : Log + Environment.NewLine + line;
    }

    [RelayCommand]
    private void SaveProviderSettings()
    {
        PersistProviderSettings();
        ProviderSettingsStatus = $"已儲存 {SelectedProvider?.DisplayName ?? _activeProvider.DisplayName()} 設定";
        StatusMessage = ProviderSettingsStatus;
    }

    private void LoadProviderSettings(GitHostingProvider provider)
    {
        _switchingProviderSettings = true;
        try
        {
            _activeProvider = provider;
            var profile = Services.Settings.GetGitProviderSettings(provider);
            var option = ProviderOptions.First(item => item.Provider == provider);
            SelectedProvider = option;
            ProviderHint = option.Hint;
            IsGitHubProvider = provider == GitHostingProvider.GitHub;
            ProviderAccount = profile.AccountOrWorkspace;
            ProviderPagesUrl = profile.PagesUrl;
            SyncRecommendedBaseUrl = profile.SyncRecommendedBaseUrl;
            CommitMessage = profile.CommitMessage;
            RepositoryUrl = profile.RepositoryUrl;
            PagesUrl = string.IsNullOrWhiteSpace(profile.PagesUrl) ? null : profile.PagesUrl;
            HasPagesRepositories = provider == GitHostingProvider.GitHub && PagesRepositories.Count > 0;
            ProviderSettingsStatus = $"已載入 {option.DisplayName} 的獨立設定";
            ResetDeploymentMonitorForProvider(option.DisplayName);
        }
        finally
        {
            _switchingProviderSettings = false;
        }

        UpdateRepositoryTarget(RepositoryUrl);
    }

    private GitHubRepositoryTarget? GetActiveRepositoryTarget()
    {
        var target = Hugoer.Services.GitHubService.ParseRepositoryTarget(RepositoryUrl);
        return target.IsValid && target.Provider == _activeProvider ? target : null;
    }

    private void PersistProviderSettings()
    {
        if (_switchingProviderSettings)
            return;

        var profile = Services.Settings.GetGitProviderSettings(_activeProvider);
        profile.RepositoryUrl = RepositoryUrl.Trim();
        profile.AccountOrWorkspace = ProviderAccount.Trim();
        profile.PagesUrl = ProviderPagesUrl.Trim();
        profile.SyncRecommendedBaseUrl = SyncRecommendedBaseUrl;
        profile.CommitMessage = string.IsNullOrWhiteSpace(CommitMessage)
            ? "Update site via Hugoer"
            : CommitMessage.Trim();
        Services.Settings.SaveGitProviderSettings(profile);
    }

    private void UpdateRepositoryTarget(string value)
    {
        var target = Hugoer.Services.GitHubService.ParseRepositoryTarget(value);
        CanConnectRepository = target.IsValid && target.Provider == _activeProvider;
        UpdateCloneAvailability();
        if (!target.IsValid)
        {
            RepositoryTargetSummary = string.IsNullOrWhiteSpace(value)
                ? "貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository / Pages 網址後，Hugoer 會先顯示目標與建議網址。"
                : target.ErrorMessage;
            return;
        }

        if (target.Provider != _activeProvider)
        {
            RepositoryTargetSummary =
                $"這個網址屬於 {target.ProviderName}，目前選擇的是 {_activeProvider.DisplayName()}。請先切換上方 Git 平台，再使用此網址。";
            return;
        }

        RepoName = target.Repository!;
        var destination = GitHubClonePath.TryGetDestination(CloneParent, target.Repository, out var pathError);
        var pagesUrl = string.IsNullOrWhiteSpace(target.PagesUrl)
            ? "此平台/此 repo 沒有可推導的專案 Pages 網址"
            : target.PagesUrl;
        RepositoryTargetSummary =
            $"平台：{target.ProviderName}\n" +
            $"Repository：{target.Owner}/{target.Repository}\n" +
            $"網站類型：{(target.IsUserOrOrganizationSite ? "使用者／組織網站" : "專案網站")}\n" +
            $"建議網址：{pagesUrl}" +
            (HasLocalSite ? string.Empty : $"\n本機目標：{destination ?? pathError}");
    }

    private void UpdateCloneAvailability() =>
        CanCloneToLocal = CanConnectRepository && !string.IsNullOrWhiteSpace(CloneParent) && !IsBusy && !HasLocalSite;

    private bool IsCurrentDeploymentTarget(GitHostingProvider provider, string? pagesUrl) =>
        _activeProvider == provider
        && string.Equals(PagesUrl, pagesUrl, StringComparison.OrdinalIgnoreCase);

    private void ResetDeploymentMonitorForProvider(string providerName)
    {
        _lastDeploymentState = null;
        _lastExpectedDeploymentId = null;
        _lastDeploymentProvider = _activeProvider;
        _lastDeploymentPagesUrl = PagesUrl;
        DeploymentMonitorTitle = $"等待 {providerName} 部署";
        DeploymentMonitorSummary = string.IsNullOrWhiteSpace(PagesUrl)
            ? $"尚未取得 {providerName} Pages 網址；推送後 Hugoer 會重新檢查。"
            : $"目前監控 {providerName}：{PagesUrl}";
        DeploymentMonitorSchedule = "每 5 分鐘自動檢查";
    }

    private void ApplyPagesRepositories(GitHubPagesRepositoryList list)
    {
        PagesRepositories.Clear();
        foreach (var item in list.Repositories)
            PagesRepositories.Add(item);
        HasPagesRepositories = IsGitHubProvider && PagesRepositories.Count > 0;
        if (PagesRepositories.Count == 1)
            SelectedPagesRepository = PagesRepositories[0];
    }
}
