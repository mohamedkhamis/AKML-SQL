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
/// Spec 021 (web edition) — M5 task T109. Phase B carries the heavier
/// columns + foreign-keys payload. Same handler shape as Phase A, so the
/// tests focus on the bits that differ: column/FK inclusion in the payload.
/// </summary>
public sealed class SchemaPhaseBHandlerTests
{
    private static SchemaPhaseBHandler Build(byte[]? payload, string? checksum = null)
    {
        return new SchemaPhaseBHandler((_, _) => (payload, checksum));
    }

    [Fact]
    public async Task Returns_payload_when_lookup_succeeds()
    {
        var bytes = new byte[] { 0xAA, 0xBB, 0xCC };
        var handler = Build(bytes, checksum: "PhaseB:42");

        var response = await handler.HandleAsync(
            new SchemaPhaseBRequest { SessionId = "s1", DatabaseName = "AdventureWorks" },
            null!, CancellationToken.None);

        Assert.True(response.HasConnection);
        Assert.Equal(bytes, response.PhaseB);
        Assert.Equal("PhaseB:42", response.Checksum);
    }

    [Fact]
    public async Task Returns_no_connection_when_payload_is_empty()
    {
        var handler = Build(payload: System.Array.Empty<byte>());

        var response = await handler.HandleAsync(
            new SchemaPhaseBRequest { SessionId = "s1", DatabaseName = "db" },
            null!, CancellationToken.None);

        Assert.False(response.HasConnection);
        Assert.NotNull(response.ErrorMessage);
    }

    [Fact]
    public async Task Lookup_exception_is_captured_into_error_message()
    {
        var handler = new SchemaPhaseBHandler(
            (_, _) => throw new InvalidOperationException("phase b unavailable"));

        var response = await handler.HandleAsync(
            new SchemaPhaseBRequest { SessionId = "s1", DatabaseName = "db" },
            null!, CancellationToken.None);

        Assert.False(response.HasConnection);
        Assert.Contains("phase b unavailable", response.ErrorMessage);
    }

    [Fact]
    public async Task Real_serializer_roundtrip_carries_columns_and_foreign_keys()
    {
        var cache = new DatabaseCache
        {
            CacheKey = "s1:Northwind",
            Phase = PopulationPhase.PhaseB,
        };
        var dbo = cache.Schemas.GetOrAdd("dbo", _ => new SchemaEntry { SchemaName = "dbo" });
        var customers = new DatabaseObject
        {
            ObjectId = 1, SchemaName = "dbo", ObjectName = "Customers",
            ObjectType = DbObjectType.Table, ColumnsLoaded = true,
        };
        customers.Columns.Add(new Column { ColumnName = "Id", TypeName = "int", IsPrimaryKey = true });
        customers.Columns.Add(new Column { ColumnName = "Name", TypeName = "nvarchar", IsNullable = true });
        dbo.Objects.Add(customers);

        cache.ForeignKeys.Add(new ForeignKey
        {
            FkName = "FK_Orders_Customers",
            ParentSchema = "dbo", ParentTable = "Orders",
            ParentColumns = new() { "CustomerId" },
            ReferencedSchema = "dbo", ReferencedTable = "Customers",
            ReferencedColumns = new() { "Id" },
        });

        var bytes = SchemaPhaseSerializer.SerializePhaseB(cache, "Northwind");
        var handler = new SchemaPhaseBHandler((_, _) => (bytes, SchemaPhaseSerializer.ComputeChecksum(cache)));

        var response = await handler.HandleAsync(
            new SchemaPhaseBRequest { SessionId = "s1", DatabaseName = "Northwind" },
            null!, CancellationToken.None);

        Assert.True(response.HasConnection);
        var decoded = MessagePackSerializer.Deserialize<SchemaPhasePayload>(response.PhaseB);
        var customersDecoded = decoded.Schemas[0].Objects[0];
        Assert.Equal(2, customersDecoded.Columns.Length);
        Assert.Contains(customersDecoded.Columns, c => c.Name == "Id" && c.IsPrimaryKey);
        Assert.Single(decoded.ForeignKeys);
        Assert.Equal("FK_Orders_Customers", decoded.ForeignKeys[0].Name);
        Assert.Equal(new[] { "CustomerId" }, decoded.ForeignKeys[0].ParentColumns);
    }
}
