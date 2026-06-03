using System.Linq;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Xunit;

namespace AkmlSql.IntelliSense.Tests;

/// <summary>
/// Spec 028 (M6) task T004 — the gate test for <see cref="SchemaPhaseRehydrator"/>.
/// The payloads here mirror exactly what <c>SchemaPhaseSerializer</c> (in AkmlSql.Engine)
/// emits — verified field-by-field against that serializer — so a green test means the
/// browser can rehydrate a cached snapshot into a <see cref="DatabaseCache"/> that
/// <c>SchemaContextBuilder</c> consumes identically to the engine's live cache. The
/// round-trip is intentionally lossy for fields the serializer never ships (row counts,
/// indexes, max-length/precision, object ids); we assert only the queryable structure
/// the AI prompt context actually uses.
/// </summary>
public class SchemaPhaseRehydratorTests
{
    private static SchemaPhasePayload BuildPhaseBPayload() => new()
    {
        DatabaseName = "Sales",
        Phase = (int)PopulationPhase.PhaseB,
        Checksum = "PhaseB:2",
        Schemas =
        [
            new SchemaPhaseSchema
            {
                Name = "dbo",
                Objects =
                [
                    new SchemaPhaseObject
                    {
                        SchemaName = "dbo",
                        ObjectName = "Orders",
                        ObjectType = (int)DbObjectType.Table,
                        Description = "Customer orders",
                        Columns =
                        [
                            new SchemaPhaseColumn { Name = "OrderId", TypeName = "int", IsNullable = false, IsPrimaryKey = true },
                            new SchemaPhaseColumn { Name = "CustomerId", TypeName = "int", IsNullable = false, IsPrimaryKey = false, Description = "FK to Customers" },
                            new SchemaPhaseColumn { Name = "Notes", TypeName = "nvarchar", MaxLength = 100 },
                            new SchemaPhaseColumn { Name = "Total", TypeName = "decimal", Precision = 18, Scale = 2 },
                        ],
                    },
                    new SchemaPhaseObject
                    {
                        SchemaName = "dbo",
                        ObjectName = "GetOrder",
                        ObjectType = (int)DbObjectType.Procedure,
                        Parameters =
                        [
                            new SchemaPhaseParameter { Name = "@OrderId", TypeName = "int", IsOutput = false, HasDefault = false },
                        ],
                    },
                ],
            },
        ],
        ForeignKeys =
        [
            new SchemaPhaseForeignKey
            {
                Name = "FK_Orders_Customers",
                ParentSchema = "dbo",
                ParentTable = "Orders",
                ParentColumns = ["CustomerId"],
                ReferencedSchema = "dbo",
                ReferencedTable = "Customers",
                ReferencedColumns = ["CustomerId"],
            },
        ],
    };

    [Fact]
    public void Rehydrate_PhaseB_ReproducesObjectsColumnsAndForeignKeys()
    {
        var cache = SchemaPhaseRehydrator.Rehydrate("srv:Sales", phaseA: null, phaseB: BuildPhaseBPayload());

        Assert.Equal("srv:Sales", cache.CacheKey);
        Assert.Equal(PopulationPhase.PhaseB, cache.Phase);

        var orders = cache.FindObject("dbo", "Orders");
        Assert.NotNull(orders);
        Assert.Equal(DbObjectType.Table, orders!.ObjectType);
        Assert.Equal("Customer orders", orders.Description);
        Assert.True(orders.ColumnsLoaded);
        Assert.Equal(4, orders.Columns.Count);

        var pk = orders.Columns.Single(c => c.ColumnName == "OrderId");
        Assert.Equal("int", pk.TypeName);
        Assert.True(pk.IsPrimaryKey);
        Assert.False(pk.IsNullable);

        var custCol = orders.Columns.Single(c => c.ColumnName == "CustomerId");
        Assert.Equal("FK to Customers", custCol.Description);
        Assert.False(custCol.IsPrimaryKey);

        // Type facets round-trip so TypeDisplay reconstructs sized types (spec 028 fix).
        Assert.Equal("nvarchar(100)", orders.Columns.Single(c => c.ColumnName == "Notes").TypeDisplay);
        Assert.Equal("decimal(18,2)", orders.Columns.Single(c => c.ColumnName == "Total").TypeDisplay);

        var proc = cache.FindObject("dbo", "GetOrder");
        Assert.NotNull(proc);
        Assert.Equal(DbObjectType.Procedure, proc!.ObjectType);
        Assert.Single(proc.Parameters);
        Assert.Equal("@OrderId", proc.Parameters[0].ParameterName);

        // FK round-tripped AND RebuildFkIndex() ran (lookup by both parent and referenced table).
        Assert.Single(cache.ForeignKeys);
        Assert.Single(cache.GetForeignKeysForTable("dbo", "Orders"));
        Assert.Single(cache.GetForeignKeysForTable("dbo", "Customers"));
        Assert.Equal("FK_Orders_Customers", cache.ForeignKeys[0].FkName);
        Assert.Equal("CustomerId", Assert.Single(cache.ForeignKeys[0].ParentColumns));
    }

    [Fact]
    public void Rehydrate_PhaseAOnly_HasObjectsButNoColumnsOrForeignKeys()
    {
        var phaseA = new SchemaPhasePayload
        {
            DatabaseName = "Sales",
            Phase = (int)PopulationPhase.PhaseA,
            Schemas =
            [
                new SchemaPhaseSchema
                {
                    Name = "dbo",
                    Objects =
                    [
                        new SchemaPhaseObject { SchemaName = "dbo", ObjectName = "Orders", ObjectType = (int)DbObjectType.Table },
                    ],
                },
            ],
            // Phase A ships no columns and no foreign keys.
        };

        var cache = SchemaPhaseRehydrator.Rehydrate("srv:Sales", phaseA: phaseA, phaseB: null);

        Assert.Equal(PopulationPhase.PhaseA, cache.Phase);
        var orders = cache.FindObject("dbo", "Orders");
        Assert.NotNull(orders);
        Assert.Empty(orders!.Columns);
        Assert.False(orders.ColumnsLoaded);
        Assert.Empty(cache.ForeignKeys);
    }

    [Fact]
    public void Rehydrate_PhaseBSupersedesPhaseA()
    {
        var phaseA = new SchemaPhasePayload
        {
            Phase = (int)PopulationPhase.PhaseA,
            Schemas = [new SchemaPhaseSchema { Name = "dbo", Objects = [new SchemaPhaseObject { SchemaName = "dbo", ObjectName = "Orders", ObjectType = (int)DbObjectType.Table }] }],
        };

        var cache = SchemaPhaseRehydrator.Rehydrate("srv:Sales", phaseA, BuildPhaseBPayload());

        // Phase B wins: columns + FKs are present.
        Assert.Equal(PopulationPhase.PhaseB, cache.Phase);
        Assert.True(cache.FindObject("dbo", "Orders")!.ColumnsLoaded);
        Assert.Single(cache.ForeignKeys);
    }

    [Fact]
    public void Rehydrate_BothNull_ReturnsEmptyNotLoadedCache()
    {
        var cache = SchemaPhaseRehydrator.Rehydrate("srv:Sales", phaseA: null, phaseB: null);

        Assert.Equal(PopulationPhase.NotLoaded, cache.Phase);
        Assert.Empty(cache.Schemas);
        Assert.Empty(cache.ForeignKeys);
        Assert.Empty(cache.GetAllObjects());
    }
}
