using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Helpers;
using Hugoer.Models;
using Hugoer.Services;

namespace Hugoer.ViewModels;

public partial class ContentViewModel : PageViewModelBase, IDisposable
{
    public ContentViewModel()
        : this(AppServices.Instance)
    {
    }

    public ContentViewModel(AppServices services)
        : base(services)
    {
        Title = "文章";
        _autoSave = new IdleAutoSave(
            () => IsDirty && SelectedFile is { IsDirectory: false },
            () => SaveCoreAsync(refreshList: false, auto: true));
        var saved = Services.Settings.Current.MarkdownEditorMode;
        if (saved.Equals("Source", StringComparison.OrdinalIgnoreCase))
            EditorMode = MarkdownEditorMode.Source;
        else
            RefreshEditorModePresentation();
        AlignCorrespondingPreview();
        RefreshPreviewPresentation();
    }

    public ObservableCollection<ContentItem> Files { get; } = [];

    public IReadOnlyList<string> SortOptions { get; } =
    [
        "文章日期（新到舊）",
        "文章日期（舊到新）",
        "名稱（A–Z）",
        "名稱（Z–A）"
    ];

    public IReadOnlyList<string> StatusOptions { get; } =
    [
        "全部文章",
        "已發布",
        "草稿",
        "未設定日期"
    ];

    public IReadOnlyList<string> ExportTargetOptions { get; } =
    [
        "Hexo",
        "Jekyll"
    ];

    [ObservableProperty]
    public partial ContentItem? SelectedFile { get; set; }

    [ObservableProperty]
    public partial string EditorText { get; set; } = string.Empty;

    /// <summary>Markdown body bound to live preview (same as editor; control strips front matter).</summary>
    [ObservableProperty]
    public partial string PreviewMarkdown { get; set; } = string.Empty;

