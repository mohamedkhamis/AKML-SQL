using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using MessagePack;
using Xunit;

namespace AkmlSql.Web.Tests.Completion;

/// <summary>
/// Spec 021 (web edition) -- M5 task T109. Cache-backed completion fallback.
/// When the bridge is closed, CompletionService consults ISchemaCacheStore and
/// synthesises a CompletionResponse from the cached Phase A (+ Phase B if
/// present) MessagePack blob. The 'online' path is covered by BridgeRoutedServicesTests.
/// </summary>
public sealed class CompletionServiceOfflineTests
{
    private static IEngineBridge ClosedBridge() =>
        new EngineBridge(() => new FakeBridgeWebSocket(_ => null!),
            new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));

    private static byte[] BuildPhaseABlob(string database, params (string Schema, string Object)[] objects)
    {
        var payload = new SchemaPhasePayload
        {
            DatabaseName = database,
            Phase = 1,
            Checksum = "PhaseA:test",
            Schemas = objects
                .GroupBy(o => o.Schema)
                .Select(g => new SchemaPhaseSchema
                {
                    Name = g.Key,
                    Objects = g.Select(o => new SchemaPhaseObject
                    {
                        SchemaName = o.Schema,
                        ObjectName = o.Object,
                        ObjectType = 0,   // Table
                        Columns = System.Array.Empty<SchemaPhaseColumn>(),
                    }).ToArray(),
                })
                .ToArray(),
        };
        return MessagePackSerializer.Serialize(payload);
    }

    private static byte[] BuildPhaseBBlob(string database, string schema, string objectName, params string[] columns)
    {
        var payload = new SchemaPhasePayload
        {
            DatabaseName = database,
            Phase = 2,
            Checksum = "PhaseB:test",
            Schemas = new[]
            {
                new SchemaPhaseSchema
                {
                    Name = schema,
                    Objects = new[]
                    {
                        new SchemaPhaseObject
                        {
                            SchemaName = schema,
                            ObjectName = objectName,
                            ObjectType = 0,
                            Columns = columns.Select(c => new SchemaPhaseColumn
                            {
                                Name = c, TypeName = "int",
                            }).ToArray(),
                        },
                    },
                },
            },
        };
        return MessagePackSerializer.Serialize(payload);
    }

    [Fact]
    public async Task With_no_cache_entry_returns_empty()
    {
        var store = new SchemaCacheStore(new InMemoryIndexedDbAdapter());
        var service = new CompletionService(ClosedBridge(), store);

        var response = await service.CompleteAsync(new CompletionRequest(), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task Returns_schemas_and_objects_from_phase_A_blob()
    {
        var store = new SchemaCacheStore(new InMemoryIndexedDbAdapter());
        await store.SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = "PROD-DB01",
            DatabaseName = "Northwind",
            PhaseA = BuildPhaseABlob("Northwind",
                ("dbo", "Customers"), ("dbo", "Orders"), ("sales", "Invoices")),
            Checksum = "PhaseA:test",
        });

        var service = new CompletionService(ClosedBridge(), store);
        var response = await service.CompleteAsync(new CompletionRequest(), CancellationToken.None);

        var customers = response.Items.FirstOrDefault(i => i.SourceObject == "dbo.Customers");
        Assert.NotNull(customers);
        Assert.Equal((int)CompletionObjectType.Table, customers!.ObjectType);

        var sales = response.Items.FirstOrDefault(i =>
            i.DisplayText == "sales" && i.ObjectType == (int)CompletionObjectType.Schema);
        Assert.NotNull(sales);

        // Keywords land too so the user has SELECT / FROM / WHERE / etc. offline.
        var selectKw = response.Items.FirstOrDefault(i =>
            i.DisplayText == "SELECT" && i.ObjectType == (int)CompletionObjectType.Keyword);
        Assert.NotNull(selectKw);
    }

    [Fact]
    public async Task Phase_B_columns_are_included_when_cached()
    {
        var store = new SchemaCacheStore(new InMemoryIndexedDbAdapter());
        await store.SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = "s",
            DatabaseName = "db",
            PhaseA = BuildPhaseABlob("db", ("dbo", "Customers")),
            PhaseB = BuildPhaseBBlob("db", "dbo", "Customers", "Id", "Name", "Email"),
        });

        var service = new CompletionService(ClosedBridge(), store);
        var response = await service.CompleteAsync(new CompletionRequest(), CancellationToken.None);

        var columns = response.Items
            .Where(i => i.ObjectType == (int)CompletionObjectType.Column)
            .Select(i => i.DisplayText)
            .ToArray();
        Assert.Contains("Id", columns);
        Assert.Contains("Name", columns);
        Assert.Contains("Email", columns);
    }

    [Fact]
    public async Task Uses_most_recently_used_snapshot_when_multiple_exist()
    {
        // The "active" session is approximated as the LAST-USED snapshot. Multi-server
        // scenarios that need explicit pointer-tracking are documented as a follow-up.
        var store = new SchemaCacheStore(new InMemoryIndexedDbAdapter());
        await store.SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = "older-server",
            DatabaseName = "OldDb",
            PhaseA = BuildPhaseABlob("OldDb", ("dbo", "LegacyTable")),
        });
        await Task.Delay(5);   // separate LastUsedAt timestamps
        await store.SetAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = "current-server",
            DatabaseName = "CurrentDb",
            PhaseA = BuildPhaseABlob("CurrentDb", ("dbo", "ActiveTable")),
        });

        var service = new CompletionService(ClosedBridge(), store);
        var response = await service.CompleteAsync(new CompletionRequest(), CancellationToken.None);

        Assert.Contains(response.Items, i => i.SourceObject == "dbo.ActiveTable");
        Assert.DoesNotContain(response.Items, i => i.SourceObject == "dbo.LegacyTable");
    }
}
