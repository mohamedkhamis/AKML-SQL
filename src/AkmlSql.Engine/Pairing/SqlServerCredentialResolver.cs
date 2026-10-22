using System;
using AkmlSql.Core.Config;
using Microsoft.Data.SqlClient;
using Serilog;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Reported 2026-10-22 (spec 043 follow-up): the remembered SQL Server password of the web edition, the SQL Server
    /// counterpart of <see cref="AkmlSql.Engine.PostgreSql.PgCredentialResolver"/>. It lives only in the engine's protected store
    /// (<see cref="SqlCredentialStore"/>: DPAPI for the account the engine runs as) under a server key and the login. The
    /// <c>mssql-web</c> prefixes keep it apart from the logins the SSMS extension saves in the same store, so a paired browser
    /// never signs in with a password it was not given.
    ///
    /// <para>
    /// Spec 043 follow-up (review): the password is bound to the certificate checking it was given with. One saved from a
    /// connection that validates the server certificate is under <c>mssql-web://{data source}</c>; one saved with
    /// <c>TrustServerCertificate=true</c> is under <c>mssql-web-trust://{data source}</c>. A request that trusts any certificate
    /// gets only a password that was already sent that way; a validating request may get either (stricter is fine). One password
    /// is kept per server and login: saving under one key removes the other in the same store write.
    /// </para>
    ///
    /// <para>
    /// The released builds before the review (1.26.1022.0927, 1.26.1022.1042) wrote EVERY web password under
    /// <c>mssql-web://</c>, whatever <c>TrustServerCertificate</c> said — and the web client always trusts the certificate of a
    /// server on the engine's own machine. So a trusting request for a server on the engine's machine also reads the
    /// <c>mssql-web://</c> entry when its own key has none (certificate checking on a loopback hop protects nothing). No other
    /// trusting request does, and nothing is rewritten on a read: the next remember moves it to its own key.
    /// </para>
    ///
    /// <para>
    /// Only bridge requests are served (<see cref="BridgeSourceIp.Current"/>): a string from the SSMS extension carries its own
    /// password. Only a plain SQL login is served (<see cref="BridgeSqlSecurity.IsPlainSqlLogin"/>): a request that asks for no
    /// encryption, another certificate host name or file, a failover partner, an SPN or another authentication method gets
    /// nothing. A string that arrives without a password gets it filled in; one that arrives with a password and "remember"
    /// saves it once the server has accepted it. It is never returned to a client and never logged.
    /// </para>
    /// </summary>
    public static class SqlServerCredentialResolver
    {
        private const string ValidatedPrefix = "mssql-web://";
        private const string TrustPrefix = "mssql-web-trust://";

        /// <summary>
        /// Test seam: what writes an entry and removes the same login's entries under the other keys in one write (true when the
        /// store file was written). A test makes it fail or throw to check that an accepted login is never reported as a failure
        /// because of the store.
        /// </summary>
        internal static Func<string, string, string, string[], bool> SaveToStore { get; set; } = SqlCredentialStore.Save;

        /// <summary>
        /// Test seam: what removes an entry (true when it is gone: there was none, or the removal was written; false when the
        /// store could not be read or written). A test makes it fail to check that a password the store still holds is never
        /// reported as forgotten.
        /// </summary>
        internal static Func<string, string, bool> RemoveFromStore { get; set; } = SqlCredentialStore.Remove;

        /// <summary>
        /// The store's server key for a data source ("host", "host\instance", "host,port"), case-insensitive: <c>mssql-web://</c>
        /// for a password given to a connection that validates the server certificate, <c>mssql-web-trust://</c> for one given
        /// with <c>TrustServerCertificate=true</c>.
        /// </summary>
        public static string ServerKey(string dataSource, bool trustServerCertificate = false) =>
            (trustServerCertificate ? TrustPrefix : ValidatedPrefix) + (dataSource ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>
        /// Fills in the remembered password of a bridge request for a plain SQL login that has none. Anything else (Windows or
        /// Entra authentication, a password already there, weaker or different security options, a string that does not parse,
        /// a request from the named pipe) comes back unchanged. The handlers go through <see cref="BridgeSqlPassword.Supply"/>,
        /// which calls <see cref="TryFill"/> on the request it already parsed.
        /// </summary>
        internal static string Resolve(string connectionString, bool fromBridge)
        {
            if (!fromBridge || string.IsNullOrEmpty(connectionString)) return connectionString;
            SqlConnectionStringBuilder builder;
            try { builder = new SqlConnectionStringBuilder(connectionString); }
            catch (ArgumentException) { return connectionString; }
            return TryFill(builder) ? builder.ConnectionString : connectionString;
        }

        /// <summary>
        /// Puts the remembered password into <paramref name="builder"/> (a bridge request; the caller checks) when it is a plain
        /// SQL login without one and a password was saved for its certificate checking; true when it did. A trusting request for
        /// a server on the engine's own machine also takes the <c>mssql-web://</c> entry the released builds wrote (see the class
        /// remarks); it is read, never moved.
        /// </summary>
        internal static bool TryFill(SqlConnectionStringBuilder builder)
        {
            if (!string.IsNullOrEmpty(builder.Password) || string.IsNullOrWhiteSpace(builder.DataSource)
                || string.IsNullOrWhiteSpace(builder.UserID) || !BridgeSqlSecurity.IsPlainSqlLogin(builder))
                return false;

            foreach (var key in FillKeys(builder.DataSource, builder.TrustServerCertificate))
            {
                if (!SqlCredentialStore.TryGet(key, builder.UserID, out var password)) continue;
                builder.Password = password;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Spec 043 follow-up (review, round 3): the store keys a request is filled from, in the order they are read — the one
        /// place both <see cref="TryFill"/> and <see cref="Has"/> take them from, so the engine never says a password is
        /// remembered for a connection it would not give it to. A validating request (false) reads <c>mssql-web://</c>, then
        /// <c>mssql-web-trust://</c>. A trusting request (true) reads <c>mssql-web-trust://</c>, and for a server on the engine's
        /// own machine then the <c>mssql-web://</c> entry the released builds wrote (see the class remarks). Null (a client that
        /// does not say which, before <c>credentials.sqlserver.v2</c>): both keys.
        /// </summary>
        internal static string[] FillKeys(string dataSource, bool? trustServerCertificate) =>
            trustServerCertificate == true
                ? IsEngineMachine(dataSource)
                    ? new[] { ServerKey(dataSource, trustServerCertificate: true), ServerKey(dataSource) }
                    : new[] { ServerKey(dataSource, trustServerCertificate: true) }
                : new[] { ServerKey(dataSource), ServerKey(dataSource, trustServerCertificate: true) };

        // The server is on the engine's own machine ("localhost", ".", "(local)", a loopback address, this machine's name), judged
        // as the bridge target guard judges it.
        private static bool IsEngineMachine(string dataSource) =>
            BridgeSqlTargetGuard.IsLocalHost(BridgeSqlTargetGuard.ParseDataSource(dataSource).Host);

        /// <summary>
        /// Saves the string's password after the server accepted it, when the user asked to remember it (bridge requests for a
        /// plain SQL login only). True only when it is now in the store: a store that cannot be written or encrypted is logged
        /// and reported as false, never thrown, so the caller never says "remembered" for a password the engine does not hold.
        /// </summary>
        public static bool RememberIfRequested(string connectionString, bool remember) =>
            RememberIfRequested(connectionString, remember, BridgeSourceIp.Current is not null);

        internal static bool RememberIfRequested(string connectionString, bool remember, bool fromBridge)
        {
            if (!remember || !fromBridge || string.IsNullOrEmpty(connectionString)) return false;
            SqlConnectionStringBuilder builder;
            try { builder = new SqlConnectionStringBuilder(connectionString); }
            catch (ArgumentException) { return false; }   // nothing sensible to remember from a string that does not parse
            if (string.IsNullOrEmpty(builder.Password) || string.IsNullOrWhiteSpace(builder.DataSource)
                || string.IsNullOrWhiteSpace(builder.UserID) || !BridgeSqlSecurity.IsPlainSqlLogin(builder))
                return false;

            var trust = builder.TrustServerCertificate;
            try
            {
                // One password per server and login: the one saved under the other certificate checking is out of date now. It is
                // removed in the same write (review), so two remembers with different settings cannot each delete the other's
                // fresh entry and leave none.
                if (!SaveToStore(ServerKey(builder.DataSource, trust), builder.UserID, builder.Password,
                        new[] { ServerKey(builder.DataSource, !trust) }))
                {
                    Log.Warning("SQL Server password not remembered for {Login}@{Server}: the credential store could not be written",
                        builder.UserID, builder.DataSource);
                    return false;
                }
            }
            catch (Exception ex)
            {
                // DPAPI or the file system failed. The login itself was accepted; only remembering it did not work.
                Log.Warning("SQL Server password not remembered for {Login}@{Server}: {Type}", builder.UserID, builder.DataSource, ex.GetType().Name);
                return false;
            }
            Log.Information("SQL Server password remembered for {Login}@{Server}", builder.UserID, builder.DataSource);
            return true;
        }

        /// <summary>
        /// Whether a password is remembered for the data source and login. Spec 043 follow-up (review, round 3): with the
        /// connection's "Trust server certificate" choice (<c>CredentialRequest</c> key 5), only a password a connection with that
        /// choice would be given counts (<see cref="FillKeys"/>); without it (null, older clients), one saved with either.
        /// </summary>
        public static bool Has(string dataSource, string login, bool? trustServerCertificate = null)
        {
            foreach (var key in FillKeys(dataSource, trustServerCertificate))
                if (SqlCredentialStore.Has(key, login)) return true;
            return false;
        }

        /// <summary>
        /// Removes the remembered password, under both keys whatever it was saved with; true when there was one. Throws
        /// <see cref="CredentialStoreWriteException"/> when the store could not be written (review): the password is still there and
        /// must not be reported as forgotten.
        /// </summary>
        public static bool Forget(string dataSource, string login)
        {
            var any = false;
            foreach (var key in new[] { ServerKey(dataSource), ServerKey(dataSource, trustServerCertificate: true) })
            {
                // Spec 043 follow-up (review, round 3): the removal runs even when Has says no. A store that cannot be read answers
                // Has "no", so skipping the removal answered it "nothing to forget" while the password stayed in the file; the
                // removal refuses an unreadable store (false). A readable store without the entry removes nothing and says so.
                var had = SqlCredentialStore.Has(key, login);
                if (!RemoveFromStore(key, login))
                {
                    Log.Warning("SQL Server password not forgotten for {Login}@{Server}: the credential store could not be read or written",
                        login, dataSource);
                    throw new CredentialStoreWriteException();
                }
                any |= had;
            }
            if (any) Log.Information("SQL Server password forgotten for {Login}@{Server}", login, dataSource);
            return any;
        }
    }
}
