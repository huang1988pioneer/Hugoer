using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hugoer.Helpers;
using Hugoer.Services;

namespace Hugoer.ViewModels;

public partial class ConfigViewModel : PageViewModelBase
{
    private readonly TomlParamsService _paramsService = new();
    private readonly HugoConfigService _configService = new();
    private List<ConfigFieldItem> _allConfigFields = [];

    public ConfigViewModel()
    {
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
    private readonly IdleAutoSave _autoSave;

    public override async Task OnNavigatedToAsync()
    {
        await RefreshFilesAsync();
    }

    partial void OnSelectedFileChanging(string? oldValue, string? newValue)
    {
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
        if (value is not null)
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
        ConfigFiles.Clear();
        if (!RequireSite(out var site))
        {
            EditorText = string.Empty;
            ParamFields.Clear();
            return;
        }

        var files = Services.Themes.ListThemeConfigFiles(site, Services.Hugo.InspectSite(site)?.ThemeName);
        foreach (var f in files)
            ConfigFiles.Add(f);

        SelectedFile = files.FirstOrDefault() ?? PathHelper.FindConfigFile(site);
        if (SelectedFile is not null)
            await LoadSelectedAsync();
        else
        {
            EditorText = "# 尚無設定檔。可建立 hugo.toml";
            StatusMessage = "找不到設定檔";
            ReloadParamsForm();
        }
    }

    [RelayCommand]
    private async Task LoadSelectedAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFile) || !File.Exists(SelectedFile))
            return;

        _autoSave.Cancel();
        _loading = true;
        try
        {
            EditorText = await File.ReadAllTextAsync(SelectedFile);
            IsDirty = false;
            ParseQuickFields(EditorText);
            ReloadParamsForm();
            ReloadAdvancedForm();
            StatusMessage = $"已載入：{SelectedFile}";
        }
        finally
        {
            _loading = false;
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
        if (string.IsNullOrWhiteSpace(SelectedFile))
        {
            if (auto) return;
            if (!RequireSite(out var site)) return;
            SelectedFile = Path.Combine(site, "hugo.toml");
        }

        try
        {
            await File.WriteAllTextAsync(SelectedFile, EditorText);
            IsDirty = false;
            _autoSave.Cancel();
            StatusMessage = auto ? $"已自動儲存：{SelectedFile}" : $"已儲存：{SelectedFile}";
        }
        catch (Exception ex)
        {
            StatusMessage = auto ? $"自動儲存失敗：{ex.Message}" : ex.Message;
        }
    }

    private async Task PersistSilentlyAsync(string path, string text)
    {
        try
        {
            await File.WriteAllTextAsync(path, text);
            StatusMessage = $"已自動儲存：{path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"自動儲存失敗：{ex.Message}";
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
        if (!RequireSite(out var site)) return;
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
            await File.WriteAllTextAsync(path, content);
        }

        await RefreshFilesAsync();
        SelectedFile = path;
        StatusMessage = "已建立 hugo.toml";
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
}
