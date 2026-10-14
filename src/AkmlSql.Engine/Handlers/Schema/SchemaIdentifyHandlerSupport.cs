using System;

namespace AkmlSql.Engine.Handlers.Schema
{
    /// <summary>
    /// Spec 021 (web edition) -- M5 task T104. Helpers for resolving a session's canonical
    /// server identity without standing up a live SQL connection. The eventual production
    /// path runs <c>SELECT @@SERVERNAME</c> against the open session; this helper provides
    /// the placeholder used until that infrastructure lands.
    /// </summary>
    public static class SchemaIdentifyHandlerSupport
    {
        /// <summary>
        /// Extracts the <c>Data Source</c> / <c>Server</c> token from a SqlClient-style
        /// connection string. Returns an empty string when neither key is present so the
        /// caller can treat it as "no identity" rather than "unknown".
        /// </summary>
        public static string ParseServerFromConnectionString(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return string.Empty;

            var parts = connectionString!.Split(';');
            foreach (var rawPart in parts)
            {
                var part = rawPart.Trim();
                if (part.Length == 0) continue;
                var eq = part.IndexOf('=');
                if (eq <= 0) continue;

                var key = part.Substring(0, eq).Trim();
                var value = part.Substring(eq + 1).Trim();
                if (IsServerKey(key) && value.Length > 0)
                {
                    return value;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Spec 042: the canonical identity of a PostgreSQL server, <c>pg://host:port</c> (host lower-cased, port 5432 when the
        /// string has none), or an empty string when the string has no host. The scheme keeps it distinct from every SQL Server
        /// identity in the browser's schema cache and the engine's.
        /// </summary>
        public static string PostgresIdentity(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return string.Empty;
            try
            {
                var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
                return string.IsNullOrWhiteSpace(builder.Host)
                    ? string.Empty
                    : AkmlSql.Core.Connections.PgConnectionString.ServerKey(builder.Host, builder.Port);
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private static bool IsServerKey(string key) =>
            key.Equals("Data Source", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Server", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Address", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Addr", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("Network Address", StringComparison.OrdinalIgnoreCase);
    }
}
