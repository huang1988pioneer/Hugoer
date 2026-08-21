using System.Collections.ObjectModel;
using System.Globalization;
using Tomlyn;
using Tomlyn.Model;

namespace Hugoer.Services;

public enum ParamFieldKind
{
    String,
    Bool,
    Number,
    Array,
    Nested
}

public sealed class ParamFieldItem
{
    public required string Key { get; init; }
    public required string Path { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ParamFieldKind Kind { get; init; }
    public string StringValue { get; set; } = string.Empty;
    public bool BoolValue { get; set; }
    public string Section { get; init; } = "params";
    public bool IsKnown { get; init; }
    public bool IsConfigured { get; set; }
    public bool IsEditable => Kind != ParamFieldKind.Nested;

    public bool IsBool => Kind == ParamFieldKind.Bool;
    public bool IsText => Kind != ParamFieldKind.Bool;
    public string KindLabel => Kind switch
    {
        ParamFieldKind.Bool => "布林",
        ParamFieldKind.Number => "數字",
        ParamFieldKind.Array => "陣列",
        ParamFieldKind.Nested => "巢狀",
        _ => "文字"
    };
}

public sealed class TomlParamsService
{
    /// <summary>Common Hugo + Stack theme params with labels.</summary>
    public static IReadOnlyList<(string Key, string Label, string Description, ParamFieldKind Kind)> KnownParams { get; } =
    [
        ("description", "網站描述", "SEO / 首頁摘要", ParamFieldKind.String),
        ("mainSections", "主要區塊", "例如 post（逗號分隔）", ParamFieldKind.Array),
        ("colorScheme.default", "色彩模式", "auto / light / dark", ParamFieldKind.String),
        ("colorScheme.toggle", "允許切換色彩模式", "顯示明暗模式切換", ParamFieldKind.Bool),
        ("rssFullContent", "RSS 顯示全文", "Stack v4 RSS 是否輸出全文", ParamFieldKind.Bool),
        ("favicon", "網站圖示", "Stack v4 assets 路徑，例如 img/favicon.png", ParamFieldKind.String),
        ("SortBy", "文章排序", "Stack v4：default / lastmod（保留大寫 S）", ParamFieldKind.String),
        ("footer.since", "Footer 起始年", "版權起始年份", ParamFieldKind.Number),
        ("footer.customText", "Footer 文字", "自訂頁尾文字", ParamFieldKind.String),
        ("dateFormat.published", "發佈日期格式", "Stack v4，例如 :date_full", ParamFieldKind.String),
        ("dateFormat.lastUpdated", "更新日期格式", "Stack v4，例如 :date_full", ParamFieldKind.String),
        ("sidebar.compact", "緊湊側欄", "Stack v4 緊湊側欄模式", ParamFieldKind.Bool),
        ("sidebar.emoji", "側欄 Emoji", "Stack 側欄圖示", ParamFieldKind.String),
        ("sidebar.subtitle", "側欄副標", "個人簡介副標", ParamFieldKind.String),
        ("sidebar.avatar", "側欄頭像", "Stack v4 單一 assets 路徑；空白代表停用", ParamFieldKind.String),
        ("article.headingAnchor", "標題錨點", "Stack v4 顯示 heading anchor", ParamFieldKind.Bool),
        ("article.math", "數學排版", "Stack v4 數學功能；亦需 Goldmark passthrough", ParamFieldKind.Bool),
        ("article.toc", "文章目錄", "Stack v4 顯示目錄", ParamFieldKind.Bool),
        ("article.readingTime", "閱讀時間", "Stack v4 顯示預估閱讀時間", ParamFieldKind.Bool),
        ("article.list.showTags", "列表顯示標籤", "Stack v4 文章列表顯示 tags", ParamFieldKind.Bool),
        ("article.license.enabled", "顯示文章授權", "Stack v4 文章授權區塊", ParamFieldKind.Bool),
        ("article.license.default", "預設授權文字", "Stack v4 預設文章授權", ParamFieldKind.String),
        ("article.mermaid.look", "Mermaid 外觀", "classic / handDrawn", ParamFieldKind.String),
        ("article.mermaid.lightTheme", "Mermaid 淺色主題", "default / neutral / forest / base", ParamFieldKind.String),
        ("article.mermaid.darkTheme", "Mermaid 深色主題", "dark / neutral / forest / base", ParamFieldKind.String),
        ("article.mermaid.securityLevel", "Mermaid 安全層級", "strict / loose / antiscript / sandbox", ParamFieldKind.String),
        ("article.mermaid.htmlLabels", "Mermaid HTML 標籤", "需搭配 loose 安全層級", ParamFieldKind.Bool),
        ("article.mermaid.transparentBackground", "Mermaid 透明背景", "圖表使用透明背景", ParamFieldKind.Bool),
        ("article.alertIcon.note", "Note 圖示", "Stack v4 alert icon", ParamFieldKind.String),
        ("article.alertIcon.tip", "Tip 圖示", "Stack v4 alert icon", ParamFieldKind.String),
        ("article.alertIcon.important", "Important 圖示", "Stack v4 alert icon", ParamFieldKind.String),
        ("article.alertIcon.warning", "Warning 圖示", "Stack v4 alert icon", ParamFieldKind.String),
        ("article.alertIcon.caution", "Caution 圖示", "Stack v4 alert icon", ParamFieldKind.String),
        ("opengraph.twitter.site", "X/Twitter 帳號", "Stack v4 Open Graph site", ParamFieldKind.String),
        ("opengraph.twitter.card", "X/Twitter 卡片", "summary / summary_large_image", ParamFieldKind.String),
        ("imageProcessing.autoOrient", "自動旋轉圖片", "依 EXIF 方向自動旋轉", ParamFieldKind.Bool),
        ("imageProcessing.external.timeout", "外部圖片逾時", "Go duration，例如 5s", ParamFieldKind.String),
        ("imageProcessing.content.enabled", "處理內容圖片", "Stack v4 響應式內容圖片", ParamFieldKind.Bool),
        ("imageProcessing.content.widths", "內容圖片寬度", "遞增正整數，例如 800, 1600, 2400", ParamFieldKind.Array),
        ("imageProcessing.thumbnail.enabled", "處理縮圖", "Stack v4；取代舊 cover.enabled", ParamFieldKind.Bool),
        ("cookies.enabled", "Cookie 同意介面", "Stack v4 cookie consent", ParamFieldKind.Bool),
        ("cookies.showSettings", "Cookie 設定按鈕", "允許訪客調整 cookie 類別", ParamFieldKind.Bool),
        ("cookies.categories.analytics", "分析 Cookie", "Stack v4 analytics 類別", ParamFieldKind.Bool),
        ("cookies.categories.functional", "功能 Cookie", "Stack v4 functional 類別", ParamFieldKind.Bool),
        ("comments.enabled", "啟用留言", "Stack v4 留言總開關", ParamFieldKind.Bool),
        ("comments.provider", "留言服務", "giscus / utterances / waline 等", ParamFieldKind.String),
        ("comments.giscus.repo", "Giscus 儲存庫", "owner/repo", ParamFieldKind.String),
        ("comments.giscus.repoID", "Giscus Repo ID", "Giscus 產生的 repo ID", ParamFieldKind.String),
        ("comments.giscus.category", "Giscus 分類", "Discussion category 名稱", ParamFieldKind.String),
        ("comments.giscus.categoryID", "Giscus Category ID", "Giscus 產生的 category ID", ParamFieldKind.String),
        ("comments.giscus.mapping", "Giscus 對應方式", "title / pathname / url 等", ParamFieldKind.String),
        ("comments.giscus.lightTheme", "Giscus 淺色主題", "例如 light", ParamFieldKind.String),
        ("comments.giscus.darkTheme", "Giscus 深色主題", "例如 dark_dimmed", ParamFieldKind.String),
        ("comments.giscus.reactionsEnabled", "Giscus 表情回應", "0 / 1", ParamFieldKind.Number),
        ("comments.giscus.emitMetadata", "Giscus metadata", "0 / 1", ParamFieldKind.Number),
        ("comments.giscus.inputPosition", "Giscus 輸入位置", "top / bottom", ParamFieldKind.String),
        ("comments.giscus.lang", "Giscus 語言", "例如 zh-TW", ParamFieldKind.String),
        ("comments.giscus.strict", "Giscus 嚴格對應", "0 / 1", ParamFieldKind.Number),
        ("comments.giscus.loading", "Giscus 載入方式", "lazy / eager", ParamFieldKind.String),
    ];

