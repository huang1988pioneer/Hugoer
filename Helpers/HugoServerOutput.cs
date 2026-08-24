namespace Hugoer.Helpers;

public static class HugoServerOutput
{
    public static bool LooksLikePortInUse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        return text.Contains("address already in use", StringComparison.OrdinalIgnoreCase)
               || text.Contains("Only one usage of each socket address", StringComparison.OrdinalIgnoreCase)
               || text.Contains("WSAEADDRINUSE", StringComparison.OrdinalIgnoreCase)
               || text.Contains("bind: An attempt was made to access a socket", StringComparison.OrdinalIgnoreCase)
               || ContainsBindFailure(text);
    }

    public static string Summarize(string output, int maxLines = 12)
    {
        if (string.IsNullOrWhiteSpace(output)) return string.Empty;

        var useful = new List<string>();
        foreach (var raw in output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (IsUsageNoise(line)) continue;
            useful.Add(line);
        }

        var errors = useful.Where(IsErrorOrWarn).ToList();
        var selected = errors.Count > 0 ? errors : useful;
        if (selected.Count > maxLines)
            selected = selected.Take(maxLines).ToList();

        return string.Join(Environment.NewLine, selected);
    }

    private static bool ContainsBindFailure(string text)
    {
        var bind = text.IndexOf("bind:", StringComparison.OrdinalIgnoreCase);
        if (bind < 0) return false;

        return text.IndexOf("listen tcp", StringComparison.OrdinalIgnoreCase) >= 0
               || text.Contains("10048", StringComparison.Ordinal)
               || text.Contains("EADDRINUSE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsageNoise(string line)
    {
        if (line.StartsWith("Flags:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Global Flags:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Aliases:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Examples:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Available Commands:", StringComparison.OrdinalIgnoreCase)
            || line.Contains("for more information about a command", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Use \"hugo", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Use 'hugo", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return LooksLikeFlagRow(line);
    }

    private static bool LooksLikeFlagRow(string line)
    {
        if (line.StartsWith("--", StringComparison.Ordinal)) return true;
        if (line.Length >= 4 && line[0] == '-' && char.IsLetterOrDigit(line[1]) && line[2] == ',')
            return true;
        return false;
    }

    private static bool IsErrorOrWarn(string line)
    {
        return line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("WARN", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("FATAL", StringComparison.OrdinalIgnoreCase)
               || line.StartsWith("FATA", StringComparison.OrdinalIgnoreCase)
               || line.Contains("command error", StringComparison.OrdinalIgnoreCase)
               || LooksLikePortInUse(line);
    }
}
