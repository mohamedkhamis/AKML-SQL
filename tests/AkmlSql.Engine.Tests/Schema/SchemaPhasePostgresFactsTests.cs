using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Handlers.Schema;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.Schema;

/// <summary>
/// Spec 042 (FR-040, FR-046, FR-061): the schema payload carries the PostgreSQL facts the explorer and completion need -
/// the search path, the hidden system schemas and the schemas the user cannot read - and the materialized view type.
/// For a SQL Server cache the three lists are empty, so the payload is what it was.
/// </summary>
public class SchemaPhasePostgresFactsTests
{
    private static DatabaseCache PostgresCache()
    {
        var cache = new DatabaseCache
        {
            Phase = PopulationPhase.PhaseB,
            SearchPath = ["public", "sales"],
            HiddenSchemas = ["pg_catalog", "information_schema"],
            InaccessibleSchemas = ["audit"],
        };
        var entry = new SchemaEntry { SchemaName = "public" };
        entry.Objects.Add(new DatabaseObject { SchemaName = "public", ObjectName = "mv_customer_orders", ObjectType = DbObjectType.MaterializedView });
        entry.Objects.Add(new DatabaseObject { SchemaName = "public", ObjectName = "customers", ObjectType = DbObjectType.Table });
        cache.Schemas["public"] = entry;
        return cache;
    }

    [Fact]
    public void Both_phases_carry_the_search_path_hidden_and_inaccessible_schemas()
    {
        foreach (var bytes in new[]
                 {
                     SchemaPhaseSerializer.SerializePhaseA(PostgresCache(), "northwind"),
                     SchemaPhaseSerializer.SerializePhaseB(PostgresCache(), "northwind"),
                 })
        {
            var payload = MessagePackSerializer.Deserialize<SchemaPhasePayload>(bytes);

            Assert.Equal(new[] { "public", "sales" }, payload.SearchPath);
            Assert.Equal(new[] { "pg_catalog", "information_schema" }, payload.HiddenSchemas);
            Assert.Equal(new[] { "audit" }, payload.InaccessibleSchemas);
        }
    }

    [Fact]
    public void The_materialized_view_type_is_eight_on_the_wire()
    {
        Assert.Equal(8, (int)DbObjectType.MaterializedView);

        var payload = MessagePackSerializer.Deserialize<SchemaPhasePayload>(SchemaPhaseSerializer.SerializePhaseA(PostgresCache(), "northwind"));
        var objects = payload.Schemas.Single().Objects;

        Assert.Equal(8, objects.Single(o => o.ObjectName == "mv_customer_orders").ObjectType);
        Assert.Equal((int)DbObjectType.Table, objects.Single(o => o.ObjectName == "customers").ObjectType);
    }

    [Fact]
    public void A_SQL_Server_cache_serializes_with_empty_PostgreSQL_lists()
    {
        var cache = new DatabaseCache { Phase = PopulationPhase.PhaseA };

        var payload = MessagePackSerializer.Deserialize<SchemaPhasePayload>(SchemaPhaseSerializer.SerializePhaseA(cache, "tempdb"));

        Assert.Empty(payload.SearchPath);
        Assert.Empty(payload.HiddenSchemas);
        Assert.Empty(payload.InaccessibleSchemas);
    }

    [Fact]
    public void The_existing_enum_values_did_not_move()
    {
        Assert.Equal(0, (int)DbObjectType.Table);
        Assert.Equal(1, (int)DbObjectType.View);
        Assert.Equal(7, (int)DbObjectType.Sequence);
    }
}
