using System.Text.Json.Serialization;

namespace Hugoer.Models;

public sealed class AppSettings
{
    public string? LastSitePath { get; set; }
    public string? PreferredHugoPath { get; set; }
    public string ThemeVariant { get; set; } = "Default";
    /// <summary>Last Markdown editor mode: Wysiwyg or Source.</summary>
    public string MarkdownEditorMode { get; set; } = "Wysiwyg";
    /// <summary>Independent connection preferences for each Git hosting provider.</summary>
    public List<GitProviderSettings> GitProviderSettings { get; set; } = [];
    /// <summary>Recently used repositories across all Git hosting providers, newest first.</summary>
    public List<RecentRepositoryEntry> RecentRepositories { get; set; } = [];
}

public enum MarkdownEditorMode
{
    Wysiwyg,
    Source
}

/// <summary>
/// CKEditor 5-style corresponding preview: rendered HTML, or the Markdown source output.
/// </summary>
public enum MarkdownPreviewKind
{
    Render,
    MarkdownOutput
}

public sealed class HugoInfo
{
    public bool IsInstalled { get; init; }
    public string? Version { get; init; }
    public string? ExecutablePath { get; init; }
    public bool IsExtended { get; init; }
    public string StatusMessage { get; init; } = string.Empty;
}

public sealed class HugoVersionCheck
{
    public bool CheckSucceeded { get; init; }
    public string? CurrentVersion { get; init; }
    public string? LatestVersion { get; init; }
    public string? ReleaseUrl { get; init; }
    public bool UpdateAvailable { get; init; }
    public string Message { get; init; } = string.Empty;
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
    public string ArticleTitle { get; init; } = string.Empty;
    public string DisplayTitle => string.IsNullOrWhiteSpace(ArticleTitle) ? Name : ArticleTitle;
    public bool IsDraft { get; init; }
    public bool IsPublished => !IsDraft;
    public bool HasArticleDate => ArticleDate.HasValue;
    public string PublicationStatusText => IsDraft ? "草稿" : "已發布";
    public string TimelineText => ArticleDate.HasValue
        ? $"文章 {ArticleDate:yyyy/MM/dd} · 更新 {LastWriteTime:yyyy/MM/dd HH:mm}"
        : $"未設定日期 · 更新 {LastWriteTime:yyyy/MM/dd HH:mm}";
}

public sealed class CommandResult
{
    public int ExitCode { get; init; }
    public string StdOut { get; init; } = string.Empty;
    public string StdErr { get; init; } = string.Empty;
    /// <summary>
    /// 主要操作（推送）已成功，但有次要步驟（如 Pages 設定）需要手動完成。
    /// </summary>
    public bool IsPartialSuccess { get; init; }
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
    public GitHostingProvider? Provider { get; set; }
    public string ProviderName => Provider?.DisplayName() ?? "未知 Git 平台";
}

public enum GitHostingProvider
{
    GitHub,
    GitLab,
    Codeberg,
    Bitbucket
}

public static class GitHostingProviderExtensions
{
    public static string DisplayName(this GitHostingProvider provider) => provider switch
    {
        GitHostingProvider.GitHub => "GitHub",
        GitHostingProvider.GitLab => "GitLab",
        GitHostingProvider.Codeberg => "Codeberg",
        GitHostingProvider.Bitbucket => "Bitbucket",
        _ => "Git"
    };

    public static string PagesProductName(this GitHostingProvider provider) => provider switch
    {
        GitHostingProvider.GitHub => "GitHub Pages",
        GitHostingProvider.GitLab => "GitLab Pages",
        GitHostingProvider.Codeberg => "Codeberg Pages",
        GitHostingProvider.Bitbucket => "Bitbucket Static Website",
        _ => "Pages"
    };
}

public sealed class GitProviderSettings
{
    public GitHostingProvider Provider { get; set; }
    public string RepositoryUrl { get; set; } = string.Empty;
    public string AccountOrWorkspace { get; set; } = string.Empty;
    public string PagesUrl { get; set; } = string.Empty;
    public bool SyncRecommendedBaseUrl { get; set; } = true;
    public string CommitMessage { get; set; } = "Update site via Hugoer";
}

