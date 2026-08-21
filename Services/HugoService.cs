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
                if (!content.Contains("languageCode", StringComparison.OrdinalIgnoreCase))
                {
                    content += """

baseURL = 'https://example.org/'
languageCode = 'zh-tw'
title = 'My New Hugo Site'
""";
                    await File.WriteAllTextAsync(config, content, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return result;
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

    public async Task<(Process? Process, string Message)> StartServerAsync(
        string sitePath,
        int port = 1313,
        CancellationToken cancellationToken = default)
    {
        var hugo = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!hugo.IsInstalled || string.IsNullOrWhiteSpace(hugo.ExecutablePath))
            return (null, "請先安裝 Hugo。");

        var psi = new ProcessStartInfo
        {
            FileName = hugo.ExecutablePath,
            Arguments = $"server -D --port {port} --bind 127.0.0.1",
            WorkingDirectory = sitePath,
            UseShellExecute = true,
            CreateNoWindow = false
        };

        try
        {
            var p = Process.Start(psi);
            return (p, $"已啟動本機預覽：http://127.0.0.1:{port}/");
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
}
