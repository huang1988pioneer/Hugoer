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

    private AppSettings _settings = new();

    public AppSettings Current => _settings;

    public void Load()
    {
        try
        {
            if (!File.Exists(PathHelper.SettingsPath))
            {
                _settings = new AppSettings();
                return;
            }

            var json = File.ReadAllText(PathHelper.SettingsPath);
            _settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            _settings.GitProviderSettings ??= [];
        }
        catch
        {
            _settings = new AppSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(_settings, JsonOptions);
        File.WriteAllText(PathHelper.SettingsPath, json);
    }

    public void SetLastSitePath(string? path)
    {
        _settings.LastSitePath = path;
        Save();
    }

    public void SetMarkdownEditorMode(string mode)
    {
        _settings.MarkdownEditorMode = mode;
        Save();
    }

    public GitProviderSettings GetGitProviderSettings(GitHostingProvider provider)
    {
        var profile = _settings.GitProviderSettings
            .FirstOrDefault(item => item.Provider == provider);
        if (profile is not null)
            return profile;

        profile = new GitProviderSettings { Provider = provider };
        _settings.GitProviderSettings.Add(profile);
        return profile;
    }

    public void SaveGitProviderSettings(GitProviderSettings profile)
    {
        var index = _settings.GitProviderSettings.FindIndex(item => item.Provider == profile.Provider);
        if (index >= 0)
            _settings.GitProviderSettings[index] = profile;
        else
            _settings.GitProviderSettings.Add(profile);
        Save();
    }
}
