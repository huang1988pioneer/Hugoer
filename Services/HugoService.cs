using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.Services;

public sealed partial class HugoService
{
    private const string UserAgent = "Hugoer/1.7.0";
    private static readonly HttpClient DefaultHttpClient = CreateHttpClient();
    private readonly SettingsService _settings;
    private readonly HttpClient _httpClient;

    public HugoService(SettingsService settings, HttpClient? httpClient = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _httpClient = httpClient ?? DefaultHttpClient;

        // A caller supplied client is useful for tests and for hosts that own
        // their networking stack. Only add a default identity when it has none.
        if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
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
        var locate = await ProcessRunner.RunAsync(which, ["hugo"], timeoutMs: 15_000, cancellationToken: cancellationToken)
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
        var result = await ProcessRunner.RunAsync(exe, ["version"], timeoutMs: 15_000, cancellationToken: cancellationToken)
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

        progress?.Report("嘗試使用 winget 更新 Hugo Extended…");
        var upgrade = await ProcessRunner.RunAsync(
            "winget",
            [
                "upgrade", "--id", "Hugo.Hugo.Extended", "-e",
                "--accept-source-agreements", "--accept-package-agreements",
                "--disable-interactivity"
            ],
            timeoutMs: 300_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (upgrade.Succeeded
            || upgrade.CombinedOutput.Contains("No available upgrade", StringComparison.OrdinalIgnoreCase)
            || upgrade.CombinedOutput.Contains("No installed package found", StringComparison.OrdinalIgnoreCase))
        {
            if (upgrade.Succeeded && !upgrade.CombinedOutput.Contains("No available upgrade", StringComparison.OrdinalIgnoreCase))
                return upgrade;
        }

        progress?.Report("嘗試使用 winget 安裝 Hugo Extended…");
        var winget = await ProcessRunner.RunAsync(
            "winget",
            [
                "install", "--id", "Hugo.Hugo.Extended", "-e",
                "--accept-source-agreements", "--accept-package-agreements",
                "--disable-interactivity"
            ],
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
            ["install", "hugo-extended", "-y"],
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

    public async Task<HugoVersionCheck> CheckLatestVersionAsync(
        string? currentVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CreateTimeoutSource(cancellationToken, TimeSpan.FromSeconds(12));

            var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(
                "https://api.github.com/repos/gohugoio/hugo/releases/latest",
                timeout.Token).ConfigureAwait(false);

            var latest = NormalizeVersion(release?.TagName);
            if (string.IsNullOrWhiteSpace(latest))
            {
                return new HugoVersionCheck
                {
                    CheckSucceeded = false,
                    CurrentVersion = currentVersion,
                    Message = "無法解析 Hugo 最新版本資訊。"
                };
            }

            var current = NormalizeVersion(currentVersion);
            if (string.IsNullOrWhiteSpace(current))
            {
                return new HugoVersionCheck
                {
                    CheckSucceeded = true,
                    CurrentVersion = currentVersion,
                    LatestVersion = latest,
                    ReleaseUrl = release?.HtmlUrl,
                    UpdateAvailable = false,
                    Message = $"Hugo 最新版是 v{latest}。尚未偵測到可比較的本機版本。"
                };
            }

            var updateAvailable = CompareVersions(current, latest) < 0;
            return new HugoVersionCheck
            {
                CheckSucceeded = true,
                CurrentVersion = current,
                LatestVersion = latest,
                ReleaseUrl = release?.HtmlUrl,
                UpdateAvailable = updateAvailable,
                Message = updateAvailable
                    ? $"可更新：本機 Hugo v{current}，官方最新版 v{latest}。建議更新 Hugo Extended。"
                    : $"Hugo 已是最新版：v{current}。"
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return new HugoVersionCheck
            {
                CheckSucceeded = false,
                CurrentVersion = currentVersion,
                Message = $"暫時無法檢查 Hugo 最新版：{ex.Message}"
            };
        }
    }

    internal static string? NormalizeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var match = SemanticVersionRegex().Match(version);
        return match.Success
            ? $"{match.Groups["major"].Value}.{match.Groups["minor"].Value}.{match.Groups["patch"].Value}"
            : null;
    }

    internal static int CompareVersions(string? left, string? right)
    {
        var leftParts = ParseVersionParts(left);
        var rightParts = ParseVersionParts(right);
        for (var index = 0; index < 3; index++)
        {
            var comparison = leftParts[index].CompareTo(rightParts[index]);
            if (comparison != 0)
                return comparison;
        }

        return 0;
    }

    private static int[] ParseVersionParts(string? version)
    {
        var normalized = NormalizeVersion(version);
        if (normalized is null)
            return [0, 0, 0];

        return normalized
            .Split('.')
            .Select(part => int.TryParse(part, out var value) ? value : 0)
            .Concat([0, 0, 0])
            .Take(3)
            .ToArray();
    }

    private async Task<CommandResult> DownloadHugoBinaryAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CreateTimeoutSource(cancellationToken, TimeSpan.FromMinutes(5));

            progress?.Report("查詢最新 Hugo Extended 版本…");
            var api = await _httpClient.GetStringAsync(
                "https://api.github.com/repos/gohugoio/hugo/releases/latest",
                timeout.Token).ConfigureAwait(false);

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
            var partialZipPath = zipPath + ".download";
            try
            {
                await using (var fs = new FileStream(
                    partialZipPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    options: FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await using var stream = await _httpClient.GetStreamAsync(url, timeout.Token)
                        .ConfigureAwait(false);
                    await stream.CopyToAsync(fs, timeout.Token).ConfigureAwait(false);
                }

                File.Move(partialZipPath, zipPath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(partialZipPath)) File.Delete(partialZipPath); }
                catch { /* preserve the original download error */ }
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

            _settings.SetPreferredHugoPath(exe);
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
        if (!TryNormalizeSiteName(parentDir, siteName, out var parentPath, out var normalizedName, out var validationError))
            return new CommandResult { ExitCode = -1, StdErr = validationError };

        var hugo = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!hugo.IsInstalled || string.IsNullOrWhiteSpace(hugo.ExecutablePath))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "請先安裝 Hugo。"
            };
        }

