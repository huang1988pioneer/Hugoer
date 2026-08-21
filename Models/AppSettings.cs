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
    public string LastWriteTimeText => LastWriteTime.ToString("yyyy/MM/dd HH:mm");
    public DateTimeOffset? ArticleDate { get; init; }
    public string ArticleDateText => ArticleDate?.ToString("yyyy/MM/dd") ?? "未設定日期";
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

public enum DeploymentVersionState
{
    NotConfigured,
    Previous,
    Latest,
    Unavailable
}

public sealed class DeploymentMarker
{
    public string DeploymentId { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
}

public sealed class DeploymentCheckResult
{
    public DeploymentVersionState State { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ExpectedDeploymentId { get; init; }
    public string? LiveDeploymentId { get; init; }
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.Now;
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

public sealed class GitHubRepositoryTarget
{
    public bool IsValid { get; init; }
    public string? Owner { get; init; }
    public string? Repository { get; init; }
    public string? CanonicalUrl { get; init; }
    public string? PagesUrl { get; init; }
    public bool IsUserOrOrganizationSite { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
}
