using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Hugoer.Models;
using Hugoer.Services;

var sitePath = Path.Combine(Path.GetTempPath(), $"hugoer-deployment-monitor-{Guid.NewGuid():N}");
Directory.CreateDirectory(sitePath);

try
{
    var service = new DeploymentMonitorService();
    var expected = await service.PrepareDeploymentAsync(sitePath);
    var markerPath = Path.Combine(sitePath, "static", DeploymentMonitorService.MarkerFileName);
    Assert(File.Exists(markerPath), "PrepareDeploymentAsync must create the static marker file.");

    using var server = new SingleResponseServer();

    var latestResponse = server.ServeOnceAsync(HttpStatusCode.OK, JsonSerializer.Serialize(expected));
    var latest = await service.CheckAsync(sitePath, server.BaseUrl);
    await latestResponse;
    Assert(latest.State == DeploymentVersionState.Latest,
        $"Expected Latest, received {latest.State}: {latest.Message}");

    var previousMarker = new DeploymentMarker
    {
        DeploymentId = "previous-deployment",
        CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
    };
    var previousResponse = server.ServeOnceAsync(HttpStatusCode.OK, JsonSerializer.Serialize(previousMarker));
    var previous = await service.CheckAsync(sitePath, server.BaseUrl);
    await previousResponse;
    Assert(previous.State == DeploymentVersionState.Previous,
        $"Expected Previous for a mismatched marker, received {previous.State}: {previous.Message}");

    var missingResponse = server.ServeOnceAsync(HttpStatusCode.NotFound, string.Empty);
    var missing = await service.CheckAsync(sitePath, server.BaseUrl);
    await missingResponse;
    Assert(missing.State == DeploymentVersionState.Previous,
        $"Expected Previous for a missing marker, received {missing.State}: {missing.Message}");

    var unauthorized = DeploymentMonitorService.BuildUnavailableMessage(
        HttpStatusCode.Unauthorized,
        "https://group5923835.gitlab.io/fengtusama.gitlab.io/");
    Assert(unauthorized.Contains("GitLab Pages", StringComparison.Ordinal)
           && unauthorized.Contains("Pages access control", StringComparison.Ordinal)
           && unauthorized.Contains("Everyone", StringComparison.Ordinal),
        $"Protected GitLab Pages must explain visibility/access control settings: {unauthorized}");

    var forbidden = DeploymentMonitorService.BuildUnavailableMessage(
        HttpStatusCode.Forbidden,
        "https://group5923835.gitlab.io/fengtusama.gitlab.io/");
    Assert(forbidden.Contains("HTTP 403", StringComparison.Ordinal)
           && forbidden.Contains("公開", StringComparison.Ordinal),
        $"Forbidden GitLab Pages must produce an actionable public-access message: {forbidden}");

    Console.WriteLine("DEPLOYMENT_MONITOR_HARNESS_OK");
}
finally
{
    if (Directory.Exists(sitePath)) Directory.Delete(sitePath, recursive: true);
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class SingleResponseServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public SingleResponseServer()
    {
        _listener.Start();
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        BaseUrl = $"http://127.0.0.1:{endpoint.Port}/";
    }

    public string BaseUrl { get; }

    public async Task ServeOnceAsync(HttpStatusCode statusCode, string body)
    {
        using var client = await _listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        while (await reader.ReadLineAsync() is { Length: > 0 })
        {
        }

        var payload = Encoding.UTF8.GetBytes(body);
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(int)statusCode} {statusCode}\r\n" +
            "Content-Type: application/json; charset=utf-8\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        if (payload.Length > 0) await stream.WriteAsync(payload);
    }

    public void Dispose() => _listener.Stop();
}
