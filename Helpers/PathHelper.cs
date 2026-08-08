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
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Hugoer");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    public static string? FindConfigFile(string sitePath)
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
                        or "config.yaml" or "config.yml" or "_default" or "config.toml";
                });
            if (any is not null)
                return any;
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
}
