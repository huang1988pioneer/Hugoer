using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Helpers;

namespace Hugoer.ViewModels;

public partial class MigrationViewModel : PageViewModelBase
{
    public MigrationViewModel()
    {
        Title = "遷移";
        DestinationParent = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        RefreshDerivedState();
    }

    public IReadOnlyList<string> SourceKindOptions { get; } =
    [
        "自動偵測",
        "Hugo",
        "Hexo",
        "Jekyll"
    ];

    public IReadOnlyList<string> DestinationKindOptions { get; } =
    [
        "Hugo",
        "Hexo",
        "Jekyll"
    ];

    [ObservableProperty]
    public partial string SourcePath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SourceKind { get; set; } = "自動偵測";

    [ObservableProperty]
    public partial string DetectedKindLabel { get; set; } = "尚未選擇來源網站";

    [ObservableProperty]
    public partial string DestinationKind { get; set; } = "Hugo";

    [ObservableProperty]
    public partial string DestinationParent { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DestinationName { get; set; } = "migrated-hugo";

    [ObservableProperty]
    public partial string MigrationPlanText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanMigrate { get; set; }

    [ObservableProperty]
    public partial bool OpenAfterMigrate { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowOpenAfterMigrate { get; set; } = true;

    [ObservableProperty]
    public partial string LastDestinationPath { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool CanOpenMigratedSite { get; set; }

    private bool _refreshing;

    public override Task OnNavigatedToAsync()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) && !string.IsNullOrWhiteSpace(Services.CurrentSitePath))
            SourcePath = Services.CurrentSitePath;
        RefreshDerivedState();
        return Task.CompletedTask;
    }

    partial void OnSourcePathChanged(string value) => RefreshDerivedState();
    partial void OnSourceKindChanged(string value) => RefreshDerivedState();
    partial void OnDestinationKindChanged(string value) => RefreshDerivedState();
    partial void OnDestinationParentChanged(string value) => RefreshDerivedState();
    partial void OnDestinationNameChanged(string value) => RefreshDerivedState();

    [RelayCommand]
    private async Task BrowseSourceAsync()
    {
        var folder = await DialogHelper.PickFolderAsync("選擇要遷移的網站資料夾");
        if (!string.IsNullOrWhiteSpace(folder))
            SourcePath = folder;
    }

    [RelayCommand]
    private void UseCurrentSite()
    {
        if (!RequireSite(out var site))
            return;
        SourcePath = site;
        StatusMessage = $"已使用目前網站：{site}";
    }

    [RelayCommand]
    private async Task BrowseDestinationParentAsync()
    {
        var folder = await DialogHelper.PickFolderAsync("選擇遷移輸出的父資料夾");
        if (!string.IsNullOrWhiteSpace(folder))
            DestinationParent = folder;
    }

