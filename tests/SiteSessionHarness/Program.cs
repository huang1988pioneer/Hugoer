using Hugoer.Services;

var temp = Path.Combine(Path.GetTempPath(), "HugoerSiteSessionHarness-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);

try
{
    var settingsPath = Path.Combine(temp, "settings.json");
    var settings = new SettingsService(settingsPath);
    settings.Load();
    var session = new SiteSession(settings);
    var site = Directory.CreateDirectory(Path.Combine(temp, "site"));
    var changed = 0;
    session.Changed += (_, _) => changed++;

    Assert(session.CurrentPath is null, "a fresh session should have no active site");
    Assert(session.Set(site.FullName), "setting a new site should report a change");
    Assert(session.HasSite, "an existing site should be recognized");
    Assert(changed == 1, "one site change should raise one event");
    Assert(session.Set(site.FullName + Path.DirectorySeparatorChar) == false,
        "equivalent normalized paths should not raise duplicate changes");

    var reloadedSettings = new SettingsService(settingsPath);
    reloadedSettings.Load();
    Assert(string.Equals(reloadedSettings.Current.LastSitePath, site.FullName,
        StringComparison.OrdinalIgnoreCase), "the active path should be persisted");

    Assert(session.Set(null), "clearing the site should report a change");
    Assert(!session.HasSite && changed == 2, "clearing the site should update state once");
    Console.WriteLine("SITE_SESSION_HARNESS_OK");
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
