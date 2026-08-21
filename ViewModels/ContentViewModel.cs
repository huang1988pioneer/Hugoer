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
        if (value is not null && !value.IsDirectory)
            _ = LoadFileAsync(value);
    }

    partial void OnEditorTextChanged(string value)
    {
        if (!_loading)
            IsDirty = true;

        if (!_syncingFrontMatter)
            PopulateFrontMatter(value);

        SchedulePreviewUpdate(value);
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
    private void Refresh()
    {
        Files.Clear();
        if (!RequireSite(out var site)) return;

        _all = Services.Content.ListAllMarkdown(site).ToList();
        ApplyFilter();
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

        foreach (var item in q)
            Files.Add(item);
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
