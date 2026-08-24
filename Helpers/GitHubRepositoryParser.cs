using System.Text.Json;
using System.Text.RegularExpressions;
using Hugoer.Models;

namespace Hugoer.Helpers;

public static partial class GitHubRepositoryParser
{
    public static GitHubRepositoryTarget Parse(string? input)
    {
        var value = input?.Trim().Trim('"', '\'') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return Invalid("請貼上 GitHub、GitLab、Codeberg 或 Bitbucket repository / Pages 網址。");

        if (TryParsePagesUrl(value, out var pagesTarget))
            return pagesTarget;

        return ParseRepositoryUrl(value);
    }

    public static bool IsValidOwner(string? owner) =>
        !string.IsNullOrWhiteSpace(owner)
        && owner.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(segment => OwnerSegmentRegex().IsMatch(segment));

    public static bool IsValidRepositoryName(string? repository) =>
        !string.IsNullOrWhiteSpace(repository) && RepositoryRegex().IsMatch(repository);

    public static IReadOnlyList<GitHubPagesRepositoryItem> ParsePagesEnabledRepositories(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            var trimmed = json.Trim();
            if (!trimmed.StartsWith('['))
                return [];

            // gh --paginate emits one JSON array per page. Join them into a single array.
            var combined = JsonArrayJoinRegex().Replace(trimmed, ",");
            using var doc = JsonDocument.Parse(combined);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return [];

            var items = new List<GitHubPagesRepositoryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in doc.RootElement.EnumerateArray())
                TryAddPagesRepository(element, items, seen);
            return items;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static GitHubRepositoryTarget ParseRepositoryUrl(string value)
    {
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = StartsWithKnownHost(value)
                ? $"https://{value}"
                : $"https://github.com/{value.TrimStart('/')}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return Invalid(UnsupportedUrlMessage);

        uri = UpgradeToHttps(uri);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return Invalid(UnsupportedUrlMessage);

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var provider = ProviderFromRepositoryHost(uri.Host);
        if (provider is null)
            return Invalid(UnsupportedUrlMessage);

        // Bitbucket's Source page URL appends /src/<branch>/... to the repository URL.
        // Treat it as the repository the user is viewing instead of rejecting a URL
        // copied directly from the browser address bar.
        if (provider == GitHostingProvider.Bitbucket
            && segments.Length >= 3
            && segments[2].Equals("src", StringComparison.OrdinalIgnoreCase))
        {
            segments = segments[..2];
        }

        var minimumSegments = provider == GitHostingProvider.GitLab ? 2 : 2;
        if (segments.Length < minimumSegments)
            return Invalid("網址必須指向 repository 首頁，不可包含空白或不完整路徑。");
        if (provider != GitHostingProvider.GitLab && segments.Length != 2)
            return Invalid("網址必須指向 repository 首頁，不可包含 issues、settings 等子路徑。");
        if (provider == GitHostingProvider.GitLab && ContainsRepositorySubPath(segments))
            return Invalid("網址必須指向 repository 首頁，不可包含 issues、settings、-/tree 等子路徑。");

        return CreateTarget(
            provider.Value,
            string.Join('/', segments[..^1].Select(Uri.UnescapeDataString)),
            Uri.UnescapeDataString(segments[^1]));
    }

