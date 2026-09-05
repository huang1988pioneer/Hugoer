namespace Hugoer.ViewModels;

/// <summary>
/// 頁面代號。分頁不再是使用者要記的東西，而是內部的定址方式；首頁、命令面板與
/// 快捷鍵都用這組代號跳頁。
/// </summary>
public static class ShellPages
{
    public const string Home = "home";
    public const string Content = "content";
    public const string Publish = "publish";
    public const string Setup = "setup";
    public const string Config = "config";
    public const string Themes = "themes";
    public const string Menu = "menu";
    public const string Migration = "migration";
}

/// <summary>外框導覽能力；由 <see cref="MainViewModel"/> 實作。</summary>
public interface IShellNavigator
{
    /// <summary>切到指定頁面。未知代號會被忽略。</summary>
    void GoTo(string key);
}

/// <summary>命令面板（Ctrl+K）的一筆項目。</summary>
public sealed class CommandEntry
{
    public required string Title { get; init; }

    public required string Subtitle { get; init; }

    public required string Group { get; init; }

    public required Func<Task> Run { get; init; }

    /// <summary>額外的比對字串（英文別名、拼音、同義詞）。</summary>
    public string Keywords { get; init; } = string.Empty;

    public string Shortcut { get; init; } = string.Empty;

    public bool HasShortcut => Shortcut.Length > 0;

    public bool Matches(string query)
    {
        if (query.Length == 0)
            return true;

        return Title.Contains(query, StringComparison.OrdinalIgnoreCase)
               || Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase)
               || Group.Contains(query, StringComparison.OrdinalIgnoreCase)
               || Keywords.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