    public ObservableCollection<ParamFieldItem> LoadParamsForm(string tomlText)
    {
        var result = new ObservableCollection<ParamFieldItem>();
        TomlTable? root = TryParse(tomlText);

        var flat = new Dictionary<string, (ParamFieldKind Kind, string Value, bool Bool)>(StringComparer.OrdinalIgnoreCase);
        if (root is not null)
        {
            var paramsTable = GetOrNullTable(root, "params");
            if (paramsTable is not null)
                FlattenTable(paramsTable, "", flat);
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, label, desc, kind) in KnownParams)
        {
            used.Add(key);
            flat.TryGetValue(key, out var existing);
            result.Add(new ParamFieldItem
            {
                Key = key.Contains('.') ? key.Split('.')[^1] : key,
                Path = key,
                DisplayName = label,
                Description = desc,
                Kind = kind,
                StringValue = existing.Value ?? string.Empty,
                BoolValue = existing.Bool,
                IsConfigured = flat.ContainsKey(key),
                IsKnown = true
            });
        }

        foreach (var (path, val) in flat.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (used.Contains(path)) continue;
            result.Add(new ParamFieldItem
            {
                Key = path.Contains('.') ? path.Split('.')[^1] : path,
                Path = path,
                DisplayName = path,
                Description = "自訂參數",
                Kind = val.Kind,
                StringValue = val.Value,
                BoolValue = val.Bool,
                IsConfigured = true,
                IsKnown = false
            });
        }

