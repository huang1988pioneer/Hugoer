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

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stdout.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                stderr.AppendLine(e.Data);
        };

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

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return new CommandResult
                {
                    ExitCode = -1,
                    StdOut = stdout.ToString(),
                    StdErr = $"Command timed out after {timeoutMs}ms.\n{stderr}"
                };
            }

            // Ensure async readers finish
            await Task.Delay(50, CancellationToken.None).ConfigureAwait(false);

            return new CommandResult
            {
                ExitCode = process.ExitCode,
                StdOut = stdout.ToString().TrimEnd(),
                StdErr = stderr.ToString().TrimEnd()
            };
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
}
