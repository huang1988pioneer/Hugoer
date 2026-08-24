using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Hugoer.Helpers;

public static class TcpListeningPort
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidListener = 3;

    public static bool IsFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port)
            {
                ExclusiveAddressUse = true
            };
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException)
        {
            return false;
        }
    }

    public static int? GetListenerPid(int port)
    {
        if (OperatingSystem.IsWindows())
        {
            var pid = GetListenerPidWindows(port);
            if (pid is > 0) return pid;
        }

        return GetListenerPidViaNetstat(port);
    }

    public static bool TryKillListener(int port, params string[] allowedProcessNames)
    {
        var pid = GetListenerPid(port);
        if (pid is null or <= 0 or 4)
            return false;

        try
        {
            var process = Process.GetProcessById(pid.Value);
            var name = process.ProcessName;
            if (!IsAllowedProcess(name, allowedProcessNames))
                return false;

            process.Kill(entireProcessTree: true);
            process.WaitForExit(3000);
            return true;
        }
        catch (Exception ex) when (
            ex is ArgumentException
            or InvalidOperationException
            or Win32Exception
            or NotSupportedException)
        {
            return false;
        }
    }

    public static async Task<int?> AllocateAsync(
        int preferredPort,
        int count,
        CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            var port = preferredPort + i;
            if (await TryPrepareAsync(port, cancellationToken).ConfigureAwait(false))
                return port;
        }

        return null;
    }

    public static async Task<bool> TryPrepareAsync(int port, CancellationToken cancellationToken = default)
    {
        if (IsFree(port))
            return true;

        if (!TryKillListener(port, "hugo"))
            return false;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            if (IsFree(port))
                return true;
        }

        return false;
    }

    private static bool IsAllowedProcess(string processName, string[] allowedProcessNames)
    {
        foreach (var allowed in allowedProcessNames)
        {
            var expected = allowed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? allowed[..^4]
                : allowed;
            if (processName.Equals(expected, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    private static int? GetListenerPidWindows(int port)
    {
        var size = 0;
        _ = GetExtendedTcpTable(IntPtr.Zero, ref size, true, AfInet, TcpTableOwnerPidListener, 0);
        if (size <= 0) return null;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var result = GetExtendedTcpTable(buffer, ref size, true, AfInet, TcpTableOwnerPidListener, 0);
            if (result != 0) return null;

            var count = Marshal.ReadInt32(buffer);
            if (count is < 0 or > 100_000)
                return null;

            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var loopback = BitConverter.ToUInt32(IPAddress.Loopback.GetAddressBytes(), 0);

            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(buffer + 4 + (i * rowSize));
                if (PortFromNetworkOrder(row.LocalPort) != port)
                    continue;
                if (row.LocalAddr != 0 && row.LocalAddr != loopback)
                    continue;
                if (row.OwningPid == 0) continue;
                return (int)row.OwningPid;
            }

            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int? GetListenerPidViaNetstat(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano -p TCP",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return null;
            }

            foreach (var raw in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;
                if (!parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase)) continue;
                if (!LocalAddressMatchesPort(parts[1], port)) continue;
                if (parts.Length >= 5 && !IsListeningState(parts[3])) continue;
                if (int.TryParse(parts[^1], out var pid) && pid > 0)
                    return pid;
            }
        }
        catch (Exception ex) when (
            ex is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }

        return null;
    }

    private static bool LocalAddressMatchesPort(string address, int port)
    {
        var sep = address.LastIndexOf(':');
        if (sep < 0) return false;
        return int.TryParse(address[(sep + 1)..], out var localPort) && localPort == port;
    }

    private static bool IsListeningState(string state) =>
        state.Contains("LISTEN", StringComparison.OrdinalIgnoreCase)
        || state.Contains("監聽", StringComparison.Ordinal)
        || state.Contains("侦听", StringComparison.Ordinal);

    private static int PortFromNetworkOrder(uint networkPort) =>
        (int)unchecked((ushort)IPAddress.NetworkToHostOrder((short)networkPort));

    [StructLayout(LayoutKind.Sequential)]
    private struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(
        IntPtr tcpTable,
        ref int tcpTableLength,
        bool sort,
        int ipVersion,
        int tableClass,
        uint reserved);
}
