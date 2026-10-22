using System;
using AkmlSql.Core.Config;
using Microsoft.Data.SqlClient;
using Serilog;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Reported 2026-10-22 (spec 043 follow-up): the remembered SQL Server password of the web edition, the SQL Server
    /// counterpart of <see cref="AkmlSql.Engine.PostgreSql.PgCredentialResolver"/>. It lives only in the engine's protected store
    /// (<see cref="SqlCredentialStore"/>: DPAPI for the account the engine runs as) under the server key
    /// <c>mssql-web://{data source}</c> and the login. That prefix keeps it apart from the logins the SSMS extension saves in the
    /// same store, so a paired browser never signs in with a password it was not given.
    ///
    /// <para>
    /// Only bridge requests are served (<see cref="BridgeSourceIp.Current"/>): a string from the SSMS extension carries its own
    /// password. A string that arrives without a password gets it filled in; one that arrives with a password and "remember"
    /// saves it once the server has accepted it. It is never returned to a client and never logged.
    /// </para>
    /// </summary>
    public static class SqlServerCredentialResolver
    {
        private const string KeyPrefix = "mssql-web://";

        /// <summary>The store's server key for a data source ("host", "host\instance", "host,port"); case-insensitive.</summary>
        public static string ServerKey(string dataSource) => KeyPrefix + (dataSource ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>
        /// Fills in the remembered password of a bridge request for SQL authentication that has none. Anything else (Windows
        /// authentication, a password already there, a string that does not parse, a request from the named pipe) comes back unchanged.
        /// </summary>
        public static string Resolve(string connectionString) => Resolve(connectionString, BridgeSourceIp.Current is not null);

        internal static string Resolve(string connectionString, bool fromBridge)
        {
            if (!fromBridge || string.IsNullOrEmpty(connectionString)) return connectionString;
            SqlConnectionStringBuilder builder;
            try { builder = new SqlConnectionStringBuilder(connectionString); }
            catch (ArgumentException) { return connectionString; }

            if (builder.IntegratedSecurity || !string.IsNullOrEmpty(builder.Password)
                || string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.UserID))
                return connectionString;

            if (!SqlCredentialStore.TryGet(ServerKey(builder.DataSource), builder.UserID, out var password))
                return connectionString;

            builder.Password = password;
            return builder.ConnectionString;
        }

        /// <summary>Saves the string's password after the server accepted it, when the user asked to remember it (bridge requests only).</summary>
        public static void RememberIfRequested(string connectionString, bool remember)
        {
            if (!remember || BridgeSourceIp.Current is null) return;
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                if (builder.IntegratedSecurity || string.IsNullOrEmpty(builder.Password)
                    || string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.UserID)) return;
                SqlCredentialStore.Save(ServerKey(builder.DataSource), builder.UserID, builder.Password);
                Log.Information("SQL Server password remembered for {Login}@{Server}", builder.UserID, builder.DataSource);
            }
            catch (ArgumentException)
            {
                // Nothing sensible to remember from a string that does not parse.
            }
        }

        public static bool Has(string dataSource, string login) => SqlCredentialStore.Has(ServerKey(dataSource), login);

        /// <summary>Removes the remembered password; true when there was one.</summary>
        public static bool Forget(string dataSource, string login)
        {
            var key = ServerKey(dataSource);
            if (!SqlCredentialStore.Has(key, login)) return false;
            SqlCredentialStore.Remove(key, login);
            Log.Information("SQL Server password forgotten for {Login}@{Server}", login, dataSource);
            return true;
        }
    }
}
