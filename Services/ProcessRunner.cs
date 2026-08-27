using System.Diagnostics;
using System.Text;
using Hugoer.Models;

namespace Hugoer.Services;

public static class ProcessRunner
{
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(2);

    public static async Task<CommandResult> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory = null,
        int timeoutMs = 120_000,
        CancellationToken cancellationToken = default,
        IDictionary<string, string?>? env = null)
        => await RunCoreAsync(
            fileName,
            arguments,
            argumentList: null,
            workingDirectory: workingDirectory,
            timeoutMs: timeoutMs,
            cancellationToken: cancellationToken,
            env: env).ConfigureAwait(false);

    /// <summary>
    /// Runs a process with structured arguments. Using <see cref="ProcessStartInfo.ArgumentList"/>
    /// avoids platform-specific quoting bugs and prevents values supplied by a user
    /// (for example a commit message or repository path) from being interpreted as
    /// additional command-line switches.
    /// </summary>
    public static async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        int timeoutMs = 120_000,
        CancellationToken cancellationToken = default,
        IDictionary<string, string?>? env = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return await RunCoreAsync(
            fileName,
            argumentsText: null,
            argumentList: arguments,
            workingDirectory: workingDirectory,
            timeoutMs: timeoutMs,
            cancellationToken: cancellationToken,
            env: env).ConfigureAwait(false);
    }

    private static async Task<CommandResult> RunCoreAsync(
        string fileName,
        string? argumentsText,
        IEnumerable<string>? argumentList,
        string? workingDirectory,
        int timeoutMs,
        CancellationToken cancellationToken,
        IDictionary<string, string?>? env)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return new CommandResult
            {
                ExitCode = -1,
                StdErr = "Command file name must not be empty."
            };
        }

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
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (argumentList is null)
            psi.Arguments = argumentsText ?? string.Empty;
        else
        {
            foreach (var argument in argumentList)
                psi.ArgumentList.Add(argument ?? string.Empty);
        }

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
                var output = await ReadOutputAsync(stdoutTask, stderrTask, OutputDrainTimeout).ConfigureAwait(false);
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
                var output = await ReadOutputAsync(stdoutTask, stderrTask, OutputDrainTimeout).ConfigureAwait(false);
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
                var output = await ReadOutputAsync(stdoutTask, stderrTask, OutputDrainTimeout).ConfigureAwait(false);
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
        Task<string> stderrTask,
        TimeSpan drainTimeout)
    {
        var allOutput = Task.WhenAll(stdoutTask, stderrTask);
        try
        {
            // A killed process normally closes both pipes immediately. A child
            // process can nevertheless inherit a handle and keep one pipe open;
            // never let timeout/cancellation handling wait forever in that case.
            await allOutput.WaitAsync(drainTimeout).ConfigureAwait(false);
        }
        catch
        {
            // Preserve any stream that was successfully drained. Process start or
            // stream failures are reported by the outer command result. Observe a
            // later fault as well so a detached pipe cannot create an unobserved
            // task exception after this method returns.
            _ = allOutput.Exception;
        }

        var stdout = stdoutTask.Status == TaskStatus.RanToCompletion ? stdoutTask.Result : string.Empty;
        var stderr = stderrTask.Status == TaskStatus.RanToCompletion ? stderrTask.Result : string.Empty;
        return (stdout.TrimEnd(), stderr.TrimEnd());
    }
}
