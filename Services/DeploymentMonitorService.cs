using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Hugoer.Helpers;
using Hugoer.Models;

namespace Hugoer.Services;

public sealed class DeploymentMonitorService
{
    public const string MarkerFileName = "hugoer-deployment.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly HttpClient Client = CreateHttpClient();

    public async Task<DeploymentMarker> PrepareDeploymentAsync(
        string sitePath,
        CancellationToken cancellationToken = default)
    {
        var marker = new DeploymentMarker
        {
            DeploymentId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..24],
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var staticDirectory = Path.Combine(sitePath, "static");
        Directory.CreateDirectory(staticDirectory);
        var markerPath = Path.Combine(staticDirectory, MarkerFileName);
        var json = JsonSerializer.Serialize(marker, JsonOptions);
        await File.WriteAllTextAsync(
            markerPath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken).ConfigureAwait(false);
        return marker;
    }

    public async Task<DeploymentCheckResult> CheckAsync(
        string sitePath,
        string? pagesUrl,
        CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTimeOffset.Now;
        var expected = await ReadExpectedMarkerAsync(sitePath, cancellationToken).ConfigureAwait(false);
        if (expected is null)
        {
            return new DeploymentCheckResult
            {
                State = DeploymentVersionState.NotConfigured,
                Message = "尚未建立部署版本標記；下次推送後會開始辨識線上版本。",
                CheckedAt = checkedAt
            };
        }

        if (!TryBuildMarkerUri(pagesUrl, expected.DeploymentId, out var markerUri))
        {
            return new DeploymentCheckResult
            {
                State = DeploymentVersionState.NotConfigured,
                Message = "尚未取得有效的 Pages 網址，無法檢查線上版本。",
                ExpectedDeploymentId = expected.DeploymentId,
                CheckedAt = checkedAt
            };
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, markerUri);
            request.Headers.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
            using var response = await Client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Previous(expected, null, checkedAt,
                    "線上網站仍是上一版本；尚未找到最新部署標記。");
            }

            if (PagesAccessStatus.TryCreateProtectedSiteMessage(
                    response.StatusCode,
                    response.Headers.Location,
                    out var protectedSiteMessage))
            {
                return Unavailable(expected, checkedAt, protectedSiteMessage);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Unavailable(expected, checkedAt,
                    BuildUnavailableMessage(response.StatusCode, pagesUrl));
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            DeploymentMarker? live;
            try
            {
                live = JsonSerializer.Deserialize<DeploymentMarker>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return Previous(expected, null, checkedAt,
                    "線上網站仍是上一版本；目前回應尚未包含有效的部署標記。");
            }

            if (live is null || string.IsNullOrWhiteSpace(live.DeploymentId))
            {
                return Previous(expected, null, checkedAt,
                    "線上網站仍是上一版本；目前回應尚未包含有效的部署標記。");
            }

            return string.Equals(live.DeploymentId, expected.DeploymentId, StringComparison.Ordinal)
                ? new DeploymentCheckResult
                {
                    State = DeploymentVersionState.Latest,
                    Message = "線上網站已是最新版本。",
                    ExpectedDeploymentId = expected.DeploymentId,
                    LiveDeploymentId = live.DeploymentId,
                    CheckedAt = checkedAt
                }
                : Previous(expected, live.DeploymentId, checkedAt,
                    "線上網站仍是上一版本；Pages 尚在部署最新內容。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(expected, checkedAt, "檢查線上版本逾時；5 分鐘後會自動重試。");
        }
        catch (HttpRequestException ex)
        {
            return Unavailable(expected, checkedAt,
                $"目前無法連線到網站：{ex.Message}。5 分鐘後會自動重試。");
        }
    }

    private static async Task<DeploymentMarker?> ReadExpectedMarkerAsync(
        string sitePath,
        CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(sitePath, "static", MarkerFileName);
        if (!File.Exists(markerPath)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false);
            var marker = JsonSerializer.Deserialize<DeploymentMarker>(json, JsonOptions);
            return marker is not null && !string.IsNullOrWhiteSpace(marker.DeploymentId) ? marker : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryBuildMarkerUri(string? pagesUrl, string expectedId, out Uri markerUri)
    {
        markerUri = null!;
        if (!Uri.TryCreate(pagesUrl?.Trim(), UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        var root = new Uri(baseUri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
        markerUri = new Uri(root,
            $"{MarkerFileName}?deployment={Uri.EscapeDataString(expectedId)}&checked={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        return true;
    }

    public static string BuildUnavailableMessage(HttpStatusCode statusCode, string? pagesUrl = null)
    {
        if (PagesAccessStatus.TryCreateProtectedSiteMessage(statusCode, location: null, out var protectedSiteMessage)
            && IsGitLabPagesUrl(pagesUrl))
        {
            return
                $"GitLab Pages 已回應 HTTP {(int)statusCode}，代表網站目前需要登入或沒有公開存取權限。" +
                "請到 GitLab 專案的 Settings > General > Visibility, project features, permissions，" +
                "將 Pages access control 設為 Everyone，或把專案/Pages 調整為可公開瀏覽；" +
                "GitLab Pages 快取更新通常需要不到 1 分鐘，之後 Hugoer 會自動重試。";
        }

        if (!string.IsNullOrWhiteSpace(protectedSiteMessage))
            return protectedSiteMessage;

        return $"暫時無法檢查線上版本（HTTP {(int)statusCode}）。5 分鐘後會自動重試。";
    }

    private static bool IsGitLabPagesUrl(string? pagesUrl) =>
        Uri.TryCreate(pagesUrl?.Trim(), UriKind.Absolute, out var uri)
        && uri.Host.EndsWith(".gitlab.io", StringComparison.OrdinalIgnoreCase);

    private static DeploymentCheckResult Previous(
        DeploymentMarker expected,
        string? liveId,
        DateTimeOffset checkedAt,
        string message) => new()
        {
            State = DeploymentVersionState.Previous,
            Message = message,
            ExpectedDeploymentId = expected.DeploymentId,
            LiveDeploymentId = liveId,
            CheckedAt = checkedAt
        };

    private static DeploymentCheckResult Unavailable(
        DeploymentMarker expected,
        DateTimeOffset checkedAt,
        string message) => new()
        {
            State = DeploymentVersionState.Unavailable,
            Message = message,
            ExpectedDeploymentId = expected.DeploymentId,
            CheckedAt = checkedAt
        };

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Hugoer-Deployment-Monitor/1.0");
        return client;
    }
}
