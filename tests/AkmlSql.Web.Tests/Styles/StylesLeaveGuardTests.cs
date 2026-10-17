using System.Linq;
using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.Web.Services;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using StylesPage = AkmlSql.Web.Pages.Styles;

namespace AkmlSql.Web.Tests.Styles;

/// <summary>
/// Review findings WEB-12 and WEB-14 on the Format styles page: leaving with unsaved changes asks the same question as
/// switching styles (Cancel stays, with the change); a saved style leaves without asking; the Import control is a focusable
/// file input.
/// </summary>
public sealed class StylesLeaveGuardTests : BunitContext
{
    private const string Defaults = "SQL Prompt defaults";
    private readonly ProfileStore _store;
    private readonly FakeDialogService _dialogs = new() { ConfirmAnswer = _ => false };

    public StylesLeaveGuardTests()
    {
        _store = new ProfileStore(new InMemoryIndexedDbAdapter());
        _store.SaveDocumentAsync(null, SqlPromptStyleDocument.CreateDefault(Defaults)).GetAwaiter().GetResult();
        Services.AddSingleton<IProfileStore>(_store);
        Services.AddSingleton<IFormatterService>(new FormatterService());
        Services.AddSingleton<IDialogService>(_dialogs);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<StylesPage> Open()
    {
        var page = Render<StylesPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=style-row]")));
        page.FindAll("[data-testid=style-row]").First(b => b.GetAttribute("data-style") == Defaults).Click();
        page.WaitForAssertion(() => Assert.Equal(Defaults, page.Find("[data-testid=style-name]").TextContent));
        return page;
    }

    [Fact]
    public void Leaving_with_unsaved_changes_asks_and_Cancel_stays_with_them()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        var page = Open();
        page.Find("[data-testid=page-lists]").Click();
        page.Find("[data-testid='option-lists.placeCommasBeforeItems']").Change(true);
        Assert.NotEmpty(page.FindAll("[data-testid=style-dirty]"));
        var before = nav.Uri;

        nav.NavigateTo("/snippets");

        var confirm = Assert.Single(_dialogs.Confirms);
        Assert.Equal("Unsaved changes", confirm.Title);
        Assert.Equal(before, nav.Uri);
        Assert.NotEmpty(page.FindAll("[data-testid=style-dirty]"));

        _dialogs.ConfirmAnswer = _ => true;
        nav.NavigateTo("/snippets");
        Assert.EndsWith("/snippets", nav.Uri);
    }

    [Fact]
    public void A_style_without_changes_leaves_without_asking()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        Open();

        nav.NavigateTo("/snippets");

        Assert.Empty(_dialogs.Confirms);
        Assert.EndsWith("/snippets", nav.Uri);
    }

    [Fact]
    public void Import_is_a_focusable_file_input_inside_its_label()
    {
        var page = Render<StylesPage>();
        var input = page.Find("[data-testid=style-import]");

        Assert.Equal("file", input.GetAttribute("type"));
        Assert.Contains("akml-visually-hidden", input.ClassList);
        Assert.NotEqual("-1", input.GetAttribute("tabindex"));
    }
}
