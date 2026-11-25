using System.Data.Common;
using System.Security.Principal;
using AkmlSql.Engine.Pairing;
using Microsoft.Data.SqlClient;

namespace AkmlSql.Engine.Execution;

/// <summary>
/// Owner request of 2026-11-23 ("automatic like SSMS"): where the engine opens a SQL Server connection. A connection with
/// Windows authentication, opened for a browser connection that <see cref="BridgeCaller"/> names, signs in as that browser's
/// Windows user; everything else opens as before — SQL logins, PostgreSQL, the SSMS named pipe and any caller the engine could
/// not place (they sign in as the engine's own account). SqlClient keeps a pool per Windows identity for integrated security,
/// so one user's pooled connections are never handed to another.
/// </summary>
internal static class SqlOpen
{
    public static Task OpenAsync(DbConnection connection, CancellationToken ct)
    {
        var caller = BridgeCaller.Current;
        if (caller == null || !OperatingSystem.IsWindows() || connection is not SqlConnection sql || !UsesWindowsIdentity(sql.ConnectionString))
            return connection.OpenAsync(ct);
        return WindowsIdentity.RunImpersonatedAsync(caller.AccessToken, () => sql.OpenAsync(ct));
    }

    /// <summary>True for a SQL Server connection string that signs in with the Windows identity of whoever opens it.</summary>
    internal static bool UsesWindowsIdentity(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        try { return new SqlConnectionStringBuilder(connectionString).IntegratedSecurity; }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            return false;
        }
    }
}
