using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Hugoer.Helpers;

var missingUnsafe = """
theme = "Stack"
title = "My New Hugo Project"
""";
var enabled = GoldmarkUnsafeHtml.EnsureEnabled(missingUnsafe, out var enabledChanged);
Assert(enabledChanged, "Missing goldmark unsafe must be added.");
Assert(GoldmarkUnsafeHtml.IsEnabled(enabled), "Appended config must enable raw HTML.");
Assert(enabled.Contains("[markup.goldmark.renderer]", StringComparison.Ordinal),
    "Renderer table must be created.");
var again = GoldmarkUnsafeHtml.EnsureEnabled(enabled, out var againChanged);
Assert(!againChanged, "Already-enabled config must be left alone.");
Assert(again == enabled, "Idempotent ensure must not rewrite the file.");

var disabled = """
[markup.goldmark.renderer]
hardWraps = false
unsafe = false
""";
var flipped = GoldmarkUnsafeHtml.EnsureEnabled(disabled, out var flippedChanged);
Assert(flippedChanged, "unsafe = false must be flipped.");
Assert(GoldmarkUnsafeHtml.IsEnabled(flipped), "Flipped config must be enabled.");
Assert(flipped.Contains("hardWraps = false", StringComparison.Ordinal),
    "Existing renderer keys must be kept.");
Assert(!flipped.Contains("unsafe = false", StringComparison.OrdinalIgnoreCase),
    "The false value must not remain.");

var tableOnly = """
[params]
description = "blog"

[markup.goldmark.renderer]
hardWraps = false
""";
var inserted = GoldmarkUnsafeHtml.EnsureEnabled(tableOnly, out var insertedChanged);
Assert(insertedChanged, "Renderer table without unsafe must get the key.");
Assert(inserted.Contains("[markup.goldmark.renderer]", StringComparison.Ordinal),
    "Existing renderer table must be reused.");
Assert(Regex.IsMatch(inserted, @"(?im)\[markup\.goldmark\.renderer\]\s*unsafe = true"),
    "unsafe must be inserted into the existing renderer table.");
Assert(inserted.Contains("hardWraps = false", StringComparison.Ordinal),
    "Other renderer keys must remain.");

var otherUnsafe = """
[privacy.x]
enableDNT = true

[markup.highlight]
noClasses = true
""";
var other = GoldmarkUnsafeHtml.EnsureEnabled(otherUnsafe, out var otherChanged);
Assert(otherChanged, "unsafe in another table must not count.");
Assert(GoldmarkUnsafeHtml.IsEnabled(other), "Markup renderer unsafe must still be added.");
Assert(other.Contains("[privacy.x]", StringComparison.Ordinal), "Unrelated tables must remain.");


var helpDump = """
-F, --buildFuture            include content with publishdate in the future
      --cacheDir string        filesystem path to cache directory
      --cleanDestinationDir    remove files from destination not found in static directories
-c, --contentDir string      filesystem path to content directory
-p, --port int               port on which the server will listen (default 1313)
-w, --watch                  watch filesystem for changes and recreate as needed (default true)
Global Flags:
      --clock string           set the clock used by Hugo
      --config string          config file (default is hugo.yaml|json|toml)
Use "hugo server [command] --help" for more information about a command.
ERROR command error: server startup failed: listen tcp 127.0.0.1:1313: bind: Only one usage of each socket address (protocol/network address/port) is normally permitted.
""";

Assert(HugoServerOutput.LooksLikePortInUse(helpDump),
    "Windows bind errors must be detected as a busy port.");
var summary = HugoServerOutput.Summarize(helpDump);
Assert(summary.Contains("ERROR command error", StringComparison.Ordinal),
    "The real Hugo error must be kept.");
Assert(!summary.Contains("--buildFuture", StringComparison.Ordinal),
    "CLI flag help must not leak into the summarized log.");
Assert(!summary.Contains("Global Flags", StringComparison.Ordinal),
    "Usage sections must not leak into the summarized log.");
Assert(HugoServerOutput.Summarize("").Length == 0, "Empty output stays empty.");

var templateError = """
ERROR failed to load config: failed to unmarshal config
WARN  this is fine
Web Server is available at http://127.0.0.1:1313/
""";
var templateSummary = HugoServerOutput.Summarize(templateError);
Assert(templateSummary.Contains("failed to load config", StringComparison.Ordinal),
    "Config errors must be kept.");
Assert(templateSummary.Contains("WARN", StringComparison.Ordinal),
    "Warnings must be kept.");
Assert(!templateSummary.Contains("Web Server is available", StringComparison.Ordinal),
    "Informational lines drop when errors are present.");

using (var occupied = BindLoopback())
{
    var port = ((IPEndPoint)occupied.LocalEndpoint).Port;
    Assert(!TcpListeningPort.IsFree(port), "A bound loopback port must not look free.");

    var pid = WaitForListenerPid(port);
    Assert(pid == Environment.ProcessId,
        $"Listener PID should be this process ({Environment.ProcessId}), got {pid?.ToString() ?? "null"}.");

    Assert(!TcpListeningPort.TryKillListener(port, "hugo"),
        "TryKillListener must not kill a non-hugo occupant.");
    Assert(!TcpListeningPort.IsFree(port),
        "A non-hugo occupant must still hold the port.");

    var allocated = TcpListeningPort.AllocateAsync(port, 3).GetAwaiter().GetResult();
    Assert(allocated == port + 1 || (allocated is int next && next > port),
        $"Allocate must skip a busy non-hugo port; got {allocated?.ToString() ?? "null"}.");
    if (allocated is int chosen)
        Assert(TcpListeningPort.IsFree(chosen), "The allocated port must be bindable.");
}

Console.WriteLine("HUGO_PREVIEW_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static TcpListener BindLoopback()
{
    var listener = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
    listener.Start();
    return listener;
}

static int? WaitForListenerPid(int port)
{
    for (var attempt = 0; attempt < 10; attempt++)
    {
        var pid = TcpListeningPort.GetListenerPid(port);
        if (pid is not null)
            return pid;
        Thread.Sleep(50);
    }

    return TcpListeningPort.GetListenerPid(port);
}