        return result;
    }

    public string ApplyParamsToToml(string tomlText, IEnumerable<ParamFieldItem> fields)
    {
        var root = TryParse(tomlText) ?? new TomlTable();

        if (!root.TryGetValue("params", out var paramsObj) || paramsObj is not TomlTable paramsTable)
        {
            paramsTable = new TomlTable();
            root["params"] = paramsTable;
        }

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Path))
                continue;

            // Arrays of tables and other complex theme structures are shown for
            // awareness, but must remain untouched by the scalar form editor.
            if (!field.IsEditable)
                continue;

            if (!field.IsConfigured)
            {
                RemovePath(paramsTable, field.Path);
                continue;
            }

            if (!field.IsKnown && field.Kind != ParamFieldKind.Bool
                && string.IsNullOrWhiteSpace(field.StringValue))
                continue;

            SetPathValue(paramsTable, field.Path, field);
        }

        return TomlSerializer.Serialize(root);
    }

    public string UpsertSimpleRootKeys(string tomlText, IDictionary<string, string> rootKeys)
    {
        var root = TryParse(tomlText) ?? new TomlTable();

        foreach (var (k, v) in rootKeys)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            root[k] = v ?? string.Empty;
        }

        return TomlSerializer.Serialize(root);
    }

    private static TomlTable? TryParse(string tomlText)
    {
        if (string.IsNullOrWhiteSpace(tomlText))
            return new TomlTable();

        try
        {
            return TomlSerializer.Deserialize<TomlTable>(tomlText);
        }
        catch
        {
            return null;
        }
    }

    private static TomlTable? GetOrNullTable(TomlTable root, string key)
    {
        if (root.TryGetValue(key, out var obj) && obj is TomlTable t)
            return t;
        return null;
    }

    private static void FlattenTable(
        TomlTable table,
        string prefix,
        Dictionary<string, (ParamFieldKind Kind, string Value, bool Bool)> flat)
    {
        foreach (var kv in table)
        {
            var path = string.IsNullOrEmpty(prefix) ? kv.Key : $"{prefix}.{kv.Key}";
            switch (kv.Value)
            {
                case string s:
                    flat[path] = (ParamFieldKind.String, s, false);
                    break;
                case bool b:
                    flat[path] = (ParamFieldKind.Bool, b ? "true" : "false", b);
                    break;
                case long l:
                    flat[path] = (ParamFieldKind.Number, l.ToString(CultureInfo.InvariantCulture), false);
                    break;
                case int i:
                    flat[path] = (ParamFieldKind.Number, i.ToString(CultureInfo.InvariantCulture), false);
                    break;
                case double d:
                    flat[path] = (ParamFieldKind.Number, d.ToString(CultureInfo.InvariantCulture), false);
                    break;
                case TomlArray arr when arr.All(item => item is not TomlTable):
                    flat[path] = (ParamFieldKind.Array, string.Join(", ", arr.Select(FormatScalar)), false);
                    break;
                case TomlArray:
                    flat[path] = (ParamFieldKind.Nested, "複雜物件陣列；請使用原始 TOML 編輯", false);
                    break;
                case TomlTable nested:
                    FlattenTable(nested, path, flat);
                    break;
                default:
                    if (kv.Value is not null)
                        flat[path] = (ParamFieldKind.String, kv.Value.ToString() ?? "", false);
                    break;
            }
        }
    }

    private static string FormatScalar(object? o) => o switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
        _ => o.ToString() ?? ""
    };

    private static void SetPathValue(TomlTable rootParams, string path, ParamFieldItem field)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;

        TomlTable current = rootParams;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetValue(parts[i], out var next) || next is not TomlTable nested)
            {
                nested = new TomlTable();
                current[parts[i]] = nested;
            }
            current = nested;
        }

        var leaf = parts[^1];
        current[leaf] = field.Kind switch
        {
            ParamFieldKind.Bool => field.BoolValue,
            ParamFieldKind.Number when long.TryParse(field.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
            ParamFieldKind.Number when double.TryParse(field.StringValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            ParamFieldKind.Array => ParseArray(field.StringValue),
            _ => field.StringValue
        };
    }

    private static void RemovePath(TomlTable root, string path)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return;
        var parents = new List<(TomlTable Parent, string Key)>();
        var current = root;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (!current.TryGetValue(parts[index], out var value) || value is not TomlTable nested) return;
            parents.Add((current, parts[index]));
            current = nested;
        }

        current.Remove(parts[^1]);
        for (var index = parents.Count - 1; index >= 0; index--)
        {
            var (parent, key) = parents[index];
            if (parent[key] is TomlTable table && table.Count == 0)
                parent.Remove(key);
            else
                break;
        }
    }

    private static TomlArray ParseArray(string value)
    {
        var arr = new TomlArray();
        if (string.IsNullOrWhiteSpace(value))
            return arr;

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            arr.Add(part);
        return arr;
    }
}
