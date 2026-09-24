using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Formatting.SqlPrompt;
using StylesPage = AkmlSql.Web.Pages.Styles;
using AkmlSql.Web.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Styles;

/// <summary>
/// The Format styles page as a user drives it: pick a style, open an option page, change an
/// option — the preview changes and says so — save, and the change is still there next time.
/// </summary>
public sealed class StylesPageTests : BunitContext
{
    private readonly InMemoryIndexedDbAdapter _db = new();
    private readonly ProfileStore _store;
    private const string Defaults = "SQL Prompt defaults";

    public StylesPageTests()
    {
        _store = new ProfileStore(_db);
        // A style at SQL Prompt's defaults, so each test starts from SQL Prompt's own baseline.
        _store.SaveDocumentAsync(null, SqlPromptStyleDocument.CreateDefault(Defaults)).GetAwaiter().GetResult();
        Services.AddSingleton<IProfileStore>(_store);
        Services.AddSingleton<IFormatterService>(new FormatterService());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private IRenderedComponent<StylesPage> Open(string style)
    {
        var page = Render<StylesPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=style-row]")));
        page.FindAll("[data-testid=style-row]").First(b => b.GetAttribute("data-style") == style).Click();
        page.WaitForAssertion(() => Assert.Equal(style, page.Find("[data-testid=style-name]").TextContent));
        return page;
    }

    private static string Preview(IRenderedComponent<StylesPage> page) => page.Find("[data-testid=preview]").TextContent;

    [Fact]
    public void Shows_sql_prompts_four_categories_and_fourteen_pages()
    {
        var page = Open("Khamis Style");
        foreach (var section in SqlPromptOptionCatalog.Sections)
            Assert.NotNull(page.Find($"[data-testid=page-{section.Id}]"));
        Assert.Equal(14, page.FindAll("[data-testid^=page-]").Count);
    }

    [Fact]
    public void Every_page_shows_its_options_with_sql_prompts_names()
    {
        var page = Open("Khamis Style");
        foreach (var section in SqlPromptOptionCatalog.Sections)
        {
            page.Find($"[data-testid=page-{section.Id}]").Click();
            foreach (var option in section.Options)
                Assert.NotNull(page.Find($"[data-testid='option-{option.Path}']"));
        }
    }

    [Fact]
    public void Changing_an_option_changes_the_preview_and_marks_the_style_unsaved()
    {
        var page = Open(Defaults);
        page.Find("[data-testid=page-lists]").Click();
        var before = Preview(page);
        Assert.Empty(page.FindAll("[data-testid=style-dirty]"));

        page.Find("[data-testid='option-lists.placeCommasBeforeItems']").Change(true);

        var after = Preview(page);
        Assert.NotEqual(before, after);
        Assert.Contains(after.Replace("\r\n", "\n").Split('\n'), line => line.TrimStart().StartsWith(", c.CompanyName AS Company"));
        Assert.Matches(@"Preview updated · \d+ lines? changed", page.Find("[data-testid=preview-status]").TextContent);
        Assert.NotEmpty(page.FindAll("[data-testid=preview] .moved"));
        Assert.NotEmpty(page.FindAll("[data-testid=style-dirty]"));
    }

    [Fact]
    public void Every_choice_on_a_page_reaches_the_preview()
    {
        var page = Open(Defaults);
        page.Find("[data-testid=page-caseExpressions]").Click();
        var baseline = Preview(page);

        page.Find("[data-testid='option-caseExpressions.placeThenOnNewLine']").Change(true);
        var thenOnNewLine = Preview(page);
        Assert.NotEqual(baseline, thenOnNewLine);

        page.Find("[data-testid='option-caseExpressions.thenAlignment']").Change("toWhenExpression");
        Assert.NotEqual(thenOnNewLine, Preview(page));
    }

    [Fact]
    public async Task Saving_keeps_the_change_for_next_time()
    {
        var page = Open(Defaults);
        page.Find("[data-testid=page-casing]").Click();
        page.Find("[data-testid='option-casing.reservedKeywords']").Change("lowercase");
        page.Find("[data-testid=style-save]").Click();
        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[data-testid=style-dirty]")));
        Assert.Contains("Saved", page.Find("[data-testid=styles-status]").TextContent);

        var saved = (await _store.ListAsync()).Single(r => r.Name == Defaults);
        Assert.Equal("lowercase", (await _store.GetDocumentAsync(saved.Id))!.Get("casing.reservedKeywords"));

        // A fresh page (a reload) shows the saved value.
        var reopened = Open(Defaults);
        reopened.Find("[data-testid=page-casing]").Click();
        Assert.Equal("lowercase", reopened.Find("[data-testid='option-casing.reservedKeywords']").GetAttribute("value"));
    }

    [Fact]
    public async Task Editing_a_built_in_saves_an_edited_copy_that_can_be_reset()
    {
        var page = Open("Collapsed");
        page.Find("[data-testid=page-lists]").Click();
        page.Find("[data-testid='option-lists.alignAliases']").Change(true);
        page.Find("[data-testid=style-save]").Click();
        page.WaitForAssertion(() => Assert.NotNull(page.Find("[data-testid=style-reset]")));
        Assert.True((await _store.GetDocumentAsync("builtin.collapsed"))!.GetBool("lists.alignAliases"));
        Assert.True((await _store.GetAsync("builtin.collapsed"))!.IsCustomizedBuiltIn);
    }

    [Fact]
    public void Resetting_an_option_returns_it_to_sql_prompts_default()
    {
        var page = Open(Defaults);
        page.Find("[data-testid=page-whitespace]").Click();
        var original = Preview(page);
        page.Find("[data-testid='option-whitespace.numberOfSpacesInTabs']").Change("8");
        Assert.NotEqual(original, Preview(page));

        page.Find("[data-testid='reset-whitespace.numberOfSpacesInTabs']").Click();
        Assert.Equal(original, Preview(page));
        Assert.Empty(page.FindAll("[data-testid=style-dirty]"));
    }

    [Fact]
    public void A_gated_option_is_disabled_until_its_switch_is_on()
    {
        var page = Open(Defaults);
        page.Find("[data-testid=page-dml]").Click();
        var threshold = page.Find("[data-testid='option-dml.collapseStatementsShorterThan']");
        Assert.True(threshold.HasAttribute("disabled"));

        page.Find("[data-testid='option-dml.collapseShortStatements']").Change(true);
        Assert.False(page.Find("[data-testid='option-dml.collapseStatementsShorterThan']").HasAttribute("disabled"));
    }

    [Fact]
    public void Classic_styles_say_saving_makes_them_sql_prompt_styles()
    {
        var page = Open("Khamis Style");
        Assert.NotNull(page.Find("[data-testid=style-classic-notice]"));
    }

    [Fact]
    public void My_sql_previews_what_the_user_pastes()
    {
        var page = Open(Defaults);
        page.Find("[data-testid=preview-mysql]").Click();
        page.Find("[data-testid=preview-input]").Input("select a,b from t where x=1");
        page.WaitForAssertion(() => Assert.Contains("x = 1", Preview(page)), System.TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Use_in_editor_makes_the_style_active()
    {
        var page = Open("Collapsed");
        page.Find("[data-testid=style-set-active]").Click();
        page.WaitForAssertion(() => Assert.Equal("builtin.collapsed", _store.GetActiveIdAsync().Result));
    }
}
