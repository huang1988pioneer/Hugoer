using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Helpers;

namespace Hugoer.ViewModels;

public partial class SetupViewModel : PageViewModelBase
{
    public SetupViewModel()
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
    public partial string SitePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SiteSummary { get; set; } = "尚未選擇網站";

    [ObservableProperty]
    public partial string NewSiteParent { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [ObservableProperty]
    public partial string NewSiteName { get; set; } = "my-hugo-site";

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool PreviewReady { get; set; }

    private const string PreviewUrl = "http://127.0.0.1:1313/";
    private Process? _previewProcess;

    public ObservableCollection<string> QuickTips { get; } =
    [
        "建議安裝 Hugo Extended（支援 SCSS / SASS）",
        "建立網站後可安裝 Stack 等主題",
        "GitHub Pages 需要本機安裝 Git 與 GitHub CLI (gh)"
    ];

    public override async Task OnNavigatedToAsync()
    {
        SitePath = Services.CurrentSitePath ?? string.Empty;
        await RefreshHugoAsync();
        RefreshSite();
    }

    [RelayCommand]
    private async Task RefreshHugoAsync()
    {
        IsBusy = true;
        try
        {
            var info = await Services.Hugo.DetectAsync();
            HugoInstalled = info.IsInstalled;
            HugoStatus = info.StatusMessage;
            HugoPath = info.ExecutablePath ?? string.Empty;
            AppendLog(info.StatusMessage);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallHugoAsync()
    {
        IsBusy = true;
        StatusMessage = "正在安裝 Hugo…";
        try
        {
            var progress = new Progress<string>(msg =>
            {
                AppendLog(msg);
                StatusMessage = msg;
            });
            var result = await Services.Hugo.InstallHugoAsync(progress);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Succeeded ? "Hugo 安裝完成" : "安裝失敗，請查看日誌";
            await RefreshHugoAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task BrowseSiteAsync()
    {
        var folder = await DialogHelper.PickFolderAsync("選擇 Hugo 網站資料夾");
        if (string.IsNullOrWhiteSpace(folder)) return;

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
        var folder = await DialogHelper.PickFolderAsync("選擇新網站的父資料夾");
        if (!string.IsNullOrWhiteSpace(folder))
            NewSiteParent = folder;
    }

    [RelayCommand]
    private async Task CreateSiteAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSiteName))
        {
            StatusMessage = "請輸入網站名稱";
            return;
        }

        IsBusy = true;
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

            var path = Path.Combine(NewSiteParent, NewSiteName.Trim());
            Services.SetSite(path);
            SitePath = path;
            RefreshSite();
            StatusMessage = $"網站已建立：{path}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartPreviewAsync()
    {
        if (!RequireSite(out var site)) return;
        if (_previewProcess is { HasExited: false })
        {
            PreviewReady = true;
            StatusMessage = $"本機預覽已就緒：{PreviewUrl}";
            return;
        }

        PreviewReady = false;
        IsBusy = true;
        try
        {
            var (process, message) = await Services.Hugo.StartServerAsync(site);
            AppendLog(message);
            StatusMessage = message;
            if (process is not null)
            {
                _previewProcess = process;
                _previewProcess.EnableRaisingEvents = true;
                _previewProcess.Exited += (_, _) =>
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        PreviewReady = false;
                        StatusMessage = "本機預覽已停止；請重新啟動預覽。";
                    });
                };
                PreviewReady = true;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenPreviewInBrowser()
    {
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
        IsBusy = true;
        try
        {
            AppendLog("hugo build…");
            var result = await Services.Hugo.BuildAsync(site);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Succeeded ? "建置成功（public/）" : "建置失敗";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshSite()
    {
        if (string.IsNullOrWhiteSpace(SitePath) || !Directory.Exists(SitePath))
        {
            SiteSummary = "尚未選擇網站";
            return;
        }

        var info = Services.Hugo.InspectSite(SitePath);
        if (info is null)
        {
            SiteSummary = "路徑無效";
            return;
        }

        SiteSummary = $"{info.Name}\n路徑：{info.Path}\n設定檔：{info.ConfigFile ?? "（無）"}\n主題：{info.ThemeName ?? "（未設定）"}\nGit：{(info.HasGit ? "已初始化" : "尚未")}";
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var line = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";
        Log = string.IsNullOrEmpty(Log) ? line : Log + Environment.NewLine + line;
    }
}
