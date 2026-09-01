using System.Text.RegularExpressions;

namespace Hugoer.Helpers;

/// <summary>
/// In-memory repairs for a Hugo root <c>hugo.toml</c>. Callers read the file
/// once, apply this module, and write back only when the text actually changed.
/// </summary>
public static partial class HugoTomlRepair
{
    /// <summary>
    /// Applies the desktop site-prep transforms in the same order previously
    /// used by sequential file rewrites: drop duplicate root keys, migrate
    /// <c>languageCode</c> to <c>locale</c>, then lift a legacy Stack
    /// <c>colorScheme</c> scalar into <c>[params.colorScheme]</c>.
    /// </summary>
    public static string Repair(string original, out bool changed)
    {
        var text = original ?? string.Empty;
        var next = DropDuplicateRootKeys(text, out var dropped);
        next = MigrateDeprecatedLanguageCode(next, out var migrated);
        next = LiftLegacyStackColorScheme(next, out var lifted);
        changed = dropped || migrated || lifted;
        return next;
    }

    internal static string DropDuplicateRootKeys(string original, out bool changed)
    {
        var newline = Newline(original);
        var lines = Split(original);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var repaired = new List<string>(lines.Length);
        var insideTable = false;
        changed = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
                insideTable = true;

            if (!insideTable && !trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                var match = SimpleTomlKeyRegex().Match(trimmed);
                if (match.Success && !seen.Add(match.Groups["key"].Value))
                {
                    changed = true;
                    continue;
                }
            }

            repaired.Add(line);
        }

        return changed ? Join(repaired, newline) : original;
    }

    internal static string MigrateDeprecatedLanguageCode(string original, out bool changed)
    {
        changed = false;
        var newline = Newline(original);
        var lines = Split(original).ToList();
        var rootEnd = lines.FindIndex(line => line.TrimStart().StartsWith("[", StringComparison.Ordinal));
        if (rootEnd < 0) rootEnd = lines.Count;

        var languageIndex = -1;
        var localeIndex = -1;
        for (var index = 0; index < rootEnd; index++)
        {
            var key = SimpleTomlKeyRegex().Match(lines[index].TrimStart());
            if (!key.Success) continue;
            if (key.Groups["key"].Value.Equals("languageCode", StringComparison.OrdinalIgnoreCase))
                languageIndex = index;
            if (key.Groups["key"].Value.Equals("locale", StringComparison.OrdinalIgnoreCase))
                localeIndex = index;
        }

        if (languageIndex < 0)
            return original;

        var equals = lines[languageIndex].IndexOf('=');
        if (equals < 0)
            return original;

        var value = lines[languageIndex][(equals + 1)..].Trim();
        var indent = new string(lines[languageIndex].TakeWhile(char.IsWhiteSpace).ToArray());

        if (localeIndex >= 0)
        {
            var localeIndent = new string(lines[localeIndex].TakeWhile(char.IsWhiteSpace).ToArray());
            lines[localeIndex] = $"{localeIndent}locale = {value}";
            lines.RemoveAt(languageIndex);
        }
        else
        {
            lines[languageIndex] = $"{indent}locale = {value}";
        }

        changed = true;
        return Join(lines, newline);
    }

    internal static string LiftLegacyStackColorScheme(string original, out bool changed)
    {
        changed = false;
        if (Regex.IsMatch(original, @"(?im)^\s*\[params\.colorScheme\]\s*$"))
            return original;

        var newline = Newline(original);
        var lines = Split(original);
        var repaired = new List<string>(lines.Length + 5);
        var currentTable = string.Empty;
        string? scheme = null;

        foreach (var line in lines)
        {
            var table = TomlTableRegex().Match(line.Trim());
            if (table.Success)
                currentTable = table.Groups["table"].Value.Trim();

            if (currentTable.Equals("params", StringComparison.OrdinalIgnoreCase))
            {
                var scalar = LegacyColorSchemeRegex().Match(line.Trim());
                if (scalar.Success)
                {
                    scheme = scalar.Groups["value"].Value;
                    continue;
                }
            }

            repaired.Add(line);
        }

        if (scheme is null)
            return original;

        while (repaired.Count > 0 && string.IsNullOrWhiteSpace(repaired[^1]))
            repaired.RemoveAt(repaired.Count - 1);
        repaired.Add(string.Empty);
        repaired.Add("[params.colorScheme]");
        repaired.Add("  toggle = true");
        repaired.Add($"  default = \"{scheme}\"");
        repaired.Add(string.Empty);

        changed = true;
        return Join(repaired, newline);
    }

    private static string Newline(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string[] Split(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

    private static string Join(IReadOnlyList<string> lines, string newline) =>
        string.Join(newline, lines);

    [GeneratedRegex(@"^(?<key>[A-Za-z0-9_.-]+)\s*=")]
    private static partial Regex SimpleTomlKeyRegex();

    [GeneratedRegex(@"^\[(?<table>[^\]]+)\]$")]
    private static partial Regex TomlTableRegex();

    [GeneratedRegex("""^colorScheme\s*=\s*['"](?<value>auto|light|dark)['"]\s*$""", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyColorSchemeRegex();
}
