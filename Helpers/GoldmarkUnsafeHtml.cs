using System.Text.RegularExpressions;

namespace Hugoer.Helpers;

/// <summary>
/// Hugo Goldmark strips raw HTML unless <c>markup.goldmark.renderer.unsafe</c>
/// is true. Hugoer stores resized/aligned images, audio, and video as HTML.
/// </summary>
public static partial class GoldmarkUnsafeHtml
{
    public const string TableName = "markup.goldmark.renderer";

    public static bool IsEnabled(string tomlText) =>
        ReadState(tomlText ?? string.Empty).Enabled;

    public static string EnsureEnabled(string tomlText, out bool changed)
    {
        var text = tomlText ?? string.Empty;
        var state = ReadState(text);
        if (state.Enabled)
        {
            changed = false;
            return text;
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .ToList();

        if (state.UnsafeLine is int unsafeLine)
        {
            var indent = new string(lines[unsafeLine].TakeWhile(char.IsWhiteSpace).ToArray());
            lines[unsafeLine] = $"{indent}unsafe = true";
            changed = true;
            return Join(lines, newline, text);
        }

        if (state.TableHeaderLine is int headerLine)
        {
            var indent = InferIndent(lines, headerLine);
            lines.Insert(headerLine + 1, $"{indent}unsafe = true");
            changed = true;
            return Join(lines, newline, text);
        }

        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        if (lines.Count > 0)
            lines.Add(string.Empty);
        lines.Add($"[{TableName}]");
        lines.Add("unsafe = true");
        lines.Add(string.Empty);
        changed = true;
        return Join(lines, newline, text);
    }

    private static State ReadState(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var currentTable = string.Empty;
        int? headerLine = null;
        int? unsafeLine = null;
        var enabled = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var table = TableHeaderRegex().Match(trimmed);
            if (table.Success)
            {
                currentTable = table.Groups["table"].Value.Trim();
                if (currentTable.Equals(TableName, StringComparison.OrdinalIgnoreCase))
                    headerLine ??= index;
                continue;
            }

            if (!currentTable.Equals(TableName, StringComparison.OrdinalIgnoreCase))
                continue;

            var unsafeMatch = UnsafeLineRegex().Match(trimmed);
            if (!unsafeMatch.Success)
                continue;

            unsafeLine = index;
            enabled = unsafeMatch.Groups["value"].Value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        return new State(enabled, headerLine, unsafeLine);
    }

    private static string InferIndent(IReadOnlyList<string> lines, int headerLine)
    {
        for (var index = headerLine + 1; index < lines.Count; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#') || trimmed.StartsWith('['))
                break;
            return new string(line.TakeWhile(char.IsWhiteSpace).ToArray());
        }

        return string.Empty;
    }

    private static string Join(IReadOnlyList<string> lines, string newline, string original)
    {
        var joined = string.Join(newline, lines);
        if (original.EndsWith('\n') && !joined.EndsWith(newline, StringComparison.Ordinal))
            joined += newline;
        return joined;
    }

    private readonly record struct State(bool Enabled, int? TableHeaderLine, int? UnsafeLine);

    [GeneratedRegex(@"^\[(?<table>[^\]]+)\]$")]
    private static partial Regex TableHeaderRegex();

    [GeneratedRegex(@"^unsafe\s*=\s*(?<value>true|false)\b", RegexOptions.IgnoreCase)]
    private static partial Regex UnsafeLineRegex();
}
