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
        var match = FrontMatterBlockRegex().Match(text);
        if (!match.Success)
            return new FrontMatterDocument { Body = text };

        var delimiter = match.Groups["delimiter"].Value;
        var fields = ParseFields(match.Groups["frontMatter"].Value, delimiter);
        var body = text[match.Length..].TrimStart('\r', '\n');
        while (FrontMatterBlockRegex().Match(body) is { Success: true } extra)
        {
            var extraFields = ParseFields(extra.Groups["frontMatter"].Value, extra.Groups["delimiter"].Value);
            foreach (var (key, value) in extraFields)
                fields.TryAdd(key, value);
            body = body[extra.Length..].TrimStart('\r', '\n');
        }

        return new FrontMatterDocument
        {
            Fields = fields,
            Body = body,
            Delimiter = delimiter
        };
    }

    public string Write(FrontMatterDocument document)
    {
        var fields = document.Fields;
        var orderedKeys = new[] { "title", "date", "slug", "categories", "tags", "image", "description", "draft" };
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var delimiter = document.Delimiter == "+++" ? "+++" : "---";
        var output = new StringBuilder(delimiter).Append('\n');

        foreach (var key in orderedKeys)
            AppendField(output, fields, key, emitted, delimiter);

        foreach (var key in fields.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            AppendField(output, fields, key, emitted, delimiter);

        output.Append(delimiter).Append("\n\n");
        output.Append(document.Body.TrimStart('\r', '\n'));
        return output.ToString();
    }

    /// <summary>
    /// Replaces Markdown body while keeping the original front matter text intact.
    /// </summary>
    public string ReplaceBody(string markdown, string body)
    {
        markdown ??= string.Empty;
        var newBody = (body ?? string.Empty).TrimStart('\r', '\n');
        var match = FrontMatterBlockRegex().Match(markdown);
        if (!match.Success)
            return newBody;

        var header = markdown[..match.Length].TrimEnd('\r', '\n');
        return header + "\n\n" + newBody;
    }

    private static Dictionary<string, string> ParseFields(string frontMatter, string delimiter) =>
        delimiter == "+++"
            ? ParseTomlFields(frontMatter)
            : ParseYamlFields(frontMatter);

    private static Dictionary<string, string> ParseYamlFields(string frontMatter)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in frontMatter.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0 || line.TrimStart().StartsWith('#')) continue;

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            fields[key] = Unquote(value);
        }

        return fields;
    }

    private static Dictionary<string, string> ParseTomlFields(string frontMatter)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in frontMatter.Split('\n'))
        {
            AddTomlFields(fields, line);
        }

        if (fields.Count == 0)
            AddTomlFields(fields, frontMatter);

        return fields;
    }

    private static void AddTomlFields(IDictionary<string, string> fields, string line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) return;
        foreach (Match match in TomlAssignmentRegex().Matches(line))
        {
            var key = match.Groups["key"].Value.Trim();
            var value = match.Groups["value"].Value.Trim();
            fields[key] = Unquote(value);
        }
    }

    private static void AppendField(
        StringBuilder output,
        IReadOnlyDictionary<string, string> fields,
        string key,
        ISet<string> emitted,
        string delimiter)
    {
        if (!fields.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value) || !emitted.Add(key))
            return;

        if (delimiter == "+++")
        {
            AppendTomlField(output, key, value);
            return;
        }

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

    private static void AppendTomlField(StringBuilder output, string key, string value)
    {
        if (key.Equals("draft", StringComparison.OrdinalIgnoreCase))
        {
            output.AppendLine($"draft = {value.ToLowerInvariant()}");
            return;
        }

        if (key.Equals("categories", StringComparison.OrdinalIgnoreCase) || key.Equals("tags", StringComparison.OrdinalIgnoreCase))
        {
            var values = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(Quote).ToArray();
            output.AppendLine($"{key} = [{string.Join(", ", values)}]");
            return;
        }

        if (key.Equals("date", StringComparison.OrdinalIgnoreCase) && DateTimeOffset.TryParse(value, out _))
        {
            output.AppendLine($"date = {Quote(value)}");
            return;
        }

        output.AppendLine($"{key} = {Quote(value)}");
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

    [GeneratedRegex(@"\A(?<delimiter>---|\+\+\+)(?:\s*\r?\n(?<frontMatter>.*?)\r?\n\k<delimiter>|\s+(?<frontMatter>.*?)\s+\k<delimiter>)\s*(?:\r?\n|\z)", RegexOptions.Singleline)]
    private static partial Regex FrontMatterBlockRegex();

    [GeneratedRegex(@"(?<key>[A-Za-z0-9_-]+)\s*=\s*(?<value>'[^']*'|""[^""]*""|\[[^\]]*\]|[^\s]+)")]
    private static partial Regex TomlAssignmentRegex();
}

public sealed class FrontMatterDocument
{
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; set; } = string.Empty;
    public string Delimiter { get; init; } = "---";
}