    private static bool TryParsePagesUrl(string value, out GitHubRepositoryTarget target)
    {
        target = Invalid(UnsupportedUrlMessage);
        var candidate = value;
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            var hostPart = candidate.Split('/', 2, StringSplitOptions.TrimEntries)[0];
            if (ProviderFromPagesHost(hostPart) is null)
                return false;
            candidate = "https://" + candidate.TrimStart('/');
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
            return false;

        uri = UpgradeToHttps(uri);
        var provider = ProviderFromPagesHost(uri.Host);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || provider is null)
            return false;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        target = CreateTargetFromPagesUrl(provider.Value, uri.Host, segments.Select(Uri.UnescapeDataString).ToArray());
        return true;
    }

    private static GitHubRepositoryTarget CreateTargetFromPagesUrl(
        GitHostingProvider provider,
        string host,
        IReadOnlyList<string> segments)
    {
        return provider switch
        {
            GitHostingProvider.GitHub => CreateGitHubTargetFromPagesUrl(host, segments),
            GitHostingProvider.GitLab => CreateGitLabTargetFromPagesUrl(host, segments),
            GitHostingProvider.Codeberg => CreateCodebergTargetFromPagesUrl(host, segments),
            GitHostingProvider.Bitbucket => CreateBitbucketTargetFromPagesUrl(host),
            _ => Invalid(UnsupportedUrlMessage)
        };
    }

    private static GitHubRepositoryTarget CreateGitHubTargetFromPagesUrl(string host, IReadOnlyList<string> segments)
    {
        var owner = host[..^".github.io".Length];
        var repository = segments.Count == 0
            ? $"{owner}.github.io"
            : segments[0];
        return CreateTarget(GitHostingProvider.GitHub, owner, repository);
    }

    private static GitHubRepositoryTarget CreateGitLabTargetFromPagesUrl(string host, IReadOnlyList<string> segments)
    {
        var rootNamespace = host[..^".gitlab.io".Length];
        if (segments.Count == 0)
            return CreateTarget(GitHostingProvider.GitLab, rootNamespace, $"{rootNamespace}.gitlab.io");

        var owner = segments.Count == 1
            ? rootNamespace
            : $"{rootNamespace}/{string.Join('/', segments.Take(segments.Count - 1))}";
        return CreateTarget(GitHostingProvider.GitLab, owner, segments[^1]);
    }

    private static GitHubRepositoryTarget CreateCodebergTargetFromPagesUrl(string host, IReadOnlyList<string> segments)
    {
        var owner = host[..^".codeberg.page".Length];
        var repository = segments.Count == 0 ? "pages" : segments[0];
        return CreateTarget(GitHostingProvider.Codeberg, owner, repository);
    }

    private static GitHubRepositoryTarget CreateBitbucketTargetFromPagesUrl(string host)
    {
        var workspace = host[..^".bitbucket.io".Length];
        return CreateTarget(GitHostingProvider.Bitbucket, workspace, $"{workspace}.bitbucket.io");
    }

    private static GitHubRepositoryTarget CreateTarget(
        GitHostingProvider provider,
        string owner,
        string repository)
    {
        if (repository.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repository = repository[..^4];

        if (!IsValidOwner(owner) || !IsValidRepositoryName(repository))
            return Invalid($"{provider.DisplayName()} owner / repository 名稱格式無效。");

        var userSite = IsUserOrOrganizationSite(provider, owner, repository);
        var pagesUrl = BuildPagesUrl(provider, owner, repository, userSite);

        return new GitHubRepositoryTarget
        {
            IsValid = true,
            Provider = provider,
            Owner = owner,
            Repository = repository,
            CanonicalUrl = BuildCanonicalUrl(provider, owner, repository),
            PagesUrl = pagesUrl,
            IsUserOrOrganizationSite = userSite
        };
    }

    private static string BuildCanonicalUrl(GitHostingProvider provider, string owner, string repository) =>
        provider switch
        {
            GitHostingProvider.GitHub => $"https://github.com/{owner}/{repository}.git",
            GitHostingProvider.GitLab => $"https://gitlab.com/{owner}/{repository}.git",
            GitHostingProvider.Codeberg => $"https://codeberg.org/{owner}/{repository}.git",
            GitHostingProvider.Bitbucket => $"https://bitbucket.org/{owner}/{repository}.git",
            _ => string.Empty
        };

    private static bool IsUserOrOrganizationSite(GitHostingProvider provider, string owner, string repository)
    {
        var rootOwner = owner.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
        return provider switch
        {
            GitHostingProvider.GitHub => repository.Equals($"{owner}.github.io", StringComparison.OrdinalIgnoreCase),
            GitHostingProvider.GitLab => repository.Equals($"{rootOwner}.gitlab.io", StringComparison.OrdinalIgnoreCase),
            GitHostingProvider.Codeberg => repository.Equals("pages", StringComparison.OrdinalIgnoreCase),
            GitHostingProvider.Bitbucket => repository.Equals($"{owner}.bitbucket.io", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static string? BuildPagesUrl(
        GitHostingProvider provider,
        string owner,
        string repository,
        bool userSite)
    {
        var ownerSegments = owner.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rootOwner = ownerSegments[0].ToLowerInvariant();
        var ownerPath = ownerSegments.Length <= 1
            ? string.Empty
            : string.Join('/', ownerSegments.Skip(1));

        return provider switch
        {
            GitHostingProvider.GitHub => userSite
                ? $"https://{owner.ToLowerInvariant()}.github.io/"
                : $"https://{owner.ToLowerInvariant()}.github.io/{repository}/",
            GitHostingProvider.GitLab => userSite
                ? $"https://{rootOwner}.gitlab.io/"
                : string.IsNullOrWhiteSpace(ownerPath)
                    ? $"https://{rootOwner}.gitlab.io/{repository}/"
                    : $"https://{rootOwner}.gitlab.io/{ownerPath}/{repository}/",
            GitHostingProvider.Codeberg => userSite
                ? $"https://{owner.ToLowerInvariant()}.codeberg.page/"
                : $"https://{owner.ToLowerInvariant()}.codeberg.page/{repository}/",
            GitHostingProvider.Bitbucket => userSite
                ? $"https://{owner.ToLowerInvariant()}.bitbucket.io/"
                : null,
            _ => null
        };
    }

    private static void TryAddPagesRepository(
        JsonElement element,
        List<GitHubPagesRepositoryItem> items,
        HashSet<string> seen)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;
        if (!element.TryGetProperty("has_pages", out var hasPages) || hasPages.ValueKind != JsonValueKind.True)
            return;

        var fullName = element.TryGetProperty("full_name", out var nameElement) ? nameElement.GetString() : null;
        var htmlUrl = element.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
        if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(htmlUrl))
            return;
        if (!seen.Add(fullName))
            return;

        items.Add(new GitHubPagesRepositoryItem
        {
            NameWithOwner = fullName,
            HtmlUrl = htmlUrl,
            DisplayName = fullName
        });
    }

    private static bool StartsWithKnownHost(string value)
    {
        var host = value.Split('/', 2, StringSplitOptions.TrimEntries)[0];
        return ProviderFromRepositoryHost(host) is not null;
    }

    private static GitHostingProvider? ProviderFromRepositoryHost(string host)
    {
        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return GitHostingProvider.GitHub;
        if (host.Equals("gitlab.com", StringComparison.OrdinalIgnoreCase))
            return GitHostingProvider.GitLab;
        if (host.Equals("codeberg.org", StringComparison.OrdinalIgnoreCase))
            return GitHostingProvider.Codeberg;
        if (host.Equals("bitbucket.org", StringComparison.OrdinalIgnoreCase))
            return GitHostingProvider.Bitbucket;
        return null;
    }

    private static GitHostingProvider? ProviderFromPagesHost(string host)
    {
        if (IsPagesHost(host, ".github.io"))
            return GitHostingProvider.GitHub;
        if (IsPagesHost(host, ".gitlab.io"))
            return GitHostingProvider.GitLab;
        if (IsPagesHost(host, ".codeberg.page"))
            return GitHostingProvider.Codeberg;
        if (IsPagesHost(host, ".bitbucket.io"))
            return GitHostingProvider.Bitbucket;
        return null;
    }

    private static bool IsPagesHost(string host, string suffix) =>
        host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
        && !host.Equals(suffix.TrimStart('.'), StringComparison.OrdinalIgnoreCase)
        && host.Length > suffix.Length;

    private static bool ContainsRepositorySubPath(IReadOnlyList<string> segments) =>
        segments.Any(segment =>
            segment is "-" or "issues" or "merge_requests" or "settings" or "pipelines" or "actions");

    private static Uri UpgradeToHttps(Uri uri)
    {
        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            return uri;

        var builder = new UriBuilder(uri)
        {
            Scheme = Uri.UriSchemeHttps,
            Port = -1
        };
        return builder.Uri;
    }

    private static GitHubRepositoryTarget Invalid(string message) => new()
    {
        IsValid = false,
        ErrorMessage = message
    };

    private const string UnsupportedUrlMessage =
        "支援 GitHub、GitLab、Codeberg、Bitbucket 的 repository 網址，或 github.io / gitlab.io / codeberg.page / bitbucket.io Pages 網址。";

    [GeneratedRegex(@"\]\s*\[")]
    private static partial Regex JsonArrayJoinRegex();

    [GeneratedRegex(@"^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,98}[A-Za-z0-9])?$")]
    private static partial Regex OwnerSegmentRegex();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,100}$")]
    private static partial Regex RepositoryRegex();
}
