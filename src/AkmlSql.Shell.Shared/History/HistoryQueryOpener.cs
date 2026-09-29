#nullable enable
using System;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Spec 040 (HIS-12/HIS-14) — the one way SQL History opens a query in a new tab: the text, the
    /// query's session continued (<see cref="DocumentSessionKeys.Adopt"/>), and a connection to its
    /// server and database when one is given. Used by Open query, Re-execute and restore on start.
    /// </summary>
    internal static class HistoryQueryOpener
    {
        /// <summary>
        /// Opens <paramref name="sqlText"/> in a new query tab that continues the query's session and
        /// connects to its server and database when it can. True when the connection was set.
        /// </summary>
        public static bool OpenInNewTab(string sqlText, string? server, string? database, string? sessionKey, string fileName)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            var connected = false;
            try
            {
                var dte = (EnvDTE.DTE)Microsoft.VisualStudio.Shell.Package.GetGlobalService(typeof(EnvDTE.DTE));
                if (dte == null)
                {
                    Serilog.Log.Warning("History: DTE service unavailable");
                    return false;
                }

                // Capture the PREVIOUSLY active document's auth mode BEFORE we create
                // the new tab (which will steal focus). This lets us build a connection
                // string that matches the user's current SSMS session (AAD vs Windows)
                // instead of always hardcoding Integrated Security — otherwise history
                // restore would fail for AAD-authenticated users just like Phase A did.
                var preExistingAuth = AkmlSql.Shell.Shared.Editor.SsmsConnectionDetector.AuthMode.Unknown;
                try
                {
                    var prevDoc = dte.ActiveDocument;
                    if (prevDoc != null)
                    {
                        var (mode, _) = AkmlSql.Shell.Shared.Editor.SsmsConnectionDetector.ReadAuthModeFromDocument(prevDoc);
                        preExistingAuth = mode;
                    }
                }
                catch { /* best effort */ }

                dte.ItemOperations.NewFile(
                    @"General\Sql File",
                    fileName,
                    EnvDTE.Constants.vsViewKindCode);

                var activeDoc = dte.ActiveDocument;
                var textDocument = activeDoc?.Object("TextDocument") as EnvDTE.TextDocument;
                if (textDocument != null)
                {
                    var editPoint = textDocument.StartPoint.CreateEditPoint();
                    editPoint.Insert(sqlText);
                    textDocument.Selection.StartOfDocument();
                }

                // Spec 040 (HIS-02): the new tab continues the query's session, so running it again
                // adds to the same history row — unless another open tab already holds that session.
                if (!string.IsNullOrEmpty(sessionKey) && activeDoc != null)
                {
                    try { DocumentSessionKeys.Adopt(activeDoc.FullName, sessionKey!); }
                    catch (Exception adoptEx) { Serilog.Log.Debug(adoptEx, "History: session adoption skipped"); }
                }

                // Try to set the connection on the new query window via SSMS ScriptFactory
                if (!string.IsNullOrEmpty(server))
                {
                    try
                    {
                        Serilog.Log.Debug("History: restoring connection to {Server}.{Database}", server, database);
                        var sfType = Type.GetType(
                            "Microsoft.SqlServer.Management.UI.VSIntegration.ScriptFactory, SqlWorkbench.Interfaces");
                        if (sfType != null && activeDoc != null)
                        {
                            var sfProp = sfType.GetProperty("Instance",
                                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                            var scriptFactory = sfProp?.GetValue(null);
                            if (scriptFactory != null)
                            {
                                // Try to get the current script and set its connection
                                var getCurrentScript = sfType.GetMethod("GetCurrentScript");
                                var currentScript = getCurrentScript?.Invoke(scriptFactory, null);
                                if (currentScript != null)
                                {
                                    // Match the user's current SSMS auth mode. We can't restore
                                    // a password (SQL auth) or replay an interactive AAD flow, so
                                    // for those modes we fall back to Integrated Security and let
                                    // SSMS prompt the user if the token isn't cached.
                                    string authClause =
                                        preExistingAuth == AkmlSql.Shell.Shared.Editor.SsmsConnectionDetector.AuthMode.AzureAdIntegrated
                                            ? "Authentication=Active Directory Integrated"
                                            : "Integrated Security=True";
                                    var connStr = string.IsNullOrEmpty(database)
                                        ? $"Data Source={server};{authClause};Trust Server Certificate=True"
                                        : $"Data Source={server};Initial Catalog={database};{authClause};Trust Server Certificate=True";
                                    var setConn = currentScript.GetType().GetMethod("SetConnectionInfo");
                                    if (setConn != null)
                                    {
                                        setConn.Invoke(currentScript, new object[] { connStr });
                                        connected = true;
                                        Serilog.Log.Information("History: connection set to {Server}.{Database} (auth={Auth})",
                                            server, database, preExistingAuth);
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception connEx)
                    {
                        Serilog.Log.Debug(connEx, "History: connection restore failed (non-fatal)");
                    }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "History: failed to open SQL in new tab");
            }
            return connected;
        }
    }
}
