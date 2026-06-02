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
    public async Task With_no_cache_entry_returns_keywords_only()
    {
        // T109 follow-up: keywords are always available offline regardless of
        // whether a schema snapshot is cached. The user types `WHERE foo = 1 AND `
        // before they've ever paired with an engine — they should still see SQL
        // keyword suggestions; schemas / objects layer on top once the cache fills.
        var store = new SchemaCacheStore(new InMemoryIndexedDbAdapter());
        var service = new CompletionService(ClosedBridge(), store);

        var response = await service.CompleteAsync(new CompletionRequest(), CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotEmpty(response.Items);
        Assert.All(response.Items, i =>
            Assert.Equal((int)CompletionObjectType.Keyword, i.ObjectType));
        Assert.Contains(response.Items, i => i.DisplayText == "SELECT");
        Assert.Contains(response.Items, i => i.DisplayText == "WHERE");
        Assert.Contains(response.Items, i => i.DisplayText == "AND");
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
    public async Task Phase_B_columns_are_included_when_cached_in_both_bare_and_qualified_forms()
    {
        // After "SELECT created_at, * FROM martyrs ORDER BY created_at" the engine
        // rejects with "Ambiguous column name". The offline path emits BOTH `Name`
        // (bare) and `Customers.Name` (table-qualified) so the user can pick the
        // disambiguated form when their query has SELECT * + an ORDER BY / GROUP BY.
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
        // Table-qualified forms for the same columns.
        Assert.Contains("Customers.Id", columns);
        Assert.Contains("Customers.Name", columns);
        Assert.Contains("Customers.Email", columns);
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

    // ── Spec 027 follow-up: SQL-Prompt-style smart GROUP BY offered offline ──
    //
    // The smart item is built from the LIVE document text passed on the call (the real JS flow
    // hands CompleteAsync `context.state.doc.toString()`), NOT from the debounced editor session —
    // so a fast typist who triggers completion before the 500 ms save still sees it.

    private static CompletionService OfflineService() =>
        new CompletionService(ClosedBridge(), new SchemaCacheStore(new InMemoryIndexedDbAdapter()));

    [Fact]
    public async Task Smart_group_by_item_is_offered_first_when_cursor_in_GROUP_BY()
    {
        // Works offline with NO schema cache — it's purely a parse of the live document's SELECT
        // list. COUNT(*) is aggregated and excluded; a and b carry into the GROUP BY.
        const string sql = "SELECT a, b, COUNT(*) AS n FROM t GROUP BY ";

        var response = await OfflineService().CompleteAsync(
            new CompletionRequest { CursorOffset = sql.Length }, CancellationToken.None, sql);

        Assert.NotEmpty(response.Items);
        // First because TryPrependSmartGroupBy does items.Insert(0, ...). NOTE: the actual
        // top-of-popup ordering in the live editor is delivered by the JS `boost` in
        // akml-editor.js (CM6 re-sorts an empty-prefix popup by label) — that half is verified
        // by the Playwright run, not by this C# test.
        var smart = response.Items[0];
        Assert.Equal("▶ Add columns from SELECT", smart.DisplayText);
        Assert.Equal("a, b", smart.InsertText);
        Assert.Equal((int)CompletionObjectType.SmartAction, smart.ObjectType);
    }

    [Fact]
    public async Task Smart_group_by_item_is_absent_outside_a_GROUP_BY_clause()
    {
        // Caret in the WHERE clause — the smart item must not appear (keywords still do).
        const string sql = "SELECT a, b FROM t WHERE ";

        var response = await OfflineService().CompleteAsync(
            new CompletionRequest { CursorOffset = sql.Length }, CancellationToken.None, sql);

        Assert.DoesNotContain(response.Items, i => i.DisplayText.StartsWith("▶", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task Smart_group_by_item_is_absent_when_no_live_document_is_supplied()
    {
        // No document forwarded (older caller / null) ⇒ nothing to parse, so the offline path
        // degrades to keywords without throwing.
        var response = await OfflineService().CompleteAsync(
            new CompletionRequest { CursorOffset = 0 }, CancellationToken.None, liveDocumentText: null);

        Assert.NotEmpty(response.Items);
        Assert.DoesNotContain(response.Items, i => i.DisplayText.StartsWith("▶", System.StringComparison.Ordinal));
    }

    [Fact]
    public async Task Smart_group_by_item_is_absent_once_a_partial_token_is_typed()
    {
        // CanHandle also requires an EMPTY partial token. Once the user starts typing a column after
        // GROUP BY ("GROUP BY a"), the smart action must yield to normal column completion. This locks
        // the offline side of the gate directly (the engine covers it in PartialTextPresent_SuppressesSmartItem).
        const string sql = "SELECT a, b, COUNT(*) FROM t GROUP BY a";

        var response = await OfflineService().CompleteAsync(
            new CompletionRequest { CursorOffset = sql.Length }, CancellationToken.None, sql);

        Assert.DoesNotContain(response.Items, i => i.DisplayText.StartsWith("▶", System.StringComparison.Ordinal));
    }
}