        Directory.CreateDirectory(parentPath);
        var args = new List<string> { "new", "site", normalizedName, "--format", "toml" };
        if (force) args.Add("--force");

        var result = await ProcessRunner.RunAsync(
            hugo.ExecutablePath,
            args,
            parentPath,
            timeoutMs: 60_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            var sitePath = Path.Combine(parentPath, normalizedName);
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
                    await AtomicFileWriter.WriteAllTextAsync(config, seeded, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await EnsureGoldmarkUnsafeHtmlAsync(sitePath, cancellationToken).ConfigureAwait(false);
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
        if (string.IsNullOrWhiteSpace(sitePath) || !Directory.Exists(sitePath))
            return new CommandResult { ExitCode = -1, StdErr = "找不到 Hugo 網站資料夾。" };

        var normalized = (relativePath ?? string.Empty).Replace('\\', '/').Trim().TrimStart('/');
        if (normalized.Length == 0)
            return new CommandResult { ExitCode = -1, StdErr = "文章路徑不可為空。" };
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            normalized += ".md";

        if (!PathHelper.TryResolveUnder(
                PathHelper.ContentDir(sitePath),
                normalized.Replace('/', Path.DirectorySeparatorChar),
                out var contentPath,
                allowRoot: false))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "文章路徑必須位於 content/ 內。"
            };
        }

