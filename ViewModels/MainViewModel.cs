using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Services;

namespace Hugoer.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    public MainViewModel()
    {
        SetupPage = new SetupViewModel();
        ConfigPage = new ConfigViewModel();
        ThemesPage = new ThemesViewModel();
        ContentPage = new ContentViewModel();
        MenuPage = new MenuViewModel();
        GitHubPage = new GitHubViewModel();

        NavItems =
        [
            new NavItem("環境", "一鍵安裝 Hugo、建立／開啟／從 Git 平台複製網站", SetupPage),
            new NavItem("設定檔", "編輯 hugo.toml 等設定", ConfigPage),
            new NavItem("主題", "安裝 Stack 等 themes", ThemesPage),
            new NavItem("文章", "撰寫部落格文章", ContentPage),
            new NavItem("選單", "編輯網站導覽選單", MenuPage),
            new NavItem("Git 部署", "推送到 GitHub / GitLab / Codeberg / Bitbucket", GitHubPage),
        ];

        SelectedNav = NavItems[0];
        AppServices.Instance.SiteChanged += (_, _) => UpdateSiteBanner();
        AppServices.Instance.AppStatusChanged += (_, message) => AppStatus = message;
        UpdateSiteBanner();
        _ = SelectedNav.Page.OnNavigatedToAsync();
    }

    public ObservableCollection<NavItem> NavItems { get; }

    public SetupViewModel SetupPage { get; }
    public ConfigViewModel ConfigPage { get; }
    public ThemesViewModel ThemesPage { get; }
    public ContentViewModel ContentPage { get; }
    public MenuViewModel MenuPage { get; }
    public GitHubViewModel GitHubPage { get; }

    [ObservableProperty]
    public partial NavItem? SelectedNav { get; set; }

    [ObservableProperty]
    public partial PageViewModelBase? CurrentPage { get; set; }

    [ObservableProperty]
    public partial string SiteBanner { get; set; } = "尚未選擇網站";

    [ObservableProperty]
    public partial string AppStatus { get; set; } = "就緒";

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null) return;
        CurrentPage = value.Page;
        AppStatus = value.Page.StatusMessage;
        _ = value.Page.OnNavigatedToAsync();
    }

    [RelayCommand]
    private void GoToSetup() => SelectedNav = NavItems[0];

    private void UpdateSiteBanner()
    {
        var path = AppServices.Instance.CurrentSitePath;
        SiteBanner = string.IsNullOrWhiteSpace(path)
            ? "尚未選擇網站 — 請到「環境」開啟、建立或從 Git 平台複製"
            : $"目前網站：{path}";
    }

    public void Dispose()
    {
        SetupPage.Dispose();
        GitHubPage.Dispose();
        GC.SuppressFinalize(this);
    }
}

public sealed class NavItem(string title, string description, PageViewModelBase page)
{
    public string Title { get; } = title;
    public string Description { get; } = description;
    public PageViewModelBase Page { get; } = page;
}
