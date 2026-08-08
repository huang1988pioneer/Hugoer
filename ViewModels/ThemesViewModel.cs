using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Models;
using Hugoer.Services;

namespace Hugoer.ViewModels;

public partial class ThemesViewModel : PageViewModelBase
{
    public ThemesViewModel()
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

    public override Task OnNavigatedToAsync()
    {
        RefreshInstalled();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void RefreshInstalled()
    {
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
    private async Task InstallSelectedAsync()
    {
        if (SelectedPreset is null)
        {
            StatusMessage = "請選擇主題";
            return;
        }

        if (!RequireSite(out var site)) return;

        IsBusy = true;
        try
        {
            var progress = new Progress<string>(m =>
            {
                AppendLog(m);
                StatusMessage = m;
            });
            AppendLog($"安裝 {SelectedPreset.DisplayName}…");
            var result = await Services.Themes.InstallThemeAsync(
                site, SelectedPreset, InstallAsSubmodule, progress);
            AppendLog(result.CombinedOutput);
            StatusMessage = result.Succeeded
                ? $"{SelectedPreset.DisplayName} 安裝完成"
                : "安裝失敗";
            RefreshInstalled();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallStackAsync()
    {
        SelectedPreset = Presets.FirstOrDefault(p => p.Id == "stack");
        await InstallSelectedAsync();
    }

    [RelayCommand]
    private async Task ActivateThemeAsync()
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
        ThemeConfigFiles.Clear();
        if (!RequireSite(out var site)) return;

        var theme = SelectedInstalled ?? Services.Hugo.InspectSite(site)?.ThemeName;
        foreach (var f in Services.Themes.ListThemeConfigFiles(site, theme))
            ThemeConfigFiles.Add(f);

        SelectedConfigFile = ThemeConfigFiles.FirstOrDefault();
    }

    partial void OnSelectedConfigFileChanged(string? value)
    {
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
        LoadThemeConfigs();
    }

    [RelayCommand]
    private async Task SaveThemeConfigAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedConfigFile)) return;
        await File.WriteAllTextAsync(SelectedConfigFile, ThemeConfigText);
        StatusMessage = $"已儲存：{SelectedConfigFile}";
        AppendLog(StatusMessage);
    }

    [RelayCommand]
    private void OpenDocs()
    {
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
}
