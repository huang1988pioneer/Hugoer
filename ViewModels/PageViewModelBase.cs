using CommunityToolkit.Mvvm.ComponentModel;
using Hugoer.Services;

namespace Hugoer.ViewModels;

public abstract partial class PageViewModelBase : ViewModelBase
{
    protected AppServices Services => AppServices.Instance;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;

    protected bool RequireSite(out string sitePath)
    {
        sitePath = Services.CurrentSitePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sitePath) || !Directory.Exists(sitePath))
        {
            StatusMessage = "請先在「環境設定」開啟或建立 Hugo 網站。";
            return false;
        }

        return true;
    }
}
