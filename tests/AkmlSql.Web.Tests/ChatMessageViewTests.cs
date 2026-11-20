using AkmlSql.Web.Shared;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests;

/// <summary>
/// Spec 046 (US6, FR-020/FR-021, contracts/chat-rendering.md Part 3): the web chat renders an
/// answer the way the SSMS panel does — from the same parser (<c>ChatMarkdown</c>, in Core) — as
/// paragraphs, headings, lists, bold and inline code, with every fenced block in its own code panel
/// carrying its own Copy and, for SQL, Insert at caret.
/// </summary>
public sealed class ChatMessageViewTests : BunitContext
{
    private BunitJSModuleInterop CopyAnswers(bool copied)
    {
        var ui = JSInterop.SetupModule("./js/akml-ui.js");
        ui.Mode = JSRuntimeMode.Loose;
        ui.Setup<bool>("copyText", _ => true).SetResult(copied);
        return ui;
    }

    private IRenderedComponent<ChatMessageView> RenderView(string text, Action<string>? onInsert = null)
        => Render<ChatMessageView>(p =>
        {
            p.Add(v => v.Text, text);
            if (onInsert != null) p.Add(v => v.OnInsert, onInsert);
        });

    [Fact]
    public void Prose_renders_as_paragraphs_headings_lists_bold_and_inline_code()
    {
        var cut = RenderView("## Steps\n\nUse **COUNT** on `Orders`.\n\n1. first\n2. second\n\n- a\n- b");

        Assert.Equal("Steps", cut.Find("h5").TextContent);
        var paragraph = cut.Find("p.akml-chat-p");
        Assert.Equal("COUNT", paragraph.QuerySelector("strong")!.TextContent);
        Assert.Equal("Orders", paragraph.QuerySelector("code.akml-chat-inline")!.TextContent);
        Assert.Equal(2, cut.FindAll("ol > li").Count);
        Assert.Equal(2, cut.FindAll("ul > li").Count);
        Assert.DoesNotContain("##", cut.Markup);
        Assert.DoesNotContain("**", cut.Markup);
    }

    [Fact]
    public void An_ordered_list_keeps_its_start_number()
    {
        var cut = RenderView("3. third");

        Assert.Equal("3", cut.Find("ol").GetAttribute("start"));
    }

    [Fact]
    public void Each_fence_is_a_code_panel_with_copy_and_insert()
    {
        var cut = RenderView("Here:\n\n```sql\nSELECT 1;\n```", _ => { });

        var panel = Assert.Single(cut.FindAll("[data-testid='chat-code-block']"));
        Assert.Equal("SELECT 1;", panel.QuerySelector("pre code")!.TextContent);
        var copy = panel.QuerySelector("button[aria-label='Copy SQL']")!;
        var insert = panel.QuerySelector("button[aria-label='Insert at caret']")!;
        Assert.Equal("Copy this SQL", copy.GetAttribute("title"));
        Assert.Equal("Insert this SQL at the editor caret", insert.GetAttribute("title"));
        Assert.DoesNotContain("```", cut.Markup);
    }

    [Fact]
    public void Two_fences_are_labelled_n_of_N()
    {
        var cut = RenderView("```sql\nSELECT 1;\n```\n\n```sql\nSELECT 2;\n```", _ => { });

        Assert.Single(cut.FindAll("button[aria-label='Copy SQL block 2 of 2']"));
        Assert.Single(cut.FindAll("button[aria-label='Insert SQL block 1 of 2']"));
    }

    [Fact]
    public void Insert_hands_over_that_blocks_sql()
    {
        string? inserted = null;
        var cut = RenderView("```sql\nSELECT 1;\n```\n\n```sql\nSELECT 2;\n```", sql => inserted = sql);

        cut.Find("button[aria-label='Insert SQL block 2 of 2']").Click();

        Assert.Equal("SELECT 2;", inserted);
    }

    [Fact]
    public void Without_an_editor_there_is_no_insert()
    {
        var cut = RenderView("```sql\nSELECT 1;\n```");   // no OnInsert delegate

        Assert.Empty(cut.FindAll("button[aria-label='Insert at caret']"));
        Assert.Single(cut.FindAll("button[aria-label='Copy SQL']"));
    }

    [Fact]
    public void A_non_sql_fence_offers_copy_only()
    {
        var cut = RenderView("```json\n{ \"a\": 1 }\n```", _ => { });

        Assert.Single(cut.FindAll("button[aria-label='Copy code']"));
        Assert.Empty(cut.FindAll("button[aria-label='Insert at caret']"));
    }

    [Fact]
    public void Copy_puts_the_blocks_code_on_the_clipboard_and_says_copied()
    {
        var ui = CopyAnswers(true);
        var cut = RenderView("```sql\nSELECT 1;\n```");

        cut.Find("button[aria-label='Copy SQL']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("SELECT 1;", Assert.Single(ui.Invocations["copyText"]).Arguments[0]);
            Assert.Equal("Copied", cut.Find("button[aria-label='Copy SQL']").TextContent.Trim());
        });
    }

    [Fact]
    public void A_failed_copy_says_so()
    {
        CopyAnswers(false);
        var cut = RenderView("```sql\nSELECT 1;\n```");

        cut.Find("button[aria-label='Copy SQL']").Click();

        cut.WaitForAssertion(() => Assert.Equal("Copy failed", cut.Find("button[aria-label='Copy SQL']").TextContent.Trim()));
    }

    [Fact]
    public void An_unclosed_fence_still_shows_its_code()
    {
        var cut = RenderView("Partial:\n\n```sql\nSELECT a\nFROM t");

        Assert.Equal("SELECT a\nFROM t", cut.Find("[data-testid='chat-code-block'] pre code").TextContent);
    }
}
