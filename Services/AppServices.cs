using System.Net.Http;

namespace Hugoer.Services;

/// <summary>
/// Application composition root.
///
/// Production uses <see cref="Instance"/> while tests and design-time hosts
/// can create an isolated graph with <see cref="CreateDefault"/>. The graph
/// owns one instance of each stateful module (settings, front matter and site
/// session), which prevents page models from silently diverging state by
/// constructing their own copies.
/// </summary>
public sealed class AppServices
{
    /// <summary>Shared graph used by the desktop application and designer.</summary>
    public static AppServices Instance { get; } = CreateDefault();

    public SettingsService Settings { get; }
    public SiteSession Site { get; }
    public HugoService Hugo { get; }
    public ThemeService Themes { get; }
    public ContentService Content { get; }
    public MenuService Menus { get; }
    public FrontMatterService FrontMatter { get; }
    public TomlParamsService Params { get; }
    public HugoConfigService Config { get; }
    public SiteMigrationService SiteMigration { get; }
    public DeploymentMonitorService DeploymentMonitor { get; }
    public GitHubService GitHub { get; }
    public PublishingService Publishing { get; }

    /// <summary>Creates a complete, isolated application graph.</summary>
    public static AppServices CreateDefault(
        string? settingsPath = null,
        HttpClient? httpClient = null) =>
        new(new SettingsService(settingsPath), httpClient);

    /// <summary>
    /// Builds the graph from a settings module and an optional shared HTTP
    /// adapter. Supplying these dependencies makes the composition seam easy
    /// to exercise without touching the user's global settings.
    /// </summary>
    public AppServices(SettingsService? settings = null, HttpClient? httpClient = null)
    {
        Settings = settings ?? new SettingsService();
        Settings.Load();

        Site = new SiteSession(Settings);
        FrontMatter = new FrontMatterService();
        Params = new TomlParamsService();
        Config = new HugoConfigService();

        DeploymentMonitor = new DeploymentMonitorService(httpClient);
        Hugo = new HugoService(Settings, httpClient);
        Themes = new ThemeService();
        Content = new ContentService(FrontMatter);
        Menus = new MenuService(FrontMatter);
        SiteMigration = new SiteMigrationService(FrontMatter);
        GitHub = new GitHubService(DeploymentMonitor, Hugo, Params);
        Publishing = new PublishingService(GitHub, Hugo);
    }

    /// <summary>
    /// Compatibility facade for older callers. New code should use
    /// <see cref="Site"/> directly.
    /// </summary>
    public string? CurrentSitePath
    {
        get => Site.CurrentPath;
        set => SetSite(value);
    }

    public event EventHandler? SiteChanged
    {
        add => Site.Changed += value;
        remove => Site.Changed -= value;
    }

    public event EventHandler<string>? AppStatusChanged;

    public bool SetSite(string? path) => Site.Set(path);

    public void SetAppStatus(string message) => AppStatusChanged?.Invoke(this, message);
}
