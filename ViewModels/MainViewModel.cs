using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Services;

namespace Hugoer.ViewModels;

/// <summary>
/// 應用程式外框。
///
/// 導覽從「七個並列分頁」改成三層：
///   1. 首頁 — 只呈現一個下一步，涵蓋 90% 的日常操作。
///   2. 文章 / 發布 — 兩個高頻工作區，永遠一鍵可達。
///   3. 更多 — 環境、設定檔、主題、選單、遷移等進階頁收在同一個選單裡。
/// 另外提供全域快捷列（新文章 / 發布）與 Ctrl+K 命令面板，讓任何功能都是一步到位。
/// </summary>
public partial class MainViewModel : ViewModelBase, IShellNavigator, IDisposable
{
    private readonly AppServices _services;
    private readonly List<CommandEntry> _commands = [];

    public MainViewModel()
        : this(AppServices.Instance)
    {
    }

    public MainViewModel(AppServices services)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));

        SetupPage = new SetupViewModel(_services);
        ConfigPage = new ConfigViewModel(_services);
        ThemesPage = new ThemesViewModel(_services);
        ContentPage = new ContentViewModel(_services);
        MigrationPage = new MigrationViewModel(_services);
        MenuPage = new MenuViewModel(_services);
        GitHubPage = new GitHubViewModel(_services);
        HomePage = new HomeViewModel(_services, SetupPage, ThemesPage, ContentPage, GitHubPage)
        {
            Shell = this,
        };

        NavItems =
        [
            new NavItem(ShellPages.Home, "首頁", "◆", "目前狀態與下一步", HomePage, isPrimary: true),
            new NavItem(ShellPages.Content, "文章", "✎", "撰寫、預覽與匯出文章", ContentPage, isPrimary: true),
            new NavItem(ShellPages.Publish, "發布", "▲", "推送到 GitHub / GitLab / Codeberg / Bitbucket Pages", GitHubPage, isPrimary: true),
            new NavItem(ShellPages.Setup, "環境", "⚙", "安裝 Hugo、建立／開啟／複製網站、本機預覽", SetupPage, isPrimary: false),
            new NavItem(ShellPages.Config, "設定檔", "⌘", "網站基本欄位、params 表單與原始 TOML", ConfigPage, isPrimary: false),
            new NavItem(ShellPages.Themes, "主題", "◐", "安裝 Stack 等佈景並切換 theme", ThemesPage, isPrimary: false),
            new NavItem(ShellPages.Menu, "選單", "≡", "編輯 menu.main / menu.social 與網站頁面", MenuPage, isPrimary: false),
            new NavItem(ShellPages.Migration, "遷移", "⇄", "Hexo／Jekyll 與 Hugo 雙向遷移", MigrationPage, isPrimary: false),
        ];

        foreach (var item in NavItems)
            item.SelectCommand = NavigateCommand;

        PrimaryNavItems = NavItems.Where(n => n.IsPrimary).ToList();
        MoreNavItems = NavItems.Where(n => !n.IsPrimary).ToList();

        BuildCommandPalette();
        ApplyPaletteFilter();

        _services.SiteChanged += OnSiteChanged;
        _services.AppStatusChanged += OnAppStatusChanged;

        UpdateSiteBanner();
        _ = RefreshCodeStatisticsAsync();
        GoTo(ShellPages.Home);
    }

    public ObservableCollection<NavItem> NavItems { get; }

    public IReadOnlyList<NavItem> PrimaryNavItems { get; }

    public IReadOnlyList<NavItem> MoreNavItems { get; }

    public SetupViewModel SetupPage { get; }
    public ConfigViewModel ConfigPage { get; }
    public ThemesViewModel ThemesPage { get; }
    public ContentViewModel ContentPage { get; }
    public MigrationViewModel MigrationPage { get; }
    public MenuViewModel MenuPage { get; }
    public GitHubViewModel GitHubPage { get; }
    public HomeViewModel HomePage { get; }

    [ObservableProperty]
    public partial NavItem? SelectedNav { get; set; }

    [ObservableProperty]
    public partial PageViewModelBase? CurrentPage { get; set; }

    [ObservableProperty]
    public partial string SiteBanner { get; set; } = "尚未選擇網站";

    [ObservableProperty]
    public partial bool HasSite { get; set; }

    [ObservableProperty]
    public partial string AppStatus { get; set; } = "就緒";

    [ObservableProperty]
    public partial string MoreLabel { get; set; } = "更多";

    [ObservableProperty]
    public partial bool IsMoreActive { get; set; }

    [ObservableProperty]
    public partial string CodeStatisticsSummary { get; set; } = "計算中…";

    [ObservableProperty]
    public partial string CodeStatisticsDetails { get; set; } = "正在掃描應用程式來源…";

    // -------------------------------------------------------------- 命令面板

    public ObservableCollection<CommandEntry> PaletteResults { get; } = [];

    [ObservableProperty]
    public partial bool IsPaletteOpen { get; set; }

    [ObservableProperty]
    public partial string PaletteQuery { get; set; } = string.Empty;

    [ObservableProperty]
    public partial CommandEntry? SelectedPaletteEntry { get; set; }

    [ObservableProperty]
    public partial bool PaletteHasResults { get; set; }

    private int _navigationGeneration;
    private bool _disposed;

    partial void OnPaletteQueryChanged(string value) => ApplyPaletteFilter();

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (_disposed || value is null)
            return;

        foreach (var item in NavItems)
            item.IsActive = ReferenceEquals(item, value);

        IsMoreActive = !value.IsPrimary;
        MoreLabel = value.IsPrimary ? "更多" : $"更多 · {value.Title}";

        CurrentPage = value.Page;
        AppStatus = value.Page.StatusMessage;
        _ = NavigateToPageAsync(value.Page, ++_navigationGeneration);
    }

    // ------------------------------------------------------------------ 導覽

    public void GoTo(string key)
    {
        var target = NavItems.FirstOrDefault(
            n => string.Equals(n.Key, key, StringComparison.OrdinalIgnoreCase));
        if (target is not null)
            SelectedNav = target;
    }

    [RelayCommand]
    private void Navigate(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            GoTo(key);
    }

    // -------------------------------------------------------------- 全域動作

    /// <summary>從任何頁面新增文章，並自動切到編輯器。</summary>
    [RelayCommand]
    private async Task NewPostAsync()
    {
        await ContentPage.CreatePostCommand.ExecuteAsync(null);
        AppStatus = ContentPage.StatusMessage;
        GoTo(ShellPages.Content);
    }

    /// <summary>儲存目前文章（Ctrl+S）。</summary>
    [RelayCommand]
    private async Task SaveCurrentAsync()
    {
        await ContentPage.SaveCommand.ExecuteAsync(null);
        AppStatus = ContentPage.StatusMessage;
    }

    /// <summary>本機預覽：沒開就開，開了就直接跳瀏覽器。</summary>
    [RelayCommand]
    private async Task PreviewSiteAsync()
    {
        if (!SetupPage.PreviewReady)
            await SetupPage.StartPreviewCommand.ExecuteAsync(null);

        if (SetupPage.PreviewReady)
            SetupPage.OpenPreviewInBrowserCommand.Execute(null);

        AppStatus = SetupPage.StatusMessage;
    }

    [RelayCommand]
    private void GoPublish() => GoTo(ShellPages.Publish);

    // -------------------------------------------------------- 命令面板行為

    [RelayCommand]
    private void OpenPalette()
    {
        PaletteQuery = string.Empty;
        ApplyPaletteFilter();
        IsPaletteOpen = true;
    }

    [RelayCommand]
    private void ClosePalette() => IsPaletteOpen = false;

    [RelayCommand]
    private async Task RunPaletteEntryAsync(CommandEntry? entry)
    {
        entry ??= SelectedPaletteEntry ?? PaletteResults.FirstOrDefault();
        if (entry is null)
            return;

        IsPaletteOpen = false;
        try
        {
            await entry.Run();
        }
        catch (Exception ex)
        {
            AppStatus = $"「{entry.Title}」失敗：{ex.Message}";
        }
    }

    public void MovePaletteSelection(int delta)
    {
        if (PaletteResults.Count == 0)
            return;

        var index = SelectedPaletteEntry is null ? -1 : PaletteResults.IndexOf(SelectedPaletteEntry);
        index += delta;
        if (index < 0)
            index = PaletteResults.Count - 1;
        else if (index >= PaletteResults.Count)
            index = 0;

        SelectedPaletteEntry = PaletteResults[index];
    }

    private void ApplyPaletteFilter()
    {
        var query = (PaletteQuery ?? string.Empty).Trim();
        PaletteResults.Clear();
        foreach (var entry in _commands.Where(c => c.Matches(query)).Take(12))
            PaletteResults.Add(entry);

        PaletteHasResults = PaletteResults.Count > 0;
        SelectedPaletteEntry = PaletteResults.FirstOrDefault();
    }

    private void BuildCommandPalette()
    {
        foreach (var nav in NavItems)
        {
            var key = nav.Key;
            _commands.Add(new CommandEntry
            {
                Title = $"前往「{nav.Title}」",
                Subtitle = nav.Description,
                Group = "前往",
                Keywords = key,
                Run = () =>
                {
                    GoTo(key);
                    return Task.CompletedTask;
                },
            });
        }

        _commands.Add(new CommandEntry
        {
            Title = "新增文章",
            Subtitle = "自動配編號與 front matter，建立後直接開始編輯",
            Group = "寫作",
            Keywords = "new post article 新文章 建立",
            Shortcut = "Ctrl+N",
            Run = () => NewPostAsync(),
        });

        _commands.Add(new CommandEntry
        {
            Title = "儲存目前文章",
            Subtitle = "寫入 Markdown 與 front matter",
            Group = "寫作",
            Keywords = "save 存檔",
            Shortcut = "Ctrl+S",
            Run = () => SaveCurrentAsync(),
        });

        _commands.Add(new CommandEntry
        {
            Title = "本機預覽網站",
            Subtitle = "啟動 hugo server 並在瀏覽器開啟",
            Group = "預覽",
            Keywords = "preview server 預覽",
            Shortcut = "Ctrl+P",
            Run = () => PreviewSiteAsync(),
        });

        _commands.Add(new CommandEntry
        {
            Title = "發布上線",
            Subtitle = "前往發布頁推送到 Pages",
            Group = "發布",
            Keywords = "publish deploy push 部署 推送",
            Shortcut = "Ctrl+Shift+P",
            Run = () =>
            {
                GoTo(ShellPages.Publish);
                return Task.CompletedTask;
            },
        });

        _commands.Add(new CommandEntry
        {
            Title = "安裝並啟用 Stack 主題",
            Subtitle = "下載 Stack 並寫入 hugo.toml 的 theme",
            Group = "外觀",
            Keywords = "theme stack 主題 佈景",
            Run = () => HomePage.PrimaryActionCommand.ExecuteAsync(null),
        });

        _commands.Add(new CommandEntry
        {
            Title = "開啟現有網站資料夾",
            Subtitle = "選擇本機已有的 Hugo 網站",
            Group = "網站",
            Keywords = "open folder 開啟 資料夾",
            Run = () => SetupPage.BrowseSiteCommand.ExecuteAsync(null),
        });

        _commands.Add(new CommandEntry
        {
            Title = "重新偵測 Hugo",
            Subtitle = "檢查安裝狀態與最新版本",
            Group = "環境",
            Keywords = "hugo detect refresh 偵測",
            Run = () => SetupPage.RefreshHugoCommand.ExecuteAsync(null),
        });
    }

    // ------------------------------------------------------------------ 其他

    private async Task RefreshCodeStatisticsAsync()
    {
        try
        {
            var stats = await CodeStatisticsService.CountAsync();
            CodeStatisticsSummary = stats.Summary;
            CodeStatisticsDetails = stats.Details;
        }
        catch (Exception ex)
        {
            CodeStatisticsSummary = "暫不可用";
            CodeStatisticsDetails = $"程式碼統計失敗：{ex.Message}";
        }
    }

    private async Task NavigateToPageAsync(PageViewModelBase page, int generation)
    {
        if (_disposed)
            return;

        try
        {
            await page.OnNavigatedToAsync();
            if (!_disposed && generation == _navigationGeneration)
                AppStatus = page.StatusMessage;
        }
        catch (Exception ex)
        {
            page.StatusMessage = ex.Message;
            if (!_disposed && generation == _navigationGeneration)
                AppStatus = $"載入「{page.Title}」失敗：{ex.Message}";
        }
    }

    private void OnSiteChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        UpdateSiteBanner();
        if (CurrentPage is not null)
            _ = NavigateToPageAsync(CurrentPage, ++_navigationGeneration);
    }

    private void OnAppStatusChanged(object? sender, string message)
    {
        if (!_disposed)
            AppStatus = message;
    }

    private void UpdateSiteBanner()
    {
        var path = _services.CurrentSitePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            HasSite = false;
            SiteBanner = "尚未選擇網站";
            return;
        }

        HasSite = true;
        SiteBanner = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name
            ? name
            : path;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _services.SiteChanged -= OnSiteChanged;
        _services.AppStatusChanged -= OnAppStatusChanged;
        HomePage.Dispose();
        SetupPage.Dispose();
        ConfigPage.Dispose();
        ThemesPage.Dispose();
        ContentPage.Dispose();
        MigrationPage.Dispose();
        MenuPage.Dispose();
        GitHubPage.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>導覽項目。<see cref="SelectCommand"/> 由外框注入，讓樣板不必做祖先繫結。</summary>
public partial class NavItem : ObservableObject
{
    public NavItem(
        string key,
        string title,
        string glyph,
        string description,
        PageViewModelBase page,
        bool isPrimary)
    {
        Key = key;
        Title = title;
        Glyph = glyph;
        Description = description;
        Page = page;
        IsPrimary = isPrimary;
    }

    public string Key { get; }

    public string Title { get; }

    public string Glyph { get; }

    public string Description { get; }

    public PageViewModelBase Page { get; }

    public bool IsPrimary { get; }

    public System.Windows.Input.ICommand? SelectCommand { get; set; }

    [ObservableProperty]
    public partial bool IsActive { get; set; }
}
