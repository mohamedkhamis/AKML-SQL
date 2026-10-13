using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using MessagePack;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Bridge;

/// <summary>
/// Spec 025 (M3 bridge closure) US4 — SchemaTreeComponent bUnit tests per
/// specs/025-m3-bridge-closure/contracts/schema-tree-contract.md §Tests.
///
/// The component renders ISchemaCacheStore snapshots; tests seed the store via the
/// already-shipped InMemoryIndexedDbAdapter and the production SchemaCacheStore so
/// we exercise the same JSON round-trip the browser would in production.
/// </summary>
public sealed class SchemaTreeComponentTests : BunitContext
{
    private const string Server = "127.0.0.1:5081";
    private const string Db = "AdventureWorks";

    public SchemaTreeComponentTests()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        var cache = new SchemaCacheStore(adapter);
        var diag = new DiagnosticsRingBuffer(adapter);
        var bridge = new FakeEngineBridge();
        var sync = new FakeSchemaSync();

        Services.AddSingleton<ISchemaCacheStore>(cache);
        Services.AddSingleton<IEngineBridge>(bridge);
        Services.AddSingleton<ISchemaSync>(sync);
        Services.AddSingleton(bridge);   // also expose the concrete fake for test driving
        Services.AddSingleton(sync);
    }

    private FakeEngineBridge BridgeFake => Services.GetRequiredService<FakeEngineBridge>();
    private FakeSchemaSync SyncFake => Services.GetRequiredService<FakeSchemaSync>();
    private ISchemaCacheStore Cache => Services.GetRequiredService<ISchemaCacheStore>();

    private async Task SeedAsync(SchemaSnapshot snapshot) => await Cache.SetAsync(snapshot);

    private static byte[] PhasePayload(string dbName, params SchemaPhaseSchema[] schemas) =>
        MessagePackSerializer.Serialize(new SchemaPhasePayload
        {
            DatabaseName = dbName,
            Phase = 1,
            Checksum = "abc",
            Schemas = schemas,
        });

    private static SchemaPhaseObject Table(string name, params SchemaPhaseColumn[] cols) => new()
    {
        SchemaName = "dbo",
        ObjectName = name,
        ObjectType = 0,    // Table
        Columns = cols,
    };

    private static SchemaPhaseColumn Col(string name, string type) => new() { Name = name, TypeName = type };

    [Fact]
    public async Task RendersDatabaseSchemaTableHierarchyFromPhaseA()
    {
        await SeedAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server,
            DatabaseName = Db,
            PhaseA = PhasePayload(Db,
                new SchemaPhaseSchema { Name = "dbo", Objects = new[] { Table("Customer"), Table("Order") } },
                new SchemaPhaseSchema { Name = "Sales", Objects = new[] { Table("Region"), Table("Territory") } }),
            FetchedAt = DateTimeOffset.UtcNow,
        });

        var cut = Render<SchemaTreeComponent>(p => p
            .Add(c => c.ServerCanonicalIdentity, Server)
            .Add(c => c.DatabaseName, Db));

        // Expand the root + each schema + Tables header — IsExpanded() is internal.
        var instance = cut.Instance;
        instance.ExpandForTest(Db);
        instance.ExpandForTest(Db + "/dbo");
        instance.ExpandForTest(Db + "/dbo/Tables");
        instance.ExpandForTest(Db + "/Sales");
        instance.ExpandForTest(Db + "/Sales/Tables");
        cut.Render();

        // 4 Object nodes (Customer, Order, Region, Territory).
        var objects = cut.FindAll("[data-testid='schema-tree-object']");
        Assert.Equal(4, objects.Count);
        // No Column nodes — Phase A has no columns.
        var columns = cut.FindAll("[data-testid='schema-tree-column']");
        Assert.Empty(columns);
    }

    [Fact]
    public async Task ExpandsTableShowsColumnsFromPhaseB()
    {
        var customer = Table("Customer",
            Col("Id", "int"), Col("Name", "nvarchar(100)"),
            Col("Email", "nvarchar(255)"), Col("CreatedAt", "datetime2"),
            Col("IsActive", "bit"));
        var phaseB = PhasePayload(Db,
            new SchemaPhaseSchema { Name = "dbo", Objects = new[] { customer } });

        await SeedAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server,
            DatabaseName = Db,
            PhaseA = phaseB,    // also seed Phase A so even Phase-A-only consumers see the same shape
            PhaseB = phaseB,
            FetchedAt = DateTimeOffset.UtcNow,
        });

        var cut = Render<SchemaTreeComponent>(p => p
            .Add(c => c.ServerCanonicalIdentity, Server)
            .Add(c => c.DatabaseName, Db));

        var instance = cut.Instance;
        instance.ExpandForTest(Db);
        instance.ExpandForTest(Db + "/dbo");
        instance.ExpandForTest(Db + "/dbo/Tables");
        instance.ExpandForTest(Db + "/dbo/Tables/Customer");
        cut.Render();

        var columns = cut.FindAll("[data-testid='schema-tree-column']");
        Assert.Equal(5, columns.Count);
        Assert.Contains(columns, c => c.TextContent.Contains("Id") && c.TextContent.Contains("int"));
        Assert.Contains(columns, c => c.TextContent.Contains("Name") && c.TextContent.Contains("nvarchar(100)"));
    }

    [Fact]
    public async Task ChecksumDriftRefreshesTreePreservesExpansion()
    {
        await SeedAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server,
            DatabaseName = Db,
            PhaseA = PhasePayload(Db,
                new SchemaPhaseSchema { Name = "dbo", Objects = new[] { Table("Customer") } }),
            FetchedAt = DateTimeOffset.UtcNow,
        });

        var cut = Render<SchemaTreeComponent>(p => p
            .Add(c => c.ServerCanonicalIdentity, Server)
            .Add(c => c.DatabaseName, Db));

        var instance = cut.Instance;
        instance.ExpandForTest(Db);
        instance.ExpandForTest(Db + "/dbo");
        instance.ExpandForTest(Db + "/dbo/Tables");
        instance.ExpandForTest(Db + "/dbo/Tables/Customer");
        cut.Render();
        Assert.True(instance.IsExpanded(Db + "/dbo/Tables/Customer"));

        // New snapshot still contains Customer + adds Orders.
        await SeedAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server,
            DatabaseName = Db,
            PhaseA = PhasePayload(Db,
                new SchemaPhaseSchema { Name = "dbo", Objects = new[] { Table("Customer"), Table("Order") } }),
            FetchedAt = DateTimeOffset.UtcNow,
        });
        await SyncFake.RaiseChecksumDriftedAsync(new ChecksumDriftNotice(Server, Db, "new-checksum"));
        cut.Render();

        Assert.True(instance.IsExpanded(Db + "/dbo/Tables/Customer"));
        // Newly-added Orders is collapsed by default.
        Assert.False(instance.IsExpanded(Db + "/dbo/Tables/Order"));
    }

    [Fact]
    public async Task StaleBadgeAppearsWhenDisconnected()
    {
        await SeedAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server,
            DatabaseName = Db,
            PhaseA = PhasePayload(Db, new SchemaPhaseSchema { Name = "dbo" }),
            FetchedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5),
        });

        BridgeFake.SetState(BridgeState.Disconnected);

        var cut = Render<SchemaTreeComponent>(p => p
            .Add(c => c.ServerCanonicalIdentity, Server)
            .Add(c => c.DatabaseName, Db));

        var badge = cut.Find("[data-testid='schema-tree-stale']");
        Assert.Contains("Stale", badge.TextContent);
        Assert.Contains("5 minutes ago", badge.TextContent);
    }

    [Fact]
    public async Task StaleBadgeHiddenWhenOpen()
    {
        await SeedAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server,
            DatabaseName = Db,
            PhaseA = PhasePayload(Db, new SchemaPhaseSchema { Name = "dbo" }),
            FetchedAt = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5),
        });
        BridgeFake.SetState(BridgeState.Open);

        var cut = Render<SchemaTreeComponent>(p => p
            .Add(c => c.ServerCanonicalIdentity, Server)
            .Add(c => c.DatabaseName, Db));

        Assert.Empty(cut.FindAll("[data-testid='schema-tree-stale']"));
    }

    [Fact]
    public async Task ClickOnObjectRaisesQualifiedName()
    {
        await SeedAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server,
            DatabaseName = Db,
            PhaseA = PhasePayload(Db,
                new SchemaPhaseSchema { Name = "dbo", Objects = new[] { Table("Customer") } }),
            FetchedAt = DateTimeOffset.UtcNow,
        });

        string? captured = null;
        var cut = Render<SchemaTreeComponent>(p => p
            .Add(c => c.ServerCanonicalIdentity, Server)
            .Add(c => c.DatabaseName, Db)
            .Add(c => c.OnObjectClicked, q => { captured = q; }));

        var instance = cut.Instance;
        instance.ExpandForTest(Db);
        instance.ExpandForTest(Db + "/dbo");
        instance.ExpandForTest(Db + "/dbo/Tables");
        cut.Render();

        var obj = cut.Find("[data-testid='schema-tree-object']");
        obj.Click();
        Assert.Equal("[dbo].[Customer]", captured);
    }

    [Fact]
    public void EmptyStatePlaceholderWhenNoSnapshot()
    {
        // No seed.
        var cut = Render<SchemaTreeComponent>(p => p
            .Add(c => c.ServerCanonicalIdentity, Server)
            .Add(c => c.DatabaseName, Db));

        Assert.Contains("Schema not yet loaded", cut.Markup);
    }

    [Fact]
    public async Task VirtualisationKicksInPastThreshold()
    {
        // 250 tables in one schema — past the 200-threshold.
        var tables = Enumerable.Range(0, 250).Select(i => Table($"T{i:D3}")).ToArray();
        await SeedAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server,
            DatabaseName = Db,
            PhaseA = PhasePayload(Db, new SchemaPhaseSchema { Name = "dbo", Objects = tables }),
            FetchedAt = DateTimeOffset.UtcNow,
        });

        var cut = Render<SchemaTreeComponent>(p => p
            .Add(c => c.ServerCanonicalIdentity, Server)
            .Add(c => c.DatabaseName, Db));

        var instance = cut.Instance;
        instance.ExpandForTest(Db);
        instance.ExpandForTest(Db + "/dbo");
        instance.ExpandForTest(Db + "/dbo/Tables");
        cut.Render();

        // The virtualized wrapper element marks itself with the test id; below the
        // threshold the wrapper isn't rendered.
        Assert.NotEmpty(cut.FindAll("[data-testid='schema-tree-virtualized']"));
    }

    [Fact]
    public void Without_a_connection_the_panel_stays_and_offers_to_connect()
    {
        // Spec 041 (T118, FR-048): the Schema panel never disappears; it says how to fill it.
        int requested = 0;
        var cut = Render<SchemaTreeComponent>(p => p
            .Add(c => c.ServerCanonicalIdentity, (string?)null)
            .Add(c => c.DatabaseName, (string?)null)
            .Add(c => c.OnConnectRequested, () => requested++));

        Assert.NotNull(cut.Find("[data-testid=schema-tree]"));
        var connect = cut.Find("[data-testid=schema-connect]");
        Assert.Equal("Connect to SQL Server…", connect.TextContent.Trim());

        connect.Click();
        Assert.Equal(1, requested);
    }

    // ── Spec 041 (T184, research R40, N27, FR-093) — header, filter, refresh, icons, loading ──────────────

    private static SchemaPhaseObject Obj(string name, int type) => new() { SchemaName = "dbo", ObjectName = name, ObjectType = type };

    private async Task<IRenderedComponent<SchemaTreeComponent>> RenderSeededAsync(params SchemaPhaseSchema[] schemas)
    {
        await SeedAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server,
            DatabaseName = Db,
            PhaseA = PhasePayload(Db, schemas),
            FetchedAt = DateTimeOffset.UtcNow,
        });
        return Render<SchemaTreeComponent>(p => p
            .Add(c => c.ServerCanonicalIdentity, Server)
            .Add(c => c.DatabaseName, Db)
            .Add(c => c.SessionId, "session-1"));
    }

    private static string[] ObjectNames(IRenderedComponent<SchemaTreeComponent> cut) =>
        cut.FindAll("[data-testid='schema-tree-object']").Select(o => o.GetAttribute("data-qualifier")!).ToArray();

    [Fact]
    public async Task The_header_names_the_server_and_the_database()
    {
        var cut = await RenderSeededAsync(new SchemaPhaseSchema { Name = "dbo" });

        Assert.Equal($"{Server} · {Db}", cut.Find("[data-testid=schema-header]").TextContent.Trim());
    }

    [Fact]
    public async Task The_filter_narrows_the_tree_after_a_pause_keeping_parents_and_opening_matches()
    {
        var cut = await RenderSeededAsync(
            new SchemaPhaseSchema { Name = "dbo", Objects = new[] { Obj("Customer", 0), Obj("Order", 0), Obj("vCustomerList", 1), Obj("usp_Orders", 2) } },
            new SchemaPhaseSchema { Name = "Sales", Objects = new[] { Obj("Region", 0) } });
        Assert.Empty(ObjectNames(cut));   // nothing open yet

        var filter = cut.Find("[data-testid=schema-filter]");
        Assert.Equal("Filter objects", filter.GetAttribute("aria-label"));
        Assert.Contains("akml-input", filter.ClassList);   // the themed control class, not the akml-field label wrapper
        filter.Input("CUST");
        Assert.Empty(ObjectNames(cut));   // 150 ms debounce: not yet

        cut.WaitForAssertion(() => Assert.Equal(new[] { "[dbo].[Customer]", "[dbo].[vCustomerList]" }, ObjectNames(cut)));
        var kinds = cut.FindAll(".akml-schema-tree-kind .akml-schema-tree-label").Select(k => k.TextContent.Trim()).ToArray();
        Assert.Equal(new[] { "Tables (1)", "Views (1)" }, kinds);   // empty kinds hidden while filtering
        Assert.DoesNotContain("Sales", cut.Markup);                     // a schema with no match is hidden

        cut.Find("[data-testid=schema-filter]").Input("");
        cut.WaitForAssertion(() => Assert.Empty(ObjectNames(cut)));   // back to what the user had open
        cut.Instance.ExpandForTest(Db);
        cut.Instance.ExpandForTest(Db + "/dbo");
        cut.Render();
        Assert.Equal(4, cut.FindAll(".akml-schema-tree-kind").Count);   // every kind again, empty ones included
    }

    [Fact]
    public async Task The_filter_applies_to_a_virtualised_kind_too()
    {
        var tables = Enumerable.Range(0, 250).Select(i => Table($"T{i:D3}")).ToArray();
        var cut = await RenderSeededAsync(new SchemaPhaseSchema { Name = "dbo", Objects = tables });

        cut.Find("[data-testid=schema-filter]").Input("t1");

        cut.WaitForAssertion(() => Assert.Equal(100, ObjectNames(cut).Length));   // T100–T199, below the threshold
        Assert.Empty(cut.FindAll("[data-testid='schema-tree-virtualized']"));
    }

    [Fact]
    public async Task Refresh_asks_the_engine_to_reload_then_fetches_the_schema_again_showing_a_loading_row()
    {
        BridgeFake.SetState(BridgeState.Open);
        var cut = await RenderSeededAsync(new SchemaPhaseSchema { Name = "dbo", Objects = new[] { Table("Customer") } });
        var gate = new TaskCompletionSource();
        var notificationsWhenRefreshed = -1;
        SyncFake.OnRefresh = () => { notificationsWhenRefreshed = BridgeFake.Notifications.Count; return gate.Task; };

        cut.Find("[data-testid=schema-refresh]").Click();

        var (type, payload) = Assert.Single(BridgeFake.Notifications);
        Assert.Equal(MessageTypes.SchemaRefreshRequest, type);
        Assert.Equal("session-1", Assert.IsType<RefreshRequest>(payload).SessionId);
        Assert.Equal(1, SyncFake.Refreshes);
        Assert.Equal(1, notificationsWhenRefreshed);   // the notification went first
        Assert.NotNull(cut.Find("[data-testid=schema-loading]"));
        Assert.True(cut.Find("[data-testid=schema-refresh]").HasAttribute("disabled"));

        await SeedAsync(new SchemaSnapshot
        {
            ServerCanonicalIdentity = Server, DatabaseName = Db, FetchedAt = DateTimeOffset.UtcNow,
            PhaseA = PhasePayload(Db, new SchemaPhaseSchema { Name = "dbo", Objects = new[] { Table("Customer"), Table("Invoice") } }),
        });
        gate.SetResult();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll("[data-testid=schema-loading]")));
        cut.Instance.ExpandForTest(Db);
        cut.Instance.ExpandForTest(Db + "/dbo");
        cut.Instance.ExpandForTest(Db + "/dbo/Tables");
        cut.Render();
        Assert.Equal(new[] { "[dbo].[Customer]", "[dbo].[Invoice]" }, ObjectNames(cut));
    }

    [Fact]
    public async Task Refresh_is_disabled_and_says_why_without_an_engine()
    {
        BridgeFake.SetState(BridgeState.Disconnected);
        var cut = await RenderSeededAsync(new SchemaPhaseSchema { Name = "dbo" });

        var refresh = cut.Find("[data-testid=schema-refresh]");
        Assert.True(refresh.HasAttribute("disabled"));
        Assert.False(string.IsNullOrWhiteSpace(refresh.GetAttribute("title")));
        Assert.Empty(BridgeFake.Notifications);
    }

    [Fact]
    public async Task Every_kind_and_object_carries_its_icon()
    {
        var customer = Table("Customer", new SchemaPhaseColumn { Name = "Id", TypeName = "int", IsPrimaryKey = true }, Col("Name", "nvarchar(50)"));
        var cut = await RenderSeededAsync(new SchemaPhaseSchema { Name = "dbo", Objects = new[] { customer, Obj("vList", 1), Obj("usp_Go", 2), Obj("fn_X", 3) } });
        foreach (var path in new[] { Db, Db + "/dbo", Db + "/dbo/Tables", Db + "/dbo/Tables/Customer" }) cut.Instance.ExpandForTest(path);
        cut.Render();

        string IconOf(AngleSharp.Dom.IElement row) =>
            row.QuerySelector(".akml-schema-icon use")!.GetAttribute("href")!.Split("#akml-icon-")[1];

        var kinds = cut.FindAll(".akml-schema-tree-kind").ToDictionary(k => k.QuerySelector(".akml-schema-tree-label")!.TextContent.Split(' ')[0], IconOf);
        Assert.Equal("table-2", kinds["Tables"]);
        Assert.Equal("eye", kinds["Views"]);
        Assert.Equal("scroll-text", kinds["Stored"]);
        Assert.Equal("square-function", kinds["Functions"]);
        Assert.Equal("database", IconOf(cut.Find(".akml-schema-tree-db")));
        Assert.Equal("folder-open", IconOf(cut.Find(".akml-schema-tree-schema")));
        Assert.Equal("table-2", IconOf(cut.Find("[data-testid='schema-tree-object']")));
        Assert.Equal(new[] { "key-round", "columns-3" }, cut.FindAll("[data-testid='schema-tree-column']").Select(IconOf));
        // chevrons are icons too, not text glyphs
        Assert.DoesNotContain("▶", cut.Markup);
        Assert.DoesNotContain("▼", cut.Markup);
    }

    [Fact]
    public void A_connected_panel_whose_schema_has_not_arrived_shows_a_loading_row()
    {
        BridgeFake.SetState(BridgeState.Open);
        var cut = Render<SchemaTreeComponent>(p => p
            .Add(c => c.ServerCanonicalIdentity, Server)
            .Add(c => c.DatabaseName, Db));

        Assert.Contains("Loading schema", cut.Find("[data-testid=schema-loading]").TextContent);
    }
}
