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
        ("colorScheme", "色彩模式", "auto / light / dark", ParamFieldKind.String),
        ("defaultTheme", "預設主題色", "Stack: auto / light / dark", ParamFieldKind.String),
        ("image", "預設圖片", "社群分享預設圖", ParamFieldKind.String),
        ("dateFormat", "日期格式", "例如 2006-01-02", ParamFieldKind.String),
        ("displayTitle", "顯示標題", "是否顯示文章標題", ParamFieldKind.Bool),
        ("displayDescription", "顯示描述", "是否顯示描述", ParamFieldKind.Bool),
        ("displayTags", "顯示標籤", "是否顯示標籤", ParamFieldKind.Bool),
        ("displayCategories", "顯示分類", "是否顯示分類", ParamFieldKind.Bool),
        ("fullWidth", "全寬版面", "內容區是否全寬", ParamFieldKind.Bool),
        ("footer.since", "Footer 起始年", "版權起始年份", ParamFieldKind.String),
        ("footer.customText", "Footer 文字", "自訂頁尾文字", ParamFieldKind.String),
        ("sidebar.emoji", "側欄 Emoji", "Stack 側欄圖示", ParamFieldKind.String),
        ("sidebar.subtitle", "側欄副標", "個人簡介副標", ParamFieldKind.String),
        ("article.showTitle", "文章顯示標題", "Stack article", ParamFieldKind.Bool),
        ("article.showDate", "文章顯示日期", "Stack article", ParamFieldKind.Bool),
        ("article.showTableOfContents", "顯示目錄", "TOC", ParamFieldKind.Bool),
        ("widgets.homepage", "首頁 widgets", "逗號分隔，如 search,archives,tag-cloud", ParamFieldKind.Array),
        ("widgets.page", "內頁 widgets", "逗號分隔", ParamFieldKind.Array),
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
                case TomlArray arr:
                    flat[path] = (ParamFieldKind.Array, string.Join(", ", arr.Select(FormatScalar)), false);
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
