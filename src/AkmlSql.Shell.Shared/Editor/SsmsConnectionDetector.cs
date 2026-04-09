using System;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
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
        /// Attempts to detect the SQL Server connection for the CURRENTLY ACTIVE
        /// document (whatever SSMS has focused). Kept for call sites that only
        /// care about the active window (e.g. execution-time safety checks).
        /// For per-text-view detection use the overload that takes an <see cref="IWpfTextView"/>.
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
        /// Attempts to detect the SQL Server connection for a SPECIFIC text view
        /// by resolving the view's file path to the matching DTE Document and
        /// reading ITS window caption. This prevents the cross-window leak where
        /// a new Server-B query window was being assigned Server-A's connection
        /// because DTE.ActiveDocument still pointed to the previously focused
        /// Server-A window at the moment the new view was created.
        ///
        /// CRITICAL: this overload does NOT fall back to
        /// <see cref="TryDetectConnection(IServiceProvider)"/>. If the text view
        /// has no resolvable file path yet (brand-new unsaved buffer), we return
        /// <c>null</c> so the caller's retry loop can try again after SSMS finishes
        /// wiring up the document. Falling back to ActiveDocument would pick up the
        /// previously focused window and leak the wrong connection info.
        /// Must be called on the UI thread.
        /// </summary>
        public static ConnectionResult TryDetectConnection(IServiceProvider serviceProvider, IWpfTextView textView)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                if (textView == null)
                {
                    return TryDetectConnection(serviceProvider);
                }

                var dte = serviceProvider.GetService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte == null)
                {
                    return null;
                }

                // Resolve the text view's file path via ITextDocument.
                string filePath = null;
                try
                {
                    if (textView.TextBuffer.Properties.TryGetProperty<ITextDocument>(typeof(ITextDocument), out var textDoc))
                    {
                        filePath = textDoc?.FilePath;
                    }
                }
                catch { /* not fatal */ }

                if (string.IsNullOrEmpty(filePath))
                {
                    // Brand-new unsaved buffer — the document isn't registered with
                    // DTE yet. Return null so the retry loop waits 500ms and tries
                    // again; by then the file path and caption will be wired up.
                    Log.Debug("SsmsConnectionDetector: no file path for text view, returning null (retry expected)");
                    return null;
                }

                // Find the DTE Document with matching FullName (case-insensitive).
                //
                // IMPORTANT: use an indexed loop instead of `foreach` so that a
                // single malformed COM object (throwing from its enumerator's
                // MoveNext or from Item(i)) only skips THAT document. A foreach
                // here would swallow the exception at the outer try/catch and
                // abandon the rest of the collection, silently returning null
                // for a valid text view whose match sits further down the list.
                EnvDTE.Documents docs = null;
                int count = 0;
                try
                {
                    docs = dte.Documents;
                    count = docs?.Count ?? 0;
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "SsmsConnectionDetector: unable to read DTE.Documents.Count");
                    return null;
                }

                for (int i = 1; i <= count; i++)
                {
                    string docFullName = null;
                    EnvDTE.Window win = null;
                    try
                    {
                        var doc = docs.Item(i);
                        if (doc == null) continue;
                        docFullName = doc.FullName;
                        if (!string.Equals(docFullName, filePath, StringComparison.OrdinalIgnoreCase))
                            continue;
                        win = doc.ActiveWindow;
                    }
                    catch
                    {
                        // This specific document is broken — skip it and keep
                        // looking for a match in the remaining documents.
                        continue;
                    }

                    if (win != null && !string.IsNullOrEmpty(win.Caption))
                    {
                        Log.Debug("SsmsConnectionDetector: matched text view to document '{Caption}'", win.Caption);
                        return ParseCaption(win.Caption);
                    }
                }

                // No matching document found — return null so the retry loop
                // waits and tries again. NO fallback to ActiveDocument.
                Log.Debug("SsmsConnectionDetector: no DTE document matched '{Path}' yet, returning null", filePath);
                return null;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "SsmsConnectionDetector: per-textview detection failed");
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

            Log.Information("SsmsConnectionDetector: parsed caption='{Caption}' → server='{Server}' database='{Database}'",
                caption, server, database);

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
