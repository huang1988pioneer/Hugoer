using System.Diagnostics;
using System.Text;
using Hugoer.Models;

namespace Hugoer.Services;

public static class ProcessRunner
{
    public static async Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        int timeoutMs = 120_000,
        CancellationToken cancellationToken = default,
        IDictionary<string, string?>? env = null)
    {
        if (timeoutMs <= 0)
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "Command timeout must be positive."
            };
        }

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (env is not null)
        {
            foreach (var (k, v) in env)
            {
                if (v is null)
                    psi.Environment.Remove(k);
                else
                    psi.Environment[k] = v;
            }
        }

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start())
            {
                return new CommandResult
                {
                    ExitCode = -1,
                    StdErr = $"Failed to start process: {fileName}"
                };
            }

            // Read both streams concurrently. Event based readers require an arbitrary
            // delay after the process exits and can lose the final output lines, which
            // makes Hugo/Git failures especially difficult to diagnose.
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
                var output = await ReadOutputAsync(stdoutTask, stderrTask).ConfigureAwait(false);
                return new CommandResult
                {
                    ExitCode = process.ExitCode,
                    StdOut = output.StdOut,
                    StdErr = output.StdErr
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                var output = await ReadOutputAsync(stdoutTask, stderrTask).ConfigureAwait(false);
                return new CommandResult
                {
                    ExitCode = -1,
                    StdOut = output.StdOut,
                    StdErr = $"Command timed out after {timeoutMs}ms.\n{output.StdErr}".TrimEnd()
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                var output = await ReadOutputAsync(stdoutTask, stderrTask).ConfigureAwait(false);
                return new CommandResult
                {
                    ExitCode = -1,
                    StdOut = output.StdOut,
                    StdErr = $"Command canceled.\n{output.StdErr}".TrimEnd()
                };
            }
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = ex.Message
            };
        }
    }

    public static async Task<CommandResult> RunShellAsync(
        string command,
        string? workingDirectory = null,
        int timeoutMs = 120_000,
        CancellationToken cancellationToken = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return await RunAsync(
                "cmd.exe",
                $"/c {command}",
                workingDirectory,
                timeoutMs,
                cancellationToken).ConfigureAwait(false);
        }

        return await RunAsync(
            "/bin/bash",
            $"-lc \"{command.Replace("\"", "\\\"")}\"",
            workingDirectory,
            timeoutMs,
            cancellationToken).ConfigureAwait(false);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // The process may have exited between HasExited and Kill. The original
            // command result is more useful to callers than a cleanup exception.
        }
    }

    private static async Task<(string StdOut, string StdErr)> ReadOutputAsync(
        Task<string> stdoutTask,
        Task<string> stderrTask)
    {
        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch
        {
            // Preserve any stream that was successfully drained. Process start or
            // stream failures are reported by the outer command result.
        }

        var stdout = stdoutTask.Status == TaskStatus.RanToCompletion ? stdoutTask.Result : string.Empty;
        var stderr = stderrTask.Status == TaskStatus.RanToCompletion ? stderrTask.Result : string.Empty;
        return (stdout.TrimEnd(), stderr.TrimEnd());
    }
}
