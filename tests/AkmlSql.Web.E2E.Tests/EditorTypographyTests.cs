using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 043 — the SQL text surfaces in a real browser: the attributes that keep writing assistants out (US5, FR-036,
/// FR-037) and the editor's typography (US2). Needs Debug builds of the web edition and the engine and a local SQL
/// Server (the session helper skips otherwise).
/// </summary>
[Trait("Category", "BridgeE2E")]
public sealed class EditorTypographyTests(ITestOutputHelper output)
{
    private static readonly string[] OptOut = { "data-gramm", "data-gramm_editor", "data-enable-grammarly", "data-lt-active" };

    private static async Task AssertOptedOutAsync(ILocator element)
    {
        foreach (var name in OptOut) Assert.Equal("false", await element.GetAttributeAsync(name));
        Assert.Equal("false", await element.GetAttributeAsync("spellcheck"));
    }

    // ── US2: typography (T065, FR-016, FR-017, FR-019, FR-020, FR-021) ──────────────────────────────────────────

    private static async Task SelectThemeAsync(IPage page, string theme)
    {
        await ShellPolishTests.NavigateAsync(page, "settings/general");
        await page.Locator("#setting-theme-control").SelectOptionAsync(theme);
        await ShellPolishTests.NavigateAsync(page, "editor");
    }

    [SkippableTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task Lines_are_spaced_at_one_and_a_quarter_and_the_gutter_shares_their_height(string theme)
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        await SelectThemeAsync(s.Page, theme);
        await s.SetEditorTextAsync(string.Join("\n", Enumerable.Range(1, 40).Select(i => $"SELECT {i} AS n -- line {i}")));

        var m = await s.Page.EvaluateAsync<double[]>("""
            () => {
                const scroller = document.querySelector('[data-testid="sql-editor"] .cm-scroller');
                const cs = getComputedStyle(scroller);
                const line = document.querySelector('[data-testid="sql-editor"] .cm-line');
                const gutter = [...document.querySelectorAll('[data-testid="sql-editor"] .cm-lineNumbers .cm-gutterElement')].find(e => e.textContent.trim() === '1');
                return [parseFloat(cs.fontSize), parseFloat(cs.lineHeight), line.getBoundingClientRect().height, gutter.getBoundingClientRect().height,
                        line.getBoundingClientRect().top, gutter.getBoundingClientRect().top];
            }
            """);
        double fontSize = m[0], lineHeight = m[1], lineBox = m[2], gutterBox = m[3];
        output.WriteLine($"{theme}: font {fontSize}px, line-height {lineHeight}px, line box {lineBox}, gutter box {gutterBox}");
        Assert.InRange(lineHeight, fontSize * 1.25 - 0.5, fontSize * 1.25 + 0.5);
        Assert.InRange(lineBox, lineHeight - 0.5, lineHeight + 0.5);
        Assert.InRange(gutterBox, lineBox - 0.5, lineBox + 0.5);
        Assert.InRange(m[5], m[4] - 0.5, m[4] + 0.5);   // number and text start on the same line box
    }

    [SkippableFact]
    public async Task The_font_and_size_from_Settings_reach_the_editor_the_completion_list_and_the_other_SQL_pages()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        var page = s.Page;
        await ShellPolishTests.NavigateAsync(page, "settings/editor");
        await page.Locator("#setting-editor-font-family-control").SelectOptionAsync("Consolas");
        await page.Locator("#setting-editor-font-size-control").FillAsync("16");
        await page.Locator("#setting-editor-font-size-control").DispatchEventAsync("change");
        await ShellPolishTests.NavigateAsync(page, "editor");

        var content = page.Locator("[data-testid='sql-editor'] .cm-content");
        await Assertions.Expect(content).ToHaveCSSAsync("font-size", "16px");
        Assert.StartsWith("Consolas", await content.EvaluateAsync<string>("el => getComputedStyle(el).fontFamily"));

        // Keywords complete without a loaded schema; retry as SqlServerAliasWebTests does while the session settles.
        var popup = page.Locator(".cm-tooltip-autocomplete");
        for (int attempt = 0; attempt < 10 && await popup.CountAsync() == 0; attempt++)
        {
            await s.SetEditorTextAsync(string.Empty);
            await page.Keyboard.TypeAsync("SEL");
            await page.Keyboard.PressAsync("Control+Space");
            await page.WaitForTimeoutAsync(1_500);
        }
        await Assertions.Expect(popup).ToBeVisibleAsync();
        await Assertions.Expect(popup).ToHaveCSSAsync("font-size", "16px");
        await page.Keyboard.PressAsync("Escape");

        await ShellPolishTests.NavigateAsync(page, "styles");
        await page.GetByTestId("style-row").First.ClickAsync();
        var preview = page.GetByTestId("preview");
        await Assertions.Expect(preview).ToHaveCSSAsync("font-size", "16px");
        Assert.StartsWith("Consolas", await preview.EvaluateAsync<string>("el => getComputedStyle(el).fontFamily"));

        await ShellPolishTests.NavigateAsync(page, "history");
        Assert.Equal("16px", (await page.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).getPropertyValue('--akml-editor-font-size')")).Trim());
    }

    [SkippableFact]
    public async Task The_bracket_matching_the_one_at_the_caret_is_outlined()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);
        await s.SetEditorTextAsync("SELECT COUNT(*) FROM sys.objects");
        await s.Page.Keyboard.PressAsync("Home");
        for (int i = 0; i < "SELECT COUNT(".Length; i++) await s.Page.Keyboard.PressAsync("ArrowRight");

        var bracket = s.Page.Locator("[data-testid='sql-editor'] .cm-matchingBracket");
        await Assertions.Expect(bracket).ToHaveCountAsync(2);
        Assert.NotEqual("none", await bracket.First.EvaluateAsync<string>("el => getComputedStyle(el).outlineStyle"));
    }

    // ── US5: writing assistants (T094) ───────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task The_sql_editor_content_asks_writing_assistants_to_stay_out()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);

        var content = s.Page.Locator("[data-testid='sql-editor'] .cm-content");
        await AssertOptedOutAsync(content);
        Assert.Equal("off", await content.GetAttributeAsync("autocorrect"));
        Assert.Equal("off", await content.GetAttributeAsync("autocapitalize"));
    }

    [SkippableFact]
    public async Task The_snippet_body_asks_writing_assistants_to_stay_out()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);

        await ShellPolishTests.NavigateAsync(s.Page, "snippets");
        await s.Page.GetByTestId("snippet-new").ClickAsync();

        await AssertOptedOutAsync(s.Page.Locator("#akml-snippet-body"));
    }

    [SkippableFact]
    public async Task The_format_styles_input_asks_writing_assistants_to_stay_out_and_the_chat_box_does_not()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output);

        await ShellPolishTests.NavigateAsync(s.Page, "styles");
        await s.Page.GetByTestId("style-row").First.ClickAsync();
        await s.Page.GetByTestId("preview-mysql").ClickAsync();
        await AssertOptedOutAsync(s.Page.GetByTestId("preview-input"));

        await ShellPolishTests.NavigateAsync(s.Page, "editor");
        var composer = s.Page.Locator(".akml-chat-input");
        if (await composer.CountAsync() > 0)
        {
            foreach (var name in OptOut) Assert.Null(await composer.First.GetAttributeAsync(name));
        }
    }
}