        var hugo = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!hugo.IsInstalled || string.IsNullOrWhiteSpace(hugo.ExecutablePath))
            return new CommandResult { ExitCode = -1, StdErr = "請先安裝 Hugo。" };

        var hugoRelativePath = Path.GetRelativePath(sitePath, contentPath).Replace('\\', '/');

        return await ProcessRunner.RunAsync(
            hugo.ExecutablePath,
            ["new", "content", hugoRelativePath],
            sitePath,
            timeoutMs: 30_000,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static bool TryNormalizeSiteName(
        string parentDir,
        string siteName,
        out string parentPath,
        out string normalizedName,
        out string error)
    {
        parentPath = string.Empty;
        normalizedName = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(parentDir))
        {
            error = "網站上層資料夾不可為空。";
            return false;
        }

        try
        {
            parentPath = Path.GetFullPath(parentDir.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            error = "網站上層資料夾路徑格式無效。";
            return false;
        }

        normalizedName = (siteName ?? string.Empty).Trim();
        if (normalizedName.Length == 0
            || normalizedName is "." or ".."
            || Path.IsPathRooted(normalizedName)
            || normalizedName.Any(character => character is '/' or '\\' || char.IsControl(character))
            || normalizedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "網站名稱只能是單一資料夾名稱，且不可包含路徑分隔符或特殊字元。";
            return false;
        }

        if (!PathHelper.TryResolveUnder(parentPath, normalizedName, out _, allowRoot: false))
        {
            error = "網站資料夾必須位於所選上層資料夾內。";
            return false;
        }

        return true;
    }

    public Task<CommandResult> BuildAsync(
        string sitePath,
        CancellationToken cancellationToken = default) =>
        BuildAsync(sitePath, extraArgs: null, cancellationToken);

    public async Task<CommandResult> BuildAsync(
        string sitePath,
        string? extraArgs,
        CancellationToken cancellationToken = default)
    {
        return await BuildCoreAsync(sitePath, extraArgs, argumentList: null, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Builds a site with structured Hugo arguments. This keeps generated URLs and
    /// other values separate from the command parser used by the legacy string API.
    /// </summary>
    public async Task<CommandResult> BuildWithArgumentsAsync(
        string sitePath,
        IEnumerable<string>? extraArgs = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sitePath);
        var arguments = extraArgs?.ToArray() ?? [];
        return await BuildCoreAsync(sitePath, extraArgsText: null, arguments, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<CommandResult> BuildCoreAsync(
        string sitePath,
        string? extraArgsText,
        IReadOnlyList<string>? argumentList,
        CancellationToken cancellationToken)
    {
        await RepairDuplicateRootTomlKeysAsync(sitePath, cancellationToken).ConfigureAwait(false);
        await MigrateDeprecatedLanguageCodeAsync(sitePath, cancellationToken).ConfigureAwait(false);
        await RepairLegacyStackColorSchemeAsync(sitePath, cancellationToken).ConfigureAwait(false);
        await RepairLegacyStackSearchPageAsync(sitePath, cancellationToken).ConfigureAwait(false);
        await EnsureGoldmarkUnsafeHtmlAsync(sitePath, cancellationToken).ConfigureAwait(false);

        var hugo = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!hugo.IsInstalled || string.IsNullOrWhiteSpace(hugo.ExecutablePath))
            return new CommandResult { ExitCode = -1, StdErr = "請先安裝 Hugo。" };

        if (argumentList is not null)
        {
            var args = new List<string>(argumentList.Count + 1) { "build" };
            args.AddRange(argumentList);
            return await ProcessRunner.RunAsync(
                hugo.ExecutablePath,
                args,
                sitePath,
                timeoutMs: 180_000,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var text = string.IsNullOrWhiteSpace(extraArgsText) ? "build" : $"build {extraArgsText.Trim()}";
        return await ProcessRunner.RunAsync(
            hugo.ExecutablePath,
            text,
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
        await AtomicFileWriter.WriteAllTextAsync(config, string.Join(newline, repaired), cancellationToken)
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
        await AtomicFileWriter.WriteAllTextAsync(config, string.Join(newline, repaired), cancellationToken)
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
        await AtomicFileWriter.WriteAllTextAsync(config, string.Join(newline, lines), cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EnsureGoldmarkUnsafeHtmlAsync(
        string sitePath,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var root = PathHelper.FindConfigFile(sitePath);
        if (root is not null &&
            root.EndsWith(".toml", StringComparison.OrdinalIgnoreCase))
        {
            files.Add(Path.GetFullPath(root));
        }

        var splitMarkup = Path.Combine(sitePath, "config", "_default", "markup.toml");
        if (File.Exists(splitMarkup))
        {
            var full = Path.GetFullPath(splitMarkup);
            if (!files.Exists(path => string.Equals(path, full, StringComparison.OrdinalIgnoreCase)))
                files.Add(full);
        }

        foreach (var file in files)
        {
            var original = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
            var next = GoldmarkUnsafeHtml.EnsureEnabled(original, out var changed);
            if (!changed) continue;

            var backup = file + ".hugoer.bak";
            if (!File.Exists(backup))
                File.Copy(file, backup);
            await AtomicFileWriter.WriteAllTextAsync(file, next, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task RepairLegacyStackSearchPageAsync(
        string sitePath,
        CancellationToken cancellationToken)
    {
        var legacy = Path.Combine(sitePath, "content", "search", "_index.md");
        var current = Path.Combine(sitePath, "content", "search", "index.md");
        if (!File.Exists(legacy) || File.Exists(current)) return;

        var content = await File.ReadAllTextAsync(legacy, cancellationToken).ConfigureAwait(false);
        if (!Regex.IsMatch(content, """(?im)^\s*layout\s*:\s*['"]?search['"]?\s*$""")) return;

        var backup = legacy + ".hugoer.bak";
        if (!File.Exists(backup))
            File.Copy(legacy, backup);
        File.Move(legacy, current);
    }

    public async Task<HugoServerStartResult> StartServerAsync(
        string sitePath,
        int preferredPort = 1313,
        CancellationToken cancellationToken = default)
    {
        await RepairDuplicateRootTomlKeysAsync(sitePath, cancellationToken).ConfigureAwait(false);
        await MigrateDeprecatedLanguageCodeAsync(sitePath, cancellationToken).ConfigureAwait(false);
        await RepairLegacyStackColorSchemeAsync(sitePath, cancellationToken).ConfigureAwait(false);
        await RepairLegacyStackSearchPageAsync(sitePath, cancellationToken).ConfigureAwait(false);
        await EnsureGoldmarkUnsafeHtmlAsync(sitePath, cancellationToken).ConfigureAwait(false);

        var hugo = await DetectAsync(cancellationToken).ConfigureAwait(false);
        if (!hugo.IsInstalled || string.IsNullOrWhiteSpace(hugo.ExecutablePath))
            return Fail("請先安裝 Hugo。");

        const int portRange = 10;
        var nextPort = preferredPort;
        var lastPort = preferredPort + portRange - 1;
        while (nextPort <= lastPort)
        {
            var port = await TcpListeningPort.AllocateAsync(nextPort, lastPort - nextPort + 1, cancellationToken)
                .ConfigureAwait(false);
            if (port is null)
                break;

            var result = await StartServerOnPortAsync(
                hugo.ExecutablePath,
                sitePath,
                port.Value,
                cancellationToken).ConfigureAwait(false);
            if (result.Succeeded || !HugoServerOutput.LooksLikePortInUse(result.Message))
                return result;

            nextPort = port.Value + 1;
        }

        return Fail($"找不到可用的本機預覽埠（{preferredPort}–{lastPort}）。請關閉占用中的程式後再試。");
    }

    private async Task<HugoServerStartResult> StartServerOnPortAsync(
        string hugoExe,
        string sitePath,
        int port,
        CancellationToken cancellationToken)
    {
        var url = $"http://127.0.0.1:{port}/";
        var psi = new ProcessStartInfo
        {
            FileName = hugoExe,
            Arguments = $"server --buildDrafts --navigateToChanged --port {port} --bind 127.0.0.1",
            WorkingDirectory = sitePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        Process? process = null;
        var collecting = true;
        var output = new StringBuilder();
        void Drain(object? sender, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;
            lock (output)
            {
                if (!collecting) return;
                output.AppendLine(e.Data);
            }
        }

        void StopCollecting()
        {
            lock (output)
                collecting = false;
        }

        try
        {
            process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += Drain;
            process.ErrorDataReceived += Drain;
            if (!process.Start())
            {
                process.Dispose();
                return Fail("無法啟動 Hugo Server 程序。", url);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            for (var attempt = 0; attempt < 40; attempt++)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
                if (process.HasExited)
                {
                    StopCollecting();
                    var snapshot = Snapshot(output);
                    process.Dispose();
                    process = null;
                    return Fail(FormatStartFailure(snapshot, port), url);
                }

                try
                {
                    using var requestTimeout = CreateTimeoutSource(cancellationToken, TimeSpan.FromSeconds(1));
                    using var response = await _httpClient.GetAsync(
                        url,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestTimeout.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        StopCollecting();
                        return new HugoServerStartResult(process, $"本機預覽已就緒：{url}", url);
                    }
                }
                catch (HttpRequestException)
                {
                    // Hugo is still compiling; retry until the readiness deadline.
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Per-request timeout; the overall readiness loop continues.
                }
            }

            StopCollecting();
            var timeoutOutput = Snapshot(output);
            KillServer(process);
            process = null;
            var summary = HugoServerOutput.Summarize(timeoutOutput);
            return Fail(string.IsNullOrWhiteSpace(summary)
                ? $"Hugo Server 在等待 8 秒後仍未就緒：{url}"
                : $"Hugo Server 在等待 8 秒後仍未就緒：{url}{Environment.NewLine}{summary}", url);
        }
        catch (Exception ex)
        {
            StopCollecting();
            if (process is not null)
                KillServer(process);
            return Fail(ex.Message, url);
        }
    }

    private static string Snapshot(StringBuilder output)
    {
        lock (output)
            return output.ToString();
    }

    private static string FormatStartFailure(string output, int port)
    {
        var summary = HugoServerOutput.Summarize(output);
        if (HugoServerOutput.LooksLikePortInUse(output) ||
            (string.IsNullOrWhiteSpace(summary) && !TcpListeningPort.IsFree(port)))
        {
            return string.IsNullOrWhiteSpace(summary)
                ? $"本機預覽啟動失敗：埠 {port} 已被占用。"
                : $"本機預覽啟動失敗：埠 {port} 已被占用。{Environment.NewLine}{summary}";
        }

        return string.IsNullOrWhiteSpace(summary)
            ? "Hugo Server 啟動後立即結束，請檢查網站設定。"
            : $"Hugo Server 啟動失敗：{summary}";
    }

    private static HugoServerStartResult Fail(string message, string url = "http://127.0.0.1:1313/") =>
        new(null, message, url);

    private static CancellationTokenSource CreateTimeoutSource(
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(timeout);
        return source;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            // Individual operations apply their own cancellation budget. Keeping
            // the shared client uncapped prevents the default 100-second timeout
            // from overriding a deliberate operation-level budget.
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }

    public void StopServer(Process? process) => KillServer(process);

    private static void KillServer(Process? process)
    {
        if (process is null) return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            if (!process.HasExited)
                process.WaitForExit(3000);
        }
        catch (InvalidOperationException)
        {
            // Process already exited.
        }
        finally
        {
            process.Dispose();
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

    [GeneratedRegex(@"v?(?<major>[0-9]+)\.(?<minor>[0-9]+)\.(?<patch>[0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SemanticVersionRegex();

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

public sealed record HugoServerStartResult(Process? Process, string Message, string Url)
{
    public bool Succeeded => Process is not null;
}

internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; init; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
}
