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
/// Spec 047 (WEB-013, contracts/workspace-ai.md §2) — when the engine says its login cannot see every object (no VIEW
/// DEFINITION), the explorer does not present a short list as complete: the procedure and function groups read "(may be
/// incomplete)" instead of a count, and a note names the account and the database and says how to fix it. Before, the
/// tree showed "Stored Procedures (0)" for a database with seven, with nothing to say why.
/// </summary>
public sealed class SchemaTreeMetadataLimitedTests : BunitContext
{
    private const string Server = "localhost";
    private const string Db = "Northwind";

    private readonly FakeEngineBridge _bridge = new();

    public SchemaTreeMetadataLimitedTests()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        Services.AddSingleton<ISchemaCacheStore>(new SchemaCacheStore(adapter));
        Services.AddSingleton<IEngineBridge>(_bridge);
        Services.AddSingleton<ISchemaSync>(new FakeSchemaSync());
    }

    private static SchemaPhaseObject Obj(string name, int type) => new() { SchemaName = "dbo", ObjectName = name, ObjectType = type };

    private async Task<IRenderedComponent<SchemaTreeComponent>> RenderAsync(bool limited)
    {
        var payload = new SchemaPhasePayload
        {
            DatabaseName = Db, Phase = 1, Checksum = "c", MetadataLimited = limited,
            Schemas = new[] { new SchemaPhaseSchema { Name = "dbo", Objects = new[] { Obj("Region", 0), Obj("Ten Most Expensive Products", 2) } } },
        };
        await Services.GetRequiredService<ISchemaCacheStore>().SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server, DatabaseName = Db, PhaseA = MessagePackSerializer.Serialize(payload), FetchedAt = DateTimeOffset.UtcNow,
        });
        var cut = Render<SchemaTreeComponent>(p => p.Add(c => c.ServerCanonicalIdentity, Server).Add(c => c.DatabaseName, Db));
        cut.Instance.ExpandForTest(Db);
        cut.Instance.ExpandForTest(Db + "/dbo");
        cut.Render();
        return cut;
    }

    private static string[] KindLabels(IRenderedComponent<SchemaTreeComponent> cut) =>
        cut.FindAll(".akml-schema-tree-kind .akml-schema-tree-label").Select(k => k.TextContent.Trim()).ToArray();

    [Fact]
    public async Task Limited_metadata_marks_the_routine_groups_and_names_the_account_and_the_database()
    {
        _bridge.EngineRunsAs = @"NT AUTHORITY\SYSTEM";

        var cut = await RenderAsync(limited: true);

        Assert.Equal(new[] { "Tables (1)", "Views (0)", "Stored Procedures (may be incomplete)", "Functions (may be incomplete)" }, KindLabels(cut));
        var note = cut.Find("[data-testid=schema-metadata-limited]");
        Assert.Equal(
            @"The engine signs in as NT AUTHORITY\SYSTEM, which cannot see every object in Northwind. Grant it VIEW DEFINITION on the database, or connect with a login that can.",
            note.TextContent.Trim());
    }

    [Fact]
    public async Task An_unknown_account_is_called_the_engines_account()
    {
        _bridge.EngineRunsAs = null;

        var cut = await RenderAsync(limited: true);

        var note = cut.Find("[data-testid=schema-metadata-limited]").TextContent.Trim();
        Assert.Contains("the engine's account", note, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Northwind", note);
        Assert.Contains("VIEW DEFINITION", note);
    }

    [Fact]
    public async Task Complete_metadata_keeps_the_counts_and_shows_no_note()
    {
        _bridge.EngineRunsAs = @"NT AUTHORITY\SYSTEM";

        var cut = await RenderAsync(limited: false);

        Assert.Equal(new[] { "Tables (1)", "Views (0)", "Stored Procedures (1)", "Functions (0)" }, KindLabels(cut));
        Assert.Empty(cut.FindAll("[data-testid=schema-metadata-limited]"));
    }
}
