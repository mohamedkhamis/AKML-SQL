using System;
using AkmlSql.Core;
using Microsoft.Data.SqlClient;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 043 follow-up (review): where the password of a web SQL Server connection comes from, decided in one place for
    /// <c>ConnectionChanged</c>, <c>TestSqlConnection</c> and <c>ListDatabases</c> so the three never drift apart. The browser
    /// holds no password but the one typed into it: one it sends is used as it is; otherwise the tab the new one was opened from
    /// gives it (<see cref="SessionPasswordCopy"/>), otherwise the engine's remembered logins do
    /// (<see cref="SqlServerCredentialResolver"/>). A bridge SQL login that is still without one is <see cref="Supplied.Missing"/>:
    /// the caller answers "password required" itself and never sends an empty password to the server (a failed login counts
    /// toward lockout and fills the server's audit). Alias resolution and <see cref="BridgeSqlTargetGuard"/> run before this.
    /// </summary>
    internal static class BridgeSqlPassword
    {
        /// <param name="ConnectionString">The string to open: the request's own when nothing was put into it.</param>
        /// <param name="Typed">The request carried its own password (the one a "remember" may save once the server accepts it).</param>
        /// <param name="Missing">A bridge SQL login with no password from anywhere: never to be sent to the server.</param>
        internal sealed record Supplied(string ConnectionString, bool Typed, bool Missing);

        /// <summary>Supplies the password of a SQL Server <paramref name="connectionString"/> (see the class remarks).</summary>
        /// <param name="copyFromSessionId">The session of the tab the request was opened from, when it names one.</param>
        /// <param name="ctx">The engine's context (null in some tests): the source session is looked up in its sessions.</param>
        public static Supplied Supply(string connectionString, string? copyFromSessionId, RpcContext? ctx) =>
            Supply(connectionString, copyFromSessionId, ctx, BridgeSourceIp.Current is not null);

        internal static Supplied Supply(string connectionString, string? copyFromSessionId, RpcContext? ctx, bool fromBridge)
        {
            if (string.IsNullOrEmpty(connectionString)) return new Supplied(connectionString, false, false);
            SqlConnectionStringBuilder builder;
            try { builder = new SqlConnectionStringBuilder(connectionString); }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
            {
                return new Supplied(connectionString, false, false);   // opened as it is, and fails with its own error
            }

            if (!string.IsNullOrEmpty(builder.Password)) return new Supplied(connectionString, true, false);
            // Neither source serves the named pipe: the SSMS extension sends its own passwords.
            if (!fromBridge) return new Supplied(connectionString, false, false);

            if (!string.IsNullOrEmpty(copyFromSessionId) && ctx?.Sessions.GetSession(copyFromSessionId!) is { } source
                && source.Kind == DatabaseKind.SqlServer && SessionPasswordCopy.MayCopyFrom(source)
                && SessionPasswordCopy.TryCopySqlServer(builder, source.ConnectionString))
                return new Supplied(builder.ConnectionString, false, false);

            if (SqlServerCredentialResolver.TryFill(builder))
                return new Supplied(builder.ConnectionString, false, false);

            var sqlLogin = !builder.IntegratedSecurity && !string.IsNullOrWhiteSpace(builder.UserID);
            return new Supplied(connectionString, false, Missing: sqlLogin);
        }
    }
}
