using AkmlSql.Web.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SnippetsPage = AkmlSql.Web.Pages.Snippets;

namespace AkmlSql.Web.Tests.Snippets;

/// <summary>
/// Spec 041 (T185, research R59, FR-086, SC-012) — deleting a personal snippet asks first, in the app; declining keeps
/// it, and the browser's own confirm is never used.
/// </summary>
public sealed class SnippetDeleteConfirmTests : BunitContext
{
    private readonly SnippetStore _store;
    private readonly FakeDialogService _dialogs = new();

    public SnippetDeleteConfirmTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var adapter = new InMemoryIndexedDbAdapter();
        _store = new SnippetStore(adapter);
        Services.AddSingleton<ISnippetStore>(_store);
        Services.AddSingleton<IDiagnosticsRingBuffer>(new DiagnosticsRingBuffer(adapter));
        Services.AddSingleton<IDialogService>(_dialogs);
    }

    private async Task<IRenderedComponent<SnippetsPage>> OpenMineAsync()
    {
        await _store.SaveAsync(new WebSnippet
        {
            Metadata = new WebSnippetMetadata { Shortcode = "mysel", Title = "My select" },
            Body = new[] { "SELECT 1;" },
        });
        var page = Render<SnippetsPage>();
        page.WaitForAssertion(() => Assert.Contains(page.FindAll("[data-testid=snippet-row]"), r => r.GetAttribute("data-shortcode") == "mysel"));
        page.FindAll("[data-testid=snippet-row]").First(r => r.GetAttribute("data-shortcode") == "mysel").Click();
        return page;
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Delete_asks_first_and_only_a_yes_deletes(bool answer, bool kept)
    {
        _dialogs.ConfirmAnswer = _ => answer;
        var page = await OpenMineAsync();

        page.Find("[data-testid=snippet-delete]").Click();

        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Equal("Delete snippet", confirm.Title);
        Assert.Contains("mysel", confirm.Message);
        Assert.Equal("Delete", confirm.ConfirmLabel);
        Assert.True(confirm.Destructive);
        page.WaitForAssertion(async () =>
            Assert.Equal(kept, (await _store.ListAsync()).Any(s => s.Metadata.Shortcode == "mysel")));
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier is "confirm" or "prompt" or "alert");
    }

    // Spec 041 (T203, FR-085) — the one page header every page shares.
    private static void HasThePageHeader(AngleSharp.Dom.IParentNode page, string title)
    {
        var header = page.QuerySelector("[data-testid=page-header]")!;
        Assert.Contains("akml-page-header", header.ClassList);
        var h1 = header.QuerySelector("[data-testid=page-title]")!;
        Assert.Equal("H1", h1.TagName);
        Assert.Contains("akml-page-title", h1.ClassList);
        Assert.Equal(title, h1.TextContent.Trim());
        Assert.Contains("akml-page-actions", header.QuerySelector("[data-testid=page-actions]")!.ClassList);
    }

    [Fact]
    public async Task Snippets_has_the_shared_page_header_and_its_selected_row_uses_the_shared_style()
    {
        var page = await OpenMineAsync();

        HasThePageHeader(page.Find(".akml-page"), "Snippets");
        var selected = page.Find("[data-testid=snippet-row][aria-selected=true]");
        Assert.Equal("mysel", selected.GetAttribute("data-shortcode"));
        Assert.Contains("akml-list-row", selected.ClassList);
        Assert.Contains("is-selected", selected.ClassList);
    }

    // Spec 041 (T205, FR-088) — with nothing selected the editor side says what to do.
    [Fact]
    public void With_nothing_selected_the_editor_says_what_to_do()
    {
        var page = Render<SnippetsPage>();

        var empty = page.WaitForElement("[data-testid=snippet-editor-empty]");
        Assert.Contains("akml-empty", empty.ClassList);
        Assert.Contains("New", empty.TextContent);
    }
}
