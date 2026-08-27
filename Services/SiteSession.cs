namespace Hugoer.Services;

/// <summary>
/// Owns the currently opened Hugo site and its persisted selection.
///
/// The session is deliberately small at the composition seam: callers only
/// need to set a path and subscribe to <see cref="Changed"/>. Path
/// normalization and persistence stay local to this module, so views and
/// page models do not need to duplicate those rules.
/// </summary>
public sealed class SiteSession
{
    private readonly SettingsService _settings;
    private string? _currentPath;

    public SiteSession(SettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _currentPath = Normalize(settings.Current.LastSitePath);
    }

    public string? CurrentPath => _currentPath;

    public bool HasSite => !string.IsNullOrWhiteSpace(_currentPath)
                           && Directory.Exists(_currentPath);

    public event EventHandler? Changed;

    /// <summary>
    /// Changes the active site. Returns <see langword="true"/> only when the
    /// normalized path differs from the previous value.
    /// </summary>
    public bool Set(string? path)
    {
        var normalized = Normalize(path);
        if (string.Equals(_currentPath, normalized, StringComparison.OrdinalIgnoreCase))
            return false;

        _currentPath = normalized;
        _settings.SetLastSitePath(normalized);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var full = Path.GetFullPath(path.Trim());
            var root = Path.GetPathRoot(full);
            if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
                full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return full;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or
                                   PathTooLongException)
        {
            // Keep invalid user input out of the rest of the application. A
            // null session is safer than allowing malformed paths to reach I/O.
            return null;
        }
    }
}
