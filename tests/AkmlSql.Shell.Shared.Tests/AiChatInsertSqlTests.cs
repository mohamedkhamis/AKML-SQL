#nullable enable
using System;
using System.Collections.Generic;
using System.Windows.Automation;
using System.Windows.Controls;
using AkmlSql.Shell.Shared.Ai;
using Xunit;
using static AkmlSql.Shell.Shared.Tests.ChatPanelTestText;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 037 chat panel per-block insert, rewritten for spec 046 (US2, FR-021): every SQL block of
    /// an answer is its own code panel, and its Insert into query sits on that panel beside its Copy
    /// (it used to sit in a detached row under the bubble). The DTE insertion itself needs a live
    /// host, so the tests route through <see cref="AiEditorSqlInserter.OverrideForTests"/> — always
    /// reset to null in a finally so no seam leaks across tests. Spec 046 (US1, FR-008) also pins
    /// that the insert targets the query window the chat is bound to.
    ///
    /// <para>Same harness as <see cref="AiChatPanelCopyButtonTests"/>: the panel merges the
    /// process-global ThemeRegistry dictionary, so the "AkmlSql ThemeRegistry" collection
    /// serialises this class against the other panel-constructing classes.</para>
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class AiChatInsertSqlTests
    {
        private const string OneBlock = "Run this:\n\n```sql\nSELECT 1;\n```";
        private const string TwoBlocks = "First:\n\n```sql\nSELECT 1;\n```\n\nThen:\n\n```sql\nSELECT 2;\n```";

        [StaFact]
        public void Two_sql_blocks_get_labelled_insert_buttons_beside_their_copy_buttons()
        {
            var panel = new AiChatPanel();
            InvokeAddAssistantMessage(panel, TwoBlocks);

            var first = Assert.Single(FindByName<Button>(panel, "Insert SQL block 1 of 2"));
            var second = Assert.Single(FindByName<Button>(panel, "Insert SQL block 2 of 2"));
            Assert.True(first.IsTabStop);
            Assert.True(second.IsTabStop);
            Assert.Equal(ChatMessageRenderer.InsertContent, first.Content);
            Assert.Equal(ChatMessageRenderer.InsertToolTip, first.ToolTip);

            // Each insert button shares its code panel's action row with its copy sibling.
            AssertShareRow(Assert.Single(FindByName<Button>(panel, "Copy SQL block 1 of 2")), first);
            AssertShareRow(Assert.Single(FindByName<Button>(panel, "Copy SQL block 2 of 2")), second);
        }

        [StaFact]
        public void A_single_sql_block_gets_one_insert_into_query_button()
        {
            var panel = new AiChatPanel();
            InvokeAddAssistantMessage(panel, OneBlock);

            var insert = Assert.Single(FindByName<Button>(panel, "Insert into query"));
            Assert.True(insert.IsTabStop);
            AssertShareRow(Assert.Single(FindByName<Button>(panel, "Copy SQL")), insert);
        }

        [StaFact]
        public void Clicking_a_blocks_insert_button_routes_that_blocks_sql_to_the_inserter()
        {
            var panel = new AiChatPanel();
            InvokeAddAssistantMessage(panel, TwoBlocks);

            string? inserted = null;
            AiEditorSqlInserter.OverrideForTests = (sql, _) => { inserted = sql; return true; };
            try
            {
                var second = Assert.Single(FindByName<Button>(panel, "Insert SQL block 2 of 2"));
                Click(second);

                Assert.Equal("SELECT 2;", inserted);
                Assert.Equal(ChatMessageRenderer.InsertedFeedback, second.Content);
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
            InvokeAddAssistantMessage(panel, OneBlock);

            AiEditorSqlInserter.OverrideForTests = (_, _) => false;   // no query window
            try
            {
                var insert = Assert.Single(FindByName<Button>(panel, "Insert into query"));
                Click(insert);

                Assert.Equal(ChatMessageRenderer.NoQueryWindowFeedback, insert.Content);
            }
            finally
            {
                AiEditorSqlInserter.OverrideForTests = null;
            }
        }

        [StaFact]
        public void A_non_sql_block_gets_copy_but_no_insert()
        {
            var panel = new AiChatPanel();
            InvokeAddAssistantMessage(panel, "Config:\n\n```json\n{ \"a\": 1 }\n```");

            Assert.Single(FindByName<Button>(panel, "Copy code"));
            Assert.Empty(InsertButtons(panel));
        }

        /// <summary>
        /// Spec 046 (US1, FR-008): Insert into query targets the window the chat is bound to —
        /// the same binding the header names and Send uses — not whatever document is active.
        /// </summary>
        [StaFact]
        public void Insert_targets_the_bound_query_window()
        {
            var priorResolver = AiChatPanel.TestBindingResolver;
            AiChatPanel.TestBindingResolver = _ => ChatBinding.ForBound("s1", @"C:\Temp\SQLQuery1.sql", "srv01", "Sales");
            string? targetPath = "(not called)";
            AiEditorSqlInserter.OverrideForTests = (_, path) => { targetPath = path; return true; };
            try
            {
                var panel = new AiChatPanel();
                InvokeAddAssistantMessage(panel, OneBlock);

                Click(Assert.Single(FindByName<Button>(panel, "Insert into query")));

                Assert.Equal(@"C:\Temp\SQLQuery1.sql", targetPath);
            }
            finally
            {
                AiEditorSqlInserter.OverrideForTests = null;
                AiChatPanel.TestBindingResolver = priorResolver;
            }
        }

        [StaFact]
        public void Insert_with_no_bound_window_passes_no_path()
        {
            var priorResolver = AiChatPanel.TestBindingResolver;
            AiChatPanel.TestBindingResolver = _ => ChatBinding.ForNoDocument();
            string? targetPath = "(not called)";
            AiEditorSqlInserter.OverrideForTests = (_, path) => { targetPath = path; return false; };
            try
            {
                var panel = new AiChatPanel();
                InvokeAddAssistantMessage(panel, OneBlock);

                Click(Assert.Single(FindByName<Button>(panel, "Insert into query")));

                Assert.Null(targetPath);   // null → the inserter's active-document route
            }
            finally
            {
                AiEditorSqlInserter.OverrideForTests = null;
                AiChatPanel.TestBindingResolver = priorResolver;
            }
        }

        [Fact]
        public void The_inserter_hands_its_override_the_document_path()
        {
            (string Sql, string? Path)? seen = null;
            AiEditorSqlInserter.OverrideForTests = (sql, path) => { seen = (sql, path); return true; };
            try
            {
                Assert.True(AiEditorSqlInserter.TryInsertAtCaret("SELECT 1;", @"C:\Temp\q.sql"));
                Assert.Equal(("SELECT 1;", (string?)@"C:\Temp\q.sql"), seen);

                Assert.True(AiEditorSqlInserter.TryInsertAtCaret("SELECT 2;", null));
                Assert.Equal(("SELECT 2;", (string?)null), seen);
            }
            finally
            {
                AiEditorSqlInserter.OverrideForTests = null;
            }
        }

        [StaFact]
        public void A_message_without_sql_adds_no_insert_buttons()
        {
            var panel = new AiChatPanel();
            InvokeAddAssistantMessage(panel, "plain answer, no SQL");

            Assert.Empty(InsertButtons(panel));
        }

        // ── helpers ────────────────────────────────────────────────────────────

        private static List<Button> InsertButtons(AiChatPanel panel)
            => FindAll<Button>(panel).FindAll(b =>
                AutomationProperties.GetName(b)?.StartsWith("Insert", StringComparison.Ordinal) == true);

        private static void AssertShareRow(Button copyButton, Button insertButton)
        {
            var row = Assert.IsType<StackPanel>(copyButton.Parent);
            Assert.Equal(Orientation.Horizontal, row.Orientation);
            Assert.Same(row, insertButton.Parent);
            Assert.True(row.Children.IndexOf(copyButton) < row.Children.IndexOf(insertButton),
                "the insert button follows its copy sibling in the row");
        }
    }
}
