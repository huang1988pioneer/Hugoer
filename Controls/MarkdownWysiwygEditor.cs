using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Hugoer.Services;

namespace Hugoer.Controls;

/// <summary>
/// Quill-based Markdown WYSIWYG surface: markdown in, markdown out, edited as rich text.
/// The page lives in <c>Assets/editor/wysiwyg.html</c>; Quill itself is vendored under
/// <c>Assets/editor/quill/</c> and inlined at load time (the page CSP blocks external scripts).
/// </summary>
public sealed class MarkdownWysiwygEditor : UserControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownWysiwygEditor, string?>(nameof(Markdown));

    public static readonly StyledProperty<string?> SitePathProperty =
        AvaloniaProperty.Register<MarkdownWysiwygEditor, string?>(nameof(SitePath));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly NativeWebView? _webView;
    private readonly TextBlock _placeholder;
    private readonly Grid _root;
    private bool _ready;
    private bool _updatingFromHtml;
    private bool _loadedHtml;
    private string? _initError;
    private TaskCompletionSource<string?>? _flushWaiter;
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly SemaphoreSlim _pushGate = new(1, 1);

    public event EventHandler? MarkdownChanged;
    public event EventHandler? SaveRequested;
    public event EventHandler? ToggleModeRequested;
    public event EventHandler<string>? EditorFailed;

    public MarkdownWysiwygEditor()
    {
        _placeholder = new TextBlock
        {
            Text = "載入 WYSIWYG 編輯器…",
            Opacity = 0.55,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontStyle = FontStyle.Italic
        };

        _root = new Grid
        {
            Background = new SolidColorBrush(Color.Parse("#0D1218"))
        };
        _root.Children.Add(_placeholder);

        try
        {
            _webView = new NativeWebView { IsVisible = false };
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.WebMessageReceived += OnWebMessageReceived;
            _root.Children.Add(_webView);
        }
        catch (Exception ex)
        {
            _initError = ex.Message;
            _placeholder.Text = "無法啟動 WYSIWYG 編輯器（需要 WebView2）。請改用原始碼模式。";
        }

        Content = _root;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public string? SitePath
    {
        get => GetValue(SitePathProperty);
        set => SetValue(SitePathProperty, value);
    }

    public bool IsReady => _ready;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == MarkdownProperty || change.Property == SitePathProperty) && !_updatingFromHtml)
            PushMarkdownToWebView();
    }

    public async Task ExecAsync(string command, string? argument = null)
    {
        if (!_ready || _webView is null)
            return;
        var script =
            $"window.hugoerCommand({JsonSerializer.Serialize(command)}, {JsonSerializer.Serialize(argument ?? string.Empty)})";
        await TryInvokeScriptAsync(script);
    }

    public async Task AddMediaAsync(IReadOnlyDictionary<string, string> media)
    {
        if (!_ready || _webView is null || media.Count == 0)
            return;
        await TryInvokeScriptAsync($"window.hugoerAddMedia({JsonSerializer.Serialize(media)})");
    }

    public async Task FocusEditorAsync()
    {
        if (!_ready || _webView is null)
            return;
        await TryInvokeScriptAsync("window.hugoerFocus()");
    }

    public async Task FlushAsync()
    {
        await _flushGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (!_ready || _webView is null)
                return;

            var waiter = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _flushWaiter = waiter;
            try
            {
                await _webView.InvokeScript("window.hugoerFlush()");
                var completed = await Task.WhenAny(waiter.Task, Task.Delay(750));
                if (completed == waiter.Task && await waiter.Task.ConfigureAwait(true) is { } html)
                    ApplyHtml(html, notify: true);
            }
            catch (Exception ex)
            {
                ShowFailure("WYSIWYG 編輯器同步失敗。已可改用原始碼模式。", ex.Message);
            }
            finally
            {
                if (ReferenceEquals(_flushWaiter, waiter))
                    _flushWaiter = null;
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_loadedHtml)
            return;
        _loadedHtml = true;

        if (_webView is null)
        {
            ShowFailure("無法啟動 WYSIWYG 編輯器（需要 WebView2）。已可改用原始碼模式。", _initError ?? "webview unavailable");
            return;
        }

        try
        {
            _webView.NavigateToString(LoadEditorHtml());
        }
        catch (Exception ex)
        {
            ShowFailure("無法啟動 WYSIWYG 編輯器（需要 WebView2）。已可改用原始碼模式。", ex.Message);
        }
    }

    private async void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            ShowFailure("WYSIWYG 編輯器載入失敗。已可改用原始碼模式。", "navigation failed");
            return;
        }

        if (_webView is null)
            return;
        _webView.IsVisible = true;
        _placeholder.IsVisible = false;
        _ready = true;
        try
        {
            await _webView.InvokeScript("window.hugoerFocus()");
            PushMarkdownToWebView();
        }
        catch (Exception ex)
        {
            ShowFailure("WYSIWYG 編輯器初始化失敗。已可改用原始碼模式。", ex.Message);
        }
    }

    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Body))
            return;

        WysiwygMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<WysiwygMessage>(e.Body, JsonOptions);
        }
        catch
        {
            return;
        }

        if (message is null || string.IsNullOrWhiteSpace(message.Type))
            return;

        Dispatcher.UIThread.Post(() => HandleMessage(message));
    }

    private void HandleMessage(WysiwygMessage message)
    {
        switch (message.Type.ToLowerInvariant())
        {
            case "ready":
                _ready = true;
                if (_webView is not null)
                    _webView.IsVisible = true;
                _placeholder.IsVisible = false;
                PushMarkdownToWebView();
                break;
            case "change":
                ApplyEditedHtml(message);
                break;
            case "flush":
                _flushWaiter?.TrySetResult(message.Dirty == false ? null : message.Html ?? string.Empty);
                ApplyEditedHtml(message);
                break;
            case "save":
                ApplyEditedHtml(message);
                SaveRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "toggleMode":
                ApplyEditedHtml(message);
                ToggleModeRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "pastemarkdown":
                _ = PasteMarkdownAsync(message.Text ?? string.Empty);
                break;
        }
    }

    /// <summary>Only edited documents flow back; an untouched article is never re-normalised.</summary>
    private void ApplyEditedHtml(WysiwygMessage message)
    {
        if (message.Dirty != false)
            ApplyHtml(message.Html, notify: true);
    }

    private void ApplyHtml(string? html, bool notify)
    {
        var markdown = MarkdownWysiwygConverter.FromEditableHtml(
            MediaAssetService.FromPreviewHtml(html ?? string.Empty, SitePath));
        if (string.Equals(Markdown, markdown, StringComparison.Ordinal))
            return;

        _updatingFromHtml = true;
        try
        {
            SetCurrentValue(MarkdownProperty, markdown);
        }
        finally
        {
            _updatingFromHtml = false;
        }

        if (notify)
            MarkdownChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Renders pasted Markdown text with the same pipeline as the editor and inserts it at the caret.</summary>
    private async Task PasteMarkdownAsync(string markdown)
    {
        string html;
        Dictionary<string, string> media;
        try
        {
            html = MarkdownWysiwygConverter.ToEditableHtml(markdown);
            media = MediaAssetService.BuildPreviewMediaMap(html, SitePath);
        }
        catch
        {
            html = string.Empty;
            media = new Dictionary<string, string>();
        }

        await TryInvokeScriptAsync(
            $"window.hugoerPasteHtml({JsonSerializer.Serialize(html)}, {JsonSerializer.Serialize(media)})");
    }

    private void PushMarkdownToWebView() => _ = PushMarkdownToWebViewAsync();

    private async Task PushMarkdownToWebViewAsync()
    {
        if (!_ready || _webView is null)
            return;

        await _pushGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (!_ready || _webView is null)
                return;

            var html = MarkdownWysiwygConverter.ToEditableHtml(Markdown ?? string.Empty);
            var media = MediaAssetService.BuildPreviewMediaMap(html, SitePath);
            var script = media.Count == 0
                ? $"window.hugoerSetHtml({JsonSerializer.Serialize(html)})"
                : $"window.hugoerSetHtml({JsonSerializer.Serialize(html)}, {JsonSerializer.Serialize(media)})";
            await _webView.InvokeScript(script);
        }
        catch (Exception ex)
        {
            ShowFailure("WYSIWYG 編輯器更新失敗。已可改用原始碼模式。", ex.Message);
        }
        finally
        {
            _pushGate.Release();
        }
    }

    private async Task<bool> TryInvokeScriptAsync(string script)
    {
        if (!_ready || _webView is null)
            return false;

        try
        {
            await _webView.InvokeScript(script);
            return true;
        }
        catch (Exception ex)
        {
            ShowFailure("WYSIWYG 編輯器操作失敗。已可改用原始碼模式。", ex.Message);
            return false;
        }
    }

    private void ShowFailure(string userMessage, string detail)
    {
        _ready = false;
        if (_webView is not null)
            _webView.IsVisible = false;
        _placeholder.Text = userMessage;
        _placeholder.IsVisible = true;
        EditorFailed?.Invoke(this, detail);
    }

    private static string LoadEditorHtml() =>
        ReadAsset("wysiwyg.html")
            .Replace("/*__QUILL_CORE_CSS__*/", ReadAsset("quill/quill.core.css"), StringComparison.Ordinal)
            .Replace("/*__QUILL_JS__*/", ReadAsset("quill/quill.js"), StringComparison.Ordinal);

    private static string ReadAsset(string relativePath)
    {
        using var stream = AssetLoader.Open(new Uri("avares://Hugoer/Assets/editor/" + relativePath));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class WysiwygMessage
    {
        public string Type { get; set; } = string.Empty;
        public string? Html { get; set; }
        public string? Text { get; set; }
        public bool? Dirty { get; set; }
    }
}
