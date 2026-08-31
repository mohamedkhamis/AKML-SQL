#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Windows.Controls;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Analysis;
using Microsoft.VisualStudio.Text;

namespace AkmlSql.Shell.Shared.Analysis
{
    /// <summary>
    /// Context menu for a warning glyph: per issue a header, then the engine's fix actions
    /// ("Fix: …"), suppress-for-this-line, and disable-rule — reusing the SAME
    /// <see cref="Microsoft.VisualStudio.Language.Intellisense.ISuggestedAction"/> classes the
    /// lightbulb uses (<see cref="FixAction"/> etc.) so the two surfaces can never drift.
    /// Click runs on the UI thread; the actions marshal their own work.
    /// </summary>
    internal static class WarningGlyphMenu
    {
        public static ContextMenu Build(ITextBuffer buffer, IReadOnlyList<CodeIssueInfo> issues)
        {
            var menu = new ContextMenu();

            for (var i = 0; i < issues.Count; i++)
            {
                var issue = issues[i];
                if (i > 0) menu.Items.Add(new Separator());

                menu.Items.Add(new MenuItem
                {
                    Header    = $"{issue.RuleId}: {Truncate(issue.Message, 90)}",
                    IsEnabled = false,
                });

                foreach (var fix in issue.FixActions)
                {
                    // Suppress-type engine fixes are represented by the dedicated
                    // suppress/disable items below (same filter as LightbulbSource).
                    if (fix.FixType == (int)FixType.Suppress) continue;

                    var fixAction = new FixAction(buffer, fix, issue.RuleId, issue.AutoFixable);
                    var item = new MenuItem { Header = "Fix: " + fix.Label };
                    item.Click += (_, __) => fixAction.Invoke(CancellationToken.None);
                    menu.Items.Add(item);
                }

                if (!string.IsNullOrEmpty(issue.RuleId))
                {
                    // Line / script / session / everywhere — narrowest first, from the same
                    // factory the lightbulb uses. Each action refreshes analysis itself, including
                    // the two that edit no buffer text and would otherwise leave the squiggles up.
                    foreach (var action in SuppressionActions.ForIssue(buffer, issue))
                    {
                        var captured = action;
                        var item = new MenuItem { Header = captured.DisplayText };
                        item.Click += (_, __) => captured.Invoke(CancellationToken.None);
                        menu.Items.Add(item);
                    }
                }
            }

            return menu;
        }

        private static string Truncate(string text, int max) =>
            text.Length <= max ? text : text.Substring(0, max - 1) + "…";
    }
}
