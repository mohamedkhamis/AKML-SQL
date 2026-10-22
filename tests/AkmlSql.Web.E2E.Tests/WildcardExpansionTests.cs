using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Reported 2026-10-22: Tab on a <c>SELECT *</c> should expand the wildcard to the table's columns, as SQL Prompt does in SSMS,
/// whether the caret is just after the '*' or just before it (before, Tab indented the line and opened the column list); Tab in
/// an open completion list commits the highlighted item; and the list reads like SQL Prompt's (a kind badge, the name, the type
/// right-aligned in a quieter colour).
/// </summary>
public sealed class WildcardExpansionTests(ITestOutputHelper output)
{
    private const string Script = "USE [Northwind]\n\n        SELECT    * FROM dbo.Customers;";
    private const string AfterStar = " FROM dbo.Customers;";   // ArrowLeft this many times from the end: the caret is just after '*'

    private static ILocator Content(IPage page) => page.Locator("[data-testid='sql-editor'] .cm-content");

    private static async Task<string> TextAsync(IPage page) =>
        (await Content(page).EvaluateAsync<string>("e => e.cmView ? e.cmView.view.state.doc.toString() : e.innerText")).Replace("\r", "");

    private static async Task CaretFromEndAsync(IPage page, int lefts)
    {
        await Content(page).ClickAsync();
        await page.Keyboard.PressAsync("Control+End");
        for (int i = 0; i < lefts; i++) await page.Keyboard.PressAsync("ArrowLeft");
    }

    [SkippableTheory]
    [InlineData(0, "just after the star")]
    [InlineData(1, "just before the star")]
    public async Task Tab_on_the_star_expands_it_to_the_columns(int extraLeft, string where)
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, database: "Northwind");
        var page = s.Page;
        await s.SetEditorTextAsync(Script);
        await page.WaitForTimeoutAsync(2_500);   // the schema of Northwind
        await CaretFromEndAsync(page, AfterStar.Length + extraLeft);

        await page.Keyboard.PressAsync("Tab");

        await Assertions.Expect(Content(page)).ToContainTextAsync("CustomerID,", new() { Timeout = 10_000 });
        var text = await page.Locator("[data-testid='sql-editor'] .cm-content").InnerTextAsync();
        output.WriteLine($"{where}: {text.Replace("\n", " / ")}");
        Assert.DoesNotContain("*", text);
        Assert.Contains("Fax FROM dbo.Customers;", text);
        Assert.Contains("        SELECT    CustomerID,", text);   // the line was not indented
        await Assertions.Expect(page.Locator(".cm-tooltip-autocomplete")).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task Tab_in_an_open_column_list_inserts_the_highlighted_column_and_the_list_reads_like_SQL_Prompts()
    {
        await using var s = await WebAppFixture.WithSqlOrSkipAsync(output, database: "Northwind");
        var page = s.Page;
        await s.SetEditorTextAsync("SELECT c. FROM dbo.Customers AS c;");
        await page.WaitForTimeoutAsync(2_500);
        await Content(page).ClickAsync();
        await page.Keyboard.PressAsync("Control+Home");
        for (int i = 0; i < "SELECT c.".Length; i++) await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("Control+Space");
        var list = page.Locator(".cm-tooltip-autocomplete");
        await Assertions.Expect(list).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // A kind badge ("C" for a column) and the type right-aligned, upright, in the quieter colour.
        var look = await list.EvaluateAsync<string[]>(@"t => {
            const li = t.querySelector('li');
            const icon = li.querySelector('.cm-completionIcon'), detail = li.querySelector('.cm-completionDetail');
            const d = getComputedStyle(detail), liRect = li.getBoundingClientRect(), dRect = detail.getBoundingClientRect();
            return [getComputedStyle(icon, '::after').content, d.fontStyle, String(Math.round(liRect.right - dRect.right)), getComputedStyle(li).display];
        }");
        output.WriteLine(string.Join(" | ", look));
        Assert.Equal("\"C\"", look[0]);
        Assert.Equal("normal", look[1]);
        Assert.True(int.Parse(look[2]) <= 16, $"the detail ends {look[2]} px from the row's right edge");
        Assert.Equal("flex", look[3]);

        // The engine's reply can replace the first (local) list once: navigate the settled one.
        await page.WaitForTimeoutAsync(1_000);
        if (await list.CountAsync() == 0) await page.Keyboard.PressAsync("Control+Space");
        await Assertions.Expect(list.Locator("li[aria-selected]")).ToHaveCountAsync(1, new() { Timeout = 10_000 });
        await page.Keyboard.PressAsync("ArrowDown");
        var highlighted = (await list.Locator("li[aria-selected] .cm-completionLabel").InnerTextAsync(new() { Timeout = 10_000 })).Trim();
        await page.Keyboard.PressAsync("Tab");

        await Assertions.Expect(Content(page)).ToContainTextAsync($"SELECT c.{highlighted} FROM dbo.Customers AS c;");
    }
}
