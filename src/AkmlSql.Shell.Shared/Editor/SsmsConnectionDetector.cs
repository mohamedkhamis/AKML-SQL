using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Serilog;

namespace AkmlSql.Shell.Shared.Editor
{
    /// <summary>
    /// Extracts the active SSMS connection info from the DTE window caption.
    /// SSMS shows "ServerName.DatabaseName - filename" in query window titles.
    /// This avoids SSMS-internal COM interop (ScriptFactory) which requires
    /// version-specific assemblies and breaks across SSMS 20/21/22.
    /// </summary>
    internal static class SsmsConnectionDetector
    {
        /// <summary>
        /// Attempts to detect the SQL Server connection for the given text view
        /// by parsing the SSMS window caption. Returns null if detection fails.
        /// Must be called on the UI thread.
        /// </summary>
        public static ConnectionResult TryDetectConnection(IServiceProvider serviceProvider)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var dte = serviceProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte?.ActiveDocument?.ActiveWindow == null)
                    return null;

                var caption = dte.ActiveDocument.ActiveWindow.Caption;
                Log.Debug("SsmsConnectionDetector: window caption = '{Caption}'", caption);

                if (string.IsNullOrEmpty(caption))
                    return null;

                return ParseCaption(caption);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "SsmsConnectionDetector: failed to detect connection");
                return null;
            }
        }

        /// <summary>
        /// Parses an SSMS window caption like "ServerName.DatabaseName - QueryFile.sql"
        /// to extract server and database names.
        /// </summary>
        internal static ConnectionResult ParseCaption(string caption)
        {
            // SSMS 22 caption format: "filename.sql - ServerName.DatabaseName (Username (SPID))"
            // SSMS 20 caption format: "ServerName.DatabaseName - filename.sql"
            // We need to handle both formats.

            var dashIndex = caption.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIndex <= 0)
                return null;

            // Try SSMS 22 format first: connection info is AFTER the dash
            var afterDash = caption.Substring(dashIndex + 3).Trim();

            // Strip trailing "(Username (SPID))" pattern
            var parenIdx = afterDash.IndexOf(" (", StringComparison.Ordinal);
            if (parenIdx > 0)
                afterDash = afterDash.Substring(0, parenIdx).Trim();

            string server;
            string database;

            // Try parsing "server.database" from afterDash
            var dotIndex = afterDash.IndexOf('.');
            if (dotIndex > 0 && !afterDash.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                // SSMS 22 format: afterDash = "(local).StockProduction"
                server = afterDash.Substring(0, dotIndex).Trim();
                database = afterDash.Substring(dotIndex + 1).Trim();
            }
            else
            {
                // Try SSMS 20 format: connection info is BEFORE the dash
                var beforeDash = caption.Substring(0, dashIndex).Trim();
                dotIndex = beforeDash.IndexOf('.');
                if (dotIndex > 0 && !beforeDash.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                {
                    server = beforeDash.Substring(0, dotIndex).Trim();
                    database = beforeDash.Substring(dotIndex + 1).Trim();
                }
                else
                {
                    return null; // Can't parse connection
                }
            }

            if (string.IsNullOrEmpty(server))
                return null;

            // Build a trusted connection string (Windows auth)
            // SSMS uses the current user's credentials by default
            // Build connection string manually to avoid SqlConnectionEncryptOption type issues
            var connStr = $"Data Source={server};Initial Catalog={database};" +
                          "Integrated Security=true;TrustServerCertificate=true;Encrypt=false;" +
                          "Connect Timeout=5;Application Name=AKML SQL Engine";

            Log.Information("SsmsConnectionDetector: detected {Server}.{Database}", server, database);

            return new ConnectionResult
            {
                Server = server,
                Database = database,
                ConnectionString = connStr
            };
        }

        internal class ConnectionResult
        {
            public string Server { get; set; }
            public string Database { get; set; }
            public string ConnectionString { get; set; }
        }
    }
}
