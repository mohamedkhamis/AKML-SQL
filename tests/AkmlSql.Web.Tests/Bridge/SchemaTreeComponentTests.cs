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
public sealed class SchemaTreeComponentTests : TestContext
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

        var cut = RenderComponent<SchemaTreeComponent>(p => p
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

        var cut = RenderComponent<SchemaTreeComponent>(p => p
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

        var cut = RenderComponent<SchemaTreeComponent>(p => p
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

        var cut = RenderComponent<SchemaTreeComponent>(p => p
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

        var cut = RenderComponent<SchemaTreeComponent>(p => p
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
        var cut = RenderComponent<SchemaTreeComponent>(p => p
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
        var cut = RenderComponent<SchemaTreeComponent>(p => p
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

        var cut = RenderComponent<SchemaTreeComponent>(p => p
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
}

// ── Test doubles ────────────────────────────────────────────────────────────────

internal sealed class FakeEngineBridge : IEngineBridge
{
    public BridgeState State { get; private set; } = BridgeState.Disconnected;
    public string[] EngineCapabilities { get; } = Array.Empty<string>();
    public string? EngineVersion => null;

    public event Action<BridgeState>? StateChanged;
    public event Action<DateTimeOffset?>? RetryScheduled;
    public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected;

    public void SetState(BridgeState newState)
    {
        State = newState;
        StateChanged?.Invoke(newState);
    }

    public Task<HandshakeResponse> ConnectAsync(EngineConnection connection, string? bearerToken, string? pairingPin, CancellationToken ct) =>
        Task.FromResult(new HandshakeResponse { Status = HandshakeStatus.Ok });

    public Task<TResponse> SendAsync<TRequest, TResponse>(int requestMessageType, TRequest request, CancellationToken ct)
        where TRequest : class
        where TResponse : class => Task.FromResult<TResponse>(default!);

    public Task DisconnectAsync() => Task.CompletedTask;
    public ValueTask DisposeAsync() => default;

    /// <summary>Test helper for the spec-025 follow-on TLS-banner tests — fires the
    /// production-event shape so the banner subscriber wakes up exactly like it would
    /// from a real bridge mismatch detection.</summary>
    public void FireMismatch(TlsFingerprintMismatch m) => FingerprintMismatchDetected?.Invoke(m);

    // Suppress unused-event-warning while keeping the contract.
    private void _suppressUnused() { RetryScheduled?.Invoke(null); }
}

internal sealed class FakeSchemaSync : ISchemaSync
{
    public event Action<ChecksumDriftNotice>? ChecksumDrifted;

    public Task StartAsync(string sessionId, string serverCanonicalIdentity, string databaseName) => Task.CompletedTask;
    public Task StopAsync() => Task.CompletedTask;
    public void ReportEditorActive() { }
    public ValueTask DisposeAsync() => default;

    public Task RaiseChecksumDriftedAsync(ChecksumDriftNotice notice)
    {
        ChecksumDrifted?.Invoke(notice);
        return Task.CompletedTask;
    }
}
