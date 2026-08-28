using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Helpers;
using Hugoer.Models;
using Hugoer.Services;

namespace Hugoer.ViewModels;

public partial class SetupViewModel : PageViewModelBase, IDisposable
{
    public SetupViewModel()
        : this(AppServices.Instance)
    {
    }

    public SetupViewModel(AppServices services)
        : base(services)
    {
        Title = "環境設定";
    }

    [ObservableProperty]
    public partial string HugoStatus { get; set; } = "偵測中…";

    [ObservableProperty]
    public partial string HugoPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HugoInstalled { get; set; }

    [ObservableProperty]
    public partial string HugoUpdateMessage { get; set; } = "尚未檢查 Hugo 最新版本。";

    [ObservableProperty]
    public partial bool HugoUpdateAvailable { get; set; }

    [ObservableProperty]
    public partial bool HugoLatestCheckSucceeded { get; set; }

    [ObservableProperty]
    public partial string HugoInstallButtonText { get; set; } = "一鍵安裝 Hugo Extended";

    [ObservableProperty]
    public partial string SitePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SiteSummary { get; set; } = "尚未選擇網站";

    [ObservableProperty]
    public partial string NewSiteParent { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [ObservableProperty]
    public partial string NewSiteName { get; set; } = "my-hugo-site";

    [ObservableProperty]
    public partial string CloneUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CloneParent { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [ObservableProperty]
    public partial string CloneTargetSummary { get; set; } = "貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository / Pages 網址後，Hugoer 會複製到本機並開啟。";

    [ObservableProperty]
    public partial bool CanClone { get; set; }

    [ObservableProperty]
    public partial bool HasPagesRepositories { get; set; }

    [ObservableProperty]
    public partial GitHubPagesRepositoryItem? SelectedPagesRepository { get; set; }

    public ObservableCollection<GitHubPagesRepositoryItem> PagesRepositories { get; } = [];

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool PreviewReady { get; set; }

    [ObservableProperty]
    public partial bool CanStartPreview { get; set; } = true;

    [ObservableProperty]
    public partial string PreviewUrl { get; set; } = "http://127.0.0.1:1313/";

    private Process? _previewProcess;
    // Commands can still be invoked directly while a previous invocation is
    // awaiting Hugo, even when the view has disabled its buttons. Serialize
    // lifecycle operations so a double-click cannot start two servers or run
    // a build against a half-written configuration.
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _disposed;

    public ObservableCollection<string> QuickTips { get; } =
    [
        "建議安裝 Hugo Extended（支援 SCSS / SASS）",
        "建立網站後可安裝 Stack 等主題",
        "GitHub Pages 可用 gh 或 Git Credential Manager／SSH 推送；Pages 設定仍可能需要 owner 權限",
        "也可從 GitLab、Codeberg、Bitbucket 複製 Hugo 原始碼 repository"
    ];

    public override async Task OnNavigatedToAsync()
    {
        if (_disposed)
            return;

        SitePath = Services.CurrentSitePath ?? string.Empty;
        await RefreshHugoAsync();
        if (!_disposed)
            RefreshSite();
    }

    [RelayCommand]
    private async Task RefreshHugoAsync()
    {
        if (!await TryEnterOperationAsync())
            return;

        try
        {
            await RefreshHugoCoreAsync();
        }
        catch (Exception ex)
        {
            ReportOperationFailure("Hugo 偵測", ex);
        }
        finally
        {
            ExitOperation();
        }
    }

    [RelayCommand]
    private async Task InstallHugoAsync()
    {
        if (!await TryEnterOperationAsync())
            return;

        StatusMessage = HugoInstalled ? "正在更新 Hugo…" : "正在安裝 Hugo…";
        try
        {
            var progress = new Progress<string>(msg =>
            {
                AppendLog(msg);
                StatusMessage = msg;
            });
            var result = await Services.Hugo.InstallHugoAsync(progress);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Succeeded ? "Hugo 安裝／更新完成" : "安裝或更新失敗，請查看日誌";
            // Refresh in the same operation scope. Calling the command wrapper
            // here would release IsBusy while the install is still unwinding.
            await RefreshHugoCoreAsync();
        }
        catch (Exception ex)
        {
            ReportOperationFailure("Hugo 安裝／更新", ex);
        }
        finally
        {
            ExitOperation();
        }
    }

    [RelayCommand]
    private async Task BrowseSiteAsync()
    {
        if (_disposed)
            return;

        var folder = await DialogHelper.PickFolderAsync("選擇 Hugo 網站資料夾");
        if (_disposed || string.IsNullOrWhiteSpace(folder)) return;

        if (!PathHelper.LooksLikeHugoSite(folder))
        {
            StatusMessage = "此資料夾看起來不是 Hugo 網站（找不到 config / content）。";
            AppendLog(StatusMessage);
            return;
        }

        Services.SetSite(folder);
        SitePath = folder;
        RefreshSite();
        StatusMessage = $"已開啟：{folder}";
    }

    [RelayCommand]
    private async Task BrowseParentAsync()
    {
        if (_disposed)
            return;

        var folder = await DialogHelper.PickFolderAsync("選擇新網站的父資料夾");
        if (!_disposed && !string.IsNullOrWhiteSpace(folder))
            NewSiteParent = folder;
    }

    [RelayCommand]
    private async Task BrowseCloneParentAsync()
    {
        if (_disposed)
            return;

        var folder = await DialogHelper.PickFolderAsync("選擇複製到本機的父資料夾");
        if (!_disposed && !string.IsNullOrWhiteSpace(folder))
            CloneParent = folder;
    }

    partial void OnCloneUrlChanged(string value) => UpdateCloneState();

    partial void OnCloneParentChanged(string value) => UpdateCloneState();

    partial void OnSelectedPagesRepositoryChanged(GitHubPagesRepositoryItem? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value.HtmlUrl))
            return;
        if (!string.Equals(CloneUrl, value.HtmlUrl, StringComparison.Ordinal))
            CloneUrl = value.HtmlUrl;
    }

