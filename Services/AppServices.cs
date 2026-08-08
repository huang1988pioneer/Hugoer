namespace Hugoer.Services;

/// <summary>Simple composition root for desktop app services.</summary>
public sealed class AppServices
{
    public static AppServices Instance { get; } = new();

    public SettingsService Settings { get; } = new();
    public HugoService Hugo { get; }
    public ThemeService Themes { get; } = new();
    public ContentService Content { get; } = new();
    public GitHubService GitHub { get; } = new();

    public string? CurrentSitePath { get; set; }

    public event EventHandler? SiteChanged;

    private AppServices()
    {
        Hugo = new HugoService(Settings);
        Settings.Load();
        CurrentSitePath = Settings.Current.LastSitePath;
    }

    public void SetSite(string? path)
    {
        CurrentSitePath = path;
        Settings.SetLastSitePath(path);
        SiteChanged?.Invoke(this, EventArgs.Empty);
    }
}
