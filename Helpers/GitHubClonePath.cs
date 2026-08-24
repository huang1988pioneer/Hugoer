namespace Hugoer.Helpers;

public static class GitHubClonePath
{
    public static string? TryGetDestination(string? parentDirectory, string? repositoryName, out string error)
    {
        if (string.IsNullOrWhiteSpace(parentDirectory))
        {
            error = "請選擇本機存放資料夾。";
            return null;
        }

        if (string.IsNullOrWhiteSpace(repositoryName))
        {
            error = "缺少 repository 名稱。";
            return null;
        }

        string parent;
        try
        {
            parent = Path.GetFullPath(parentDirectory.Trim());
        }
        catch (Exception ex)
        {
            error = $"本機存放資料夾無效：{ex.Message}";
            return null;
        }

        string destination;
        try
        {
            destination = Path.GetFullPath(Path.Combine(parent, repositoryName.Trim()));
        }
        catch (Exception ex)
        {
            error = $"本機目標路徑無效：{ex.Message}";
            return null;
        }

        var prefix = parent.EndsWith(Path.DirectorySeparatorChar) || parent.EndsWith(Path.AltDirectorySeparatorChar)
            ? parent
            : parent + Path.DirectorySeparatorChar;
        if (!destination.Equals(parent, StringComparison.OrdinalIgnoreCase)
            && !destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "本機路徑超出選擇的資料夾，已停止複製。";
            return null;
        }

        error = string.Empty;
        return destination;
    }

    public static bool IsVacantDirectory(string path) =>
        !Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any();

    public static bool LooksLikeStaticPagesOutput(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return false;

        var hasIndex = File.Exists(Path.Combine(path, "index.html"));
        if (!hasIndex)
            return false;

        if (PathHelper.FindConfigFile(path) is not null)
            return false;

        return !Directory.Exists(Path.Combine(path, "content"))
               && !Directory.Exists(Path.Combine(path, "archetypes"))
               && !Directory.Exists(Path.Combine(path, "layouts"));
    }
}
