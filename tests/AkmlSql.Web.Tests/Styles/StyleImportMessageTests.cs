using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.Web.Services;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using StylesPage = AkmlSql.Web.Pages.Styles;

namespace AkmlSql.Web.Tests.Styles;

/// <summary>
/// Spec 047 (UX-002, contracts/workspace-ai.md §4) — importing a file that is not a SQL Prompt style says so in one plain
/// sentence and adds nothing. Before, the page printed the JSON parser's message ("ExpectedNull, nope LineNumber: 0 …"),
/// and a JSON file of another kind (a snippet) was taken as a style.
/// </summary>
public sealed class StyleImportMessageTests : BunitContext
{
    private const string NotAStyle = "That file is not a SQL Prompt style (.json).";
    private readonly ProfileStore _store;

    public StyleImportMessageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var adapter = new InMemoryIndexedDbAdapter();
        _store = new ProfileStore(adapter);
        Services.AddSingleton<IProfileStore>(_store);
        Services.AddSingleton<IFormatterService>(new FormatterService());
        Services.AddSingleton<IDialogService>(new FakeDialogService());
        Services.AddSingleton<IDiagnosticsRingBuffer>(new DiagnosticsRingBuffer(adapter));
    }

    private async Task<(IRenderedComponent<StylesPage> Page, int Before)> ImportAsync(string content, string fileName)
    {
        var before = (await _store.ListAsync()).Count;
        var page = Render<StylesPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=style-row]")));
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(content, fileName));
        return (page, before);
    }

    [Theory]
    [InlineData("nope", "notastyle.txt")]
    [InlineData("{ \"Metadata\": { \"Shortcode\": \"x\" }, \"Body\": [ \"SELECT 1;\" ] }", "snippet.json")]
    [InlineData("[ 1, 2, 3 ]", "array.json")]
    public async Task A_file_that_is_not_a_style_gets_one_plain_sentence_and_adds_nothing(string content, string fileName)
    {
        var (page, before) = await ImportAsync(content, fileName);

        page.WaitForAssertion(() => Assert.Equal(NotAStyle, page.Find("[data-testid=styles-status]").TextContent.Trim()));
        var status = page.Find("[data-testid=styles-status]").TextContent;
        Assert.DoesNotContain("Exception", status);
        Assert.DoesNotContain("LineNumber", status);
        Assert.DoesNotContain("BytePosition", status);
        Assert.Equal(before, (await _store.ListAsync()).Count);
    }

    [Fact]
    public async Task A_style_still_imports()
    {
        var (page, before) = await ImportAsync(SqlPromptStyleDocument.CreateDefault("Imported").ToJson(), "imported.json");

        page.WaitForAssertion(() => Assert.Equal("Imported", page.Find("[data-testid=style-name]").TextContent));
        Assert.Equal(before + 1, (await _store.ListAsync()).Count);
    }
}
