using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SnippetsPage = AkmlSql.Web.Pages.Snippets;

namespace AkmlSql.Web.Tests.Snippets;

/// <summary>Spec 042 (task T097, T107, FR-066) - the Snippets page shows one database kind at a time and creates snippets in it.</summary>
public sealed class SnippetsPageKindFilterTests : BunitContext
{
    private readonly SnippetStore _store;

    public SnippetsPageKindFilterTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var adapter = new InMemoryIndexedDbAdapter();
        _store = new SnippetStore(adapter);
        Services.AddSingleton<ISnippetStore>(_store);
        Services.AddSingleton<IDiagnosticsRingBuffer>(new DiagnosticsRingBuffer(adapter));
        Services.AddSingleton<IDialogService>(new FakeDialogService());
    }

    private IRenderedComponent<SnippetsPage> Open()
    {
        var page = Render<SnippetsPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=snippet-row]")));
        return page;
    }

    private static string[] Codes(IRenderedComponent<SnippetsPage> page) =>
        page.FindAll("[data-testid=snippet-row]").Select(r => r.GetAttribute("data-shortcode")!).ToArray();

    [Fact]
    public void With_no_connection_every_kind_is_listed_and_the_postgresql_ones_are_badged()
    {
        var page = Open();

        var rows = page.FindAll("[data-testid=snippet-row]");
        Assert.Contains(rows, r => r.QuerySelector("[data-testid=snippet-kind-badge]") != null);
        Assert.Contains(rows, r => r.QuerySelector("[data-testid=snippet-kind-badge]") == null);
        Assert.Equal("-1", page.Find("[data-testid=snippet-kind-filter]").GetAttribute("value"));
    }

    [Fact]
    public void The_filter_shows_only_the_postgresql_snippets_or_only_the_sql_server_ones()
    {
        var page = Open();
        var all = Codes(page).Length;

        page.Find("[data-testid=snippet-kind-filter]").Change("1");
        var postgres = page.FindAll("[data-testid=snippet-row]");
        Assert.NotEmpty(postgres);
        Assert.All(postgres, r => Assert.NotNull(r.QuerySelector("[data-testid=snippet-kind-badge]")));

        page.Find("[data-testid=snippet-kind-filter]").Change("0");
        var sqlServer = page.FindAll("[data-testid=snippet-row]");
        Assert.NotEmpty(sqlServer);
        Assert.All(sqlServer, r => Assert.Null(r.QuerySelector("[data-testid=snippet-kind-badge]")));

        Assert.Equal(all, postgres.Count + sqlServer.Count);
    }

    [Fact]
    public async Task A_new_snippet_made_under_the_postgresql_filter_is_a_postgresql_snippet()
    {
        var page = Open();
        page.Find("[data-testid=snippet-kind-filter]").Change("1");

        page.Find("[data-testid=snippet-new]").Click();
        page.Find("#akml-snippet-shortcode").Input("mypg");
        page.Find("[data-testid=snippet-save]").Click();

        await Task.Yield();
        page.WaitForAssertion(async () =>
        {
            var saved = (await _store.ListAsync()).FirstOrDefault(s => s.Metadata.Shortcode == "mypg");
            Assert.NotNull(saved);
            Assert.Equal(1, saved!.Metadata.Kind);
        });
    }
}
