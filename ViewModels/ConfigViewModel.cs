using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Helpers;
using Hugoer.Services;

namespace Hugoer.ViewModels;

public partial class ConfigViewModel : PageViewModelBase, IDisposable
{
    private readonly TomlParamsService _paramsService;
    private readonly HugoConfigService _configService;
    private List<ConfigFieldItem> _allConfigFields = [];

    public ConfigViewModel()
        : this(AppServices.Instance)
    {
    }

    public ConfigViewModel(AppServices services)
        : base(services)
    {
        _paramsService = services.Params;
        _configService = services.Config;
        Title = "設定檔";
        _autoSave = new IdleAutoSave(
            () => IsDirty && !string.IsNullOrWhiteSpace(SelectedFile),
            () => SaveCoreAsync(auto: true));
    }

    public ObservableCollection<string> ConfigFiles { get; } = [];
    public ObservableCollection<ParamFieldItem> ParamFields { get; } = [];
    public ObservableCollection<ConfigFieldItem> ConfigFields { get; } = [];
    public ObservableCollection<string> ConfigGroups { get; } = ["全部"];

    [ObservableProperty]
    public partial string? SelectedFile { get; set; }

    [ObservableProperty]
    public partial string EditorText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string BaseUrl { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SiteTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string LanguageCode { get; set; } = "zh-tw";

    [ObservableProperty]
    public partial string ThemeName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedTabIndex { get; set; }

    [ObservableProperty]
    public partial string NewParamKey { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewParamValue { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ConfigSearch { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SelectedConfigGroup { get; set; } = "全部";

    [ObservableProperty]
    public partial string ConfigCatalogSummary { get; set; } = string.Empty;

    private bool _loading;
    private bool _suppressSelectionLoad;
    private bool _disposed;
    private int _loadGeneration;
    private CancellationTokenSource? _loadCts;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly IdleAutoSave _autoSave;

    public override async Task OnNavigatedToAsync()
    {
        if (_disposed)
            return;

        await RefreshFilesAsync();
    }

    partial void OnSelectedFileChanging(string? oldValue, string? newValue)
    {
        if (_disposed)
            return;

        _autoSave.Cancel();
        if (_loading || !IsDirty || string.IsNullOrWhiteSpace(oldValue))
            return;

        var path = oldValue;
        var text = EditorText;
        IsDirty = false;
        _ = PersistSilentlyAsync(path, text);
    }

    partial void OnSelectedFileChanged(string? value)
    {
        if (_disposed)
            return;

        if (value is null)
        {
            if (!_loading)
                ResetEditorState();
            return;
        }

        if (!_suppressSelectionLoad)
            _ = LoadSelectedAsync();
    }

    partial void OnEditorTextChanged(string value)
    {
        if (!_loading)
            MarkDirty();
    }

    partial void OnConfigSearchChanged(string value) => ApplyConfigFilter();
    partial void OnSelectedConfigGroupChanged(string value) => ApplyConfigFilter();

    [RelayCommand]
    private async Task RefreshFilesAsync()
    {
        if (_disposed)
            return;

        CancelPendingLoad();
        ConfigFiles.Clear();
        if (!RequireSite(out var site))
        {
            ResetEditorState();
            return;
        }

        var files = Services.Themes.ListThemeConfigFiles(site, Services.Hugo.InspectSite(site)?.ThemeName);
        foreach (var f in files)
            ConfigFiles.Add(f);

        var selected = files.FirstOrDefault() ?? PathHelper.FindConfigFile(site);
        _suppressSelectionLoad = true;
        try
        {
            SelectedFile = selected;
        }
        finally
        {
            _suppressSelectionLoad = false;
        }

        if (selected is not null)
            await LoadSelectedAsync();
        else
        {
            _loading = true;
            try
            {
                EditorText = "# 尚無設定檔。可建立 hugo.toml";
                IsDirty = false;
                StatusMessage = "找不到設定檔";
                ParseQuickFields(EditorText);
                ReloadParamsForm();
                ReloadAdvancedForm();
            }
            finally
            {
                _loading = false;
            }
        }
    }

    [RelayCommand]
    private async Task LoadSelectedAsync()
    {
        var path = SelectedFile;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        _autoSave.Cancel();
        var load = BeginLoad();
        _loading = true;
        try
        {
            var text = await File.ReadAllTextAsync(path, load.Source.Token);
            if (!IsCurrentLoad(path, load.Generation))
                return;

            EditorText = text;
            IsDirty = false;
            ParseQuickFields(EditorText);
            ReloadParamsForm();
            ReloadAdvancedForm();
            StatusMessage = $"已載入：{path}";
        }
        catch (OperationCanceledException) when (load.Source.IsCancellationRequested)
        {
            // A newer selection or refresh superseded this read.
        }
        catch (Exception ex)
        {
            if (IsCurrentLoad(path, load.Generation))
                StatusMessage = ex.Message;
        }
        finally
        {
            if (load.Generation == _loadGeneration)
                _loading = false;
            if (ReferenceEquals(_loadCts, load.Source))
                _loadCts = null;
            load.Source.Dispose();
        }
    }

    [RelayCommand]
    private Task SaveAsync() => SaveCoreAsync(auto: false);

    private void MarkDirty()
    {
        IsDirty = true;
        _autoSave.Schedule();
    }

    private async Task SaveCoreAsync(bool auto)
    {
        if (_disposed)
            return;

        var selected = SelectedFile;
        if (string.IsNullOrWhiteSpace(selected))
        {
            if (auto) return;
            if (!RequireSite(out var site)) return;
            selected = Path.Combine(site, "hugo.toml");
            SelectedFile = selected;
        }

        var text = EditorText;
        await _saveGate.WaitAsync().ConfigureAwait(true);

        try
        {
            await AtomicFileWriter.WriteAllTextAsync(selected, text).ConfigureAwait(true);
            if (string.Equals(SelectedFile, selected, StringComparison.OrdinalIgnoreCase)
                && string.Equals(EditorText, text, StringComparison.Ordinal))
            {
                IsDirty = false;
                _autoSave.Cancel();
                StatusMessage = auto ? $"已自動儲存：{selected}" : $"已儲存：{selected}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = auto ? $"自動儲存失敗：{ex.Message}" : ex.Message;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task PersistSilentlyAsync(string path, string text)
    {
        await _saveGate.WaitAsync().ConfigureAwait(true);
        try
        {
            await AtomicFileWriter.WriteAllTextAsync(path, text).ConfigureAwait(true);
            if (string.Equals(SelectedFile, path, StringComparison.OrdinalIgnoreCase))
                StatusMessage = $"已自動儲存：{path}";
        }
        catch (Exception ex)
        {
            if (string.Equals(SelectedFile, path, StringComparison.OrdinalIgnoreCase))
                StatusMessage = $"自動儲存失敗：{ex.Message}";
        }
        finally
        {
            _saveGate.Release();
        }
    }

    [RelayCommand]
    private void ApplyQuickFields()
    {
        try
        {
            EditorText = _paramsService.UpsertSimpleRootKeys(EditorText, new Dictionary<string, string>
            {
                ["baseURL"] = BaseUrl,
                ["title"] = SiteTitle,
                ["locale"] = LanguageCode,
                ["theme"] = ThemeName
            });
            StatusMessage = "已套用網站基本欄位（記得按儲存）";
            MarkDirty();
            ReloadParamsForm();
            ReloadAdvancedForm();
        }
        catch (Exception ex)
        {
            // Fallback to line-based upsert
            var text = EditorText;
            text = UpsertTomlLine(text, "baseURL", BaseUrl);
            text = UpsertTomlLine(text, "title", SiteTitle);
            text = UpsertTomlLine(text, "locale", LanguageCode);
            if (!string.IsNullOrWhiteSpace(ThemeName))
                text = UpsertTomlLine(text, "theme", ThemeName);
            EditorText = text;
            StatusMessage = $"已套用快速欄位（簡易模式）：{ex.Message}";
            ReloadAdvancedForm();
        }
    }

    [RelayCommand]
    private void ApplyParamsForm()
    {
        try
        {
            EditorText = _paramsService.ApplyParamsToToml(EditorText, ParamFields);
            ParseQuickFields(EditorText);
            MarkDirty();
            StatusMessage = "已將 params 表單寫入編輯器（記得按儲存）";
            ReloadParamsForm();
        }
        catch (Exception ex)
        {
            StatusMessage = $"套用 params 失敗：{ex.Message}";
        }
    }

    [RelayCommand]
    private void ReloadParamsForm()
    {
        ParamFields.Clear();
        foreach (var item in _paramsService.LoadParamsForm(EditorText))
            ParamFields.Add(item);
    }

    [RelayCommand]
    private void ReloadAdvancedForm()
    {
        try
        {
            _allConfigFields = _configService.LoadForm(EditorText).ToList();
            ConfigGroups.Clear();
            ConfigGroups.Add("全部");
            foreach (var group in _allConfigFields.Select(field => field.Group).Distinct().OrderBy(group => group))
                ConfigGroups.Add(group);
            if (!ConfigGroups.Contains(SelectedConfigGroup))
                SelectedConfigGroup = "全部";
            ApplyConfigFilter();
            ConfigCatalogSummary = $"官方欄位 {HugoConfigService.Definitions.Count} 項；目前設定 {_allConfigFields.Count(field => field.IsConfigured)} 項";
        }
        catch (Exception ex)
        {
            ConfigFields.Clear();
            ConfigCatalogSummary = "無法載入進階設定";
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void ApplyAdvancedForm()
    {
        try
        {
            EditorText = _configService.ApplyToToml(EditorText, _allConfigFields);
            ParseQuickFields(EditorText);
            ReloadParamsForm();
            ReloadAdvancedForm();
            MarkDirty();
            StatusMessage = "已將 Hugo 進階設定套用到編輯器（記得按儲存）";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void OpenOfficialConfigDocs()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://gohugo.io/configuration/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"無法開啟官方文檔：{ex.Message}";
        }
    }

    private void ApplyConfigFilter()
    {
        ConfigFields.Clear();
        IEnumerable<ConfigFieldItem> fields = _allConfigFields;
        if (!string.IsNullOrWhiteSpace(SelectedConfigGroup) && SelectedConfigGroup != "全部")
            fields = fields.Where(field => field.Group.Equals(SelectedConfigGroup, StringComparison.Ordinal));
        if (!string.IsNullOrWhiteSpace(ConfigSearch))
            fields = fields.Where(field => field.SearchText.Contains(ConfigSearch.Trim(), StringComparison.OrdinalIgnoreCase));
        foreach (var field in fields)
            ConfigFields.Add(field);
    }

    [RelayCommand]
    private void AddCustomParam()
    {
        if (string.IsNullOrWhiteSpace(NewParamKey))
        {
            StatusMessage = "請輸入參數名稱（例如 social.github）";
            return;
        }

        var path = NewParamKey.Trim();
        if (ParamFields.Any(p => p.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "參數已存在";
            return;
        }

        ParamFields.Add(new ParamFieldItem
        {
            Key = path.Contains('.') ? path.Split('.')[^1] : path,
            Path = path,
            DisplayName = path,
            Description = "自訂參數",
            Kind = ParamFieldKind.String,
            StringValue = NewParamValue,
            IsConfigured = true,
            IsKnown = false
        });
        NewParamKey = string.Empty;
        NewParamValue = string.Empty;
        StatusMessage = $"已新增參數 {path}（按「套用 params 表單」寫入）";
    }

    [RelayCommand]
    private async Task CreateDefaultConfigAsync()
    {
        if (_disposed)
            return;

        if (!RequireSite(out var site)) return;
        try
        {
            var path = Path.Combine(site, "hugo.toml");
            if (!File.Exists(path))
            {
                var content = """
baseURL = 'https://example.org/'
locale = 'zh-tw'
title = 'My Hugo Site'
theme = ''

[params]
  description = 'A site managed by Hugoer'
  mainSections = ['post']

[params.colorScheme]
  toggle = true
  default = 'auto'
""";
                await AtomicFileWriter.WriteAllTextAsync(path, content);
            }

            await RefreshFilesAsync();
            if (!_disposed)
            {
                SelectedFile = path;
                StatusMessage = "已建立 hugo.toml";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "建立設定檔已取消。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"建立 hugo.toml 失敗：{ex.Message}";
        }
    }

    private void ParseQuickFields(string text)
    {
        BaseUrl = ReadTomlLine(text, "baseURL") ?? ReadTomlLine(text, "baseurl") ?? string.Empty;
        SiteTitle = ReadTomlLine(text, "title") ?? string.Empty;
        LanguageCode = ReadTomlLine(text, "locale") ?? ReadTomlLine(text, "languageCode") ?? "zh-tw";
        ThemeName = ReadTomlLine(text, "theme") ?? string.Empty;
    }

    private static string? ReadTomlLine(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("#", StringComparison.Ordinal)) continue;
            if (t.StartsWith("[", StringComparison.Ordinal)) continue;
            var idx = t.IndexOf('=');
            if (idx <= 0) continue;
            var k = t[..idx].Trim();
            if (!k.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            var v = t[(idx + 1)..].Trim().Trim('\'', '"');
            return v;
        }

        return null;
    }

    private static string UpsertTomlLine(string text, string key, string value)
    {
        value = value.Replace("'", "\\'");
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var found = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("#", StringComparison.Ordinal)) continue;
            if (t.StartsWith("[", StringComparison.Ordinal)) continue;
            var idx = t.IndexOf('=');
            if (idx <= 0) continue;
            var k = t[..idx].Trim();
            if (!k.Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            lines[i] = $"{key} = '{value}'";
            found = true;
            break;
        }

        if (!found)
            lines.Insert(0, $"{key} = '{value}'");

        return string.Join(Environment.NewLine, lines);
    }

    private (CancellationTokenSource Source, int Generation) BeginLoad()
    {
        _loadCts?.Cancel();
        var source = new CancellationTokenSource();
        _loadCts = source;
        return (source, ++_loadGeneration);
    }

    private void CancelPendingLoad()
    {
        _loadCts?.Cancel();
        _loadCts = null;
        _loadGeneration++;
    }

    private bool IsCurrentLoad(string path, int generation) =>
        !_disposed
        && generation == _loadGeneration
        && string.Equals(SelectedFile, path, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Clears all state that belongs to a previously selected site without
    /// triggering an auto-save of that site's last edited configuration file.
    /// </summary>
    private void ResetEditorState()
    {
        _autoSave.Cancel();
        _loading = true;
        try
        {
            IsDirty = false;
            SelectedFile = null;
            EditorText = string.Empty;
            BaseUrl = string.Empty;
            SiteTitle = string.Empty;
            LanguageCode = "zh-tw";
            ThemeName = string.Empty;
            ParamFields.Clear();
            _allConfigFields = [];
            ConfigFields.Clear();
            ConfigGroups.Clear();
            ConfigGroups.Add("全部");
            SelectedConfigGroup = "全部";
            ConfigCatalogSummary = string.Empty;
        }
        finally
        {
            _loading = false;
        }
    }

    public override void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _autoSave.Dispose();
        _loadCts?.Cancel();
        _loadCts = null;
        base.Dispose();
        GC.SuppressFinalize(this);
    }
}
