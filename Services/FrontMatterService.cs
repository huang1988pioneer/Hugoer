using System.Text;
using System.Text.RegularExpressions;

namespace Hugoer.Services;

/// <summary>
/// Handles the YAML front matter Hugo uses by default. Unknown fields are preserved
/// so editing common metadata never destroys theme- or project-specific settings.
/// </summary>
public sealed partial class FrontMatterService
{
    public FrontMatterDocument Parse(string text)
    {
        text ??= string.Empty;
        var match = YamlBlockRegex().Match(text);
        if (!match.Success)
            return new FrontMatterDocument { Body = text };

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in match.Groups["frontMatter"].Value.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 || line.TrimStart().StartsWith('#')) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            fields[key] = Unquote(value);
        }

        return new FrontMatterDocument
        {
            Fields = fields,
            Body = text[match.Length..].TrimStart('\r', '\n')
        };
    }

    public string Write(FrontMatterDocument document)
    {
        var fields = document.Fields;
        var orderedKeys = new[] { "title", "date", "slug", "categories", "tags", "image", "description", "draft" };
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = new StringBuilder("---\n");

        foreach (var key in orderedKeys)
            AppendField(output, fields, key, emitted);

        foreach (var key in fields.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            AppendField(output, fields, key, emitted);

        output.Append("---\n\n");
        output.Append(document.Body.TrimStart('\r', '\n'));
        return output.ToString();
    }

    private static void AppendField(
        StringBuilder output,
        IReadOnlyDictionary<string, string> fields,
        string key,
        ISet<string> emitted)
    {
        if (!fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value) || !emitted.Add(key))
            return;

        if (key.Equals("draft", StringComparison.OrdinalIgnoreCase))
        {
            output.AppendLine($"draft: {value.ToLowerInvariant()}");
            return;
        }

        if (key.Equals("categories", StringComparison.OrdinalIgnoreCase) || key.Equals("tags", StringComparison.OrdinalIgnoreCase))
        {
            var values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(Quote).ToArray();
            output.AppendLine($"{key}: [{string.Join(", ", values)}]");
            return;
        }

        if (key.Equals("date", StringComparison.OrdinalIgnoreCase) && DateTimeOffset.TryParse(value, out _))
        {
            output.AppendLine($"date: {value}");
            return;
        }

        output.AppendLine($"{key}: {Quote(value)}");
    }

    private static string Quote(string value)
    {
        if (value.StartsWith('[') && value.EndsWith(']')) return value;
        return $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }

    private static string Unquote(string value)
    {
        value = value.Trim();
        if (value.StartsWith('[') && value.EndsWith(']'))
            return string.Join(", ", value[1..^1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(Unquote));
        if (value.Length >= 2 && ((value[0] == '\"' && value[^1] == '\"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
        return value;
    }

    [GeneratedRegex(@"\A---\s*\r?\n(?<frontMatter>.*?)\r?\n---\s*(?:\r?\n|\z)", RegexOptions.Singleline)]
    private static partial Regex YamlBlockRegex();
}

public sealed class FrontMatterDocument
{
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; init; } = string.Empty;
}
