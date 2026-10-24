using System;
using AkmlSql.Core;
using AkmlSql.Engine.Server;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 043 follow-up (reported 2026-10-22): SSMS's New Query opens on the connection already open, without asking again.
    /// The web edition's new tab sends the connection without its password (the browser never holds it) and names the tab it was
    /// opened from; the password is taken from that tab's engine session when, and only when:
    /// <list type="bullet">
    ///   <item>the source session is connected, and was connected over the bridge by the same web page the request comes from
    ///   (<see cref="SessionState.BridgeOrigin"/>, <see cref="BridgeConnection"/>; the same page after a reconnect too) — never an
    ///   SSMS session, never another page's or another browser's;</item>
    ///   <item>for SQL Server: the request is a plain SQL login (<see cref="BridgeSqlSecurity.IsPlainSqlLogin"/>: encrypted, SQL
    ///   authentication, no failover partner, certificate host name, certificate file or SPN of its own) and names the same
    ///   server, login, encryption, certificate checking and authentication as the source (<see cref="BridgeSqlSecurity.SameSecurity"/>),
    ///   so a password given to a validated server is never sent to one whose certificate is not checked, or anywhere else;</item>
    ///   <item>for PostgreSQL: the same host, port, user, TLS mode and root certificate;</item>
    ///   <item>the request carries no password of its own.</item>
    /// </list>
    /// Nothing else is copied: the database is the request's. The password never leaves the engine.
    /// </summary>
    public static class SessionPasswordCopy
    {
        /// <summary>Notes that <paramref name="session"/> was connected by the current bridge page (null over the named pipe).</summary>
        public static void NoteConnected(SessionState? session)
        {
            if (session != null) session.BridgeOrigin = BridgeConnection.Current;
        }

        /// <summary>Whether a request from the current bridge page may take the password of <paramref name="source"/>.</summary>
        public static bool MayCopyFrom(SessionState? source) =>
            source is { IsConnected: true, BridgeOrigin: { } origin }
            && BridgeConnection.Current is { } current
            && string.Equals(origin, current, StringComparison.Ordinal);

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
                if (kind == DatabaseKind.PostgreSql) return ApplyPostgres(connectionString, sourceConnectionString!);
                var target = new SqlConnectionStringBuilder(connectionString);
                return TryCopySqlServer(target, sourceConnectionString!) ? target.ConnectionString : connectionString;
            }
            catch (ArgumentException)
            {
                return connectionString;   // a string that does not parse is opened as it is, and fails with its own error
            }
        }

        /// <summary>
        /// Puts the password of <paramref name="source"/> into <paramref name="target"/> when the SQL Server rules above allow it;
        /// true when it did. <paramref name="target"/> is left untouched otherwise.
        /// </summary>
        internal static bool TryCopySqlServer(SqlConnectionStringBuilder target, string source)
        {
            if (!string.IsNullOrEmpty(target.Password) || !BridgeSqlSecurity.IsPlainSqlLogin(target)) return false;
            SqlConnectionStringBuilder from;
            try { from = new SqlConnectionStringBuilder(source); }
            catch (ArgumentException) { return false; }
            if (from.IntegratedSecurity || string.IsNullOrEmpty(from.Password) || !BridgeSqlSecurity.SameSecurity(target, from))
                return false;
            target.Password = from.Password;
            return true;
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
    }
}
