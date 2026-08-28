using System.Security;

namespace Hugoer.Helpers;

public static class PathHelper
{
    private static readonly string[] ConfigNames =
    [
        "hugo.toml", "hugo.yaml", "hugo.yml", "hugo.json",
        "config.toml", "config.yaml", "config.yml", "config.json"
    ];

    public static string AppDataDir
    {
        get
        {
            var preferred = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Hugoer");
            try
            {
                Directory.CreateDirectory(preferred);
                return preferred;
            }
            catch (Exception ex) when (
                ex is IOException
                or UnauthorizedAccessException
                or SecurityException
                or ArgumentException
                or NotSupportedException)
            {
                // Portable and managed desktop environments can expose a
                // read-only roaming profile. Keep the app usable by placing
                // transient preferences/tools in the OS temp directory; the
                // normal AppData path remains unchanged when it is writable.
                try
                {
                    var fallback = Path.Combine(Path.GetTempPath(), "Hugoer");
                    Directory.CreateDirectory(fallback);
                    return fallback;
                }
                catch (Exception fallbackError) when (
                    fallbackError is IOException
                    or UnauthorizedAccessException
                    or SecurityException
                    or ArgumentException
                    or NotSupportedException)
                {
                    // Let callers handle the final write failure, but avoid
                    // throwing while merely resolving a path during startup.
                    return preferred;
                }
            }
        }
    }

    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    public static string? FindConfigFile(string sitePath)
    {
        if (string.IsNullOrWhiteSpace(sitePath) || !Directory.Exists(sitePath))
            return null;

        try
        {
            foreach (var name in ConfigNames)
            {
                var path = Path.Combine(sitePath, name);
                if (File.Exists(path))
                    return path;
            }

            var configDir = Path.Combine(sitePath, "config");
            if (Directory.Exists(configDir))
            {
                foreach (var name in ConfigNames)
                {
                    var path = Path.Combine(configDir, name);
                    if (File.Exists(path))
                        return path;
                }

                var any = Directory.EnumerateFiles(configDir, "*", SearchOption.AllDirectories)
                    .FirstOrDefault(f =>
                    {
                        var n = Path.GetFileName(f).ToLowerInvariant();
                        return n is "hugo.toml" or "hugo.yaml" or "hugo.yml" or "config.toml"
                            or "config.yaml" or "config.yml" or "_default";
                    });
                if (any is not null)
                    return any;
            }
        }
        catch (IOException)
        {
            // A partially copied site may disappear while it is being inspected.
        }
        catch (UnauthorizedAccessException)
        {
            // Treat inaccessible configuration as not found.
        }

        return null;
    }

    public static bool LooksLikeHugoSite(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        if (FindConfigFile(path) is not null)
            return true;

        return Directory.Exists(Path.Combine(path, "content"))
               || Directory.Exists(Path.Combine(path, "themes"))
               || Directory.Exists(Path.Combine(path, "layouts"));
    }

    public static string ContentDir(string sitePath) => Path.Combine(sitePath, "content");
    public static string ThemesDir(string sitePath) => Path.Combine(sitePath, "themes");
    public static string ArchetypesDir(string sitePath) => Path.Combine(sitePath, "archetypes");
    public static string StaticDir(string sitePath) => Path.Combine(sitePath, "static");

    /// <summary>
    /// Resolves a path against <paramref name="root"/> and verifies that the
    /// resulting path remains inside that directory. This boundary check is
    /// intentionally separator-aware so a sibling such as <c>content-backup</c>
    /// cannot pass a simple string-prefix test.
    /// </summary>
    public static bool TryResolveUnder(
        string root,
        string candidate,
        out string fullPath,
        bool allowRoot = true)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate))
            return false;

        try
        {
            var rawRoot = Path.GetFullPath(root);
            var trimmedRoot = rawRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var volumeRoot = Path.GetPathRoot(rawRoot);
            var fullRoot = !string.IsNullOrEmpty(volumeRoot)
                           && string.Equals(
                               trimmedRoot,
                               volumeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                               StringComparison.OrdinalIgnoreCase)
                ? volumeRoot
                : trimmedRoot;
            fullPath = Path.GetFullPath(Path.Combine(fullRoot, candidate));

            if (allowRoot && string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
                return true;

            var rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
                || fullRoot.EndsWith(Path.AltDirectorySeparatorChar)
                ? fullRoot
                : fullRoot + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                return true;

            if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
            {
                var alternateRootWithSeparator = fullRoot.EndsWith(Path.AltDirectorySeparatorChar)
                    ? fullRoot
                    : fullRoot + Path.AltDirectorySeparatorChar;
                return fullPath.StartsWith(alternateRootWithSeparator, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }
        catch (ArgumentException)
        {
            fullPath = string.Empty;
            return false;
        }
        catch (NotSupportedException)
        {
            fullPath = string.Empty;
            return false;
        }
    }
}
