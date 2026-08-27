using System.Text.Json;
using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const int MaxRecentRepositories = 10;

    private readonly object _gate = new();
    private readonly string _settingsPath;
    private AppSettings _settings = new();

    public SettingsService(string? settingsPath = null)
    {
        _settingsPath = string.IsNullOrWhiteSpace(settingsPath)
            ? PathHelper.SettingsPath
            : Path.GetFullPath(settingsPath);
    }

    public AppSettings Current => _settings;

    public void Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    _settings = new AppSettings();
                    return;
                }

                var json = File.ReadAllText(_settingsPath);
                _settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                _settings.GitProviderSettings ??= [];
                _settings.RecentRepositories ??= [];
                foreach (var profile in _settings.GitProviderSettings)
                {
                    if (profile is not null && GitProviderSettings.IsAutomaticCommitMessage(profile.CommitMessage))
                        profile.CommitMessage = string.Empty;
                }
            }
            catch
            {
                _settings = new AppSettings();
            }
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            var json = JsonSerializer.Serialize(_settings, JsonOptions);
            AtomicFileWriter.WriteAllText(_settingsPath, json);
        }
    }

    public void SetLastSitePath(string? path)
    {
        lock (_gate)
        {
            _settings.LastSitePath = path;
            Save();
        }
    }

    public void SetMarkdownEditorMode(string mode)
    {
        lock (_gate)
        {
            _settings.MarkdownEditorMode = mode;
            Save();
        }
    }

    public void SetPreferredHugoPath(string? path)
    {
        lock (_gate)
        {
            _settings.PreferredHugoPath = path;
            Save();
        }
    }

    public GitProviderSettings GetGitProviderSettings(GitHostingProvider provider)
    {
        lock (_gate)
        {
            var profile = _settings.GitProviderSettings
                .FirstOrDefault(item => item.Provider == provider);
            if (profile is not null)
                return profile;

            profile = new GitProviderSettings { Provider = provider };
            _settings.GitProviderSettings.Add(profile);
            return profile;
        }
    }

    public void SaveGitProviderSettings(GitProviderSettings profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        lock (_gate)
        {
            var index = _settings.GitProviderSettings.FindIndex(item => item.Provider == profile.Provider);
            if (index >= 0)
                _settings.GitProviderSettings[index] = profile;
            else
                _settings.GitProviderSettings.Add(profile);
            Save();
        }
    }

    public IReadOnlyList<RecentRepositoryEntry> GetRecentRepositories()
    {
        lock (_gate)
        {
            return _settings.RecentRepositories.ToArray();
        }
    }

    // Local path and remote repository are tracked separately: an existing entry keeps its
    // previously known local path / Pages URL when a new usage doesn't supply one.
    public void AddRecentRepository(GitHubRepositoryTarget target, string? localPath = null)
    {
        if (!target.IsValid
            || string.IsNullOrWhiteSpace(target.Owner)
            || string.IsNullOrWhiteSpace(target.Repository)
            || string.IsNullOrWhiteSpace(target.CanonicalUrl))
            return;

        lock (_gate)
        {
            var existing = _settings.RecentRepositories.FirstOrDefault(item =>
                item.Provider == target.Provider
                && string.Equals(item.Owner, target.Owner, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Repository, target.Repository, StringComparison.OrdinalIgnoreCase));

            var resolvedLocalPath = string.IsNullOrWhiteSpace(localPath) ? existing?.LocalPath : localPath;
            var resolvedPagesUrl = string.IsNullOrWhiteSpace(target.PagesUrl) ? existing?.PagesUrl : target.PagesUrl;

            _settings.RecentRepositories.RemoveAll(item =>
                item.Provider == target.Provider
                && string.Equals(item.Owner, target.Owner, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Repository, target.Repository, StringComparison.OrdinalIgnoreCase));

            _settings.RecentRepositories.Insert(0, new RecentRepositoryEntry
            {
                Provider = target.Provider,
                Owner = target.Owner,
                Repository = target.Repository,
                CanonicalUrl = target.CanonicalUrl,
                PagesUrl = resolvedPagesUrl,
                LocalPath = resolvedLocalPath,
                LastUsedUtc = DateTimeOffset.UtcNow
            });

            if (_settings.RecentRepositories.Count > MaxRecentRepositories)
                _settings.RecentRepositories.RemoveRange(
                    MaxRecentRepositories,
                    _settings.RecentRepositories.Count - MaxRecentRepositories);

            Save();
        }
    }

    public void RemoveRecentRepository(RecentRepositoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            _settings.RecentRepositories.RemoveAll(item =>
                item.Provider == entry.Provider
                && string.Equals(item.Owner, entry.Owner, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.Repository, entry.Repository, StringComparison.OrdinalIgnoreCase));
            Save();
        }
    }

    public void ClearRecentRepositories()
    {
        lock (_gate)
        {
            _settings.RecentRepositories.Clear();
            Save();
        }
    }
}
