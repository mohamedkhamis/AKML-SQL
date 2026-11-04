using System.Collections.Generic;
using System.Linq;
using System.Text;
using AkmlSql.Core.Text;
using Xunit;

namespace AkmlSql.Core.Tests.Text;

/// <summary>
/// Spec 046 (FR-020, FR-021, contracts/chat-rendering.md Part 1): the block parser both chat
/// renderers share. Answers are rendered from these blocks — prose, headings, lists, and SQL in code
/// panels with their own Copy / Insert — so the markers the old panel showed as raw text ("```sql",
/// "-", "**") never reach the reader. Nothing may be dropped: an unclosed fence keeps every line.
///
/// <para>Each test compares a compact signature of the parse (see <see cref="Describe"/>) so a
/// case reads as one line: <c>P(...)</c> paragraph, <c>H2(...)</c> heading, <c>UL(a|b)</c> /
/// <c>OL1(a|b)</c> lists, <c>CODE1[sql]:...</c> code block (<c>!</c> = unclosed), <c>**x**</c> bold,
/// <c>`x`</c> inline code, <c>⏎</c> a line break.</para>
/// </summary>
public class ChatMarkdownTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n \n\t")]
    public void Empty_or_whitespace_text_has_no_blocks(string? text)
        => Assert.Empty(ChatMarkdown.Parse(text));

    [Fact]
    public void One_paragraph()
        => Assert.Equal("P(Use COUNT(*).)", Describe("Use COUNT(*)."));

    [Fact]
    public void Lines_of_one_paragraph_keep_their_breaks()
        => Assert.Equal("P(Msg 156, Level 15⏎Incorrect syntax)", Describe("Msg 156, Level 15\nIncorrect syntax"));

    [Fact]
    public void A_blank_line_separates_paragraphs()
        => Assert.Equal("P(first) P(second)", Describe("first\n\nsecond"));

    [Theory]
    [InlineData("# Title", "H1(Title)")]
    [InlineData("## Steps", "H2(Steps)")]
    [InlineData("### Detail", "H3(Detail)")]
    [InlineData("#### Too deep", "P(#### Too deep)")]
    [InlineData("#NoSpace", "P(#NoSpace)")]
    public void Headings_one_to_three(string text, string expected)
        => Assert.Equal(expected, Describe(text));

    [Fact]
    public void Dash_and_star_bullets_form_one_unordered_list()
        => Assert.Equal("UL(one|two|three)", Describe("- one\n* two\n- three"));

    [Fact]
    public void Numbered_items_form_one_ordered_list_with_its_start()
        => Assert.Equal("OL1(first|second)", Describe("1. first\n2. second"));

    [Fact]
    public void An_ordered_list_can_start_later()
        => Assert.Equal("OL3(third)", Describe("3. third"));

    [Fact]
    public void A_list_then_a_paragraph()
        => Assert.Equal("UL(a|b) P(After the list.)", Describe("- a\n- b\n\nAfter the list."));

    [Fact]
    public void An_indented_line_continues_the_list_item()
        => Assert.Equal("OL1(**Step one**: do x more detail|Step two)",
            Describe("1. **Step one**: do x\n   more detail\n2. Step two"));

    [Fact]
    public void A_nested_bullet_list_splits_the_ordered_list_and_resumes_its_numbering()
        => Assert.Equal("OL1(first) UL(sub) OL2(second)", Describe("1. first\n   - sub\n2. second"));

    [Fact]
    public void Bold_star_text_is_not_a_bullet()
        => Assert.Equal("P(**Note**: careful)", Describe("**Note**: careful"));

    [Fact]
    public void A_sql_fence_between_prose()
    {
        var text = "Here is the query:\n\n```sql\nSELECT 1;\nSELECT 2;\n```\n\nRun it.";
        Assert.Equal("P(Here is the query:) CODE1[sql]:SELECT 1;⏎SELECT 2; P(Run it.)", Describe(text));
    }

    [Fact]
    public void A_fence_right_after_a_paragraph_line_still_opens()
        => Assert.Equal("P(Query:) CODE1[sql]:SELECT 1;", Describe("Query:\n```sql\nSELECT 1;\n```"));

    [Fact]
    public void Two_fences_are_numbered_in_order()
    {
        var blocks = ChatMarkdown.Parse("```sql\nSELECT 1;\n```\ntext\n```sql\nSELECT 2;\n```");
        var code = blocks.OfType<ChatCodeBlock>().ToList();

        Assert.Equal(new[] { 1, 2 }, code.Select(c => c.Index));
        Assert.Equal(new[] { "SELECT 1;", "SELECT 2;" }, code.Select(c => c.Code));
        Assert.Equal(2, ChatMarkdown.CountCodeBlocks(blocks));
    }

    [Fact]
    public void A_fence_without_a_language()
        => Assert.Equal("CODE1[]:SELECT 1;", Describe("```\nSELECT 1;\n```"));

    [Theory]
    [InlineData("```SQL\nx\n```", "CODE1[sql]:x")]
    [InlineData("```  TSql  \nx\n```", "CODE1[tsql]:x")]
    [InlineData("```sql title\nx\n```", "CODE1[sql]:x")]
    public void The_language_is_lower_cased_trimmed_and_first_token(string text, string expected)
        => Assert.Equal(expected, Describe(text));

    [Fact]
    public void An_unclosed_fence_keeps_every_remaining_line()
    {
        var blocks = ChatMarkdown.Parse("Intro\n```sql\nSELECT a\nFROM t\n\nWHERE x = 1");
        var code = Assert.IsType<ChatCodeBlock>(blocks.Last());

        Assert.True(code.Unclosed);
        Assert.Equal("SELECT a\nFROM t\n\nWHERE x = 1", code.Code);
        Assert.Equal("P(Intro) CODE1[sql]!:SELECT a⏎FROM t⏎⏎WHERE x = 1", Describe("Intro\n```sql\nSELECT a\nFROM t\n\nWHERE x = 1"));
    }

    [Fact]
    public void Markers_inside_a_fence_are_code_not_markdown()
        => Assert.Equal("CODE1[sql]:-- comment⏎# not a heading⏎- not a bullet",
            Describe("```sql\n-- comment\n# not a heading\n- not a bullet\n```"));

    [Fact]
    public void An_indented_fence_strips_its_indent_from_the_code()
        => Assert.Equal("CODE1[sql]:SELECT 1⏎  FROM t", Describe("   ```sql\n   SELECT 1\n     FROM t\n   ```"));

    [Fact]
    public void Inline_code_and_bold()
        => Assert.Equal("P(Use `COUNT(*)` on **every** table.)", Describe("Use `COUNT(*)` on **every** table."));

    [Fact]
    public void Inline_markers_parse_into_their_kinds()
    {
        var paragraph = Assert.IsType<ChatParagraph>(Assert.Single(ChatMarkdown.Parse("a `b` **c**")));

        Assert.Equal(
            new[] { (ChatInlineKind.Text, "a "), (ChatInlineKind.Code, "b"), (ChatInlineKind.Text, " "), (ChatInlineKind.Bold, "c") },
            paragraph.Inlines.Select(i => (i.Kind, i.Text)));
    }

    [Theory]
    [InlineData("an `unclosed tick", "P(an `unclosed tick)")]
    [InlineData("an **unclosed bold", "P(an **unclosed bold)")]
    [InlineData("Use ```sql to start", "P(Use ```sql to start)")]
    public void Unclosed_inline_markers_stay_literal(string text, string expected)
        => Assert.Equal(expected, Describe(text));

    [Fact]
    public void A_double_backtick_span_can_hold_a_backtick()
        => Assert.Equal("P(type ``a`b`` here)", Describe("type ``a`b`` here"));

    [Fact]
    public void Crlf_input_parses_like_lf_input()
    {
        const string lf = "# T\n\n- a\n- b\n\n```sql\nSELECT 1;\n```\n\nend\nline";
        Assert.Equal(Describe(lf), Describe(lf.Replace("\n", "\r\n")));
    }

    [Fact]
    public void A_horizontal_rule_separates_blocks_and_renders_nothing()
        => Assert.Equal("P(above) P(below)", Describe("above\n\n---\n\nbelow"));

    [Fact]
    public void The_reported_answer_shape_parses_into_prose_and_one_code_panel()
    {
        // The 2026-11-04 screenshot: prose, a fenced block with USE/GO and a long SELECT, then nothing.
        var text = "counts, grouping correctly so each table appears only once, sorted from the largest to the smallest:\n\n" +
                   "```sql\nUSE dbCentralQuota;\nGO\n\nSELECT\n    s.Name AS SchemaName,\n    t.Name AS TableName\nFROM sys.tables AS t\n```";
        var blocks = ChatMarkdown.Parse(text);

        Assert.IsType<ChatParagraph>(blocks[0]);
        var code = Assert.IsType<ChatCodeBlock>(blocks[1]);
        Assert.False(code.Unclosed);
        Assert.StartsWith("USE dbCentralQuota;\nGO\n\nSELECT", code.Code);
        Assert.Equal(2, blocks.Count);
    }

    // ── signature ───────────────────────────────────────────────────────────────────────────

    private static string Describe(string text)
        => string.Join(" ", ChatMarkdown.Parse(text).Select(Describe));

    private static string Describe(ChatBlock block) => block switch
    {
        ChatParagraph p => $"P({Inlines(p.Inlines)})",
        ChatHeading h => $"H{h.Level}({Inlines(h.Inlines)})",
        ChatListBlock l => (l.Ordered ? $"OL{l.Start}(" : "UL(") + string.Join("|", l.Items.Select(Inlines)) + ")",
        ChatCodeBlock c => $"CODE{c.Index}[{c.Language}]{(c.Unclosed ? "!" : "")}:{c.Code.Replace("\n", "⏎")}",
        _ => "?",
    };

    private static string Inlines(IReadOnlyList<ChatInline> inlines)
    {
        var sb = new StringBuilder();
        foreach (var inline in inlines)
        {
            sb.Append(inline.Kind switch
            {
                ChatInlineKind.Bold => $"**{inline.Text}**",
                ChatInlineKind.Code => inline.Text.Contains('`') ? $"``{inline.Text}``" : $"`{inline.Text}`",
                _ => inline.Text,
            });
        }
        return sb.ToString().Replace("\n", "⏎");
    }
}
