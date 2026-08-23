using System.Text;
using System.Text.RegularExpressions;
using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.Services;

public sealed partial class ThemeService
{
    public static IReadOnlyList<ThemePreset> Presets { get; } =
    [
        new ThemePreset
        {
            Id = "stack",
            DisplayName = "Stack",
            RepoUrl = "https://github.com/CaiJimmy/hugo-theme-stack.git",
            FolderName = "Stack",
            Description = "卡片式部落格主題，支援標籤、歸檔、搜尋與暗色模式，適合個人網站。",
            DocsUrl = "https://docs.stack.jimmycai.com/",
            ConfigHint = "theme = 'Stack'"
        },
        new ThemePreset
        {
            Id = "paper",
            DisplayName = "Paper",
            RepoUrl = "https://github.com/nanxiaobei/hugo-paper.git",
            FolderName = "Paper",
            Description = "極簡乾淨的部落格主題。",
            DocsUrl = "https://github.com/nanxiaobei/hugo-paper",
            ConfigHint = "theme = 'Paper'"
        },
        new ThemePreset
        {
            Id = "ananke",
            DisplayName = "Ananke",
            RepoUrl = "https://github.com/theNewDynamic/gohugo-theme-ananke.git",
            FolderName = "ananke",
            Description = "Hugo 官方文件常用的經典主題。",
            DocsUrl = "https://github.com/theNewDynamic/gohugo-theme-ananke",
            ConfigHint = "theme = 'ananke'"
        },
        new ThemePreset
        {
            Id = "congo",
            DisplayName = "Congo",
            RepoUrl = "https://github.com/jpanther/congo.git",
            FolderName = "congo",
            Description = "現代化、高度可自訂的文件／部落格主題。",
            DocsUrl = "https://jpanther.github.io/congo/",
            ConfigHint = "theme = 'congo'"
        },
        new ThemePreset
        {
            Id = "blowfish",
            DisplayName = "Blowfish",
            RepoUrl = "https://github.com/nunocoracao/blowfish.git",
            FolderName = "blowfish",
            Description = "美觀的現代主題，適合作品集與部落格。",
            DocsUrl = "https://blowfish.page/",
            ConfigHint = "theme = 'blowfish'"
        }
    ];

