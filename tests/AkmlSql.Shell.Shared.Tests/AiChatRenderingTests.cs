#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using AkmlSql.Core.Text;
using AkmlSql.Shell.Shared.Ai;
using Xunit;
using static AkmlSql.Shell.Shared.Tests.ChatPanelTestText;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 046 (US2, FR-020/FR-021/FR-025, contracts/chat-rendering.md Part 2): an answer renders
    /// like Copilot Chat — prose as rich text (paragraphs, headings, lists, bold, inline code in the
    /// editor font) and each fenced block as its own code panel with its own Copy and, for SQL,
    /// Insert into query. The renderer is driven directly; the panel tests cover its wiring.
    /// </summary>
    [Collection("AkmlSql ThemeRegistry")]
    public class AiChatRenderingTests
    {
        private static StackPanel Render(string text, List<string>? copied = null, List<string>? inserted = null,
            bool copyOk = true, bool insertOk = true)
            => ChatMessageRenderer.Build(ChatMarkdown.Parse(text),
                code => { copied?.Add(code); return copyOk; },
                code => { inserted?.Add(code); return insertOk; });

        [StaFact]
        public void Prose_is_one_read_only_borderless_rich_text_host()
        {
            var body = Render("Use **COUNT** on `Orders`.\n\nSecond paragraph.");

            var host = Assert.IsType<RichTextBox>(Assert.Single(body.Children.Cast<UIElement>()));
            Assert.True(host.IsReadOnly);
            Assert.Equal(0.0, host.BorderThickness.Left);
            Assert.Equal(ChatMessageRenderer.MessageTextAutomationName, AutomationProperties.GetName(host));
            Assert.Equal("Use COUNT on Orders.\r\nSecond paragraph.", Of(host));
        }

        [StaFact]
        public void Bold_and_inline_code_render_as_their_own_runs()
        {
            var host = Assert.IsType<RichTextBox>(Render("Use **COUNT** on `Orders`.").Children[0]);
            var paragraph = Assert.IsType<Paragraph>(Assert.Single(host.Document.Blocks));
            var inlines = paragraph.Inlines.ToList();

            Assert.Contains(inlines, i => i is Bold b && new TextRange(b.ContentStart, b.ContentEnd).Text == "COUNT");
            var code = Assert.Single(inlines.OfType<Run>(), r => r.Text == "Orders");
            Assert.Equal(AkmlSql.Shell.Shared.Ui.Theme.Typography.MonoFont, code.FontFamily);
        }

        [StaFact]
        public void Headings_and_lists_render_as_flow_blocks_without_markers()
        {
            var host = Assert.IsType<RichTextBox>(Render("## Steps\n\n1. first\n2. second\n\n- a\n- b").Children[0]);
            var blocks = host.Document.Blocks.ToList();

            var heading = Assert.IsType<Paragraph>(blocks[0]);
            Assert.Equal(FontWeights.SemiBold, heading.FontWeight);
            var ordered = Assert.IsType<List>(blocks[1]);
            Assert.Equal(TextMarkerStyle.Decimal, ordered.MarkerStyle);
            Assert.Equal(2, ordered.ListItems.Count);
            var bullets = Assert.IsType<List>(blocks[2]);
            Assert.Equal(TextMarkerStyle.Disc, bullets.MarkerStyle);
            Assert.DoesNotContain("##", Of(host));
        }

        [StaFact]
        public void A_sql_fence_becomes_a_code_panel_with_copy_and_insert()
        {
            var body = Render("Here:\n\n```sql\nSELECT 1;\n```\n\nDone.");

            Assert.Equal(3, body.Children.Count);   // prose, code panel, prose
            var panel = Assert.IsType<Border>(body.Children[1]);
            var code = Assert.Single(FindByName<TextBox>(panel, ChatMessageRenderer.CodeTextAutomationName));
            Assert.Equal("SELECT 1;", code.Text);
            Assert.True(code.IsReadOnly);
            Assert.Equal(TextWrapping.NoWrap, code.TextWrapping);

            var copy = Assert.Single(FindByName<Button>(panel, "Copy SQL"));
            var insert = Assert.Single(FindByName<Button>(panel, "Insert into query"));
            Assert.Equal(ChatMessageRenderer.CopySqlToolTip, copy.ToolTip);
            Assert.Equal(ChatMessageRenderer.InsertToolTip, insert.ToolTip);
            Assert.DoesNotContain("```", string.Join("\n", body.Children.OfType<RichTextBox>().Select(Of)));
        }

        [StaFact]
        public void Two_blocks_are_labelled_n_of_N_and_each_action_gets_its_own_code()
        {
            var copied = new List<string>();
            var inserted = new List<string>();
            var body = Render("```sql\nSELECT 1;\n```\n\n```sql\nSELECT 2;\n```", copied, inserted);

            Click(Assert.Single(FindByName<Button>(body, "Copy SQL block 2 of 2")));
            Click(Assert.Single(FindByName<Button>(body, "Insert SQL block 1 of 2")));

            Assert.Equal(new[] { "SELECT 2;" }, copied);
            Assert.Equal(new[] { "SELECT 1;" }, inserted);
        }

        [StaFact]
        public void Action_outcomes_flash_on_the_button()
        {
            var body = Render("```sql\nSELECT 1;\n```", copyOk: true, insertOk: false);
            var copy = Assert.Single(FindByName<Button>(body, "Copy SQL"));
            var insert = Assert.Single(FindByName<Button>(body, "Insert into query"));

            Click(copy);
            Click(insert);

            Assert.Equal(ChatMessageRenderer.CopiedFeedback, copy.Content);
            Assert.Equal(ChatMessageRenderer.NoQueryWindowFeedback, insert.Content);
        }

        [StaFact]
        public void A_failed_copy_says_so()
        {
            var body = Render("```sql\nSELECT 1;\n```", copyOk: false);
            var copy = Assert.Single(FindByName<Button>(body, "Copy SQL"));

            Click(copy);

            Assert.Equal(ChatMessageRenderer.CopyFailedFeedback, copy.Content);
        }

        [StaFact]
        public void An_unclosed_fence_still_renders_its_code_panel()
        {
            var body = Render("Partial:\n\n```sql\nSELECT a\nFROM t");

            var code = Assert.Single(FindByName<TextBox>(body, ChatMessageRenderer.CodeTextAutomationName));
            Assert.Equal("SELECT a\nFROM t", code.Text);
        }

        [StaFact]
        public void A_non_sql_block_offers_copy_only()
        {
            var body = Render("```powershell\nGet-Date\n```");

            Assert.Single(FindByName<Button>(body, "Copy code"));
            Assert.Empty(FindAll<Button>(body).Where(b =>
                (AutomationProperties.GetName(b) ?? string.Empty).StartsWith("Insert", System.StringComparison.Ordinal)));
            Assert.Equal(ChatMessageRenderer.CopyCodeToolTip, FindByName<Button>(body, "Copy code")[0].ToolTip);
        }

        [StaFact]
        public void Every_action_is_a_named_tab_stop()
        {
            var body = Render("```sql\nSELECT 1;\n```\n\n```sql\nSELECT 2;\n```");

            var buttons = FindAll<Button>(body);
            Assert.Equal(4, buttons.Count);
            foreach (var button in buttons)
            {
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                Assert.True(button.IsTabStop);
                Assert.NotNull(button.ToolTip);
            }
        }

        [StaFact]
        public void Line_breaks_inside_a_paragraph_are_kept()
        {
            var host = Assert.IsType<RichTextBox>(Render("Msg 156, Level 15\nIncorrect syntax").Children[0]);

            Assert.Equal("Msg 156, Level 15\r\nIncorrect syntax", Of(host));
        }
    }
}