    [RelayCommand]
    private async Task ListPagesRepositoriesAsync()
    {
        if (!await TryEnterOperationAsync())
            return;

        try
        {
            StatusMessage = "正在列出 GitHub Pages 網站…";
            var list = await Services.GitHub.ListPagesRepositoriesAsync();
            ApplyPagesRepositories(list);
            StatusMessage = list.Message;
            AppendLog(list.Message);
        }
        catch (Exception ex)
        {
            ReportOperationFailure("列出 GitHub Pages 網站", ex);
        }
        finally
        {
            ExitOperation();
        }
    }

    [RelayCommand]
    private async Task CloneSiteAsync()
    {
        var target = Hugoer.Services.GitHubService.ParseRepositoryTarget(CloneUrl);
        if (!target.IsValid)
        {
            StatusMessage = target.ErrorMessage;
            AppendLog(StatusMessage);
            return;
        }

        if (string.IsNullOrWhiteSpace(CloneParent))
        {
            StatusMessage = "請選擇本機存放資料夾。";
            return;
        }

        if (!await TryEnterOperationAsync())
            return;

        try
        {
            var progress = new Progress<string>(message =>
            {
                AppendLog(message);
                StatusMessage = message;
            });
            var result = await Services.GitHub.CloneSiteFromGitHubAsync(CloneUrl, CloneParent, progress);
            if (!string.IsNullOrWhiteSpace(result.CombinedOutput))
                AppendLog(result.CombinedOutput);
            StatusMessage = result.Message;
            if (!result.Succeeded || string.IsNullOrWhiteSpace(result.SitePath))
                return;

            Services.SetSite(result.SitePath);
            SitePath = result.SitePath;
            RefreshSite();
        }
        catch (Exception ex)
        {
            ReportOperationFailure("複製網站", ex);
        }
        finally
        {
            ExitOperation();
        }
    }

