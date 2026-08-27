using Hugoer.Services;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("PROCESS_RUNNER_HARNESS_SKIPPED");
    return;
}

var output = await ProcessRunner.RunAsync(
    "cmd.exe",
    "/c \"echo stdout-line & echo stderr-line 1>&2\"",
    timeoutMs: 10_000);
Assert(output.Succeeded, output.CombinedOutput);
Assert(output.StdOut.Contains("stdout-line", StringComparison.Ordinal), output.StdOut);
Assert(output.StdErr.Contains("stderr-line", StringComparison.Ordinal), output.StdErr);

var timeout = await ProcessRunner.RunAsync(
    "cmd.exe",
    "/c \"ping -n 6 127.0.0.1 >nul\"",
    timeoutMs: 100);
Assert(!timeout.Succeeded, "a long-running command must time out");
Assert(timeout.StdErr.Contains("timed out", StringComparison.OrdinalIgnoreCase), timeout.StdErr);

using var cancellation = new CancellationTokenSource(100);
var canceled = await ProcessRunner.RunAsync(
    "cmd.exe",
    "/c \"ping -n 6 127.0.0.1 >nul\"",
    timeoutMs: 10_000,
    cancellationToken: cancellation.Token);
Assert(!canceled.Succeeded, "a canceled command must fail");
Assert(canceled.StdErr.Contains("canceled", StringComparison.OrdinalIgnoreCase), canceled.StdErr);

Console.WriteLine("PROCESS_RUNNER_HARNESS_OK");

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}
