using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Hugoer.Helpers;

public static class DialogHelper
{
    public static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    public static async Task<string?> PickFolderAsync(string title = "選擇資料夾")
    {
        var window = GetMainWindow();
        if (window is null) return null;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        }).ConfigureAwait(true);

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public static async Task<string?> PickFileAsync(string title, IReadOnlyList<FilePickerFileType>? types = null)
    {
        var window = GetMainWindow();
        if (window is null) return null;

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types
        }).ConfigureAwait(true);

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