public sealed class GitHostingProviderOption
{
    public required GitHostingProvider Provider { get; init; }
    public required string DisplayName { get; init; }
    public string Hint { get; init; } = string.Empty;
}

public sealed class GitHubRepositoryTarget
{
    public bool IsValid { get; init; }
    public GitHostingProvider Provider { get; init; } = GitHostingProvider.GitHub;
    public string? Owner { get; init; }
    public string? Repository { get; init; }
    public string? CanonicalUrl { get; init; }
    public string? PagesUrl { get; init; }
    public bool IsUserOrOrganizationSite { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string ProviderName => Provider.DisplayName();
    public string PagesProductName => Provider.PagesProductName();
    public string NameWithOwner =>
        string.IsNullOrWhiteSpace(Owner) || string.IsNullOrWhiteSpace(Repository)
            ? string.Empty
            : $"{Owner}/{Repository}";
}

public sealed class GitHubRepositoryLookup
{
    public bool CheckSucceeded { get; init; }
    public bool Exists { get; init; }
    public bool CanReuse { get; init; }
    public bool LooksLikeHugo { get; init; }
    public GitHubRepositoryTarget? Target { get; init; }
    public string Message { get; init; } = string.Empty;

    public static GitHubRepositoryLookup Fail(string message) => new()
    {
        CheckSucceeded = false,
        Message = message
    };

    public static GitHubRepositoryLookup Missing() => new()
    {
        CheckSucceeded = true,
        Exists = false
    };
}

public sealed class GitHubPagesRepositoryItem
{
    public string NameWithOwner { get; init; } = string.Empty;
    public string HtmlUrl { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
}

public sealed class RecentRepositoryEntry
{
    public GitHostingProvider Provider { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    /// <summary>遠端 repository 網址（Git 平台上的來源，例如 https://github.com/owner/repo）。</summary>
    public string CanonicalUrl { get; set; } = string.Empty;
    /// <summary>最終發布網址（例如 GitHub/GitLab/Codeberg/Bitbucket Pages 網址），與遠端 repository 網址不同。</summary>
    public string? PagesUrl { get; set; }
    /// <summary>本機資料夾路徑；可能與 repository 名稱不同（例如使用者自行改名的資料夾）。</summary>
    public string? LocalPath { get; set; }
    public DateTimeOffset LastUsedUtc { get; set; }
    [JsonIgnore]
    public string DisplayName => $"{Owner}/{Repository} · {Provider.DisplayName()}";
    [JsonIgnore]
    public string RemoteSummary => $"遠端：{CanonicalUrl}";
    [JsonIgnore]
    public string LocalSummary => string.IsNullOrWhiteSpace(LocalPath) ? "本地：尚未複製到本機" : $"本地：{LocalPath}";
    [JsonIgnore]
    public string PagesSummary => string.IsNullOrWhiteSpace(PagesUrl) ? "發布網址：無" : $"發布網址：{PagesUrl}";
}

public sealed class GitHubPagesRepositoryList
{
    public bool Succeeded { get; init; }
    public IReadOnlyList<GitHubPagesRepositoryItem> Repositories { get; init; } = [];
    public string Message { get; init; } = string.Empty;
}

public sealed class CloneSiteResult
{
    public bool Succeeded { get; init; }
    public bool OpenedExisting { get; init; }
    public string? SitePath { get; init; }
    public GitHubRepositoryTarget? Target { get; init; }
    public string Message { get; init; } = string.Empty;
    public string CombinedOutput { get; init; } = string.Empty;

    public static CloneSiteResult Fail(
        string message,
        GitHubRepositoryTarget? target = null,
        string? output = null) => new()
    {
        Succeeded = false,
        Target = target,
        Message = message,
        CombinedOutput = string.IsNullOrWhiteSpace(output) ? message : output
    };

    public static CloneSiteResult Ok(
        string sitePath,
        GitHubRepositoryTarget target,
        string message,
        bool openedExisting = false,
        string? output = null) => new()
    {
        Succeeded = true,
        OpenedExisting = openedExisting,
        SitePath = sitePath,
        Target = target,
        Message = message,
        CombinedOutput = string.IsNullOrWhiteSpace(output) ? message : output
    };
}
