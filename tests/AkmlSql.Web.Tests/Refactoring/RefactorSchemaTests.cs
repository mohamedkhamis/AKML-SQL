using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using MessagePack;
using Xunit;

namespace AkmlSql.Web.Tests.Refactoring;

/// <summary>
/// Spec 047 (WEB-011, contracts/workspace-ai.md §3) — the browser's Expand refactorings get the connected database's
/// cached schema, and the Refactor menu says why an item cannot run. Before, the browser ran them with no schema at all,
/// so Expand INSERT columns only ever warned "Could not resolve columns", and the menu gave no reason.
/// </summary>
public sealed class RefactorSchemaTests
{
    internal static SchemaPhasePayload NorthwindRegionPhaseB() => new()
    {
        DatabaseName = "Northwind", Phase = 2, Checksum = "PhaseB:2",
        Schemas = new[]
        {
            new SchemaPhaseSchema
            {
                Name = "dbo",
                Objects = new[]
                {
                    new SchemaPhaseObject
                    {
                        SchemaName = "dbo", ObjectName = "Region", ObjectType = 0,
                        Columns = new[]
                        {
                            new SchemaPhaseColumn { Name = "RegionID", TypeName = "int", IsPrimaryKey = true },
                            new SchemaPhaseColumn { Name = "RegionDescription", TypeName = "nchar", MaxLength = 100 },
                        },
                    },
                    new SchemaPhaseObject
                    {
                        SchemaName = "dbo", ObjectName = "CustOrderHist", ObjectType = 2,
                        Parameters = new[] { new SchemaPhaseParameter { Name = "@CustomerID", TypeName = "nchar" } },
                    },
                },
            },
        },
    };

    internal static SchemaPhasePayload PhaseAOf(SchemaPhasePayload phaseB)
    {
        var a = MessagePackSerializer.Deserialize<SchemaPhasePayload>(MessagePackSerializer.Serialize(phaseB));
        a.Phase = 1;
        foreach (var s in a.Schemas)
            foreach (var o in s.Objects) { o.Columns = System.Array.Empty<SchemaPhaseColumn>(); o.Parameters = System.Array.Empty<SchemaPhaseParameter>(); }
        return a;
    }

    internal static SchemaSnapshot Snapshot(string server, string database, bool withPhaseB) => new()
    {
        ServerCanonicalIdentity = server,
        DatabaseName = database,
        PhaseA = MessagePackSerializer.Serialize(PhaseAOf(NorthwindRegionPhaseB())),
        PhaseB = withPhaseB ? MessagePackSerializer.Serialize(NorthwindRegionPhaseB()) : null,
    };

    [Theory]
    [InlineData(LightweightRefactorKind.ExpandInsertColumns, true)]
    [InlineData(LightweightRefactorKind.ExpandUpdateColumns, true)]
    [InlineData(LightweightRefactorKind.ExpandExecParameters, true)]
    [InlineData(LightweightRefactorKind.RemoveSemicolons, false)]
    [InlineData(LightweightRefactorKind.ConvertOldStyleJoins, false)]
    [InlineData(LightweightRefactorKind.Unformat, false)]
    public void Only_the_three_Expand_items_need_the_schema(LightweightRefactorKind kind, bool needs)
    {
        Assert.Equal(needs, RefactorSchema.NeedsSchema(kind));
    }

    [Fact]
    public void A_snapshot_with_columns_is_ready_and_carries_them()
    {
        var state = RefactorSchema.From(Snapshot("srv", "Northwind", withPhaseB: true));

        Assert.Equal(RefactorSchemaState.Ready, state.State);
        var region = state.Cache!.FindObject("dbo", "Region");
        Assert.NotNull(region);
        Assert.Equal(new[] { "RegionID", "RegionDescription" }, region!.Columns.ConvertAll(c => c.ColumnName));
        Assert.Equal("@CustomerID", state.Cache.FindObject("dbo", "CustOrderHist")!.Parameters[0].ParameterName);
    }

    [Fact]
    public void Names_only_is_still_loading_and_nothing_is_none()
    {
        Assert.Equal(RefactorSchemaState.StillLoading, RefactorSchema.From(Snapshot("srv", "Northwind", withPhaseB: false)).State);
        Assert.Null(RefactorSchema.From(Snapshot("srv", "Northwind", withPhaseB: false)).Cache);
        Assert.Equal(RefactorSchemaState.None, RefactorSchema.From(null).State);

        var unreadable = Snapshot("srv", "Northwind", withPhaseB: false);
        unreadable.PhaseB = new byte[] { 0xC1, 0x00 };   // not MessagePack: read as no columns yet
        Assert.Equal(RefactorSchemaState.StillLoading, RefactorSchema.From(unreadable).State);
    }

    [Fact]
    public async Task Only_the_connected_databases_snapshot_is_used()
    {
        var store = new SchemaCacheStore(new InMemoryIndexedDbAdapter());
        await store.SetAsync(Snapshot("srv", "Northwind", withPhaseB: true));
        var connection = new FakeActiveConnection { Server = "srv", Database = "Northwind" };

        Assert.Equal(RefactorSchemaState.Ready, (await RefactorSchema.LoadAsync(store, connection)).State);

        connection.Database = "Payroll";   // no snapshot of it
        Assert.Equal(RefactorSchemaState.None, (await RefactorSchema.LoadAsync(store, connection)).State);

        connection.Database = "Northwind";
        connection.IsConnected = false;    // never the last-used snapshot, as offline completion falls back to
        Assert.Equal(RefactorSchemaState.None, (await RefactorSchema.LoadAsync(store, connection)).State);
    }

    [Fact]
    public void The_menu_reasons_read_as_the_contract_says()
    {
        Assert.Null(RefactorSchema.ExpandReason(RefactorSchemaState.Ready));
        Assert.Equal("Still loading the column list.", RefactorSchema.ExpandReason(RefactorSchemaState.StillLoading));
        Assert.Equal("Needs the database schema. Connect to a database first.", RefactorSchema.ExpandReason(RefactorSchemaState.None));

        Assert.Null(RefactorSchema.HeavyReason(heavyAvailable: true, engineOpen: true));
        Assert.Equal("Not available in the browser yet.", RefactorSchema.HeavyReason(heavyAvailable: false, engineOpen: true));
        Assert.Equal("Needs a paired engine.", RefactorSchema.HeavyReason(heavyAvailable: false, engineOpen: false));
    }
}
