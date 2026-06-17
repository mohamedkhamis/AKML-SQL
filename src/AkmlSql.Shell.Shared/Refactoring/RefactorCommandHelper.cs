#nullable enable
using System;
using System.Linq;
using System.Windows.Forms;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ipc;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Refactoring
{
    /// <summary>
    /// Spec 030 T067 — shared plumbing for the editor-context refactor commands (Inline EXEC,
    /// INSERT→UPDATE, Inline Stored Procedure). Resolves the active editor's real session id +
    /// document text + caret/selection (the missing dispatch the heavyweight refactor commands
    /// never had), runs a <c>RequestRefactorPreview</c>, shows <see cref="RefactoringPreviewDialog"/>,
    /// and — unlike Safe Rename, which emits a script — applies the approved changes straight to the
    /// current editor buffer.
    /// </summary>
    internal static class RefactorCommandHelper
    {
        /// <summary>Resolved active-editor context for a refactor command.</summary>
        internal sealed class EditorRefactorContext
        {
            public IWpfTextView View { get; set; } = null!;
            public string SessionId { get; set; } = string.Empty;
            public string DocumentText { get; set; } = string.Empty;
            public int CaretOffset { get; set; }
            public int SelectionStart { get; set; }
            public int SelectionLength { get; set; }
        }

        /// <summary>
        /// Resolves the active SQL editor: its WPF view, the real <c>AkmlSqlSessionId</c> (so engine
        /// ops that need a live connection — e.g. Inline Stored Procedure — can resolve it), the full
        /// document text, and the caret/selection. Returns null when there is no active managed view.
        /// </summary>
        public static EditorRefactorContext? TryGetActiveEditor()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var textManager = (IVsTextManager?)Package.GetGlobalService(typeof(SVsTextManager));
                if (textManager == null) return null;

                textManager.GetActiveView(1, null, out var vsView);
                if (vsView == null) return null;

                var componentModel = Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
                var adapters = componentModel?.GetService<IVsEditorAdaptersFactoryService>();
                var view = adapters?.GetWpfTextView(vsView);
                if (view == null) return null;

                var snapshot = view.TextBuffer.CurrentSnapshot;
                int caret = view.Caret.Position.BufferPosition.Position;
                int selStart = caret, selLen = 0;
                if (!view.Selection.IsEmpty)
                {
                    selStart = view.Selection.Start.Position.Position;
                    selLen   = view.Selection.End.Position.Position - selStart;
                }

                string sessionId =
                    view.TextBuffer.Properties.TryGetProperty<string>("AkmlSqlSessionId", out var sid)
                    && !string.IsNullOrEmpty(sid)
                        ? sid
                        : Guid.NewGuid().ToString("N"); // pure-text ops still work without a real session

                return new EditorRefactorContext
                {
                    View            = view,
                    SessionId       = sessionId,
                    DocumentText    = snapshot.GetText(),
                    CaretOffset     = caret,
                    SelectionStart  = selStart,
                    SelectionLength = selLen
                };
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "RefactorCommandHelper: failed to resolve active editor");
                return null;
            }
        }

        /// <summary>
        /// Runs a heavyweight refactor that rewrites a span of the CURRENT document (Inline EXEC /
        /// INSERT→UPDATE / Inline Stored Procedure): preview → dialog → apply-to-buffer.
        /// </summary>
        public static void RunInlineRefactor(int operationType, string opLabel, string applyButtonText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var ctx = TryGetActiveEditor();
                if (ctx == null || string.IsNullOrEmpty(ctx.DocumentText))
                {
                    MessageBox.Show("Open a SQL document and place the cursor on the target statement.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                var client = EngineLifecycle.Manager?.Client;
                if (client == null || !client.IsConnected)
                {
                    MessageBox.Show("The AKML SQL engine is not running yet — try again in a moment.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var request = new RefactorPreviewRequest
                {
                    SessionId       = ctx.SessionId,
                    OperationType   = operationType,
                    DocumentText    = ctx.DocumentText,
                    SelectionStart  = ctx.SelectionStart,
                    SelectionLength = ctx.SelectionLength
                };

                RefactorPreviewResponse? response = null;
                ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    response = await client.SendRequestAsync<RefactorPreviewResponse, RefactorPreviewRequest>(
                        MessageTypes.RequestRefactorPreview, request, timeoutMs: 30_000);
                });

                if (response == null)
                {
                    MessageBox.Show("No response from the engine — the operation timed out.",
                        Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!response.CanApply || response.Changes.Length == 0)
                {
                    var msg = response.Errors.Length > 0
                        ? string.Join("\n", response.Errors)
                        : $"No {opLabel} is available at the cursor.";
                    MessageBox.Show(msg, Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                using var dialog = new RefactoringPreviewDialog(response, opLabel, opLabel, applyButtonText);
                if (dialog.ShowDialog() != DialogResult.OK) return;

                var approved = dialog.ApprovedChanges;
                if (approved.Length == 0) return;

                ApplyChangesToBuffer(ctx.View, approved);
                Log.Information("{Op}: applied {Count} change(s) to the buffer", opLabel, approved.Length);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "RefactorCommandHelper.RunInlineRefactor failed for {Op}", opLabel);
                MessageBox.Show($"{opLabel} failed: {ex.Message}",
                    Constants.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Applies current-document changes (empty FilePath) to the editor buffer in a single edit,
        /// descending by offset so earlier offsets stay valid. Bounds-checked against the snapshot.
        /// </summary>
        private static void ApplyChangesToBuffer(IWpfTextView view, RefactorChangeInfo[] changes)
        {
            int len = view.TextBuffer.CurrentSnapshot.Length;
            using var edit = view.TextBuffer.CreateEdit();
            foreach (var c in changes.Where(c => string.IsNullOrEmpty(c.FilePath))
                                     .OrderByDescending(c => c.StartOffset))
            {
                int start = c.StartOffset;
                int span  = c.EndOffset - c.StartOffset;
                if (start < 0 || span < 0 || start + span > len) continue;
                edit.Replace(start, span, c.NewText ?? string.Empty);
            }
            edit.Apply();
        }

        /// <summary>
        /// Extracts a (schema, object) identifier at the caret from the document text — handles a
        /// bare name, a <c>schema.object</c> qualifier, and <c>[bracketed]</c> identifiers. Returns
        /// ("", "") when no identifier is under the caret.
        /// </summary>
        public static (string Schema, string Name) ExtractObjectAtCaret(string docText, int caret)
        {
            if (string.IsNullOrEmpty(docText)) return (string.Empty, string.Empty);
            if (caret < 0) caret = 0;
            if (caret > docText.Length) caret = docText.Length;

            static bool IsIdent(char ch) => char.IsLetterOrDigit(ch) || ch == '_' || ch == '#' || ch == '@' || ch == '$';

            // Expand left/right over the identifier under the caret. The left scan must allow BOTH
            // brackets (']' AND '['): with only ']' allowed, a bracketed name like [hr].[Proc] stops
            // the scan just after the opening '[', so the schema look-left below sees '[' instead of
            // the '.' and silently defaults the schema to "dbo" — scripting the wrong object.
            int start = caret;
            while (start > 0 && (IsIdent(docText[start - 1]) || docText[start - 1] == ']' || docText[start - 1] == '[')) start--;
            int end = caret;
            while (end < docText.Length && (IsIdent(docText[end]) || docText[end] == '[' || docText[end] == ']')) end++;
            // Caret just past the identifier (start==end): step back one.
            if (start == end && start > 0 && IsIdent(docText[start - 1]))
            {
                while (start > 0 && IsIdent(docText[start - 1])) start--;
            }
            if (start >= end) return (string.Empty, string.Empty);

            var token = docText.Substring(start, end - start).Trim();
            // Is there a schema qualifier immediately before the token?
            string schema = "dbo", name = token;
            int dot = token.LastIndexOf('.');
            if (dot > 0)
            {
                schema = token.Substring(0, dot);
                name   = token.Substring(dot + 1);
            }
            else
            {
                // Look left for "schema." preceding start.
                int p = start;
                while (p > 0 && char.IsWhiteSpace(docText[p - 1])) p--;
                if (p > 0 && docText[p - 1] == '.')
                {
                    int s = p - 1;
                    int sEnd = s;
                    while (s > 0 && (IsIdent(docText[s - 1]) || docText[s - 1] == ']' || docText[s - 1] == '[')) s--;
                    var sch = docText.Substring(s, sEnd - s).Trim();
                    if (!string.IsNullOrEmpty(sch)) schema = sch;
                }
            }

            schema = schema.Trim('[', ']', ' ');
            name   = name.Trim('[', ']', ' ');
            return (schema, name);
        }

        /// <summary>
        /// Spec 030 / T062 / FR-018 — resolves the identifier under the caret for database-wide Smart
        /// Rename, classifying it as an OBJECT or a COLUMN. A column is recognised only when the caret
        /// token is a real multi-part reference qualified by a TABLE name (<c>[schema.]table.column</c>),
        /// not an alias — the engine cannot resolve an alias to a table from text alone.
        /// <para>
        /// Returns the pieces in the exact shape the engine's database-wide preview expects:
        /// <list type="bullet">
        /// <item><description>OBJECT: <c>IsColumn=false</c>, <c>Schema</c>=object schema (default dbo),
        /// <c>Name</c>=object name, <c>ParentTable=null</c>.</description></item>
        /// <item><description>COLUMN: <c>IsColumn=true</c>, <c>Schema</c>=the TABLE's schema (default dbo),
        /// <c>ParentTable</c>=the table name, <c>Name</c>=the column name. (The engine binds
        /// <c>@schema=Schema, @table=ParentTable, @column=Name</c>; the command then sends
        /// <c>OriginalIdentifier="Schema.Name"</c> and <c>ExtractedUnitName="ParentTable"</c>.)</description></item>
        /// </list>
        /// </para>
        /// Returns <c>("","",..,false)</c> with an empty Name when nothing identifiable is under the caret.
        /// </summary>
        public static (string Schema, string? ParentTable, string Name, bool IsColumn) ExtractRenameTargetAtCaret(
            string docText, int caret)
        {
            if (string.IsNullOrEmpty(docText)) return (string.Empty, null, string.Empty, false);
            if (caret < 0) caret = 0;
            if (caret > docText.Length) caret = docText.Length;

            static bool IsIdent(char ch) => char.IsLetterOrDigit(ch) || ch == '_' || ch == '#' || ch == '@' || ch == '$';

            // Grab the FULL dotted reference around the caret (identifiers, brackets, and the dots
            // between them), e.g. "dbo.Orders.Total" or "[dbo].[Orders].[Total]".
            int start = caret;
            while (start > 0)
            {
                char ch = docText[start - 1];
                if (IsIdent(ch) || ch == ']' || ch == '[' || ch == '.') start--;
                else break;
            }
            int end = caret;
            while (end < docText.Length)
            {
                char ch = docText[end];
                if (IsIdent(ch) || ch == '[' || ch == ']' || ch == '.') end++;
                else break;
            }
            if (start >= end) return (string.Empty, null, string.Empty, false);

            var token = docText.Substring(start, end - start).Trim().Trim('.');
            if (string.IsNullOrEmpty(token)) return (string.Empty, null, string.Empty, false);

            var parts = SplitDotted(token);
            if (parts.Count == 0) return (string.Empty, null, string.Empty, false);

            switch (parts.Count)
            {
                case 1:
                    // Bare name → object in dbo.
                    return ("dbo", null, parts[0], false);

                case 2:
                    // Ambiguous: "schema.object" OR "table.column". We cannot tell from text alone, so
                    // treat as an OBJECT (schema.object). A 2-part table.column without a schema is the
                    // alias-or-table case the engine can't bind reliably; the engine refuses if it isn't
                    // a real object. (Column rename is supported via the explicit 3-part form below.)
                    return (parts[0], null, parts[1], false);

                default:
                    // 3+ parts: schema.table.column → a COLUMN rename. Use the LAST three parts.
                    int n = parts.Count;
                    return (parts[n - 3], parts[n - 2], parts[n - 1], true);
            }
        }

        /// <summary>Splits a dotted identifier into its (bracket-stripped) parts, respecting [brackets].</summary>
        private static System.Collections.Generic.List<string> SplitDotted(string token)
        {
            var parts = new System.Collections.Generic.List<string>();
            var sb = new System.Text.StringBuilder();
            bool inBracket = false;
            foreach (char ch in token)
            {
                if (ch == '[') { inBracket = true; continue; }
                if (ch == ']') { inBracket = false; continue; }
                if (ch == '.' && !inBracket)
                {
                    parts.Add(sb.ToString().Trim());
                    sb.Clear();
                    continue;
                }
                sb.Append(ch);
            }
            if (sb.Length > 0) parts.Add(sb.ToString().Trim());
            return parts.Where(p => p.Length > 0).ToList();
        }
    }
}
