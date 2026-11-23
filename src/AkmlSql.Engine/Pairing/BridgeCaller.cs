using System.Security.Principal;
using System.Threading;

namespace AkmlSql.Engine.Pairing;

/// <summary>
/// Owner request of 2026-11-23 ("automatic like SSMS"): the Windows user a browser connection signs in to SQL Server as,
/// carried as a per-connection ambient like <see cref="BridgeSourceIp"/>. <c>WebSocketTransport</c> sets it for a browser on
/// this computer (<see cref="LoopbackCaller"/>); <see cref="Execution.SqlOpen"/> opens every Windows-authenticated SQL Server
/// connection of that flow as this user, and the handshake names it. Null — the SSMS named pipe, a browser on another
/// computer, a caller that could not be placed, or <c>bridge.windowsSignInAsBrowserUser</c> off — signs in as the engine's
/// own account, as before.
/// </summary>
internal static class BridgeCaller
{
    private static readonly AsyncLocal<WindowsIdentity?> Ambient = new();

    /// <summary>The Windows user of the connection being served, or null for the engine's own account.</summary>
    public static WindowsIdentity? Current => Ambient.Value;

    /// <summary>Sets the user for the enclosing async flow; disposing the scope restores the previous value.</summary>
    public static System.IDisposable Set(WindowsIdentity? identity)
    {
        var previous = Ambient.Value;
        Ambient.Value = identity;
        return new Scope(previous);
    }

    private sealed class Scope(WindowsIdentity? previous) : System.IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Ambient.Value = previous;
        }
    }
}