    [RelayCommand]
    private async Task CreateSiteAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSiteName))
        {
            StatusMessage = "請輸入網站名稱";
            return;
        }

        if (!await TryEnterOperationAsync())
            return;

        try
        {
            AppendLog($"建立網站 {NewSiteName} 於 {NewSiteParent}…");
            var result = await Services.Hugo.CreateSiteAsync(NewSiteParent, NewSiteName.Trim());
            AppendLog(result.CombinedOutput);
            if (!result.Succeeded)
            {
                StatusMessage = "建立失敗";
                return;
            }

            var path = Path.Combine(Path.GetFullPath(NewSiteParent.Trim()), NewSiteName.Trim());
            Services.SetSite(path);
            SitePath = path;
            RefreshSite();
            StatusMessage = $"網站已建立：{path}";
        }
        catch (Exception ex)
        {
            ReportOperationFailure("建立網站", ex);
        }
        finally
        {
            ExitOperation();
        }
    }

    [RelayCommand]
    private async Task StartPreviewAsync()
    {
        if (!RequireSite(out var site)) return;
        if (!await TryEnterOperationAsync())
            return;

        try
        {
            // Re-check inside the gate. Two command invocations can both pass
            // the outer state check before the first one has started Hugo.
            if (_previewProcess is { HasExited: false })
            {
                PreviewReady = true;
                StatusMessage = $"本機預覽已就緒：{PreviewUrl}";
                return;
            }

            PreviewReady = false;
            var result = await Services.Hugo.StartServerAsync(site);
            AppendLog(result.Message);
            StatusMessage = result.Message;
            if (result.Process is not null)
            {
                PreviewUrl = result.Url;
                _previewProcess = result.Process;
                _previewProcess.EnableRaisingEvents = true;
                _previewProcess.Exited += HandlePreviewProcessExited;
                PreviewReady = true;
            }
        }
        catch (Exception ex)
        {
            PreviewReady = false;
            ReportOperationFailure("啟動本機預覽", ex);
        }
        finally
        {
            ExitOperation();
        }
    }

    [RelayCommand]
    private async Task StopPreviewAsync()
    {
        if (!await TryEnterOperationAsync())
            return;

        try
        {
            var process = _previewProcess;
            _previewProcess = null;
            PreviewReady = false;

            if (process is null || process.HasExited)
            {
                try { process?.Dispose(); } catch { /* ignore */ }
                StatusMessage = "本機預覽未在執行。";
                return;
            }

            try
            {
                process.Exited -= HandlePreviewProcessExited;
            }
            catch { /* ignore */ }

            StatusMessage = "正在關閉本機預覽…";
            AppendLog(StatusMessage);
            await Task.Run(() => Services.Hugo.StopServer(process));
            StatusMessage = "本機預覽已關閉。";
            AppendLog(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"無法關閉本機預覽：{ex.Message}";
            AppendLog(StatusMessage);
        }
        finally
        {
            ExitOperation();
        }
    }

    [RelayCommand]
    private void OpenPreviewInBrowser()
    {
        if (_disposed)
            return;

        if (_previewProcess is null || _previewProcess.HasExited)
        {
            PreviewReady = false;
            StatusMessage = "本機預覽尚未啟動，請先按「本機預覽」。";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = PreviewUrl,
                UseShellExecute = true
            });
            StatusMessage = $"已在瀏覽器開啟：{PreviewUrl}";
            AppendLog(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"無法開啟瀏覽器：{ex.Message}";
            AppendLog(StatusMessage);
        }
    }

    [RelayCommand]
    private async Task BuildSiteAsync()
    {
        if (!RequireSite(out var site)) return;
        if (!await TryEnterOperationAsync())
            return;

        try
        {
            AppendLog("hugo build…");
            var result = await Services.Hugo.BuildAsync(site);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Succeeded ? "本機備援建置成功（public/）" : "本機備援建置失敗";
        }
        catch (Exception ex)
        {
            ReportOperationFailure("本機備援建置", ex);
        }
        finally
        {
            ExitOperation();
        }
    }

    private async Task<bool> TryEnterOperationAsync()
    {
        if (_disposed)
            return false;

        if (!await _operationGate.WaitAsync(0).ConfigureAwait(true))
        {
            StatusMessage = "已有環境操作進行中，請稍候完成後再試。";
            return false;
        }

        IsBusy = true;
        return true;
    }

    private void ExitOperation()
    {
        IsBusy = false;
        try { _operationGate.Release(); } catch (SemaphoreFullException) { /* defensive */ }
    }

    private void ReportOperationFailure(string operationName, Exception ex)
    {
        StatusMessage = ex is OperationCanceledException
            ? $"{operationName}已取消。"
            : $"{operationName}失敗：{ex.Message}";
        AppendLog(StatusMessage);
    }

    private async Task RefreshHugoCoreAsync()
    {
        try
        {
            var info = await Services.Hugo.DetectAsync();
            HugoInstalled = info.IsInstalled;
            HugoStatus = info.StatusMessage;
            HugoPath = info.ExecutablePath ?? string.Empty;
            AppendLog(info.StatusMessage);

            if (info.IsInstalled)
            {
                HugoUpdateMessage = "正在檢查 Hugo 最新版本…";
                var latest = await Services.Hugo.CheckLatestVersionAsync(info.Version);
                HugoLatestCheckSucceeded = latest.CheckSucceeded;
                HugoUpdateAvailable = latest.UpdateAvailable;
                HugoUpdateMessage = latest.Message;
                HugoInstallButtonText = latest.UpdateAvailable
                    ? $"更新到 Hugo Extended v{latest.LatestVersion}"
                    : "一鍵安裝／更新 Hugo Extended";
                AppendLog(latest.Message);
            }
            else
            {
                HugoLatestCheckSucceeded = false;
                HugoUpdateAvailable = false;
                HugoUpdateMessage = "尚未安裝 Hugo；安裝時會取得最新版 Hugo Extended。";
                HugoInstallButtonText = "一鍵安裝 Hugo Extended";
            }
        }
        catch (Exception ex)
        {
            HugoInstalled = false;
            HugoLatestCheckSucceeded = false;
            HugoUpdateAvailable = false;
            HugoStatus = $"Hugo 偵測失敗：{ex.Message}";
            HugoUpdateMessage = "暫時無法偵測 Hugo；請稍後重試。";
            AppendLog(HugoStatus);
        }
    }

}
