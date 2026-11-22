using System;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using MessagePack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Spec 047 (WEB-014, contracts/workspace-ai.md §2) — a filter that matches nothing says so and offers to clear it; clearing
/// brings back the folders that were open before. Before, the tree just went blank, which read as "the schema is gone".
/// </summary>
public sealed class SchemaTreeFilterEmptyTests : BunitContext
{
    private const string Server = "localhost";
    private const string Db = "Northwind";

    public SchemaTreeFilterEmptyTests()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        Services.AddSingleton<ISchemaCacheStore>(new SchemaCacheStore(adapter));
        Services.AddSingleton<IEngineBridge>(new FakeEngineBridge());
        Services.AddSingleton<ISchemaSync>(new FakeSchemaSync());
    }

    private static SchemaPhaseObject Table(string name) => new() { SchemaName = "dbo", ObjectName = name, ObjectType = 0 };

    private async Task<IRenderedComponent<SchemaTreeComponent>> RenderOpenAsync()
    {
        var payload = new SchemaPhasePayload
        {
            DatabaseName = Db, Phase = 1, Checksum = "c",
            Schemas = new[] { new SchemaPhaseSchema { Name = "dbo", Objects = new[] { Table("Customers"), Table("Region") } } },
        };
        await Services.GetRequiredService<ISchemaCacheStore>().SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server, DatabaseName = Db, PhaseA = MessagePackSerializer.Serialize(payload), FetchedAt = DateTimeOffset.UtcNow,
        });
        var cut = Render<SchemaTreeComponent>(p => p.Add(c => c.ServerCanonicalIdentity, Server).Add(c => c.DatabaseName, Db));
        cut.Instance.ExpandForTest(Db);
        cut.Instance.ExpandForTest(Db + "/dbo");
        cut.Instance.ExpandForTest(Db + "/dbo/Tables");
        cut.Render();
        return cut;
    }

    private static string[] ObjectNames(IRenderedComponent<SchemaTreeComponent> cut) =>
        cut.FindAll("[data-testid='schema-tree-object']").Select(o => o.GetAttribute("data-qualifier")!).ToArray();

    [Fact]
    public async Task A_filter_with_no_match_says_so_and_Clear_restores_the_open_folders()
    {
        var cut = await RenderOpenAsync();
        var before = ObjectNames(cut);
        Assert.Equal(new[] { "[dbo].[Customers]", "[dbo].[Region]" }, before);

        cut.Find("[data-testid=schema-filter]").Input("zzzz");

        cut.WaitForAssertion(() => Assert.Equal("No objects match 'zzzz'.", cut.Find("[data-testid=schema-filter-empty] .akml-schema-filter-empty-text").TextContent.Trim()));
        Assert.Empty(ObjectNames(cut));
        var clear = cut.Find("[data-testid=schema-filter-clear]");
        Assert.Equal("Clear filter", clear.TextContent.Trim());

        clear.Click();

        Assert.Equal(string.Empty, cut.Find("[data-testid=schema-filter]").GetAttribute("value") ?? string.Empty);
        Assert.Empty(cut.FindAll("[data-testid=schema-filter-empty]"));
        Assert.Equal(before, ObjectNames(cut));
    }

    [Fact]
    public async Task A_filter_that_matches_shows_no_empty_row()
    {
        var cut = await RenderOpenAsync();

        cut.Find("[data-testid=schema-filter]").Input("Reg");

        cut.WaitForAssertion(() => Assert.Equal(new[] { "[dbo].[Region]" }, ObjectNames(cut)));
        Assert.Empty(cut.FindAll("[data-testid=schema-filter-empty]"));
    }
}
