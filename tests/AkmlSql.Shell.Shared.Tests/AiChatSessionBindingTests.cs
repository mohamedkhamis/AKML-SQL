using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AkmlSql.Shell.Shared.Ai;
using AkmlSql.Shell.Shared.Refactoring;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 036 (US1) T008 — chat panel session binding (FR-027/FR-028). With no editor bound,
    /// the panel must refuse to send (no fabricated session id reaches the engine) and say why;
    /// the header must reflect the bound server.database.
    ///
    /// <para>The single-panel constraint from <see cref="AiChatPanelCopyButtonTests"/> applies:
    /// <see cref="AiChatPanel"/> merges the process-global ThemeRegistry dictionary, so this
    /// class constructs a panel in exactly one [StaFact], keeps the rest static-only, and shares
    /// the "AkmlSql ThemeRegistry" collection so the two panel classes never run in parallel.</para>
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class AiChatSessionBindingTests
    {
        [Fact]
        public void Real_session_resolution_returns_null_outside_a_shell_host()
        {
            // No IVsTextManager / no active view outside VS/SSMS — the resolver must report
            // "unbound" (null) rather than fabricating an id (R1/R2).
            Assert.Null(RefactorCommandHelper.TryGetActiveRealSessionId());
        }

        [StaFact]
        public void Panel_refuses_to_send_when_unbound_and_header_reflects_binding()
        {
            var panel = new AiChatPanel();

            // Header reflects SetDatabaseContext: server.database when bound, explicit
            // not-connected state when cleared (FR-027).
            panel.SetDatabaseContext("sqlserver01.Sales");
            Assert.Contains(FindTextBlocks(panel), t => t.Text.Contains("sqlserver01.Sales"));

            panel.SetDatabaseContext(string.Empty);
            Assert.Contains(FindTextBlocks(panel), t => t.Text.Contains("Not connected"));

            // Send with no editor bound: the no-connection message must appear and the request
            // must never reach the engine path (whose failure text would be the
            // engine-not-connected error instead) (FR-028).
            var input = FindChatInput(panel);
            input.Text = "what tables do I have?";
            FindSendButton(panel).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            // Spec 036 US3: message bubbles are read-only TextBoxes now (FR-017), so visible
            // text lives in TextBlocks (header/strip) AND TextBoxes (bubbles).
            var texts = FindVisibleTexts(panel);
            Assert.Contains(texts, t => t.Contains("No database connection"));
            Assert.DoesNotContain(texts, t => t.Contains("engine is not connected"));
        }

        /// <summary>All user-visible text: TextBlock.Text plus read-only bubble TextBox.Text.</summary>
        private static List<string> FindVisibleTexts(DependencyObject root)
        {
            var texts = new List<string>();
            foreach (var tb in FindAll<TextBlock>(root))
                texts.Add(tb.Text ?? string.Empty);
            foreach (var box in FindAll<TextBox>(root))
            {
                if (box.IsReadOnly)
                    texts.Add(box.Text ?? string.Empty);
            }
            return texts;
        }

        private static TextBox FindChatInput(DependencyObject root)
        {
            foreach (var tb in FindAll<TextBox>(root))
            {
                if (AutomationProperties.GetName(tb) == "Chat input")
                    return tb;
            }
            throw new System.InvalidOperationException("Chat input TextBox not found");
        }

        private static Button FindSendButton(DependencyObject root)
        {
            foreach (var b in FindAll<Button>(root))
            {
                if ("Send".Equals(b.Content as string, System.StringComparison.Ordinal))
                    return b;
            }
            throw new System.InvalidOperationException("Send button not found");
        }

        private static List<TextBlock> FindTextBlocks(DependencyObject root) => FindAll<TextBlock>(root);

        private static List<T> FindAll<T>(DependencyObject root) where T : DependencyObject
        {
            var found = new List<T>();
            Walk(root, found);
            return found;
        }

        private static void Walk<T>(DependencyObject node, List<T> found) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is T match)
                    found.Add(match);
                if (child is DependencyObject d)
                    Walk(d, found);
            }
        }
    }
}
