namespace Hugoer.Services;

public sealed record CodeStatisticsResult(
    bool IsAvailable,
    int FileCount,
    int CodeLineCount,
    int TotalLineCount,
    string? SourceRoot)
{
    public string Summary => IsAvailable ? $"{CodeLineCount:N0} 行" : "暫不可用";
    public string Details => !IsAvailable
        ? "找不到 Hugoer 專案來源目錄"
        : $"{FileCount:N0} 個來源檔案 · 非空行數 · 不含測試、文件與建置輸出";
}

/// <summary>
/// Counts the application's source files for the compact status metric in the main window.
/// </summary>
public static class CodeStatisticsService
{
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs",
        ".axaml"
    };

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".gradle",
        ".gradle-cache",
        ".tmp-hugo-invalid",
        "artifacts",
        "bin",
        "dist",
        "docs",
        "installer",
        "mobile",
        "obj",
        "tests"
    };

    public static Task<CodeStatisticsResult> CountAsync() => Task.Run(Count);

    private static CodeStatisticsResult Count()
    {
        var root = FindSourceRoot();
        if (root is null)
            return new CodeStatisticsResult(false, 0, 0, 0, null);

        var fileCount = 0;
        var codeLineCount = 0;
        var totalLineCount = 0;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", options))
            {
                if (!SourceExtensions.Contains(Path.GetExtension(path)) || IsExcluded(path, root))
                    continue;

                try
                {
                    fileCount++;
                    foreach (var line in File.ReadLines(path))
                    {
                        totalLineCount++;
                        if (!string.IsNullOrWhiteSpace(line))
                            codeLineCount++;
                    }
                }
                catch (IOException)
                {
                    // A file may change while the application is counting; keep the other files.
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore files that become inaccessible during the scan.
                }
            }
        }
        catch (IOException)
        {
            // Return the partial count gathered before an enumeration failure.
        }
        catch (UnauthorizedAccessException)
        {
            // Return the partial count gathered before an enumeration failure.
        }

        return new CodeStatisticsResult(true, fileCount, codeLineCount, totalLineCount, root);
    }

    private static string? FindSourceRoot()
    {
        var starts = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var start in starts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Hugoer.csproj")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        return null;
    }

    private static bool IsExcluded(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => ExcludedDirectories.Contains(segment));
    }
}
