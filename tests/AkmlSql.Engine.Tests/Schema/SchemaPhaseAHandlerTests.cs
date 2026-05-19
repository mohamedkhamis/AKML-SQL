using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Schema;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.Schema;

/// <summary>
/// Spec 021 (web edition) — M5 task T109. Mirrors SchemaChecksumHandlerTests:
/// callback-pure handler, every path exercised without a live SQL connection.
/// </summary>
public sealed class SchemaPhaseAHandlerTests
{
    private static SchemaPhaseAHandler Build(byte[]? payload, string? checksum = null)
    {
        return new SchemaPhaseAHandler((_, _) => (payload, checksum));
    }

    [Fact]
    public async Task Returns_payload_when_lookup_succeeds()
    {
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var handler = Build(bytes, checksum: "PhaseA:42");

        var response = await handler.HandleAsync(
            new SchemaPhaseARequest { SessionId = "s1", DatabaseName = "AdventureWorks" },
            null!, CancellationToken.None);

        Assert.True(response.HasConnection);
        Assert.Equal(bytes, response.PhaseA);
        Assert.Equal("PhaseA:42", response.Checksum);
        Assert.Null(response.ErrorMessage);
    }

    [Fact]
    public async Task Returns_no_connection_when_lookup_returns_null_payload()
    {
        var handler = Build(payload: null);

        var response = await handler.HandleAsync(
            new SchemaPhaseARequest { SessionId = "s1", DatabaseName = "db" },
            null!, CancellationToken.None);

        Assert.False(response.HasConnection);
        Assert.NotNull(response.ErrorMessage);
        Assert.Empty(response.PhaseA);
    }

    [Fact]
    public async Task Empty_session_or_database_is_rejected()
    {
        var handler = Build(new byte[] { 1 });

        var noSession = await handler.HandleAsync(
            new SchemaPhaseARequest { SessionId = string.Empty, DatabaseName = "db" },
            null!, CancellationToken.None);
        Assert.False(noSession.HasConnection);
        Assert.Contains("SessionId", noSession.ErrorMessage);

        var noDb = await handler.HandleAsync(
            new SchemaPhaseARequest { SessionId = "s1", DatabaseName = string.Empty },
            null!, CancellationToken.None);
        Assert.False(noDb.HasConnection);
        Assert.Contains("DatabaseName", noDb.ErrorMessage);
    }

    [Fact]
    public async Task Lookup_exception_is_captured_into_error_message()
    {
        var handler = new SchemaPhaseAHandler(
            (_, _) => throw new InvalidOperationException("cache disposed"));

        var response = await handler.HandleAsync(
            new SchemaPhaseARequest { SessionId = "s1", DatabaseName = "db" },
            null!, CancellationToken.None);

        Assert.False(response.HasConnection);
        Assert.Contains("cache disposed", response.ErrorMessage);
    }

    [Fact]
    public async Task Real_serializer_roundtrip_is_consumable_by_browser_side()
    {
        // End-to-end: build a cache → SerializePhaseA → hand to the handler via
        // a stub lookup → assert the response bytes deserialise back to the same
        // schema/object list. This is the "happy path" the browser will see.
        var cache = new DatabaseCache
        {
            CacheKey = "s1:AdventureWorks",
            Phase = PopulationPhase.PhaseA,
        };
        var dbo = cache.Schemas.GetOrAdd("dbo", _ => new SchemaEntry { SchemaName = "dbo" });
        dbo.Objects.Add(new DatabaseObject
        {
            ObjectId = 1, SchemaName = "dbo", ObjectName = "Customers",
            ObjectType = DbObjectType.Table,
        });
        dbo.Objects.Add(new DatabaseObject
        {
            ObjectId = 2, SchemaName = "dbo", ObjectName = "Orders",
            ObjectType = DbObjectType.Table,
        });

        var bytes = SchemaPhaseSerializer.SerializePhaseA(cache, "AdventureWorks");
        var checksum = SchemaPhaseSerializer.ComputeChecksum(cache);
        var handler = new SchemaPhaseAHandler((_, _) => (bytes, checksum));

        var response = await handler.HandleAsync(
            new SchemaPhaseARequest { SessionId = "s1", DatabaseName = "AdventureWorks" },
            null!, CancellationToken.None);

        Assert.True(response.HasConnection);
        var decoded = MessagePackSerializer.Deserialize<SchemaPhasePayload>(response.PhaseA);
        Assert.Equal("AdventureWorks", decoded.DatabaseName);
        Assert.Single(decoded.Schemas);
        Assert.Equal("dbo", decoded.Schemas[0].Name);
        Assert.Equal(2, decoded.Schemas[0].Objects.Length);
        Assert.Contains(decoded.Schemas[0].Objects, o => o.ObjectName == "Customers");
        // Phase A view leaves columns/FKs empty even when the cache happens to have them.
        Assert.All(decoded.Schemas[0].Objects, o => Assert.Empty(o.Columns));
        Assert.Empty(decoded.ForeignKeys);
    }
}
