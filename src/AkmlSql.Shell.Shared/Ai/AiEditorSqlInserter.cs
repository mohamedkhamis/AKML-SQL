#nullable enable
using System;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Ai
{
    /// <summary>
    /// Spec 037 chat panel per-block insert: the "⇩ Insert" button beside each SQL code action
    /// in an assistant message puts that block's SQL into the ACTIVE query editor at the caret —
    /// the same insertion branch <see cref="Commands.TextToSqlCommand"/> uses. Returns
    /// <c>false</c> when no query editor is active (or the host is unavailable) so the caller
    /// can surface it on the button instead of failing silently.
    /// </summary>
    internal static class AiEditorSqlInserter
    {
        /// <summary>
        /// Test seam: when set, <see cref="TryInsertAtCaret"/> routes to it and returns its
        /// result WITHOUT touching the DTE — the shell tests run off-host, where
        /// <c>Package.GetGlobalService</c> cannot answer.
        /// </summary>
        internal static Func<string, bool>? OverrideForTests;

        /// <summary>
        /// Inserts <paramref name="sql"/> into the active document's text editor at the caret.
        /// </summary>
        /// <returns><c>true</c> when the insert ran; <c>false</c> when there is no active query
        /// editor or the insert threw.</returns>
        internal static bool TryInsertAtCaret(string sql)
        {
            if (OverrideForTests != null)
                return OverrideForTests(sql);

            try
            {
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                var textDocument = dte?.ActiveDocument?.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                    return false;

                textDocument.Selection.Insert(sql, (int)vsInsertFlags.vsInsertFlagsInsertAtEnd);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AiEditorSqlInserter: failed to insert SQL into the active query editor");
                return false;
            }
        }
    }
}