    public IReadOnlyList<string> ListInstalledThemes(string sitePath)
    {
        var dir = PathHelper.ThemesDir(sitePath);
        if (!Directory.Exists(dir))
            return [];

        return Directory.GetDirectories(dir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<CommandResult> InstallThemeAsync(
        string sitePath,
        ThemePreset preset,
        bool asSubmodule = true,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var themesDir = PathHelper.ThemesDir(sitePath);
        Directory.CreateDirectory(themesDir);
        var target = Path.Combine(themesDir, preset.FolderName);

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            return new CommandResult
            {
                ExitCode = 0,
                StdOut = $"主題已存在：{preset.FolderName}"
            };
        }

        CommandResult result;
        if (asSubmodule && Directory.Exists(Path.Combine(sitePath, ".git")))
        {
            progress?.Report($"以 git submodule 安裝 {preset.DisplayName}…");
            result = await ProcessRunner.RunAsync(
                "git",
                $"submodule add --depth 1 {preset.RepoUrl} themes/{preset.FolderName}",
                sitePath,
                timeoutMs: 180_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                progress?.Report("submodule 失敗，改為 clone…");
                result = await ProcessRunner.RunAsync(
                    "git",
                    $"clone --depth 1 {preset.RepoUrl} \"{target}\"",
                    sitePath,
                    timeoutMs: 180_000,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            progress?.Report($"git clone {preset.DisplayName}…");
            result = await ProcessRunner.RunAsync(
                "git",
                $"clone --depth 1 {preset.RepoUrl} \"{target}\"",
                sitePath,
                timeoutMs: 180_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (result.Succeeded)
        {
            progress?.Report("寫入 config theme 設定…");
            await EnsureThemeInConfigAsync(sitePath, preset.FolderName, cancellationToken).ConfigureAwait(false);

            if (string.Equals(preset.Id, "stack", StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report("套用 Stack 主題建議設定…");
                await ApplyStackDefaultsAsync(sitePath, cancellationToken).ConfigureAwait(false);
            }
        }

        return result;
    }

    public async Task EnsureThemeInConfigAsync(
        string sitePath,
        string themeFolderName,
        CancellationToken cancellationToken = default)
    {
        var config = PathHelper.FindConfigFile(sitePath);
        if (config is null)
        {
            config = Path.Combine(sitePath, "hugo.toml");
            await File.WriteAllTextAsync(config, $"theme = '{themeFolderName}'\n", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var text = await File.ReadAllTextAsync(config, cancellationToken).ConfigureAwait(false);
        var ext = Path.GetExtension(config).ToLowerInvariant();

        if (ext is ".toml")
        {
            if (ThemeLineRegex().IsMatch(text))
            {
                text = ThemeLineRegex().Replace(text, $"theme = '{themeFolderName}'", 1);
            }
            else
            {
                text = $"theme = '{themeFolderName}'\n" + text;
            }
        }
        else if (ext is ".yaml" or ".yml")
        {
            if (YamlThemeRegex().IsMatch(text))
                text = YamlThemeRegex().Replace(text, $"theme: {themeFolderName}", 1);
            else
                text = $"theme: {themeFolderName}\n" + text;
        }
        else
        {
            // json or unknown — append toml-style note as best effort
            if (!text.Contains("theme", StringComparison.OrdinalIgnoreCase))
                text = text.TrimEnd() + $",\n  \"theme\": \"{themeFolderName}\"\n";
        }

        await File.WriteAllTextAsync(config, text, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ApplyStackDefaultsAsync(string sitePath, CancellationToken cancellationToken)
    {
        // Ensure menus and basic params that Stack expects
        var config = PathHelper.FindConfigFile(sitePath);
        if (config is null || !config.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
            return;

        var text = await File.ReadAllTextAsync(config, cancellationToken).ConfigureAwait(false);
        var sb = new StringBuilder(text);

        if (!text.Contains("[menu.main]", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("[[menu.main]]", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("""
# --- Stack menus ---
[[menu.main]]
identifier = "home"
name = "Home"
url = "/"
weight = 1

[menu.main.params]
icon = "home"

[[menu.main]]
identifier = "archives"
name = "Archives"
url = "/archives/"
weight = 2

[menu.main.params]
icon = "archives"

[[menu.main]]
identifier = "search"
name = "Search"
url = "/search/"
weight = 3

[menu.main.params]
icon = "search"
""");
        }

        if (!text.Contains("[params]", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("""
[params]
  description = "A personal blog powered by Hugo and Stack"
  mainSections = ["post"]

[params.colorScheme]
  toggle = true
  default = "auto"
""");
        }

        // Stack content structure helpers
        var postDir = Path.Combine(sitePath, "content", "post");
        Directory.CreateDirectory(postDir);
        Directory.CreateDirectory(Path.Combine(sitePath, "content", "categories"));
        Directory.CreateDirectory(Path.Combine(sitePath, "content", "tags"));

        var archives = Path.Combine(sitePath, "content", "archives", "_index.md");
        if (!File.Exists(archives))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(archives)!);
            await File.WriteAllTextAsync(archives, """
---
title: "Archives"
layout: "archives"
slug: "archives"
---
""", cancellationToken).ConfigureAwait(false);
        }

        // Stack's search layouts live under layouts/page, so this must be a
        // regular page (index.md), not a section (_index.md).
        var search = Path.Combine(sitePath, "content", "search", "index.md");
        if (!File.Exists(search))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(search)!);
            await File.WriteAllTextAsync(search, """
---
title: "Search"
layout: "search"
outputs: ["html", "json"]
---
""", cancellationToken).ConfigureAwait(false);
        }

        var about = Path.Combine(sitePath, "content", "about", "index.md");
        if (!File.Exists(about) && !File.Exists(Path.Combine(sitePath, "content", "about", "_index.md")))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(about)!);
            await File.WriteAllTextAsync(about, """
---
title: About
date: 2024-01-01
---

Hello, this site is powered by **Hugo** + **Stack** theme, managed with **Hugoer**.
""", cancellationToken).ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(config, sb.ToString(), cancellationToken).ConfigureAwait(false);
    }

    public string? FindThemeConfig(string sitePath, string themeName)
    {
        var candidates = new[]
        {
            Path.Combine(sitePath, "themes", themeName, "config.toml"),
            Path.Combine(sitePath, "themes", themeName, "theme.toml"),
            Path.Combine(sitePath, "themes", themeName, "config.yaml"),
            Path.Combine(sitePath, "config", "_default", "params.toml"),
            Path.Combine(sitePath, "config", "_default", "config.toml"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public IReadOnlyList<string> ListThemeConfigFiles(string sitePath, string? themeName = null)
    {
        var list = new List<string>();
        var siteConfig = PathHelper.FindConfigFile(sitePath);
        if (siteConfig is not null)
            list.Add(siteConfig);

        var configDir = Path.Combine(sitePath, "config");
        if (Directory.Exists(configDir))
        {
            list.AddRange(Directory.EnumerateFiles(configDir, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".toml", StringComparison.OrdinalIgnoreCase)
                            || f.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
                            || f.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                            || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(themeName))
        {
            var themeDir = Path.Combine(sitePath, "themes", themeName);
            if (Directory.Exists(themeDir))
            {
                foreach (var name in new[] { "theme.toml", "config.toml", "config.yaml", "hugo.toml" })
                {
                    var p = Path.Combine(themeDir, name);
                    if (File.Exists(p))
                        list.Add(p);
                }

                var example = Path.Combine(themeDir, "exampleSite");
                if (Directory.Exists(example))
                {
                    var exConfig = PathHelper.FindConfigFile(example);
                    if (exConfig is not null)
                        list.Add(exConfig);
                }
            }
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [GeneratedRegex(@"^\s*theme\s*=\s*.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex ThemeLineRegex();

    [GeneratedRegex(@"^\s*theme\s*:\s*.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex YamlThemeRegex();
}
