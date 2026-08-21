namespace Hugoer.Services;

/// <summary>Simple composition root for desktop app services.</summary>
public sealed class AppServices
{
    public static AppServices Instance { get; } = new();

    public SettingsService Settings { get; } = new();
    public HugoService Hugo { get; }
    public ThemeService Themes { get; } = new();
    public ContentService Content { get; } = new();
    public FrontMatterService FrontMatter { get; } = new();
    public DeploymentMonitorService DeploymentMonitor { get; } = new();
    public GitHubService GitHub { get; }

    public string? CurrentSitePath { get; set; }

    public event EventHandler? SiteChanged;
    public event EventHandler<string>? AppStatusChanged;

    private AppServices()
    {
        Hugo = new HugoService(Settings);
        GitHub = new GitHubService(DeploymentMonitor);
        Settings.Load();
        CurrentSitePath = Settings.Current.LastSitePath;
    }

    public void SetSite(string? path)
    {
        CurrentSitePath = path;
        Settings.SetLastSitePath(path);
        SiteChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetAppStatus(string message) => AppStatusChanged?.Invoke(this, message);
}
