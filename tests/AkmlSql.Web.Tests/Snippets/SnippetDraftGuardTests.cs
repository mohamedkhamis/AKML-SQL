using AkmlSql.Web.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SnippetsPage = AkmlSql.Web.Pages.Snippets;

namespace AkmlSql.Web.Tests.Snippets;

/// <summary>
/// Review findings on the Snippets page:
/// <list type="bullet">
///   <item>WEB-11 — an unsaved draft is never replaced without asking: selecting another snippet, New, Refresh and leaving
///   the page all ask; Cancel keeps the draft; a successful Save ends it.</item>
///   <item>WEB-13 — the list is one tab stop; Arrow keys move the active option and Enter selects it.</item>
///   <item>WEB-14 — the Import control is a focusable file input, not a hidden one.</item>
/// </list>
/// </summary>
public sealed class SnippetDraftGuardTests : BunitContext
{
    private const string Marker = "SELECT 918273 AS unsaved_marker;";
    private readonly SnippetStore _store;
    private readonly FakeDialogService _dialogs = new() { ConfirmAnswer = _ => false };

    public SnippetDraftGuardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var adapter = new InMemoryIndexedDbAdapter();
        _store = new SnippetStore(adapter);
        Services.AddSingleton<ISnippetStore>(_store);
        Services.AddSingleton<IDiagnosticsRingBuffer>(new DiagnosticsRingBuffer(adapter));
        Services.AddSingleton<IDialogService>(_dialogs);
    }

    private IRenderedComponent<SnippetsPage> OpenWithDraft()
    {
        var page = Render<SnippetsPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=snippet-row]")));
        page.Find("[data-testid=snippet-new]").Click();
        page.Find("#akml-snippet-shortcode").Input("draft1");
        page.Find("#akml-snippet-body").Input(Marker);
        return page;
    }

    private static string Body(IRenderedComponent<SnippetsPage> page) => page.Find("#akml-snippet-body").GetAttribute("value") ?? string.Empty;

    [Fact]
    public void Selecting_another_snippet_asks_and_Cancel_keeps_the_draft()
    {
        var page = OpenWithDraft();

        page.FindAll("[data-testid=snippet-row]")[0].Click();

        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Equal("Unsaved changes", confirm.Title);
        Assert.Equal(Marker, Body(page));

        _dialogs.ConfirmAnswer = _ => true;
        page.FindAll("[data-testid=snippet-row]")[0].Click();
        Assert.NotEqual(Marker, Body(page));
    }

    [Theory]
    [InlineData("snippet-new")]
    [InlineData("snippet-refresh")]
    public void New_and_Refresh_ask_before_replacing_a_draft(string button)
    {
        var page = OpenWithDraft();

        page.Find($"[data-testid={button}]").Click();

        Assert.Single(_dialogs.Confirms);
        Assert.Equal(Marker, Body(page));
    }

    [Fact]
    public void A_saved_draft_is_no_longer_one()
    {
        var page = OpenWithDraft();
        page.Find("[data-testid=snippet-save]").Click();
        page.WaitForAssertion(() => Assert.Contains("Saved", page.Find("[data-testid=snippet-status]").TextContent));

        page.FindAll("[data-testid=snippet-row]")[0].Click();

        Assert.Empty(_dialogs.Confirms);
    }

    [Fact]
    public void Leaving_the_page_with_a_draft_asks_and_Cancel_stays()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var page = OpenWithDraft();
        var before = nav.Uri;

        nav.NavigateTo("/styles");

        Assert.Single(_dialogs.Confirms);
        Assert.Equal(before, nav.Uri);
        Assert.Equal(Marker, Body(page));
    }

    [Fact]
    public void The_list_is_one_tab_stop_moved_by_arrows_and_Enter_selects()
    {
        var page = Render<SnippetsPage>();
        page.WaitForAssertion(() => Assert.True(page.FindAll("[data-testid=snippet-row]").Count >= 2));
        var list = page.Find("[data-testid=snippet-list]");
        Assert.Equal("0", list.GetAttribute("tabindex"));
        var first = list.GetAttribute("aria-activedescendant");

        list.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        var second = page.Find("[data-testid=snippet-list]").GetAttribute("aria-activedescendant");
        Assert.NotEqual(first, second);

        page.Find("[data-testid=snippet-list]").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        var option = page.Find("#" + second);
        Assert.Equal("true", option.GetAttribute("aria-selected"));
        Assert.Equal(option.GetAttribute("data-shortcode"), page.Find("#akml-snippet-shortcode").GetAttribute("value"));
    }

    [Fact]
    public void Import_is_a_focusable_file_input_inside_its_label()
    {
        var page = Render<SnippetsPage>();
        var input = page.Find("[data-testid=snippet-import]");

        Assert.Equal("file", input.GetAttribute("type"));
        Assert.Contains("akml-visually-hidden", input.ClassList);   // in the tab order, not display:none
        Assert.NotEqual("-1", input.GetAttribute("tabindex"));
        Assert.Contains("Import", page.Find("[data-testid=snippet-import-label]").TextContent);
    }
}
