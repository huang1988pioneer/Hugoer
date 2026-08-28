using System.Text.Json;
using Hugoer.Models;
using Hugoer.Services;

var temp = Path.Combine(Path.GetTempPath(), "HugoerSettingsHarness-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);

try
{
    var service = new SettingsService(Path.Combine(temp, "settings.json"));
    service.Load();
    Assert(service.GetDeploymentMode() == DeploymentMode.GitHubPages,
        "a fresh settings profile must default to remote GitHub Pages publishing");
    Assert(service.GetAllowLocalDeploymentFallback(),
        "local deployment fallback should be enabled by default");
    service.SetDeploymentMode(DeploymentMode.Local);
    service.SetAllowLocalDeploymentFallback(false);

    Parallel.For(0, 16, index =>
    {
        service.AddRecentRepository(new GitHubRepositoryTarget
        {
            IsValid = true,
            Provider = index % 2 == 0 ? GitHostingProvider.Bitbucket : GitHostingProvider.GitHub,
            Owner = "owner",
            Repository = $"repo-{index}",
            CanonicalUrl = $"https://example.test/owner/repo-{index}"
        });
    });

    var path = Path.Combine(temp, "settings.json");
    Assert(File.Exists(path), "settings file should be written");
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    Assert(document.RootElement.TryGetProperty("recentRepositories", out _), "settings JSON should be complete");
    Assert(service.GetRecentRepositories().Count <= 10, "recent repositories must be capped");

    var reloaded = new SettingsService(path);
    reloaded.Load();
    Assert(reloaded.GetRecentRepositories().Count == service.GetRecentRepositories().Count, "settings should reload atomically");
    Assert(reloaded.GetDeploymentMode() == DeploymentMode.Local,
        "publishing mode should persist across reloads");
    Assert(!reloaded.GetAllowLocalDeploymentFallback(),
        "fallback preference should persist across reloads");

    var invalidPath = Path.Combine(temp, "invalid-settings.json");
    File.WriteAllText(invalidPath, "{\"deploymentMode\":\"999\"}");
    var invalid = new SettingsService(invalidPath);
    invalid.Load();
    Assert(invalid.GetDeploymentMode() == DeploymentMode.GitHubPages,
        "undefined publishing enum values must migrate to the remote default");

    var blockedDirectory = Path.Combine(temp, "blocked");
    File.WriteAllText(blockedDirectory, "not a directory");
    var readOnlyProfile = new SettingsService(Path.Combine(blockedDirectory, "settings.json"));
    readOnlyProfile.Load();
    readOnlyProfile.SetDeploymentMode(DeploymentMode.Local);
    Assert(readOnlyProfile.LastPersistenceError is not null,
        "a profile write failure should be captured without throwing");
    Console.WriteLine("SETTINGS_HARNESS_OK");
}
finally
{
    if (Directory.Exists(temp))
        Directory.Delete(temp, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
