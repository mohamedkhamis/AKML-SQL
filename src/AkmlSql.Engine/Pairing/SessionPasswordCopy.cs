using System;
using AkmlSql.Core;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Spec 043 follow-up (reported 2026-10-22): SSMS's New Query opens on the connection already open, without asking again.
    /// The web edition's new tab sends the connection without its password (the browser never holds it) and names the tab it was
    /// opened from; the password is taken from that tab's engine session — only when that session is connected to the same
    /// server with the same login, and only into a string that has no password of its own. Nothing else is copied: the database
    /// and the security options are the request's. The password never leaves the engine.
    /// </summary>
    public static class SessionPasswordCopy
    {
        /// <summary>
        /// <paramref name="connectionString"/> with the password of <paramref name="sourceConnectionString"/> when both name the
        /// same server and login of <paramref name="kind"/>; otherwise unchanged.
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
                || !string.Equals(target.UserID, from.UserID, StringComparison.OrdinalIgnoreCase))
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
                || !string.Equals(target.Username, from.Username, StringComparison.Ordinal))
                return connectionString;
            target.Password = from.Password;
            return target.ConnectionString;
        }
    }
}
