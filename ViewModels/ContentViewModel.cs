using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Models;
using Hugoer.Services;

namespace Hugoer.ViewModels;

public partial class ContentViewModel : PageViewModelBase
{
    public ContentViewModel()
    {
        Title = "內容 Markdown";
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

    [ObservableProperty]
    public partial ContentItem? SelectedFile { get; set; }

    [ObservableProperty]
    public partial string EditorText { get; set; } = string.Empty;

    /// <summary>Markdown body bound to live preview (same as editor; control strips front matter).</summary>
    [ObservableProperty]
    public partial string PreviewMarkdown { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowPreview { get; set; } = true;

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string NewPostTitle { get; set; } = "hello-world";

    [ObservableProperty]
    public partial string NewPostFolder { get; set; } = "post";

    [ObservableProperty]
    public partial string Filter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SortMode { get; set; } = "文章日期（新到舊）";

    [ObservableProperty]
    public partial string StatusFilter { get; set; } = "全部文章";

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

    private bool _loading;
    private bool _syncingFrontMatter;
    private List<ContentItem> _all = [];
    private CancellationTokenSource? _previewCts;

    public override Task OnNavigatedToAsync()
    {
        Refresh();
        return Task.CompletedTask;
    }

    partial void OnSelectedFileChanged(ContentItem? value)
    {
        HasSelection = value is not null && !value.IsDirectory;
        if (value is not null && !value.IsDirectory)
            _ = LoadFileAsync(value);
    }

    partial void OnEditorTextChanged(string value)
    {
        UpdateEditorStatistics(value);
        if (!_loading)
            IsDirty = true;

        if (!_syncingFrontMatter)
            PopulateFrontMatter(value);

        SchedulePreviewUpdate(value);
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

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnSortModeChanged(string value) => ApplyFilter();

    partial void OnStatusFilterChanged(string value) => ApplyFilter();

    private void SchedulePreviewUpdate(string value)
    {
        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        // Debounce ~120ms so typing stays smooth
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(120, token);
                if (token.IsCancellationRequested) return;
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!token.IsCancellationRequested)
                        PreviewMarkdown = value;
                });
            }
            catch (TaskCanceledException)
            {
                // ignore
            }
        }, token);
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
        var selectedPath = SelectedFile?.FullPath;
        Files.Clear();
        if (!RequireSite(out var site)) return;

        _all = Services.Content.ListAllMarkdown(site).ToList();
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
            ? "在上方輸入標題，建立第一篇 Markdown 文章。"
            : "調整搜尋文字或狀態篩選後再試一次。";

        if (SelectedFile is not null && !Files.Contains(SelectedFile))
            SelectedFile = null;
    }

    private async Task LoadFileAsync(ContentItem item)
    {
        _loading = true;
        try
        {
            EditorText = await Services.Content.ReadAsync(item.FullPath);
            PreviewMarkdown = EditorText;
            PopulateFrontMatter(EditorText);
            IsDirty = false;
            StatusMessage = item.RelativePath;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedFile is null || SelectedFile.IsDirectory)
        {
            StatusMessage = "請先選擇檔案";
            return;
        }

        await Services.Content.SaveAsync(SelectedFile.FullPath, EditorText);
        IsDirty = false;
        StatusMessage = $"已儲存：{SelectedFile.RelativePath}";
        Refresh();
    }

    [RelayCommand]
    private async Task CreatePostAsync()
    {
        if (!RequireSite(out var site)) return;
        if (string.IsNullOrWhiteSpace(NewPostTitle))
        {
            StatusMessage = "請輸入標題";
            return;
        }

        var slug = Slugify(NewPostTitle);
        var relative = $"{NewPostFolder.Trim().Trim('/')}/{slug}.md";

        try
        {
            var hugoResult = await Services.Hugo.NewContentAsync(site, relative);
            if (!hugoResult.Succeeded)
                await Services.Content.CreateMarkdownAsync(site, relative, NewPostTitle);

            StatusMessage = $"已建立：{relative}";
            Refresh();
            var created = _all.FirstOrDefault(f =>
                f.RelativePath.Equals(relative, StringComparison.OrdinalIgnoreCase)
                || f.RelativePath.EndsWith(slug + ".md", StringComparison.OrdinalIgnoreCase));
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
            Refresh();
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

    private static string Slugify(string title)
    {
        var s = title.Trim().ToLowerInvariant();
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", "-");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[^a-z0-9\u4e00-\u9fff\-_]", "");
        return string.IsNullOrWhiteSpace(s) ? $"post-{DateTime.Now:yyyyMMddHHmmss}" : s;
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
}
