using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.Web.Services;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SnippetsPage = AkmlSql.Web.Pages.Snippets;

namespace AkmlSql.Web.Tests.Snippets;

/// <summary>
/// Spec 047 (UX-002, contracts/workspace-ai.md §4) — every snippet import says what happened, in a status a screen reader
/// announces: "Imported 'x'." for a snippet, and "No snippet was found in that file. Existing snippets are unchanged." for
/// anything else (a style, a text file). Before, a file that was not a snippet left no visible message.
/// </summary>
public sealed class SnippetImportMessageTests : BunitContext
{
    private const string NothingFound = "No snippet was found in that file. Existing snippets are unchanged.";
    private readonly SnippetStore _store;

    public SnippetImportMessageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var adapter = new InMemoryIndexedDbAdapter();
        _store = new SnippetStore(adapter);
        Services.AddSingleton<ISnippetStore>(_store);
        Services.AddSingleton<IDiagnosticsRingBuffer>(new DiagnosticsRingBuffer(adapter));
        Services.AddSingleton<IDialogService>(new FakeDialogService());
    }

    private async Task<(IRenderedComponent<SnippetsPage> Page, int Before)> ImportAsync(string content, string fileName)
    {
        var before = (await _store.ListAsync()).Count;
        var page = Render<SnippetsPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=snippet-row]")));
        page.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(content, fileName));
        return (page, before);
    }

    [Fact]
    public async Task A_snippet_is_imported_and_named()
    {
        var json = JsonSerializer.Serialize(new WebSnippet
        {
            Metadata = new WebSnippetMetadata { Shortcode = "x", Title = "X" },
            Body = new[] { "SELECT 1;" },
        });

        var (page, before) = await ImportAsync(json, "x.akmlsnippet");

        page.WaitForAssertion(() => Assert.Equal("Imported 'x'.", page.Find("[data-testid=snippet-status]").TextContent.Trim()));
        Assert.Equal(before + 1, (await _store.ListAsync()).Count);
    }

    [Theory]
    [InlineData("nope", "nope.txt")]
    [InlineData("[ 1, 2 ]", "array.akmlsnippet")]
    public async Task A_file_that_is_not_a_snippet_says_none_was_found(string content, string fileName)
    {
        var (page, before) = await ImportAsync(content, fileName);

        page.WaitForAssertion(() => Assert.Equal(NothingFound, page.Find("[data-testid=snippet-status]").TextContent.Trim()));
        Assert.Equal(before, (await _store.ListAsync()).Count);
    }

    [Fact]
    public async Task A_style_file_is_not_a_snippet()
    {
        var (page, before) = await ImportAsync(SqlPromptStyleDocument.CreateDefault("Team style").ToJson(), "team.json");

        page.WaitForAssertion(() => Assert.Equal(NothingFound, page.Find("[data-testid=snippet-status]").TextContent.Trim()));
        Assert.Equal(before, (await _store.ListAsync()).Count);
    }

    [Fact]
    public void The_status_is_announced_politely_and_is_there_before_any_message()
    {
        var page = Render<SnippetsPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=snippet-row]")));

        var status = page.Find("[data-testid=snippet-status]");   // present while empty, so the first message is announced
        Assert.Equal("polite", status.GetAttribute("aria-live"));
        Assert.Equal("status", status.GetAttribute("role"));
    }
}
