using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Models;
using Hugoer.Services;

namespace Hugoer.ViewModels;

/// <summary>
/// 首頁（工作台）。
///
/// 設計原則：任何時刻只呈現「一個最重要的下一步」，其餘動作降級為次要按鈕或
/// 進階分頁。使用者開啟 Hugoer 後不需要先理解七個分頁的分工，只要按畫面正中央
/// 那顆按鈕就能一路把網站做出來並發布上線。
/// </summary>
public partial class HomeViewModel : PageViewModelBase, IDisposable
{
    private readonly SetupViewModel _setup;
    private readonly ThemesViewModel _themes;
    private readonly ContentViewModel _content;
    private readonly GitHubViewModel _github;
    private bool _disposed;

    public HomeViewModel(
        AppServices services,
        SetupViewModel setup,
        ThemesViewModel themes,
        ContentViewModel content,
        GitHubViewModel github)
        : base(services)
    {
        _setup = setup ?? throw new ArgumentNullException(nameof(setup));
        _themes = themes ?? throw new ArgumentNullException(nameof(themes));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _github = github ?? throw new ArgumentNullException(nameof(github));

        Title = "首頁";

        Steps =
        [
            new HomeStep(1, "安裝 Hugo", "取得靜態網站產生器"),
            new HomeStep(2, "選定網站", "建立、開啟或從 Git 複製"),
            new HomeStep(3, "套用主題", "一鍵安裝並啟用 Stack"),
            new HomeStep(4, "寫下第一篇", "新增文章並即時預覽"),
            new HomeStep(5, "發布上線", "推送到 Pages 並監控版本"),
        ];

        _setup.PropertyChanged += OnDependencyPropertyChanged;
        _github.PropertyChanged += OnDependencyPropertyChanged;
        _themes.InstalledThemes.CollectionChanged += OnDependencyCollectionChanged;
        _content.Files.CollectionChanged += OnDependencyCollectionChanged;
        Services.SiteChanged += OnSiteChangedForHome;

        UpdateState();
    }

    /// <summary>由外框（MainViewModel）注入，讓首頁能把使用者帶到對應分頁。</summary>
    public IShellNavigator? Shell { get; set; }

    public ObservableCollection<HomeStep> Steps { get; }

    public ObservableCollection<ContentItem> RecentArticles { get; } = [];

    [ObservableProperty]
    public partial string NextStepBadge { get; set; } = "第 1 步 / 共 5 步";

    [ObservableProperty]
    public partial string NextStepTitle { get; set; } = "正在檢查環境…";

    [ObservableProperty]
    public partial string NextStepDescription { get; set; } =
        "Hugoer 會自動偵測 Hugo、目前網站與主題狀態，稍候即會顯示下一步。";

    [ObservableProperty]
    public partial string PrimaryActionText { get; set; } = "請稍候…";

