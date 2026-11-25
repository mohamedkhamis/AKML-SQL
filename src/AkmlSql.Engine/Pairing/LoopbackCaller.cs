using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace AkmlSql.Engine.Pairing;

/// <summary>
/// Owner request of 2026-11-23 ("automatic like SSMS"): who a browser on this computer is, so its Windows authentication
/// signs in to SQL Server as that person, as SSMS does — not as the engine service's account (NT AUTHORITY\SYSTEM).
/// <para>
/// A WebSocket from a loopback address is a TCP connection between two processes of this machine, and Windows' TCP table
/// names the process that owns the browser's end. The token used is the one of the user signed in to that process's session
/// (<c>WTSQueryUserToken</c>: a full token, not the browser's sandboxed one), after checking the process runs as that same
/// user. Anything that does not fit gives no identity, so the engine's own account is used, as before: not Windows, not a
/// loopback connection, no row, a session with no signed-in user (session 0 services), a process running as another user,
/// or an engine without the right to read session tokens — a Debug engine started by hand, which signs in as its own user,
/// the same person as the browser's.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class LoopbackCaller
{
    /// <summary>The outcome: the identity (or null for the engine's account), the browser's process (0 when unknown), and why.</summary>
    internal sealed record Resolution(WindowsIdentity? Identity, int ProcessId, string Reason);

    /// <summary>One row of the TCP table: a connection's two endpoints as one process sees them, and that process.</summary>
    internal readonly record struct TcpRow(IPEndPoint Local, IPEndPoint Remote, int ProcessId);

    /// <summary>Who the browser at <paramref name="client"/>, connected to the engine at <paramref name="server"/>, signs in as.</summary>
    public static Resolution Resolve(IPEndPoint? client, IPEndPoint? server)
    {
        if (client == null || server == null) return new(null, 0, "the connection's endpoints are unknown");
        var clientAddress = Normalize(client.Address);
        if (!IPAddress.IsLoopback(clientAddress)) return new(null, 0, "the browser is on another computer");

        List<TcpRow> rows;
        try { rows = ReadTcpTable(clientAddress.AddressFamily); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or OutOfMemoryException)
        {
            return new(null, 0, "the TCP table could not be read: " + ex.Message);
        }

        var pid = MatchOwningProcess(rows, new IPEndPoint(clientAddress, client.Port), new IPEndPoint(Normalize(server.Address), server.Port));
        if (pid <= 0) return new(null, 0, "no process owns the browser's end of the connection");
        if (!ProcessIdToSessionId((uint)pid, out var session)) return new(null, pid, $"no session for process {pid}");
        var processUser = ProcessUser(pid);
        if (processUser == null) return new(null, pid, $"the user of process {pid} could not be read");

        if (!WTSQueryUserToken(session, out var token))
        {
            var error = Marshal.GetLastWin32Error();
            using var self = WindowsIdentity.GetCurrent();
            return new(null, pid, processUser.Equals(self.User)
                ? "the browser runs as the engine's own user"
                : $"no signed-in user token for session {session} (error {error})");
        }

        try
        {
            var identity = new WindowsIdentity(token);   // duplicates the token; ours is closed below
            if (!processUser.Equals(identity.User))
            {
                identity.Dispose();
                return new(null, pid, $"process {pid} runs as another user than the one signed in to session {session}");
            }
            return new(identity, pid, "ok");
        }
        finally
        {
            CloseHandle(token);
        }
    }

    /// <summary>
    /// The process that owns the browser's end of the connection: the row whose local endpoint is the client's and whose
    /// remote endpoint is the engine's (the engine's own row is the reverse, and is not it). 0 when there is none.
    /// </summary>
    internal static int MatchOwningProcess(IEnumerable<TcpRow> rows, IPEndPoint client, IPEndPoint server)
    {
        var clientAddress = Normalize(client.Address);
        var serverAddress = Normalize(server.Address);
        foreach (var row in rows)
        {
            if (row.Local.Port == client.Port && row.Remote.Port == server.Port
                && Normalize(row.Local.Address).Equals(clientAddress)
                && Normalize(row.Remote.Address).Equals(serverAddress))
                return row.ProcessId;
        }
        return 0;
    }

    private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

    // ── the TCP table ─────────────────────────────────────────────────────────────────────────────────────────────────

    private const int AfInet = 2;
    private const int AfInet6 = 23;
    private const int TcpTableOwnerPidConnections = 4;
    private const uint ErrorInsufficientBuffer = 122;

    private static List<TcpRow> ReadTcpTable(AddressFamily family)
    {
        var af = family == AddressFamily.InterNetworkV6 ? AfInet6 : AfInet;
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, TcpTableOwnerPidConnections, 0);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var buffer = Marshal.AllocHGlobal(Math.Max(size, 4));
            try
            {
                var result = GetExtendedTcpTable(buffer, ref size, false, af, TcpTableOwnerPidConnections, 0);
                if (result == ErrorInsufficientBuffer) continue;   // connections opened meanwhile: size now holds the new need
                if (result != 0) throw new Win32Exception((int)result);
                return af == AfInet ? ReadV4(buffer) : ReadV6(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        throw new InvalidOperationException("the TCP table kept growing while it was read");
    }

    // MIB_TCPTABLE_OWNER_PID: a DWORD count, then MIB_TCPROW_OWNER_PID rows of 24 bytes
    // (state, local address, local port, remote address, remote port, owning pid).
    private static List<TcpRow> ReadV4(IntPtr table)
    {
        var count = Marshal.ReadInt32(table);
        var rows = new List<TcpRow>(count);
        for (var i = 0; i < count; i++)
        {
            var row = table + 4 + i * 24;
            var local = new IPAddress((uint)Marshal.ReadInt32(row, 4));
            var localPort = Port(Marshal.ReadInt32(row, 8));
            var remote = new IPAddress((uint)Marshal.ReadInt32(row, 12));
            var remotePort = Port(Marshal.ReadInt32(row, 16));
            rows.Add(new TcpRow(new IPEndPoint(local, localPort), new IPEndPoint(remote, remotePort), Marshal.ReadInt32(row, 20)));
        }
        return rows;
    }

    // MIB_TCP6TABLE_OWNER_PID: a DWORD count, then MIB_TCP6ROW_OWNER_PID rows of 56 bytes (local address [16], local scope,
    // local port, remote address [16], remote scope, remote port, state, owning pid).
    private static List<TcpRow> ReadV6(IntPtr table)
    {
        var count = Marshal.ReadInt32(table);
        var rows = new List<TcpRow>(count);
        var local = new byte[16];
        var remote = new byte[16];
        for (var i = 0; i < count; i++)
        {
            var row = table + 4 + i * 56;
            Marshal.Copy(row, local, 0, 16);
            var localScope = (uint)Marshal.ReadInt32(row, 16);
            var localPort = Port(Marshal.ReadInt32(row, 20));
            Marshal.Copy(row + 24, remote, 0, 16);
            var remoteScope = (uint)Marshal.ReadInt32(row, 40);
            var remotePort = Port(Marshal.ReadInt32(row, 44));
            rows.Add(new TcpRow(
                new IPEndPoint(new IPAddress(local, localScope), localPort),
                new IPEndPoint(new IPAddress(remote, remoteScope), remotePort),
                Marshal.ReadInt32(row, 52)));
        }
        return rows;
    }

    /// <summary>A port as the TCP table stores it: network byte order in the low 16 bits.</summary>
    internal static int Port(int stored) => ((stored & 0xFF) << 8) | ((stored >> 8) & 0xFF);

    // ── the process's user ────────────────────────────────────────────────────────────────────────────────────────────

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenUserClass = 1;

    private static SecurityIdentifier? ProcessUser(int pid)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out var token)) return null;
            try
            {
                GetTokenInformation(token, TokenUserClass, IntPtr.Zero, 0, out var needed);
                if (needed <= 0) return null;
                var buffer = Marshal.AllocHGlobal(needed);
                try
                {
                    if (!GetTokenInformation(token, TokenUserClass, buffer, needed, out _)) return null;
                    return new SecurityIdentifier(Marshal.ReadIntPtr(buffer));   // TOKEN_USER.User.Sid
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int addressFamily, int tableClass, uint reserved);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, IntPtr information, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
