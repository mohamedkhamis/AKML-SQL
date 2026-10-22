using System;
using System.Collections.Concurrent;
using AkmlSql.Core;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 043 follow-up (reported 2026-10-22): SSMS's New Query opens on the connection already open, without asking again.
    /// The web edition's new tab sends the connection without its password (the browser never holds it) and names the tab it was
    /// opened from; the password is taken from that tab's engine session when, and only when:
    /// <list type="bullet">
    ///   <item>the source session was connected over the bridge by the same WebSocket connection the request arrives on
    ///   (<see cref="BridgeConnection"/>) — never an SSMS session, never another browser's;</item>
    ///   <item>both name the same server and login, and the request asks for the same encryption and certificate checking (a
    ///   password given to a validated server is never sent to one whose certificate is not checked);</item>
    ///   <item>the request carries no password of its own.</item>
    /// </list>
    /// Nothing else is copied: the database is the request's. The password never leaves the engine.
    /// </summary>
    public static class SessionPasswordCopy
    {
        // session id → the bridge connection that last connected it.
        private static readonly ConcurrentDictionary<string, string> Origins = new(StringComparer.Ordinal);

        /// <summary>Notes that <paramref name="sessionId"/> was connected by the current bridge connection (no-op for the named pipe).</summary>
        public static void NoteConnected(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;
            if (BridgeConnection.Current is { } connection) Origins[sessionId] = connection;
            else Origins.TryRemove(sessionId, out _);
        }

        /// <summary>The session is gone (its tab closed).</summary>
        public static void Forget(string sessionId)
        {
            if (!string.IsNullOrEmpty(sessionId)) Origins.TryRemove(sessionId, out _);
        }

        /// <summary>Whether the current bridge connection connected <paramref name="sessionId"/>.</summary>
        public static bool MayCopyFrom(string? sessionId) =>
            !string.IsNullOrEmpty(sessionId) && BridgeConnection.Current is { } connection
            && Origins.TryGetValue(sessionId!, out var owner) && string.Equals(owner, connection, StringComparison.Ordinal);

        /// <summary>
        /// <paramref name="connectionString"/> with the password of <paramref name="sourceConnectionString"/> when both name the
        /// same server, login and security options of <paramref name="kind"/>; otherwise unchanged. The origin check is the
        /// caller's (<see cref="MayCopyFrom"/>).
        /// </summary>
        public static string Apply(string connectionString, DatabaseKind kind, string? sourceConnectionString, DatabaseKind sourceKind)
        {
            if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(sourceConnectionString) || kind != sourceKind)
                return connectionString;
            try
            {
                return kind == DatabaseKind.PostgreSql
                    ? ApplyPostgres(connectionString, sourceConnectionString!)
                    : ApplySqlServer(connectionString, sourceConnectionString!);
            }
            catch (ArgumentException)
            {
                return connectionString;   // a string that does not parse is opened as it is, and fails with its own error
            }
        }

        private static string ApplySqlServer(string connectionString, string source)
        {
            var target = new SqlConnectionStringBuilder(connectionString);
            var from = new SqlConnectionStringBuilder(source);
            if (target.IntegratedSecurity || from.IntegratedSecurity || !string.IsNullOrEmpty(target.Password) || string.IsNullOrEmpty(from.Password))
                return connectionString;
            if (!string.Equals(target.DataSource?.Trim(), from.DataSource?.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(target.UserID, from.UserID, StringComparison.OrdinalIgnoreCase)
                || !Equals(target.Encrypt, from.Encrypt)
                || target.TrustServerCertificate != from.TrustServerCertificate)
                return connectionString;
            target.Password = from.Password;
            return target.ConnectionString;
        }

        private static string ApplyPostgres(string connectionString, string source)
        {
            var target = new NpgsqlConnectionStringBuilder(connectionString);
            var from = new NpgsqlConnectionStringBuilder(source);
            if (!string.IsNullOrEmpty(target.Password) || string.IsNullOrEmpty(from.Password)) return connectionString;
            if (!string.Equals(target.Host, from.Host, StringComparison.OrdinalIgnoreCase) || target.Port != from.Port
                || !string.Equals(target.Username, from.Username, StringComparison.Ordinal)
                || target.SslMode != from.SslMode
                || !string.Equals(target.RootCertificate ?? string.Empty, from.RootCertificate ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return connectionString;
            target.Password = from.Password;
            return target.ConnectionString;
        }

        /// <summary>
        /// A bridge request for SQL authentication that still has no password after the copy and the remembered-password lookup:
        /// the engine answers "password required" itself instead of sending an empty password to the server (a failed login counts
        /// toward lockout and fills the server's audit).
        /// </summary>
        public static bool IsSqlLoginWithoutPassword(string connectionString)
        {
            if (BridgeSourceIp.Current is null || string.IsNullOrEmpty(connectionString)) return false;
            try
            {
                var b = new SqlConnectionStringBuilder(connectionString);
                return !b.IntegratedSecurity && !string.IsNullOrWhiteSpace(b.UserID) && string.IsNullOrEmpty(b.Password);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
