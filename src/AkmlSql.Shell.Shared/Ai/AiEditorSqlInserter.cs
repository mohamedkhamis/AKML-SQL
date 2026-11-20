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
    /// in an assistant message puts that block's SQL into a query editor at the caret —
    /// the same insertion branch <see cref="Commands.TextToSqlCommand"/> uses. Returns
    /// <c>false</c> when there is no such query editor (or the host is unavailable) so the caller
    /// can surface it on the button instead of failing silently.
    /// <para>Spec 046 (US1, FR-008): the chat passes the document of the query window it is bound
    /// to, so the SQL lands in the window the answer was about — the one the header names — not in
    /// whatever document happens to be active.</para>
    /// </summary>
    internal static class AiEditorSqlInserter
    {
        /// <summary>
        /// Test seam: when set, <see cref="TryInsertAtCaret"/> routes to it (with the SQL and the
        /// target document path) and returns its result WITHOUT touching the DTE — the shell tests
        /// run off-host, where <c>Package.GetGlobalService</c> cannot answer.
        /// </summary>
        internal static Func<string, string?, bool>? OverrideForTests;

        /// <summary>
        /// Inserts <paramref name="sql"/> at the caret of the document whose full name is
        /// <paramref name="documentPath"/>; with no path, into the active document (the behaviour
        /// before spec 046).
        /// </summary>
        /// <returns><c>true</c> when the insert ran; <c>false</c> when the document is not open, is
        /// not a text editor, or the insert threw.</returns>
        internal static bool TryInsertAtCaret(string sql, string? documentPath = null)
        {
            if (OverrideForTests != null)
                return OverrideForTests(sql, documentPath);

            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();   // a button click: always the UI thread
                var dte = (DTE2)Package.GetGlobalService(typeof(DTE));
                if (dte == null)
                    return false;

                // A path that is no longer open means the bound window closed: report it rather
                // than inserting into some other document.
                var document = string.IsNullOrEmpty(documentPath)
                    ? dte.ActiveDocument
                    : FindDocument(dte, documentPath!);
                var textDocument = document?.Object("TextDocument") as TextDocument;
                if (textDocument == null)
                    return false;

                textDocument.Selection.Insert(sql, (int)vsInsertFlags.vsInsertFlagsInsertAtEnd);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "AiEditorSqlInserter: failed to insert SQL into the query editor");
                return false;
            }
        }

        /// <summary>
        /// The open document named <paramref name="path"/>. Indexed loop, as in
        /// <c>SsmsConnectionDetector</c>: one broken COM document skips only itself.
        /// </summary>
        private static Document? FindDocument(DTE2 dte, string path)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Documents documents;
            int count;
            try
            {
                documents = dte.Documents;
                count = documents?.Count ?? 0;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "AiEditorSqlInserter: unable to read DTE.Documents");
                return null;
            }

            for (var i = 1; i <= count; i++)
            {
                try
                {
                    var document = documents!.Item(i);
                    if (document != null && string.Equals(document.FullName, path, StringComparison.OrdinalIgnoreCase))
                        return document;
                }
                catch
                {
                    // This document is broken — keep looking.
                }
            }
            return null;
        }
    }
}
