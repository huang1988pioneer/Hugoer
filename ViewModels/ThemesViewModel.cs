using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Helpers;
using Hugoer.Models;
using Hugoer.Services;

namespace Hugoer.ViewModels;

public partial class ThemesViewModel : PageViewModelBase, IDisposable
{
    public ThemesViewModel()
        : this(AppServices.Instance)
    {
    }

    public ThemesViewModel(AppServices services)
        : base(services)
    {
        Title = "主題 Themes";
        foreach (var p in ThemeService.Presets)
            Presets.Add(p);
        SelectedPreset = Presets.FirstOrDefault(p => p.Id == "stack") ?? Presets.FirstOrDefault();
    }

    public ObservableCollection<ThemePreset> Presets { get; } = [];
    public ObservableCollection<string> InstalledThemes { get; } = [];
    public ObservableCollection<string> ThemeConfigFiles { get; } = [];

    [ObservableProperty]
    public partial ThemePreset? SelectedPreset { get; set; }

    [ObservableProperty]
    public partial string? SelectedInstalled { get; set; }

    [ObservableProperty]
    public partial string? SelectedConfigFile { get; set; }

    [ObservableProperty]
    public partial string ThemeConfigText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool InstallAsSubmodule { get; set; } = true;

    [ObservableProperty]
    public partial string Log { get; set; } = string.Empty;

    // Theme installation and config writes touch the same site tree. A view
    // can still receive a second command from keyboard automation while the
    // first task is awaiting git or disk I/O, so serialize those operations.
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private bool _disposed;

    public override Task OnNavigatedToAsync()
    {
        if (!_disposed)
            RefreshInstalled();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void RefreshInstalled()
    {
        if (_disposed)
            return;

        InstalledThemes.Clear();
        if (!RequireSite(out var site)) return;

        foreach (var t in Services.Themes.ListInstalledThemes(site))
            InstalledThemes.Add(t);

        SelectedInstalled = InstalledThemes.FirstOrDefault();
        LoadThemeConfigs();
        StatusMessage = InstalledThemes.Count == 0
            ? "尚未安裝主題，建議一鍵安裝 Stack"
            : $"已安裝 {InstalledThemes.Count} 個主題";
    }

    [RelayCommand]
    private Task InstallSelectedAsync() => RunOperationAsync(
        "安裝主題",
        InstallSelectedCoreAsync);

    private async Task InstallSelectedCoreAsync()
    {
        var preset = SelectedPreset;
        if (preset is null)
        {
            StatusMessage = "請選擇主題";
            return;
        }

        if (!RequireSite(out var site)) return;

        var installAsSubmodule = InstallAsSubmodule;
        var progress = new Progress<string>(m =>
        {
            AppendLog(m);
            StatusMessage = m;
        });
        AppendLog($"安裝 {preset.DisplayName}…");
        var result = await Services.Themes.InstallThemeAsync(
            site, preset, installAsSubmodule, progress);
        AppendLog(result.CombinedOutput);
        StatusMessage = result.Succeeded
            ? $"{preset.DisplayName} 安裝完成"
            : "安裝失敗";
        RefreshInstalled();
    }

    [RelayCommand]
    private Task InstallStackAsync() => RunOperationAsync(
        "安裝 Stack",
        InstallStackCoreAsync);

    private async Task InstallStackCoreAsync()
    {
        SelectedPreset = Presets.FirstOrDefault(p => p.Id == "stack");
        await InstallSelectedCoreAsync();
    }

    [RelayCommand]
    private Task ActivateThemeAsync() => RunOperationAsync(
        "啟用主題",
        ActivateThemeCoreAsync);

    private async Task ActivateThemeCoreAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedInstalled)) return;
        if (!RequireSite(out var site)) return;

        await Services.Themes.EnsureThemeInConfigAsync(site, SelectedInstalled);
        StatusMessage = $"已將 theme 設為 {SelectedInstalled}";
        AppendLog(StatusMessage);
        LoadThemeConfigs();
    }

    [RelayCommand]
    private void LoadThemeConfigs()
    {
        if (_disposed)
            return;

        ThemeConfigFiles.Clear();
        if (!RequireSite(out var site)) return;

        var theme = SelectedInstalled ?? Services.Hugo.InspectSite(site)?.ThemeName;
        foreach (var f in Services.Themes.ListThemeConfigFiles(site, theme))
            ThemeConfigFiles.Add(f);

        SelectedConfigFile = ThemeConfigFiles.FirstOrDefault();
    }

    partial void OnSelectedConfigFileChanged(string? value)
    {
        if (_disposed)
            return;

        if (value is null || !File.Exists(value)) return;
        try
        {
            ThemeConfigText = File.ReadAllText(value);
            StatusMessage = $"編輯：{value}";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    partial void OnSelectedInstalledChanged(string? value)
    {
        if (_disposed)
            return;

        LoadThemeConfigs();
    }

    [RelayCommand]
    private Task SaveThemeConfigAsync() => RunOperationAsync(
        "儲存主題設定",
        SaveThemeConfigCoreAsync);

    private async Task SaveThemeConfigCoreAsync()
    {
        var path = SelectedConfigFile;
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            await AtomicFileWriter.WriteAllTextAsync(path, ThemeConfigText).ConfigureAwait(true);
            if (string.Equals(SelectedConfigFile, path, StringComparison.OrdinalIgnoreCase))
            {
                StatusMessage = $"已儲存：{path}";
                AppendLog(StatusMessage);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"儲存失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenDocs()
    {
        if (_disposed)
            return;

        if (SelectedPreset is null || string.IsNullOrWhiteSpace(SelectedPreset.DocsUrl)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SelectedPreset.DocsUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var line = $"[{DateTime.Now:HH:mm:ss}] {message.Trim()}";
        Log = string.IsNullOrEmpty(Log) ? line : Log + Environment.NewLine + line;
    }

    private async Task RunOperationAsync(string operationName, Func<Task> operation)
    {
        if (_disposed)
            return;

        if (!await _operationGate.WaitAsync(0).ConfigureAwait(true))
        {
            StatusMessage = "已有主題操作進行中，請稍候完成後再試。";
            return;
        }

        IsBusy = true;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"{operationName}已取消。";
            AppendLog(StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"{operationName}失敗：{ex.Message}";
            AppendLog(StatusMessage);
        }
        finally
        {
            IsBusy = false;
            _operationGate.Release();
        }
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
