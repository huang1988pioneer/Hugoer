using System.Text.Json.Serialization;

namespace Hugoer.Models;

public sealed class AppSettings
{
    public string? LastSitePath { get; set; }
    public string? PreferredHugoPath { get; set; }
    public string ThemeVariant { get; set; } = "Default";
}

public sealed class HugoInfo
{
    public bool IsInstalled { get; init; }
    public string? Version { get; init; }
    public string? ExecutablePath { get; init; }
    public bool IsExtended { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
}

public sealed class SiteInfo
{
    public string Path { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? ConfigFile { get; init; }
    public bool HasGit { get; init; }
    public string? ThemeName { get; init; }
}

public sealed class ThemePreset
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string RepoUrl { get; init; }
    public required string FolderName { get; init; }
    public string Description { get; init; } = string.Empty;
    public string DocsUrl { get; init; } = string.Empty;
    public string ConfigHint { get; init; } = string.Empty;
}

public sealed class ContentItem
{
    public required string FullPath { get; init; }
    public required string RelativePath { get; init; }
    public required string Name { get; init; }
    public bool IsDirectory { get; init; }
    public DateTime LastWriteTime { get; init; }
}

public sealed class CommandResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;
    public bool Succeeded => ExitCode == 0;
    public string CombinedOutput =>
        string.IsNullOrWhiteSpace(StdErr) ? StdOut : $"{StdOut}\n{StdErr}".Trim();
}

public sealed class GitHubPagesStatus
{
    public bool Enabled { get; init; }
    public string? HtmlUrl { get; init; }
    public string? Status { get; init; }
    public string? SourceBranch { get; init; }
    public string? SourcePath { get; init; }
    public string? BuildType { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? Cname { get; init; }
}

public sealed class GitRemoteInfo
{
    public string? RemoteUrl { get; set; }
    public string? Owner { get; set; }
    public string? Repo { get; set; }
    public string? Branch { get; set; }
    public bool GhAuthenticated { get; set; }
    public string? GhUser { get; set; }
}
