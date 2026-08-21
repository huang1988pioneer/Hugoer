using System.Diagnostics;
using System.Text.RegularExpressions;
using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.Services;

public sealed partial class HugoService
{
    private readonly SettingsService _settings;

    public HugoService(SettingsService settings)
    {
        _settings = settings;
    }

    public async Task<HugoInfo> DetectAsync(CancellationToken cancellationToken = default)
    {
        var preferred = _settings.Current.PreferredHugoPath;
        if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred))
        {
            var info = await ProbeHugoAsync(preferred, cancellationToken).ConfigureAwait(false);
            if (info.IsInstalled)
                return info;
        }

        var which = OperatingSystem.IsWindows() ? "where" : "which";
        var locate = await ProcessRunner.RunAsync(which, "hugo", timeoutMs: 15_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (locate.Succeeded)
        {
            var path = locate.StdOut
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim();

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                var info = await ProbeHugoAsync(path, cancellationToken).ConfigureAwait(false);
                if (info.IsInstalled)
                    return info;
            }
        }

        // Common install locations on Windows
        if (OperatingSystem.IsWindows())
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WinGet", "Links", "hugo.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Hugo", "hugo.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Hugo", "hugo.exe"),
                Path.Combine(PathHelper.AppDataDir, "tools", "hugo", "hugo.exe"),
            };

            foreach (var c in candidates)
            {
                if (!File.Exists(c)) continue;
                var info = await ProbeHugoAsync(c, cancellationToken).ConfigureAwait(false);
                if (info.IsInstalled)
                    return info;
            }

            // Winget normally creates a link under ...\WinGet\Links, but it is not
            // guaranteed to be available to an already-running desktop process.
            // Fall back to the package payload so Hugoer works immediately after install.
            var wingetHugo = FindWingetHugoExecutable();
            if (wingetHugo is not null)
            {
                var info = await ProbeHugoAsync(wingetHugo, cancellationToken).ConfigureAwait(false);
                if (info.IsInstalled)
                    return info;
            }
        }

        return new HugoInfo
        {
            IsInstalled = false,
            StatusMessage = "未偵測到 Hugo。可使用「一鍵安裝 Hugo Extended」。"
        };
    }

    private static string? FindWingetHugoExecutable()
    {
        try
        {
            var packages = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Packages");
            if (!Directory.Exists(packages)) return null;

            var hugoPackage = Directory.EnumerateDirectories(packages, "Hugo.Hugo.*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .FirstOrDefault();
            return hugoPackage is null
                ? null
                : Directory.EnumerateFiles(hugoPackage, "hugo.exe", SearchOption.AllDirectories).FirstOrDefault();
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task<HugoInfo> ProbeHugoAsync(string exe, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(exe, "version", timeoutMs: 15_000, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded && string.IsNullOrWhiteSpace(result.StdOut))
        {
            return new HugoInfo
            {
                IsInstalled = false,
                ExecutablePath = exe,
                StatusMessage = result.StdErr
            };
        }

        var text = string.IsNullOrWhiteSpace(result.StdOut) ? result.StdErr : result.StdOut;
        var versionMatch = VersionRegex().Match(text);
        var version = versionMatch.Success ? versionMatch.Groups[1].Value : text.Trim();
        var isExtended = text.Contains("extended", StringComparison.OrdinalIgnoreCase);

        return new HugoInfo
        {
            IsInstalled = true,
            Version = version,
            ExecutablePath = exe,
            IsExtended = isExtended,
            StatusMessage = isExtended
                ? $"Hugo Extended {version}"
                : $"Hugo {version}（建議安裝 Extended 以支援 SCSS）"
        };
    }

    public async Task<CommandResult> InstallHugoAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            progress?.Report("非 Windows：請使用套件管理員安裝 hugo（例如 brew install hugo）。");
            return await ProcessRunner.RunShellAsync(
                "command -v brew >/dev/null && brew install hugo || (command -v snap >/dev/null && snap install hugo) || echo 'Please install Hugo manually: https://gohugo.io/installation/'",
                timeoutMs: 300_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        progress?.Report("嘗試使用 winget 安裝 Hugo Extended…");
        var winget = await ProcessRunner.RunAsync(
            "winget",
            "install --id Hugo.Hugo.Extended -e --accept-source-agreements --accept-package-agreements --disable-interactivity",
            timeoutMs: 300_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (winget.Succeeded || winget.CombinedOutput.Contains("already installed", StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report("winget 安裝完成。");
            return winget;
        }

        progress?.Report("winget 失敗，嘗試 chocolatey…");
        var choco = await ProcessRunner.RunAsync(
            "choco",
            "install hugo-extended -y",
            timeoutMs: 300_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (choco.Succeeded)
        {
            progress?.Report("chocolatey 安裝完成。");
            return choco;
        }

        progress?.Report("改為下載官方 binary 到本機 tools…");
        return await DownloadHugoBinaryAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CommandResult> DownloadHugoBinaryAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Hugoer/1.0");

            progress?.Report("查詢最新 Hugo Extended 版本…");
            var api = await http.GetStringAsync(
                "https://api.github.com/repos/gohugoio/hugo/releases/latest",
                cancellationToken).ConfigureAwait(false);

            // Prefer windows-amd64 extended zip
            var assetMatch = AssetRegex().Match(api);
            if (!assetMatch.Success)
            {
                return new CommandResult
                {
                    ExitCode = -1,
                    StdErr = "找不到 Windows amd64 extended 下載連結。"
                };
            }

            var url = assetMatch.Groups[1].Value.Replace("\\/", "/");
            var toolsDir = Path.Combine(PathHelper.AppDataDir, "tools", "hugo");
            Directory.CreateDirectory(toolsDir);
            var zipPath = Path.Combine(toolsDir, "hugo.zip");

            progress?.Report($"下載 {url}…");
            await using (var fs = File.Create(zipPath))
            {
                await using var stream = await http.GetStreamAsync(url, cancellationToken).ConfigureAwait(false);
                await stream.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report("解壓縮…");
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, toolsDir, overwriteFiles: true);
            try { File.Delete(zipPath); } catch { /* ignore */ }

            var exe = Path.Combine(toolsDir, "hugo.exe");
            if (!File.Exists(exe))
            {
                exe = Directory.EnumerateFiles(toolsDir, "hugo.exe", SearchOption.AllDirectories).FirstOrDefault()
                      ?? exe;
            }

            if (!File.Exists(exe))
            {
                return new CommandResult { ExitCode = -1, StdErr = "解壓後找不到 hugo.exe" };
            }

            _settings.Current.PreferredHugoPath = exe;
            _settings.Save();
            progress?.Report($"已安裝到 {exe}");
            return new CommandResult { ExitCode = 0, StdOut = exe };
        }
        catch (Exception ex)
        {
            return new CommandResult { ExitCode = -1, StdErr = ex.Message };
        }
    }

    public async Task<CommandResult> CreateSiteAsync(
        string parentDir,
        string siteName,
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        var hugo = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!hugo.IsInstalled || string.IsNullOrWhiteSpace(hugo.ExecutablePath))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "請先安裝 Hugo。"
            };
        }

        Directory.CreateDirectory(parentDir);
        var args = $"new site \"{siteName}\" --format toml";
        if (force) args += " --force";

        var result = await ProcessRunner.RunAsync(
            hugo.ExecutablePath,
            args,
            parentDir,
            timeoutMs: 60_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            var sitePath = Path.Combine(parentDir, siteName);
            // Seed a friendly starter config
            var config = Path.Combine(sitePath, "hugo.toml");
            if (File.Exists(config))
            {
                var content = await File.ReadAllTextAsync(config, cancellationToken).ConfigureAwait(false);
                var seeded = AppendTomlStringIfMissing(content, "baseURL", "https://example.org/");
                seeded = AppendTomlStringIfMissing(seeded, "locale", "zh-tw");
                seeded = AppendTomlStringIfMissing(seeded, "title", "My New Hugo Site");
                if (!string.Equals(content, seeded, StringComparison.Ordinal))
                {
                    await File.WriteAllTextAsync(config, seeded, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return result;
    }

    private static string AppendTomlStringIfMissing(string content, string key, string value)
    {
        if (Regex.IsMatch(
                content,
                $@"(?im)^\s*{Regex.Escape(key)}\s*=",
                RegexOptions.CultureInvariant))
        {
            return content;
        }

        var separator = content.EndsWith('\n') || content.Length == 0 ? string.Empty : Environment.NewLine;
        var escaped = value.Replace("\\", "\\\\").Replace("'", "\\'");
        return $"{content}{separator}{key} = '{escaped}'{Environment.NewLine}";
    }

    public async Task<CommandResult> NewContentAsync(
        string sitePath,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        var hugo = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!hugo.IsInstalled || string.IsNullOrWhiteSpace(hugo.ExecutablePath))
            return new CommandResult { ExitCode = -1, StdErr = "請先安裝 Hugo。" };

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            normalized += ".md";

        return await ProcessRunner.RunAsync(
            hugo.ExecutablePath,
            $"new content \"{normalized}\"",
            sitePath,
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<CommandResult> BuildAsync(
        string sitePath,
        CancellationToken cancellationToken = default)
    {
        await RepairDuplicateRootTomlKeysAsync(sitePath, cancellationToken).ConfigureAwait(false);
        await MigrateDeprecatedLanguageCodeAsync(sitePath, cancellationToken).ConfigureAwait(false);
        await RepairLegacyStackColorSchemeAsync(sitePath, cancellationToken).ConfigureAwait(false);

        var hugo = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!hugo.IsInstalled || string.IsNullOrWhiteSpace(hugo.ExecutablePath))
            return new CommandResult { ExitCode = -1, StdErr = "請先安裝 Hugo。" };

        return await ProcessRunner.RunAsync(
            hugo.ExecutablePath,
            "build",
            sitePath,
            timeoutMs: 180_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static async Task RepairDuplicateRootTomlKeysAsync(
        string sitePath,
        CancellationToken cancellationToken)
    {
        var config = Path.Combine(sitePath, "hugo.toml");
        if (!File.Exists(config)) return;

        var original = await File.ReadAllTextAsync(config, cancellationToken).ConfigureAwait(false);
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = original.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var repaired = new List<string>(lines.Length);
        var insideTable = false;
        var changed = false;

        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
                insideTable = true;

            if (!insideTable && !trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                var match = SimpleTomlKeyRegex().Match(trimmed);
                if (match.Success && !seen.Add(match.Groups["key"].Value))
                {
                    changed = true;
                    continue;
                }
            }

            repaired.Add(line);
        }

        if (!changed) return;

        var backup = config + ".hugoer.bak";
        if (!File.Exists(backup))
            File.Copy(config, backup);
        await File.WriteAllTextAsync(config, string.Join(newline, repaired), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task RepairLegacyStackColorSchemeAsync(
        string sitePath,
        CancellationToken cancellationToken)
    {
        var config = Path.Combine(sitePath, "hugo.toml");
        if (!File.Exists(config)) return;

        var original = await File.ReadAllTextAsync(config, cancellationToken).ConfigureAwait(false);
        if (Regex.IsMatch(original, @"(?im)^\s*\[params\.colorScheme\]\s*$")) return;

        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = original.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var repaired = new List<string>(lines.Length + 5);
        var currentTable = string.Empty;
        string? scheme = null;

        foreach (var line in lines)
        {
            var table = TomlTableRegex().Match(line.Trim());
            if (table.Success)
                currentTable = table.Groups["table"].Value.Trim();

            if (currentTable.Equals("params", StringComparison.OrdinalIgnoreCase))
            {
                var scalar = LegacyColorSchemeRegex().Match(line.Trim());
                if (scalar.Success)
                {
                    scheme = scalar.Groups["value"].Value;
                    continue;
                }
            }

            repaired.Add(line);
        }

        if (scheme is null) return;

        while (repaired.Count > 0 && string.IsNullOrWhiteSpace(repaired[^1]))
            repaired.RemoveAt(repaired.Count - 1);
        repaired.Add(string.Empty);
        repaired.Add("[params.colorScheme]");
        repaired.Add("  toggle = true");
        repaired.Add($"  default = \"{scheme}\"");
        repaired.Add(string.Empty);

        var backup = config + ".hugoer.bak";
        if (!File.Exists(backup))
            File.Copy(config, backup);
        await File.WriteAllTextAsync(config, string.Join(newline, repaired), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task MigrateDeprecatedLanguageCodeAsync(
        string sitePath,
        CancellationToken cancellationToken)
    {
        var config = Path.Combine(sitePath, "hugo.toml");
        if (!File.Exists(config)) return;

        var original = await File.ReadAllTextAsync(config, cancellationToken).ConfigureAwait(false);
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = original.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        var rootEnd = lines.FindIndex(line => line.TrimStart().StartsWith("[", StringComparison.Ordinal));
        if (rootEnd < 0) rootEnd = lines.Count;

        var languageIndex = -1;
        var localeIndex = -1;
        for (var index = 0; index < rootEnd; index++)
        {
            var key = SimpleTomlKeyRegex().Match(lines[index].TrimStart());
            if (!key.Success) continue;
            if (key.Groups["key"].Value.Equals("languageCode", StringComparison.OrdinalIgnoreCase))
                languageIndex = index;
            if (key.Groups["key"].Value.Equals("locale", StringComparison.OrdinalIgnoreCase))
                localeIndex = index;
        }

        if (languageIndex < 0) return;

        var equals = lines[languageIndex].IndexOf('=');
        if (equals < 0) return;
        var value = lines[languageIndex][(equals + 1)..].Trim();
        var indent = new string(lines[languageIndex].TakeWhile(char.IsWhiteSpace).ToArray());

        if (localeIndex >= 0)
        {
            var localeIndent = new string(lines[localeIndex].TakeWhile(char.IsWhiteSpace).ToArray());
            lines[localeIndex] = $"{localeIndent}locale = {value}";
            lines.RemoveAt(languageIndex);
        }
        else
        {
            lines[languageIndex] = $"{indent}locale = {value}";
        }

        var backup = config + ".hugoer.bak";
        if (!File.Exists(backup))
            File.Copy(config, backup);
        await File.WriteAllTextAsync(config, string.Join(newline, lines), cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(Process? Process, string Message)> StartServerAsync(
        string sitePath,
        int port = 1313,
        CancellationToken cancellationToken = default)
    {
        await RepairDuplicateRootTomlKeysAsync(sitePath, cancellationToken).ConfigureAwait(false);
        await MigrateDeprecatedLanguageCodeAsync(sitePath, cancellationToken).ConfigureAwait(false);
        await RepairLegacyStackColorSchemeAsync(sitePath, cancellationToken).ConfigureAwait(false);

        var hugo = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!hugo.IsInstalled || string.IsNullOrWhiteSpace(hugo.ExecutablePath))
            return (null, "請先安裝 Hugo。");

        var psi = new ProcessStartInfo
        {
            FileName = hugo.ExecutablePath,
            Arguments = $"server --buildDrafts --navigateToChanged --port {port} --bind 127.0.0.1",
            WorkingDirectory = sitePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            var p = Process.Start(psi);
            if (p is null)
                return (null, "無法啟動 Hugo Server 程序。");

            var stdout = p.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = p.StandardError.ReadToEndAsync(cancellationToken);
            var url = $"http://127.0.0.1:{port}/";
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };

            for (var attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                if (p.HasExited)
                {
                    var output = string.Join(
                        Environment.NewLine,
                        new[] { await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false) }
                            .Where(value => !string.IsNullOrWhiteSpace(value)));
                    p.Dispose();
                    return (null, string.IsNullOrWhiteSpace(output)
                        ? "Hugo Server 啟動後立即結束，請檢查網站設定。"
                        : $"Hugo Server 啟動失敗：{output.Trim()}");
                }

                try
                {
                    using var response = await http.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                        return (p, $"本機預覽已就緒：{url}");
                }
                catch (HttpRequestException)
                {
                    // Hugo is still compiling; retry until the readiness deadline.
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Per-request timeout; the overall readiness loop continues.
                }
            }

            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            p.Dispose();
            return (null, $"Hugo Server 在等待 8 秒後仍未就緒：{url}");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public SiteInfo? InspectSite(string sitePath)
    {
        if (!PathHelper.LooksLikeHugoSite(sitePath))
            return null;

        var config = PathHelper.FindConfigFile(sitePath);
        string? theme = null;
        if (config is not null)
        {
            try
            {
                var text = File.ReadAllText(config);
                var m = ThemeRegex().Match(text);
                if (m.Success)
                    theme = m.Groups[1].Value.Trim('"', '\'', ' ');
            }
            catch { /* ignore */ }
        }

        var themesDir = PathHelper.ThemesDir(sitePath);
        if (theme is null && Directory.Exists(themesDir))
        {
            theme = Directory.GetDirectories(themesDir)
                .Select(Path.GetFileName)
                .FirstOrDefault();
        }

        return new SiteInfo
        {
            Path = sitePath,
            Name = Path.GetFileName(sitePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ConfigFile = config,
            HasGit = Directory.Exists(Path.Combine(sitePath, ".git")),
            ThemeName = theme
        };
    }

    [GeneratedRegex(@"hugo\s+v?([0-9]+\.[0-9]+\.[0-9]+[^\s]*)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"""browser_download_url"":\s*""(https:[^""]*hugo_extended_[^""]*windows-amd64\.zip)""", RegexOptions.IgnoreCase)]
    private static partial Regex AssetRegex();

    [GeneratedRegex(@"theme\s*=\s*[\[""']?([^\]""'\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ThemeRegex();

    [GeneratedRegex(@"^(?<key>[A-Za-z0-9_.-]+)\s*=")]
    private static partial Regex SimpleTomlKeyRegex();

    [GeneratedRegex(@"^\[(?<table>[^\]]+)\]$")]
    private static partial Regex TomlTableRegex();

    [GeneratedRegex("""^colorScheme\s*=\s*['"](?<value>auto|light|dark)['"]\s*$""", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyColorSchemeRegex();
}