    /// <summary>Markdown body shown in the CKEditor-style source output pane (front matter excluded).</summary>
    [ObservableProperty]
    public partial string PreviewBodyMarkdown { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasPreviewBody { get; set; }

    [ObservableProperty]
    public partial bool ShowPreview { get; set; } = true;

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string NewPostTitle { get; set; } = ArticleCode.Format(DateTimeOffset.Now, 1);

    [ObservableProperty]
    public partial string NewPostFolder { get; set; } = "post";

    [ObservableProperty]
    public partial string NextArticleCode { get; set; } = ArticleCode.Format(DateTimeOffset.Now, 1);

    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SortMode { get; set; } = "文章日期（新到舊）";

    [ObservableProperty]
    public partial string StatusFilter { get; set; } = "全部文章";

    [ObservableProperty]
    public partial string ExportTarget { get; set; } = "Hexo";

    [ObservableProperty]
    public partial string FileCountLabel { get; set; } = "0 篇";

    [ObservableProperty]
    public partial string ContentSummary { get; set; } = "尚未載入文章";

    [ObservableProperty]
    public partial bool HasSelection { get; set; }

    [ObservableProperty]
    public partial bool HasVisibleFiles { get; set; }

    [ObservableProperty]
    public partial bool ShowEmptyState { get; set; } = true;

    [ObservableProperty]
    public partial bool HasActiveFilters { get; set; }

    [ObservableProperty]
    public partial string EmptyStateTitle { get; set; } = "尚無文章";

    [ObservableProperty]
    public partial string EmptyStateDescription { get; set; } = "建立第一篇文章後，會顯示在這裡。";

    [ObservableProperty]
    public partial string FrontMatterTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrontMatterDate { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrontMatterSlug { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrontMatterCategories { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrontMatterTags { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrontMatterImage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FrontMatterDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDraft { get; set; } = true;

    [ObservableProperty]
    public partial string PreviewModeLabel { get; set; } = "即時預覽：開";

    [ObservableProperty]
    public partial string EditorStatistics { get; set; } = "正文 0 字元 · 1 行";

    [ObservableProperty]
    public partial MarkdownEditorMode EditorMode { get; set; } = MarkdownEditorMode.Wysiwyg;

    [ObservableProperty]
    public partial bool IsWysiwygMode { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSourceMode { get; set; }

    [ObservableProperty]
    public partial string EditorModeTitle { get; set; } = "WYSIWYG 視覺編輯";

    [ObservableProperty]
    public partial string EditorModeHint { get; set; } = "像 CKEditor 一樣直接編排內容；右側可查看產生的 Markdown。";

    [ObservableProperty]
    public partial MarkdownPreviewKind PreviewKind { get; set; } = MarkdownPreviewKind.MarkdownOutput;

    [ObservableProperty]
    public partial bool IsRenderPreview { get; set; }

    [ObservableProperty]
    public partial bool IsMarkdownOutputPreview { get; set; } = true;

    [ObservableProperty]
    public partial string PreviewCorrespondenceHint { get; set; } = "對應 WYSIWYG：產生的 Markdown 原文";

    [ObservableProperty]
    public partial string PreviewKindTitle { get; set; } = "Markdown 輸出";

    private bool _loading;
    private bool _syncingFrontMatter;
    private bool _suppressEditorState;
    private bool _disposed;
    private List<ContentItem> _all = [];
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _loadCts;
    private int _loadGeneration;
    private readonly IdleAutoSave _autoSave;

    public override Task OnNavigatedToAsync()
    {
        if (!_disposed)
            Refresh();
        return Task.CompletedTask;
    }

    partial void OnSelectedFileChanging(ContentItem? oldValue, ContentItem? newValue)
    {
        if (_disposed)
            return;

        _loadGeneration++;
        _loadCts?.Cancel();
        _autoSave.Cancel();
        if (_loading || !IsDirty || oldValue is null || oldValue.IsDirectory)
            return;

        var path = oldValue.FullPath;
        var text = EditorText;
        IsDirty = false;
        _ = PersistSilentlyAsync(path, text);
    }

    partial void OnSelectedFileChanged(ContentItem? value)
    {
        if (_disposed)
            return;

        if (value is null)
        {
            if (!_suppressEditorState)
                ResetEditorState();
            return;
        }

        HasSelection = !value.IsDirectory;
        if (!value.IsDirectory)
        {
            var load = new CancellationTokenSource();
            _loadCts = load;
            _ = LoadFileAsync(value, load, _loadGeneration);
        }
    }

    partial void OnEditorTextChanged(string value)
    {
        if (_disposed || _suppressEditorState)
            return;

        UpdateEditorStatistics(value);
        UpdatePreviewBody(value);
        if (!_loading)
            MarkDirty();

        if (!_syncingFrontMatter)
            PopulateFrontMatter(value);

        SchedulePreviewUpdate(value);
    }

    private void UpdatePreviewBody(string markdown)
    {
        PreviewBodyMarkdown = MarkdownPreviewService.StripFrontMatter(markdown ?? string.Empty);
        HasPreviewBody = !string.IsNullOrWhiteSpace(PreviewBodyMarkdown);
    }

    private void UpdateEditorStatistics(string markdown)
    {
        var body = MarkdownPreviewService.StripFrontMatter(markdown);
        var characters = body.Count(character => !char.IsWhiteSpace(character));
        var lines = string.IsNullOrEmpty(body) ? 1 : body.Count(character => character == '\n') + 1;
        EditorStatistics = $"正文 {characters:N0} 字元 · {lines:N0} 行";
    }

    partial void OnFrontMatterTitleChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnFrontMatterDateChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnFrontMatterSlugChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnFrontMatterCategoriesChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnFrontMatterTagsChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnFrontMatterImageChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnFrontMatterDescriptionChanged(string value) => UpdateEditorFromFrontMatter();
    partial void OnIsDraftChanged(bool value) => UpdateEditorFromFrontMatter();

    partial void OnShowPreviewChanged(bool value)
    {
        PreviewModeLabel = value ? "即時預覽：開" : "即時預覽：關";
        if (value)
            SchedulePreviewUpdate(EditorText);
    }

    partial void OnEditorModeChanged(MarkdownEditorMode value)
    {
        RefreshEditorModePresentation();
        AlignCorrespondingPreview();
        Services.Settings.SetMarkdownEditorMode(value == MarkdownEditorMode.Source ? "Source" : "Wysiwyg");
    }

    partial void OnPreviewKindChanged(MarkdownPreviewKind value) => RefreshPreviewPresentation();

    private void RefreshEditorModePresentation()
    {
        IsWysiwygMode = EditorMode == MarkdownEditorMode.Wysiwyg;
        IsSourceMode = EditorMode == MarkdownEditorMode.Source;
        EditorModeTitle = IsWysiwygMode ? "WYSIWYG 視覺編輯" : "Markdown 原始碼";
        EditorModeHint = IsWysiwygMode
            ? "像 CKEditor 一樣直接編排內容；右側可查看產生的 Markdown。"
            : "直接編輯 Markdown 原文；右側可查看即時渲染。";
        RefreshPreviewPresentation();
    }

    /// <summary>
    /// CKEditor 5 markdown demo: WYSIWYG corresponds to Markdown output;
    /// source editing corresponds to the rendered preview.
    /// </summary>
    private void AlignCorrespondingPreview()
    {
        var corresponding = EditorMode == MarkdownEditorMode.Source
            ? MarkdownPreviewKind.Render
            : MarkdownPreviewKind.MarkdownOutput;
        if (PreviewKind != corresponding)
            PreviewKind = corresponding;
        else
            RefreshPreviewPresentation();
    }

    private void RefreshPreviewPresentation()
    {
        IsRenderPreview = PreviewKind == MarkdownPreviewKind.Render;
        IsMarkdownOutputPreview = PreviewKind == MarkdownPreviewKind.MarkdownOutput;
        PreviewKindTitle = IsRenderPreview ? "渲染預覽" : "Markdown 輸出";
        PreviewCorrespondenceHint = (EditorMode, PreviewKind) switch
        {
            (MarkdownEditorMode.Wysiwyg, MarkdownPreviewKind.Render) =>
                "對應 WYSIWYG：Markdig 渲染結果",
            (MarkdownEditorMode.Wysiwyg, MarkdownPreviewKind.MarkdownOutput) =>
                "對應 WYSIWYG：產生的 Markdown 原文",
            (MarkdownEditorMode.Source, MarkdownPreviewKind.Render) =>
                "對應原始碼：即時渲染預覽",
            _ =>
                "對應原始碼：目前 Markdown 正文"
        };
    }

    [RelayCommand]
    private void SetWysiwygMode() => EditorMode = MarkdownEditorMode.Wysiwyg;

    [RelayCommand]
    private void SetSourceMode() => EditorMode = MarkdownEditorMode.Source;

    [RelayCommand]
    private void SetRenderPreview() => PreviewKind = MarkdownPreviewKind.Render;

    [RelayCommand]
    private void SetMarkdownOutputPreview() => PreviewKind = MarkdownPreviewKind.MarkdownOutput;

    [RelayCommand]
    private void ToggleEditorMode() =>
        EditorMode = EditorMode == MarkdownEditorMode.Wysiwyg
            ? MarkdownEditorMode.Source
            : MarkdownEditorMode.Wysiwyg;

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnSortModeChanged(string value) => ApplyFilter();

    partial void OnStatusFilterChanged(string value) => ApplyFilter();

    private void SchedulePreviewUpdate(string value)
    {
        if (_disposed)
            return;

        _previewCts?.Cancel();
        var debounce = new CancellationTokenSource();
        _previewCts = debounce;
        _ = ApplyPreviewAfterDelayAsync(value, debounce);
    }

    private async Task ApplyPreviewAfterDelayAsync(string value, CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(120, debounce.Token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && !debounce.IsCancellationRequested)
                    PreviewMarkdown = value;
            });
        }
        catch (OperationCanceledException) when (debounce.IsCancellationRequested)
        {
            // A newer keystroke replaced this pending preview update.
        }
        catch
        {
            // Preview updates are best-effort; a closed dispatcher must not
            // surface as an unobserved exception from the debounce task.
        }
        finally
        {
            if (ReferenceEquals(_previewCts, debounce))
                _previewCts = null;
            debounce.Dispose();
        }
    }

    [RelayCommand]
    private void TogglePreview()
    {
        ShowPreview = !ShowPreview;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Filter = string.Empty;
        StatusFilter = "全部文章";
    }

    [RelayCommand]
    private void Refresh()
    {
        if (_disposed)
            return;

        var selectedPath = SelectedFile?.FullPath;
        Files.Clear();
        if (!RequireSite(out var site))
        {
            _all = [];
            ResetEditorState();
            ApplyFilter();
            return;
        }

        _all = Services.Content.ListArticles(site).ToList();
        ApplyFilter();
        if (selectedPath is not null)
            SelectedFile = Files.FirstOrDefault(item =>
                item.FullPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
        StatusMessage = $"共 {_all.Count} 篇 Markdown";
    }

    private void ApplyFilter()
    {
        Files.Clear();
        IEnumerable<ContentItem> q = _all;
        if (!string.IsNullOrWhiteSpace(Filter))
        {
            q = q.Where(f =>
                f.RelativePath.Contains(Filter, StringComparison.OrdinalIgnoreCase)
                || f.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase));
        }

        q = StatusFilter switch
        {
            "已發布" => q.Where(item => item.IsPublished),
            "草稿" => q.Where(item => item.IsDraft),
            "未設定日期" => q.Where(item => !item.HasArticleDate),
            _ => q
        };

        q = SortMode switch
        {
            "文章日期（舊到新）" => q.OrderByDescending(item => item.ArticleDate.HasValue)
                .ThenBy(item => item.ArticleDate)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase),
            "名稱（A–Z）" => q.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase),
            "名稱（Z–A）" => q.OrderByDescending(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase),
            _ => q.OrderByDescending(item => item.ArticleDate.HasValue)
                .ThenByDescending(item => item.ArticleDate)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        };

        foreach (var item in q)
            Files.Add(item);

        var publishedCount = _all.Count(item => item.IsPublished);
        var draftCount = _all.Count(item => item.IsDraft);
        var missingDateCount = _all.Count(item => !item.HasArticleDate);
        ContentSummary = $"{_all.Count} 篇文章 · {publishedCount} 已發布 · {draftCount} 草稿 · {missingDateCount} 缺少日期";

        HasActiveFilters = !string.IsNullOrWhiteSpace(Filter) || StatusFilter != "全部文章";
        FileCountLabel = !HasActiveFilters
            ? $"{Files.Count} 篇"
            : $"顯示 {Files.Count} / {_all.Count} 篇";
        HasVisibleFiles = Files.Count > 0;
        ShowEmptyState = !HasVisibleFiles;
        EmptyStateTitle = _all.Count == 0 ? "尚無文章" : "找不到符合條件的文章";
        EmptyStateDescription = _all.Count == 0
            ? "按「新增文章」會以西元年月日-編號建立檔名，例如 20260823-1.md。歸檔、搜尋、關於等請到「選單」分頁。"
            : "調整搜尋文字或狀態篩選後再試一次。";

        RefreshSuggestedArticleCode();

        if (SelectedFile is not null && !Files.Contains(SelectedFile))
            SelectedFile = null;
    }

    private async Task LoadFileAsync(
        ContentItem item,
        CancellationTokenSource load,
        int generation)
    {
        if (_disposed)
        {
            load.Dispose();
            return;
        }

        _autoSave.Cancel();
        _loading = true;
        try
        {
            var text = await Services.Content.ReadAsync(item.FullPath, load.Token);
            if (!IsCurrentLoad(item, generation))
                return;

            EditorText = text;
            PreviewMarkdown = EditorText;
            UpdatePreviewBody(EditorText);
            PopulateFrontMatter(EditorText);
            IsDirty = false;
            StatusMessage = item.RelativePath;
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
            // Selection changed before the file finished loading.
        }
        catch (Exception ex)
        {
            if (IsCurrentLoad(item, generation))
                StatusMessage = ex.Message;
        }
        finally
        {
            if (generation == _loadGeneration)
                _loading = false;
            if (ReferenceEquals(_loadCts, load))
                _loadCts = null;
            load.Dispose();
        }
    }

    private bool IsCurrentLoad(ContentItem item, int generation) =>
        !_disposed
        && generation == _loadGeneration
        && SelectedFile?.FullPath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase) == true;

