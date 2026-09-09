#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Shell.Shared.Ai;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 chat panel per-block insert: beside every SQL block's copy button sits an
    /// insert button that drops that block's SQL into the ACTIVE query editor at the caret
    /// (<see cref="AiEditorSqlInserter"/>). The DTE insertion itself needs a live host, so the
    /// tests route through <see cref="AiEditorSqlInserter.OverrideForTests"/> — always reset to
    /// null in a finally so no seam leaks across tests.
    ///
    /// <para>Same harness as <see cref="AiChatPanelCopyButtonTests"/>: the panel merges the
    /// process-global ThemeRegistry dictionary, so the "AkmlSql ThemeRegistry" collection
    /// serialises this class against the other panel-constructing classes.</para>
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class AiChatInsertSqlTests
    {
        [StaFact]
        public void Two_sql_blocks_get_labelled_insert_buttons_beside_their_copy_buttons()
        {
            var panel = new AiChatPanel();
            InvokeAddAssistantMessage(panel, "two blocks", new List<CodeActionDto>
            {
                new() { Label = "Copy Script", ActionType = "copyToClipboard", Code = "SELECT 1;" },
                new() { Label = "Copy Script 2", ActionType = "copyToClipboard", Code = "SELECT 2;" },
            });

            var first = Assert.Single(FindAll<Button>(panel, "⇩ Insert SQL block 1 of 2"));
            var second = Assert.Single(FindAll<Button>(panel, "⇩ Insert SQL block 2 of 2"));
            Assert.True(first.IsTabStop);
            Assert.True(second.IsTabStop);

            // Each insert button shares one horizontal row with its copy sibling.
            AssertShareRow(Assert.Single(FindAll<Button>(panel, "Copy SQL block 1 of 2")), first);
            AssertShareRow(Assert.Single(FindAll<Button>(panel, "Copy SQL block 2 of 2")), second);
        }

        [StaFact]
        public void A_single_sql_block_gets_one_insert_into_query_button()
        {
            var panel = new AiChatPanel();
            InvokeAddAssistantMessage(panel, "one block", new List<CodeActionDto>
            {
                new() { Label = "Copy Script", ActionType = "copyToClipboard", Code = "SELECT 1;" },
            });

            var insert = Assert.Single(FindAll<Button>(panel, "⇩ Insert into query"));
            Assert.True(insert.IsTabStop);
            AssertShareRow(Assert.Single(FindAll<Button>(panel, "Copy Script")), insert);
        }

        [StaFact]
        public void Clicking_a_blocks_insert_button_routes_that_blocks_sql_to_the_inserter()
        {
            var panel = new AiChatPanel();
            InvokeAddAssistantMessage(panel, "two blocks", new List<CodeActionDto>
            {
                new() { Label = "Copy Script", ActionType = "copyToClipboard", Code = "SELECT 1;" },
                new() { Label = "Copy Script 2", ActionType = "copyToClipboard", Code = "SELECT 2;" },
            });

            string? inserted = null;
            AiEditorSqlInserter.OverrideForTests = sql => { inserted = sql; return true; };
            try
            {
                var second = Assert.Single(FindAll<Button>(panel, "⇩ Insert SQL block 2 of 2"));
                second.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                Assert.Equal("SELECT 2;", inserted);
                Assert.Equal("✓ Inserted", second.Content);
            }
            finally
            {
                AiEditorSqlInserter.OverrideForTests = null;
            }
        }

        [StaFact]
        public void A_failed_insert_is_surfaced_on_the_button()
        {
            var panel = new AiChatPanel();
            InvokeAddAssistantMessage(panel, "one block", new List<CodeActionDto>
            {
                new() { Label = "Copy Script", ActionType = "copyToClipboard", Code = "SELECT 1;" },
            });

            AiEditorSqlInserter.OverrideForTests = _ => false;   // no active query editor
            try
            {
                var insert = Assert.Single(FindAll<Button>(panel, "⇩ Insert into query"));
                insert.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

                Assert.Equal("⚠ No active query", insert.Content);
            }
            finally
            {
                AiEditorSqlInserter.OverrideForTests = null;
            }
        }

        [StaFact]
        public void A_message_without_code_actions_adds_no_insert_buttons()
        {
            var panel = new AiChatPanel();
            InvokeAddAssistantMessage(panel, "plain answer, no SQL", null);

            var insertButtons = new List<Button>();
            Walk<Button>(panel, b =>
            {
                if (AutomationProperties.GetName(b)?.StartsWith("⇩ Insert", StringComparison.Ordinal) == true)
                    insertButtons.Add(b);
            });
            Assert.Empty(insertButtons);
        }

        // ── helpers ────────────────────────────────────────────────────────────

        private static void AssertShareRow(Button copyButton, Button insertButton)
        {
            var row = Assert.IsType<StackPanel>(copyButton.Parent);
            Assert.Equal(Orientation.Horizontal, row.Orientation);
            Assert.Same(row, insertButton.Parent);
            Assert.True(row.Children.IndexOf(copyButton) < row.Children.IndexOf(insertButton),
                "the insert button follows its copy sibling in the row");
        }

        private static void InvokeAddAssistantMessage(AiChatPanel panel, string text, List<CodeActionDto>? actions)
        {
            var method = typeof(AiChatPanel).GetMethod("AddAssistantMessage",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(method);
            // Spec 037 (US3): the method gained optional agentName/selectedAgentName parameters
            // for per-answer attribution; reflection must pass every parameter explicitly.
            method!.Invoke(panel, new object?[] { text, actions, null, null });
        }

        private static List<T> FindAll<T>(DependencyObject root, string automationName) where T : DependencyObject
        {
            var found = new List<T>();
            Walk(root, (T match) =>
            {
                if (AutomationProperties.GetName(match) == automationName)
                    found.Add(match);
            });
            return found;
        }

        private static void Walk<T>(DependencyObject node, Action<T> visit) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                if (child is T match)
                    visit(match);
                if (child is DependencyObject d)
                    Walk(d, visit);
            }
        }
    }
}
