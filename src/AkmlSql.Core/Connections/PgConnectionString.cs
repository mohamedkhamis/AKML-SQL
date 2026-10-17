using System;
using System.Data.Common;

namespace AkmlSql.Core.Connections
{
    /// <summary>
    /// Spec 042 (FR-002, FR-100, FR-103; contracts/ipc.md "Connection strings") - the one place that spells a PostgreSQL
    /// connection string and the one place that describes one for a log. The web edition builds the string and sends it to
    /// the engine, the engine opens it with Npgsql, tests build it to compare; all three must agree on the keys and on
    /// what the TLS mode names mean, so they share this.
    ///
    /// <para>
    /// Uses <see cref="DbConnectionStringBuilder"/> (BCL, WASM-safe, and independent of any driver) so every value is
    /// quoted and escaped: a value holding <c>;</c> or <c>=</c> stays a value and cannot add a keyword. Core stays
    /// netstandard2.0 and has no Npgsql reference; the keys are Npgsql's spellings.
    /// </para>
    /// </summary>
    public static class PgConnectionString
    {
        public const int DefaultPort = 5432;
        public const string ApplicationName = "AKML SQL";

        /// <summary>
        /// Builds the connection string. <paramref name="password"/> null (not empty) leaves the key out, which is how the
        /// engine knows to supply a remembered password. <paramref name="sslMode"/> is the dialog's spelling
        /// (<c>disable</c>, <c>prefer</c>, <c>require</c>, <c>verify-ca</c>, <c>verify-full</c>); null or empty is
        /// <c>prefer</c>, and an unknown mode throws rather than quietly connecting with weaker TLS than the user chose.
        /// </summary>
        public static string Build(string host, int? port, string database, string username, string? password,
            string? sslMode, string? rootCertificatePath)
        {
            var builder = new DbConnectionStringBuilder
            {
                ["Host"] = host,
                ["Port"] = (port.HasValue && port.Value > 0 ? port.Value : DefaultPort).ToString(),
                ["Database"] = database,
                ["Username"] = username,
                ["SSL Mode"] = DriverSslMode(sslMode),
                ["Application Name"] = ApplicationName,
                ["Timeout"] = 15,
            };
            if (password != null) builder["Password"] = password;
            if (!string.IsNullOrEmpty(rootCertificatePath)) builder["Root Certificate"] = rootCertificatePath;
            return builder.ConnectionString;
        }

        /// <summary>The driver's name for a dialog TLS mode; <see cref="ArgumentException"/> for a mode this product does not offer.</summary>
        public static string DriverSslMode(string? sslMode)
        {
            switch ((sslMode ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "":
                case "prefer": return "Prefer";
                case "disable": return "Disable";
                case "require": return "Require";
                case "verify-ca": return "VerifyCA";
                case "verify-full": return "VerifyFull";
                default:
                    throw new ArgumentException("The TLS mode must be disable, prefer, require, verify-ca or verify-full.", nameof(sslMode));
            }
        }

        /// <summary>
        /// A short, passwordless description for logs: <c>host:port database='d' user='u' ssl=Require</c>. Never throws;
        /// a string that does not parse is described as such. Every log line that mentions a PostgreSQL connection
        /// goes through this (FR-103).
        /// </summary>
        public static string Describe(string? connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) return "(no connection string)";
            try
            {
                var b = new DbConnectionStringBuilder { ConnectionString = connectionString };
                string Get(string key) => b.TryGetValue(key, out var v) ? Convert.ToString(v) ?? string.Empty : string.Empty;
                var host = Get("Host");
                var port = Get("Port");
                var target = string.IsNullOrEmpty(host) ? "(no host)" : host + ":" + (string.IsNullOrEmpty(port) ? DefaultPort.ToString() : port);
                return target + " database='" + Get("Database") + "' user='" + Get("Username") + "' ssl=" + (Get("SSL Mode") is { Length: > 0 } mode ? mode : "Prefer");
            }
            catch (ArgumentException)
            {
                return "(invalid connection string)";
            }
        }

        /// <summary>
        /// The key a server has in the engine's stores: <c>pg://{host}:{port}</c>, host lower-cased and trimmed, port 0 or
        /// null meaning 5432. The remembered-password entry, the schema cache identity and the saved-connection match all
        /// use it, so two spellings of the same server are one server.
        /// </summary>
        public static string ServerKey(string host, int? port)
        {
            var effective = port.HasValue && port.Value > 0 ? port.Value : DefaultPort;
            return "pg://" + (host ?? string.Empty).Trim().ToLowerInvariant() + ":" + effective;
        }
    }
}