    [RelayCommand]
    private Task SaveAsync() => SaveCoreAsync(refreshList: true, auto: false);

    private void MarkDirty()
    {
        IsDirty = true;
        _autoSave.Schedule();
    }

    private async Task SaveCoreAsync(bool refreshList, bool auto)
    {
        if (_disposed)
            return;

        var selected = SelectedFile;
        var text = EditorText;
        if (selected is null || selected.IsDirectory)
        {
            if (!auto)
                StatusMessage = "請先選擇檔案";
            return;
        }

        try
        {
            await Services.Content.SaveAsync(selected.FullPath, text);
            if (SelectedFile?.FullPath.Equals(selected.FullPath, StringComparison.OrdinalIgnoreCase) != true)
                return;

            IsDirty = false;
            _autoSave.Cancel();
            StatusMessage = auto
                ? $"已自動儲存：{selected.RelativePath}"
                : $"已儲存：{selected.RelativePath}";
            if (refreshList)
                Refresh();
        }
        catch (Exception ex)
        {
            StatusMessage = auto ? $"自動儲存失敗：{ex.Message}" : ex.Message;
        }
    }

    private async Task PersistSilentlyAsync(string fullPath, string text)
    {
        if (_disposed)
            return;

        try
        {
            await Services.Content.SaveAsync(fullPath, text);
            if (SelectedFile?.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true)
                StatusMessage = $"已自動儲存：{Path.GetFileName(fullPath)}";
        }
        catch (Exception ex)
        {
            if (SelectedFile?.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase) == true)
                StatusMessage = $"自動儲存失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CreatePostAsync()
    {
        if (!RequireSite(out var site)) return;

        if (!TryNormalizeContentFolder(site, NewPostFolder, out var folder))
        {
            StatusMessage = "文章資料夾路徑無效，請使用 content/ 內的相對路徑。";
            return;
        }

        var code = AllocateNextArticleCode(site, folder);
        var title = string.IsNullOrWhiteSpace(NewPostTitle) ? code : NewPostTitle.Trim();
        var relative = $"{folder}/{code}.md";

        try
        {
            var hugoResult = await Services.Hugo.NewContentAsync(site, relative);
            if (!hugoResult.Succeeded)
                await Services.Content.CreateMarkdownAsync(site, relative, title, slug: code);
            else
                await ApplyArticleCodeAsync(site, relative, title, code);

            StatusMessage = $"已建立：{relative}";
            Refresh();
            var created = _all.FirstOrDefault(f =>
                f.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase)
                || f.RelativePath.EndsWith(code + ".md", StringComparison.OrdinalIgnoreCase));
            if (created is not null)
                SelectedFile = created;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedFile is null) return;
        try
        {
            Services.Content.Delete(SelectedFile.FullPath);
            StatusMessage = $"已刪除：{SelectedFile.RelativePath}";
            SelectedFile = null;
            EditorText = string.Empty;
            PreviewMarkdown = string.Empty;
            UpdatePreviewBody(string.Empty);
            Refresh();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportSelectedAsync()
    {
        if (!RequireSite(out var site))
            return;
        if (SelectedFile is null || SelectedFile.IsDirectory)
        {
            StatusMessage = "請先選擇文章";
            return;
        }

        var target = StaticSiteDetector.Parse(ExportTarget);
        if (target is not StaticSiteKind.Hexo and not StaticSiteKind.Jekyll)
        {
            StatusMessage = "請選擇 Hexo 或 Jekyll 作為匯出格式。";
            return;
        }

        var folder = await DialogHelper.PickFolderAsync($"選擇 {ExportTarget} 匯出資料夾");
        if (string.IsNullOrWhiteSpace(folder))
            return;

        if (IsDirty)
            await SaveCoreAsync(refreshList: false, auto: false);

        await ExportArticlesAsync(
            site,
            [
                new ArticleExportInput
                {
                    FullPath = SelectedFile.FullPath,
                    RelativePath = SelectedFile.RelativePath,
                    Markdown = EditorText
                }
            ],
            target,
            folder);
    }

    [RelayCommand]
    private async Task ExportAllAsync()
    {
        if (!RequireSite(out var site))
            return;

        var target = StaticSiteDetector.Parse(ExportTarget);
        if (target is not StaticSiteKind.Hexo and not StaticSiteKind.Jekyll)
        {
            StatusMessage = "請選擇 Hexo 或 Jekyll 作為匯出格式。";
            return;
        }

        if (IsDirty)
            await SaveCoreAsync(refreshList: false, auto: false);

        var selectedPath = SelectedFile?.FullPath;
        var articles = Services.Content.ListArticles(site)
            .Select(item => new ArticleExportInput
            {
                FullPath = item.FullPath,
                RelativePath = item.RelativePath,
                Markdown = selectedPath is not null
                           && item.FullPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase)
                    ? EditorText
                    : null
            })
            .ToList();
        if (articles.Count == 0)
        {
            StatusMessage = "沒有可匯出的文章。";
            return;
        }

        var folder = await DialogHelper.PickFolderAsync($"選擇 {ExportTarget} 匯出資料夾");
        if (string.IsNullOrWhiteSpace(folder))
            return;

        await ExportArticlesAsync(site, articles, target, folder);
    }

    private async Task ExportArticlesAsync(
        string site,
        IReadOnlyList<ArticleExportInput> articles,
        StaticSiteKind target,
        string folder)
    {
        IsBusy = true;
        try
        {
            StatusMessage = $"正在匯出為 {StaticSiteDetector.DisplayName(target)} 相容格式…";
            var result = await Task.Run(() =>
                Services.SiteMigration.ExportArticles(site, articles, target, folder));
            StatusMessage = result.Message;
            if (result.Succeeded && Directory.Exists(result.DestinationPath))
                TryOpenFolder(result.DestinationPath);
        }
        catch (Exception ex)
        {
            StatusMessage = "匯出失敗：" + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void TryOpenFolder(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch
        {
            // The status message already contains the destination path.
        }
    }

    [RelayCommand]
    private async Task UploadCoverImageAsync()
    {
        if (!RequireSite(out var site)) return;
        if (!HasSelection)
        {
            StatusMessage = "請先選擇文章";
            return;
        }

        var path = await DialogHelper.PickFileAsync("選擇封面圖片", [DialogHelper.Images]);
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            var asset = MediaAssetService.Import(site, path, MediaKind.Image);
            FrontMatterImage = asset.PublicUrl;
            StatusMessage = $"封面已上傳至 static/{asset.Folder}/{Path.GetFileName(asset.DestinationPath)}";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private async Task OpenHtmlPreviewAsync()
    {
        try
        {
            var html = MarkdownPreviewService.ToHtmlDocument(
                EditorText,
                SelectedFile?.Name ?? "preview");
            html = MediaAssetService.ToPreviewHtml(html, Services.CurrentSitePath);
            var dir = Path.Combine(Path.GetTempPath(), "HugoerPreview");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "preview.html");
            await File.WriteAllTextAsync(path, html);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            StatusMessage = "已在瀏覽器開啟 HTML 預覽";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    partial void OnNewPostFolderChanged(string value) => RefreshSuggestedArticleCode();

    private void RefreshSuggestedArticleCode()
    {
        var site = Services.CurrentSitePath;
        var folder = TryNormalizeContentFolder(site, NewPostFolder, out var normalizedFolder)
            ? normalizedFolder
            : "post";
        var previous = NextArticleCode;
        NextArticleCode = string.IsNullOrWhiteSpace(site) || !Directory.Exists(site)
            ? ArticleCode.Format(DateTimeOffset.Now, 1)
            : AllocateNextArticleCode(site, folder);
        if (string.IsNullOrWhiteSpace(NewPostTitle)
            || NewPostTitle == previous
            || ArticleCodeLooksLikeDefault(NewPostTitle))
            NewPostTitle = NextArticleCode;
    }

    private static string AllocateNextArticleCode(string site, string folder)
    {
        var directory = Path.Combine(PathHelper.ContentDir(site), folder.Replace('/', Path.DirectorySeparatorChar));
        return ArticleCode.NextInDirectory(directory, DateTimeOffset.Now);
    }

    private static bool TryNormalizeContentFolder(string? site, string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(site))
            return false;

        var candidate = string.IsNullOrWhiteSpace(value) ? "post" : value.Trim().Trim('/');
        if (!PathHelper.TryResolveUnder(PathHelper.ContentDir(site), candidate, out var full, allowRoot: false))
            return false;

        normalized = Path.GetRelativePath(PathHelper.ContentDir(site), full).Replace('\\', '/');
        return !string.IsNullOrWhiteSpace(normalized) && normalized != ".";
    }

    private static bool ArticleCodeLooksLikeDefault(string value)
    {
        var text = value.Trim();
        return text.Length >= 10
               && text[8] == '-'
               && text.Take(8).All(char.IsDigit)
               && text.Skip(9).All(char.IsDigit);
    }

    /// <summary>
    /// Clears all state that belongs to a previously selected site. The editor
    /// property callbacks are deliberately suppressed so clearing a page cannot
    /// mark a phantom document dirty or auto-save it back into the old site.
    /// </summary>
    private void ResetEditorState()
    {
        _autoSave.Cancel();
        _previewCts?.Cancel();
        _previewCts = null;
        _loadCts?.Cancel();
        _loadCts = null;
        _loadGeneration++;
        _loading = true;
        _syncingFrontMatter = true;
        _suppressEditorState = true;
        try
        {
            IsDirty = false;
            SelectedFile = null;
            HasSelection = false;
            EditorText = string.Empty;
            PreviewMarkdown = string.Empty;
            PreviewBodyMarkdown = string.Empty;
            HasPreviewBody = false;
            UpdateEditorStatistics(string.Empty);
            FrontMatterTitle = string.Empty;
            FrontMatterDate = string.Empty;
            FrontMatterSlug = string.Empty;
            FrontMatterCategories = string.Empty;
            FrontMatterTags = string.Empty;
            FrontMatterImage = string.Empty;
            FrontMatterDescription = string.Empty;
            IsDraft = true;
        }
        finally
        {
            _suppressEditorState = false;
            _syncingFrontMatter = false;
            _loading = false;
        }
    }

    private async Task ApplyArticleCodeAsync(string site, string relative, string title, string code)
    {
        var full = Path.Combine(
            PathHelper.ContentDir(site),
            relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return;

        var document = Services.FrontMatter.Parse(await Services.Content.ReadAsync(full));
        document.Fields["title"] = title;
        document.Fields["slug"] = code;
        await Services.Content.SaveAsync(full, Services.FrontMatter.Write(document));
    }

    private void PopulateFrontMatter(string text)
    {
        var document = Services.FrontMatter.Parse(text);
        _syncingFrontMatter = true;
        try
        {
            FrontMatterTitle = GetField(document, "title");
            FrontMatterDate = GetField(document, "date");
            FrontMatterSlug = GetField(document, "slug");
            FrontMatterCategories = GetField(document, "categories");
            FrontMatterTags = GetField(document, "tags");
            FrontMatterImage = GetField(document, "image");
            FrontMatterDescription = GetField(document, "description");
            IsDraft = !document.Fields.TryGetValue("draft", out var draft)
                || !draft.Equals("false", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _syncingFrontMatter = false;
        }
    }

    private void UpdateEditorFromFrontMatter()
    {
        if (_loading || _syncingFrontMatter) return;

        var document = Services.FrontMatter.Parse(EditorText);
        SetField(document, "title", FrontMatterTitle);
        SetField(document, "date", FrontMatterDate);
        SetField(document, "slug", FrontMatterSlug);
        SetField(document, "categories", FrontMatterCategories);
        SetField(document, "tags", FrontMatterTags);
        SetField(document, "image", FrontMatterImage);
        SetField(document, "description", FrontMatterDescription);
        document.Fields["draft"] = IsDraft ? "true" : "false";

        _syncingFrontMatter = true;
        try
        {
            EditorText = Services.FrontMatter.Write(document);
        }
        finally
        {
            _syncingFrontMatter = false;
        }
    }

    private static string GetField(FrontMatterDocument document, string key) =>
        document.Fields.TryGetValue(key, out var value) ? value : string.Empty;

    private static void SetField(FrontMatterDocument document, string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            document.Fields.Remove(key);
        else
            document.Fields[key] = value.Trim();
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _loadGeneration++;
        _autoSave.Dispose();
        _suppressEditorState = true;
        _previewCts?.Cancel();
        _previewCts = null;
        _loadCts?.Cancel();
        _loadCts = null;
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