    [ObservableProperty]
    public partial string PrimaryActionHint { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasPrimaryActionHint { get; set; }

    [ObservableProperty]
    public partial bool ShowOpenFolderAction { get; set; }

    [ObservableProperty]
    public partial bool ShowCloneAction { get; set; }

    [ObservableProperty]
    public partial bool IsReady { get; set; }

    [ObservableProperty]
    public partial bool HasSite { get; set; }

    [ObservableProperty]
    public partial string SiteTitle { get; set; } = "尚未選擇網站";

    [ObservableProperty]
    public partial string SitePathText { get; set; } = "從下方按鈕建立或開啟一個 Hugo 網站";

    [ObservableProperty]
    public partial string HugoSummary { get; set; } = "偵測中…";

    [ObservableProperty]
    public partial string ThemeSummary { get; set; } = "尚未安裝主題";

    [ObservableProperty]
    public partial string ArticleSummary { get; set; } = "0 篇文章";

    [ObservableProperty]
    public partial string OnlineSummary { get; set; } = "尚未發布";

    [ObservableProperty]
    public partial string PagesUrlText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasPagesUrl { get; set; }

    [ObservableProperty]
    public partial bool HasRecentArticles { get; set; }

    [ObservableProperty]
    public partial bool PreviewReady { get; set; }

    private HomeStage _stage = HomeStage.Detecting;

    public override async Task OnNavigatedToAsync()
    {
        if (_disposed)
            return;

        // 首頁是啟動後的第一個畫面，因此它負責把「環境／主題／文章」三份狀態
        // 一次讀齊；使用者不需要為了看到現況而先逐一點過分頁。
        await _setup.OnNavigatedToAsync();
        if (_disposed) return;

        await _themes.OnNavigatedToAsync();
        if (_disposed) return;

        await _content.OnNavigatedToAsync();
        if (_disposed) return;

        UpdateState();
    }

    // ---------------------------------------------------------------- 主要動作

    [RelayCommand]
    private async Task PrimaryActionAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            switch (_stage)
            {
                case HomeStage.InstallHugo:
                    await _setup.InstallHugoCommand.ExecuteAsync(null);
                    break;

                case HomeStage.ChooseSite:
                    await _setup.CreateSiteCommand.ExecuteAsync(null);
                    break;

                case HomeStage.InstallTheme:
                    await InstallAndActivateStackAsync();
                    break;

                case HomeStage.FirstPost:
                case HomeStage.Ready:
                    await NewPostAsync();
                    break;

                case HomeStage.Publish:
                    Shell?.GoTo(ShellPages.Publish);
                    break;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        UpdateState();
    }

    /// <summary>安裝 Stack 並直接啟用，省去「安裝 → 回清單 → 選取 → 啟用」四步。</summary>
    private async Task InstallAndActivateStackAsync()
    {
        await _themes.InstallStackCommand.ExecuteAsync(null);
        if (_disposed) return;

        var installed = _themes.InstalledThemes.FirstOrDefault(
                            t => t.Contains("stack", StringComparison.OrdinalIgnoreCase))
                        ?? _themes.InstalledThemes.FirstOrDefault();
        if (installed is null)
        {
            StatusMessage = _themes.StatusMessage;
            return;
        }

        _themes.SelectedInstalled = installed;
        await _themes.ActivateThemeCommand.ExecuteAsync(null);
        StatusMessage = _themes.StatusMessage;
    }

    [RelayCommand]
    private async Task NewPostAsync()
    {
        await _content.CreatePostCommand.ExecuteAsync(null);
        StatusMessage = _content.StatusMessage;
        Shell?.GoTo(ShellPages.Content);
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        await _setup.BrowseSiteCommand.ExecuteAsync(null);
        UpdateState();
    }

    [RelayCommand]
    private void GoClone() => Shell?.GoTo(ShellPages.Setup);

    [RelayCommand]
    private void GoPublish() => Shell?.GoTo(ShellPages.Publish);

    [RelayCommand]
    private void GoContent() => Shell?.GoTo(ShellPages.Content);

    [RelayCommand]
    private void GoThemes() => Shell?.GoTo(ShellPages.Themes);

    [RelayCommand]
    private void GoConfig() => Shell?.GoTo(ShellPages.Config);

    [RelayCommand]
    private void GoSetup() => Shell?.GoTo(ShellPages.Setup);

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (_setup.PreviewReady)
        {
            _setup.OpenPreviewInBrowserCommand.Execute(null);
            return;
        }

        await _setup.StartPreviewCommand.ExecuteAsync(null);
        if (_disposed) return;

        if (_setup.PreviewReady)
            _setup.OpenPreviewInBrowserCommand.Execute(null);

        StatusMessage = _setup.StatusMessage;
    }

    [RelayCommand]
    private void OpenArticle(ContentItem? item)
    {
        if (item is null || item.IsDirectory)
            return;

        _content.SelectedFile = item;
        Shell?.GoTo(ShellPages.Content);
    }

    [RelayCommand]
    private void OpenSiteFolder()
    {
        var path = Services.CurrentSitePath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        TryOpen(path);
    }

    [RelayCommand]
    private void OpenPagesUrl()
    {
        if (!HasPagesUrl)
            return;

        TryOpen(PagesUrlText);
    }