    [RelayCommand]
    private async Task MigrateAsync()
    {
        var sourceKind = ResolveSourceKind();
        var targetKind = StaticSiteDetector.Parse(DestinationKind);
        if (sourceKind == StaticSiteKind.Unknown)
        {
            StatusMessage = "無法辨識來源引擎，請改為手動選擇 Hugo、Hexo 或 Jekyll。";
            AppendLog(StatusMessage);
            return;
        }

        if (string.IsNullOrWhiteSpace(DestinationParent) || string.IsNullOrWhiteSpace(DestinationName))
        {
            StatusMessage = "請指定目標資料夾。";
            return;
        }

        var dest = Path.Combine(DestinationParent.Trim(), DestinationName.Trim());
        IsBusy = true;
        CanMigrate = false;
        try
        {
            StatusMessage = "正在遷移網站…";
            AppendLog($"開始遷移 {StaticSiteDetector.DisplayName(sourceKind)} → {StaticSiteDetector.DisplayName(targetKind)}");
            var result = await Task.Run(() =>
                Services.SiteMigration.Migrate(SourcePath, dest, sourceKind, targetKind));
            foreach (var line in result.Log)
                AppendLog(line);
            foreach (var warning in result.Warnings)
                AppendLog("警告：" + warning);
            StatusMessage = result.Message;
            AppendLog(result.Message);
            if (!result.Succeeded)
                return;

            LastDestinationPath = result.DestinationPath;
            CanOpenMigratedSite = Directory.Exists(result.DestinationPath);
            if (OpenAfterMigrate && targetKind == StaticSiteKind.Hugo && Directory.Exists(result.DestinationPath))
            {
                Services.SetSite(result.DestinationPath);
                AppendLog($"已開啟遷移後的 Hugo 網站：{result.DestinationPath}");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "遷移失敗：" + ex.Message;
            AppendLog(StatusMessage);
        }
        finally
        {
            IsBusy = false;
            RefreshDerivedState();
        }
    }

    [RelayCommand]
    private void OpenMigratedSite()
    {
        if (string.IsNullOrWhiteSpace(LastDestinationPath) || !Directory.Exists(LastDestinationPath))
        {
            StatusMessage = "還沒有遷移完成的目標資料夾。";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = LastDestinationPath,
                UseShellExecute = true
            });
            StatusMessage = $"已開啟：{LastDestinationPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = "無法開啟資料夾：" + ex.Message;
        }
    }

    private StaticSiteKind ResolveSourceKind()
    {
        if (!string.Equals(SourceKind, "自動偵測", StringComparison.Ordinal))
            return StaticSiteDetector.Parse(SourceKind);
        return Services.SiteMigration.Detect(SourcePath);
    }

    private void RefreshDerivedState()
    {
        if (_refreshing)
            return;
        _refreshing = true;
        try
        {
            RefreshDerivedStateCore();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshDerivedStateCore()
    {
        var resolved = ResolveSourceKind();
        DetectedKindLabel = string.IsNullOrWhiteSpace(SourcePath)
            ? "尚未選擇來源網站"
            : SourceKind == "自動偵測"
                ? $"偵測結果：{StaticSiteDetector.DisplayName(resolved)}"
                : $"來源引擎：{StaticSiteDetector.DisplayName(resolved)}";

        var target = StaticSiteDetector.Parse(DestinationKind);
        ShowOpenAfterMigrate = target == StaticSiteKind.Hugo;
        MigrationPlanText = Services.SiteMigration.MigrationPlan(resolved, target);

        var sourceName = Path.GetFileName((SourcePath ?? string.Empty).TrimEnd('\\', '/'));
        var suffix = (DestinationKind ?? "hugo").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(DestinationName) || LooksAutoGeneratedName(DestinationName))
        {
            DestinationName = string.IsNullOrWhiteSpace(sourceName)
                ? $"migrated-{suffix}"
                : $"{sourceName}-{suffix}";
        }

        if (string.IsNullOrWhiteSpace(DestinationParent) && !string.IsNullOrWhiteSpace(SourcePath))
        {
            var parent = Directory.GetParent(SourcePath)?.FullName;
            if (!string.IsNullOrWhiteSpace(parent))
                DestinationParent = parent;
        }

        CanMigrate = !IsBusy
                     && !string.IsNullOrWhiteSpace(SourcePath)
                     && Directory.Exists(SourcePath)
                     && !string.IsNullOrWhiteSpace(DestinationParent)
                     && !string.IsNullOrWhiteSpace(DestinationName)
                     && resolved != StaticSiteKind.Unknown
                     && target != StaticSiteKind.Unknown
                     && resolved != target;
    }

    private static bool LooksAutoGeneratedName(string name)
    {
        var text = name.Trim();
        return text.StartsWith("migrated-", StringComparison.OrdinalIgnoreCase)
               || text.EndsWith("-hugo", StringComparison.OrdinalIgnoreCase)
               || text.EndsWith("-hexo", StringComparison.OrdinalIgnoreCase)
               || text.EndsWith("-jekyll", StringComparison.OrdinalIgnoreCase);
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        var line = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";
        Log = string.IsNullOrEmpty(Log) ? line : Log + Environment.NewLine + line;
    }
}
