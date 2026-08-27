using CommunityToolkit.Mvvm.ComponentModel;
using Hugoer.Services;

namespace Hugoer.ViewModels;

public abstract partial class PageViewModelBase : ViewModelBase
{
    private bool _runtimeDetached;

    protected PageViewModelBase()
        : this(AppServices.Instance)
    {
    }

    protected PageViewModelBase(AppServices services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Services.SiteChanged += OnRuntimeSiteChanged;
    }

    /// <summary>The application graph injected at the page's composition seam.</summary>
    protected AppServices Services { get; }

    /// <summary>Exposes the graph to view code that must coordinate native controls.</summary>
    public AppServices Runtime => Services;

    /// <summary>Current site path for bindings and controls.</summary>
    public string? CurrentSitePath => Services.Site.CurrentPath;

    [ObservableProperty]
    public partial string Title { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    partial void OnIsBusyChanged(bool value) => OnBusyChanged(value);

    protected virtual void OnBusyChanged(bool isBusy)
    {
    }

    public virtual Task OnNavigatedToAsync() => Task.CompletedTask;

    protected bool RequireSite(out string sitePath)
    {
        sitePath = Services.CurrentSitePath ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sitePath) || !Directory.Exists(sitePath))
        {
            StatusMessage = "請先在「環境設定」開啟、建立，或從 Git 平台複製 Hugo 網站。";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Detaches the page from the runtime graph. Derived pages override
    /// <see cref="Dispose"/> when they own additional resources and call the
    /// base implementation.
    /// </summary>
    public virtual void Dispose()
    {
        if (_runtimeDetached)
            return;

        _runtimeDetached = true;
        Services.SiteChanged -= OnRuntimeSiteChanged;
        GC.SuppressFinalize(this);
    }

    private void OnRuntimeSiteChanged(object? sender, EventArgs e) =>
        OnPropertyChanged(nameof(CurrentSitePath));
}
