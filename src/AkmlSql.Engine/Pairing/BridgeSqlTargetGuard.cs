using System;
using System.Net;
using Microsoft.Data.SqlClient;

namespace AkmlSql.Engine.Pairing
{
    /// <summary>
    /// Which SQL Servers a request that arrived over the WebSocket bridge (AKML SQL Web) may make
    /// the engine connect to. The engine-side half of the check the web client also runs
    /// (<c>SqlConnectionService.ValidateTarget</c>): a browser-side check alone protects nothing,
    /// because anything that can reach the bridge can skip it.
    ///
    /// <para>
    /// The rule is about <b>whose identity</b> the connection uses, not where the server is:
    /// </para>
    /// <list type="bullet">
    ///   <item><description>
    ///     A server on the engine's own machine: anything goes, as before.
    ///   </description></item>
    ///   <item><description>
    ///     A remote server with credentials the user typed (SQL Server authentication): allowed.
    ///     The engine only forwards what it was given.
    ///   </description></item>
    ///   <item><description>
    ///     A remote server with the engine's AMBIENT identity -- Windows authentication, Entra
    ///     integrated / managed identity / default, or a named-pipe (SMB) path -- is refused. The
    ///     engine runs as a service, so that would sign in as the service account rather than the
    ///     person at the browser, and a hostile page could use it to make the engine authenticate
    ///     to a machine of its choosing (the confused-deputy / NTLM-relay case).
    ///   </description></item>
    /// </list>
    ///
    /// <para>
    /// Requests over the named pipe come from the SSMS extension running as the
    /// signed-in user, where Windows authentication to a remote server is the normal, intended
    /// case -- they are not checked.
    /// </para>
    /// </summary>
    internal static class BridgeSqlTargetGuard
    {
        internal const string AmbientIdentityRefused =
            "Windows authentication is only available for a SQL Server on the engine's own machine. " +
            "For a remote server, use SQL Server authentication: through AKML SQL Web the engine would sign in " +
            "as its service account, not as you.";

        internal const string NonTcpRefused =
            "A remote SQL Server must be reached over TCP (host, host\\instance or host,port). " +
            "Named-pipe and UNC addresses would authenticate as the engine's service account.";

        /// <summary>
        /// Null when the engine may open <paramref name="connectionString"/> for the current
        /// request; otherwise the reason, phrased for the person who asked.
        /// </summary>
        public static string? Check(string connectionString) =>
            Check(connectionString, fromBridge: BridgeSourceIp.Current is not null);

        internal static string? Check(string connectionString, bool fromBridge)
        {
            if (!fromBridge)
            {
                return null;
            }

            SqlConnectionStringBuilder builder;
            try
            {
                builder = new SqlConnectionStringBuilder(connectionString);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
            {
                return "The connection string is not valid.";
            }

            var (host, overTcp) = ParseDataSource(builder.DataSource);
            if (IsLocalHost(host))
            {
                return null;
            }

            if (!overTcp)
            {
                return NonTcpRefused;
            }

            if (builder.IntegratedSecurity || UsesAmbientIdentity(builder.Authentication))
            {
                return AmbientIdentityRefused;
            }

            return null;
        }

        /// <summary>Entra modes that sign in with whatever identity the engine process has.</summary>
        private static bool UsesAmbientIdentity(SqlAuthenticationMethod method) =>
            method is not (SqlAuthenticationMethod.NotSpecified
                or SqlAuthenticationMethod.SqlPassword
                or SqlAuthenticationMethod.ActiveDirectoryPassword
                or SqlAuthenticationMethod.ActiveDirectoryServicePrincipal);

        /// <summary>
        /// The host a Data Source names, and whether it is reached over TCP. Handles the forms
        /// SqlClient accepts: <c>host</c>, <c>host\instance</c>, <c>host,port</c>, a <c>tcp:</c> /
        /// <c>np:</c> / <c>lpc:</c> / <c>admin:</c> prefix, and a UNC pipe path.
        /// </summary>
        internal static (string Host, bool OverTcp) ParseDataSource(string? dataSource)
        {
            var s = (dataSource ?? string.Empty).Trim();
            var overTcp = true;

            var colon = s.IndexOf(':');
            if (colon > 0 && colon < 6)
            {
                var protocol = s[..colon].ToLowerInvariant();
                if (protocol is "tcp" or "np" or "lpc" or "admin")
                {
                    overTcp = protocol is "tcp" or "admin";
                    s = s[(colon + 1)..].Trim();
                }
            }

            if (s.StartsWith(@"\\", StringComparison.Ordinal))
            {
                // \\host\pipe\sql\query
                overTcp = false;
                s = s[2..];
                var end = s.IndexOf('\\');
                return (end >= 0 ? s[..end] : s, overTcp);
            }

            var cut = s.IndexOfAny(['\\', ',']);
            var host = (cut >= 0 ? s[..cut] : s).Trim().Trim('[', ']');
            return (host, overTcp);
        }

        /// <summary>
        /// True for the engine's own machine: the local aliases, a loopback address, or this
        /// machine's name. Exact matches only -- <c>127.0.0.1.example.com</c> is a remote host.
        /// </summary>
        internal static bool IsLocalHost(string host)
        {
            if (host.Length == 0)
            {
                return false;
            }

            if (host is "." or "(local)"
                || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.Equals("(localdb)", StringComparison.OrdinalIgnoreCase)
                || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
        }
    }
}