    private void TryOpen(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusMessage = $"無法開啟 {target}：{ex.Message}";
        }
    }

    // ------------------------------------------------------------ 狀態機計算

    private void UpdateState()
    {
        if (_disposed)
            return;

        var site = Services.CurrentSitePath;
        var siteReady = !string.IsNullOrWhiteSpace(site) && Directory.Exists(site);
        var hugoReady = _setup.HugoInstalled;
        var themeReady = _themes.InstalledThemes.Count > 0;
        var articleCount = _content.Files.Count;
        var pagesUrl = FirstUrl(_github.PagesUrl, _github.ProviderPagesUrl);

        HasSite = siteReady;
        PreviewReady = _setup.PreviewReady;

        SiteTitle = siteReady ? Path.GetFileName(site!.TrimEnd(Path.DirectorySeparatorChar)) : "尚未選擇網站";
        SitePathText = siteReady ? site! : "從下方按鈕建立或開啟一個 Hugo 網站";

        HugoSummary = hugoReady ? _setup.HugoStatus : "尚未安裝";
        ThemeSummary = themeReady
            ? string.Join("、", _themes.InstalledThemes.Take(3))
            : "尚未安裝主題";
        ArticleSummary = siteReady ? $"{articleCount} 篇文章" : "—";

        HasPagesUrl = pagesUrl.Length > 0;
        PagesUrlText = pagesUrl;
        OnlineSummary = HasPagesUrl ? "已設定線上網址" : "尚未發布";

        RefreshRecentArticles();

        _stage = !hugoReady ? HomeStage.InstallHugo
            : !siteReady ? HomeStage.ChooseSite
            : !themeReady ? HomeStage.InstallTheme
            : articleCount == 0 ? HomeStage.FirstPost
            : !HasPagesUrl ? HomeStage.Publish
            : HomeStage.Ready;

        ApplyStagePresentation(hugoReady, siteReady, themeReady, articleCount);
    }

    private void ApplyStagePresentation(bool hugoReady, bool siteReady, bool themeReady, int articleCount)
    {
        ShowOpenFolderAction = false;
        ShowCloneAction = false;
        IsReady = false;

        switch (_stage)
        {
            case HomeStage.InstallHugo:
                NextStepBadge = "第 1 步 / 共 5 步";
                NextStepTitle = "先把 Hugo 裝起來";
                NextStepDescription = "Hugoer 會下載官方 Hugo Extended 並放進應用程式資料夾，不需要你自己設定 PATH。";
                PrimaryActionText = _setup.HugoInstallButtonText;
                PrimaryActionHint = _setup.HugoUpdateMessage;
                ShowOpenFolderAction = true;
                break;

            case HomeStage.ChooseSite:
                NextStepBadge = "第 2 步 / 共 5 步";
                NextStepTitle = "建立你的第一個網站";
                NextStepDescription = "按右邊那顆按鈕就會在文件資料夾建立一個可用的 Hugo 網站；已經有網站或遠端原始碼時，改用下方兩個選項。";
                PrimaryActionText = "建立網站";
                PrimaryActionHint = BuildNewSiteHint();
                ShowOpenFolderAction = true;
                ShowCloneAction = true;
                break;

            case HomeStage.InstallTheme:
                NextStepBadge = "第 3 步 / 共 5 步";
                NextStepTitle = "套用外觀";
                NextStepDescription = "一次完成「安裝 Stack 主題」與「寫入 hugo.toml 的 theme 設定」，不用再回主題分頁手動啟用。";
                PrimaryActionText = "安裝並啟用 Stack 主題";
                PrimaryActionHint = "也可以到「更多 → 主題」挑其他佈景。";
                break;

            case HomeStage.FirstPost:
                NextStepBadge = "第 4 步 / 共 5 步";
                NextStepTitle = "寫下第一篇文章";
                NextStepDescription = "Hugoer 會自動配好文章編號、日期與 front matter，建立後直接進入編輯器，右側即時預覽。";
                PrimaryActionText = "新增文章";
                PrimaryActionHint = "快捷鍵 Ctrl+N";
                break;

            case HomeStage.Publish:
                NextStepBadge = "第 5 步 / 共 5 步";
                NextStepTitle = "把網站發布上線";
                NextStepDescription = $"目前有 {articleCount} 篇文章可以上線。發布頁會帶你連結 repository 並推送到 Pages，之後每 5 分鐘自動確認線上版本。";
                PrimaryActionText = "前往發布";
                PrimaryActionHint = "快捷鍵 Ctrl+Shift+P";
                break;

            default:
                IsReady = true;
                NextStepBadge = "設定完成";
                NextStepTitle = "網站已就緒，開始寫作吧";
                NextStepDescription = "環境、主題、文章與線上網址都已備妥。日常只需要「新增文章 → 發布」兩個動作。";
                PrimaryActionText = "新增文章";
                PrimaryActionHint = "快捷鍵 Ctrl+N；發布請按 Ctrl+Shift+P";
                break;
        }

        HasPrimaryActionHint = !string.IsNullOrWhiteSpace(PrimaryActionHint);
        UpdateSteps(hugoReady, siteReady, themeReady, articleCount);
    }

    private string BuildNewSiteHint()
    {
        var parent = _setup.NewSiteParent;
        var name = _setup.NewSiteName;
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
            return "可在「更多 → 環境」調整資料夾與名稱。";

        return $"將建立於 {Path.Combine(parent, name)}";
    }

    private void UpdateSteps(bool hugoReady, bool siteReady, bool themeReady, int articleCount)
    {
        var done = new[]
        {
            hugoReady,
            siteReady,
            themeReady,
            articleCount > 0,
            HasPagesUrl,
        };

        var current = Array.FindIndex(done, d => !d);
        for (var i = 0; i < Steps.Count && i < done.Length; i++)
        {
            var step = Steps[i];
            step.IsDone = done[i];
            step.IsCurrent = i == current;
            step.IsPending = !step.IsDone && !step.IsCurrent;
            step.StateText = step.IsDone ? "已完成" : step.IsCurrent ? "進行中" : "待完成";
        }
    }

    private void RefreshRecentArticles()
    {
        RecentArticles.Clear();
        foreach (var item in _content.Files.Where(f => !f.IsDirectory).Take(6))
            RecentArticles.Add(item);

        HasRecentArticles = RecentArticles.Count > 0;
    }

    private static string FirstUrl(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate.Trim();
        }

        return string.Empty;
    }

    // ------------------------------------------------------------------ 事件

    private void OnDependencyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
            return;

        switch (e.PropertyName)
        {
            case nameof(SetupViewModel.HugoInstalled):
            case nameof(SetupViewModel.HugoStatus):
            case nameof(SetupViewModel.HugoInstallButtonText):
            case nameof(SetupViewModel.HugoUpdateMessage):
            case nameof(SetupViewModel.PreviewReady):
            case nameof(SetupViewModel.NewSiteName):
            case nameof(SetupViewModel.NewSiteParent):
            case nameof(GitHubViewModel.PagesUrl):
            case nameof(GitHubViewModel.ProviderPagesUrl):
                UpdateState();
                break;
        }
    }

    private void OnDependencyCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        UpdateState();

    private void OnSiteChangedForHome(object? sender, EventArgs e) => UpdateState();

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _setup.PropertyChanged -= OnDependencyPropertyChanged;
        _github.PropertyChanged -= OnDependencyPropertyChanged;
        _themes.InstalledThemes.CollectionChanged -= OnDependencyCollectionChanged;
        _content.Files.CollectionChanged -= OnDependencyCollectionChanged;
        Services.SiteChanged -= OnSiteChangedForHome;
        base.Dispose();
    }

    private enum HomeStage
    {
        Detecting,
        InstallHugo,
        ChooseSite,
        InstallTheme,
        FirstPost,
        Publish,
        Ready,
    }
}

/// <summary>首頁進度列的一站。</summary>
public partial class HomeStep : ObservableObject
{
    public HomeStep(int index, string title, string description)
    {
        Index = index;
        Title = title;
        Description = description;
    }

    public int Index { get; }

    public string Title { get; }

    public string Description { get; }

    public string IndexText => Index.ToString();

    [ObservableProperty]
    public partial bool IsDone { get; set; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    public partial bool IsPending { get; set; } = true;

    [ObservableProperty]
    public partial string StateText { get; set; } = "待完成";
}
