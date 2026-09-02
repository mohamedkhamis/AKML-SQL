using System.Diagnostics;
using AkmlSql.Core.Models.Ai;
using AkmlSql.Engine.Ai.Context;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.AI.Tests;

/// <summary>
/// Spec 036 (US1) T006/T009/T023 — the inventory-first schema-context assembly contract
/// (contracts/schema-context.md Part 2): relevance may only PROMOTE detail, never remove
/// inventory (FR-024/FR-025); named objects and their FK 1-hop neighbours get level-3 detail
/// (FR-023); the budget is explicit and truncation is signalled (FR-026); unbound renders
/// distinctly from connected-but-empty (FR-028); ghost text stays at detail level 1 (T009).
/// </summary>
public sealed class SchemaContextAssemblyTests
{
    private readonly ITestOutputHelper _output;

    public SchemaContextAssemblyTests(ITestOutputHelper output) => _output = output;

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Column Col(string name, string type, bool pk = false, bool nullable = true,
        int maxLength = 0, int precision = 0, int scale = 0) =>
        new()
        {
            ColumnName = name, TypeName = type, IsPrimaryKey = pk, IsNullable = nullable,
            MaxLength = maxLength, Precision = precision, Scale = scale,
        };

    private static DatabaseObject Table(string schema, string name, params Column[] columns) =>
        new()
        {
            SchemaName = schema,
            ObjectName = name,
            ObjectType = DbObjectType.Table,
            Columns = columns.ToList(),
            ColumnsLoaded = columns.Length > 0,
        };

    private static DatabaseCache CreateCache(params DatabaseObject[] objects)
    {
        var cache = new DatabaseCache { CacheKey = "session-1:TestDb" };
        foreach (var group in objects.GroupBy(o => o.SchemaName, StringComparer.OrdinalIgnoreCase))
        {
            cache.Schemas[group.Key] = new SchemaEntry
            {
                SchemaName = group.Key,
                Objects = group.ToList(),
            };
        }
        return cache;
    }

    /// <summary>The canonical Orders/Customers cache with a FK between them (mirrors the
    /// spec-036 verification database, plus an unconnected Products table).</summary>
    private static DatabaseCache CreateOrdersCache()
    {
        var cache = CreateCache(
            Table("dbo", "Orders",
                Col("OrderId", "int", pk: true, nullable: false),
                Col("CustomerId", "int", nullable: false),
                Col("Notes", "nvarchar", maxLength: 100)),
            Table("dbo", "Customers",
                Col("CustomerId", "int", pk: true, nullable: false),
                Col("Email", "nvarchar", maxLength: 320)),
            Table("dbo", "Products",
                Col("ProductId", "int", pk: true, nullable: false)));
        cache.ForeignKeys.Add(new ForeignKey
        {
            FkName = "FK_Orders_Customers",
            ParentSchema = "dbo", ParentTable = "Orders", ParentColumns = ["CustomerId"],
            ReferencedSchema = "dbo", ReferencedTable = "Customers", ReferencedColumns = ["CustomerId"],
        });
        cache.RebuildFkIndex();
        return cache;
    }

    private static Task<SchemaContext> BuildAsync(
        DatabaseCache? cache, string? prompt, int compressionLevel = 3, int maxObjects = 500) =>
        new SchemaContextBuilder((_, _) => cache).BuildAsync(
            sessionId: "session-1",
            sessionLookup: _ => ("Server=localhost;Integrated Security=true", "TestDb"),
            prompt: prompt,
            compressionLevel: compressionLevel,
            maxObjects: maxObjects);

    // ── FR-024: general prompt → full inventory ─────────────────────────────

    [Fact]
    public async Task General_prompt_yields_the_full_inventory()
    {
        var ctx = await BuildAsync(CreateOrdersCache(), "what tables do I have in this database?");

        Assert.Equal(3, ctx.Objects.Count);
        Assert.Equal(3, ctx.TotalObjectCount);
        Assert.False(ctx.Truncated);

        var text = SchemaContextFormatter.Format(ctx);
        Assert.Contains("dbo.Orders", text);
        Assert.Contains("dbo.Customers", text);
        Assert.Contains("dbo.Products", text);
    }

    // ── FR-025 / R6: incidental noise-token match must not shrink inventory ──

    [Fact]
    public async Task Noise_token_incidental_match_still_yields_full_inventory()
    {
        // "do" survives the stop list (length 2, not a keyword) and substring-matches "Domino".
        // Under the old filter-then-cap path this returned ONLY dbo.Domino (R6).
        var cache = CreateCache(
            Table("dbo", "Domino"),
            Table("dbo", "Orders"),
            Table("dbo", "Customers"));

        var ctx = await BuildAsync(cache, "what do I have");

        Assert.Equal(3, ctx.Objects.Count);
        var text = SchemaContextFormatter.Format(ctx);
        Assert.Contains("dbo.Domino", text);
        Assert.Contains("dbo.Orders", text);
        Assert.Contains("dbo.Customers", text);
    }

    // ── FR-023: named object promoted to level-3 detail ─────────────────────

    [Fact]
    public async Task Named_object_is_promoted_to_level3_detail_with_columns_pk_and_fks()
    {
        var ctx = await BuildAsync(CreateOrdersCache(), "describe the columns of Orders");

        Assert.Contains("dbo.Orders", ctx.DetailedObjectNames);

        var orders = Assert.Single(ctx.Objects, o => o.Name == "Orders");
        Assert.NotNull(orders.Columns);
        Assert.Contains(orders.Columns, c => c.Name == "OrderId" && c.IsPrimaryKey);
        Assert.NotNull(orders.PrimaryKey);
        Assert.Contains("OrderId", orders.PrimaryKey);

        var customerId = Assert.Single(orders.Columns, c => c.Name == "CustomerId");
        Assert.Equal("dbo.Customers.CustomerId", customerId.ForeignKeyTarget);

        Assert.Contains(ctx.ForeignKeys,
            fk => fk.ParentTable == "dbo.Orders" && fk.ReferencedTable == "dbo.Customers");

        var text = SchemaContextFormatter.Format(ctx);
        Assert.Contains("PK: OrderId", text);
        Assert.Contains("FK: CustomerId -> dbo.Customers.CustomerId", text);
    }

    // ── FR-023: FK 1-hop neighbours are promoted too ────────────────────────

    [Fact]
    public async Task Fk_one_hop_neighbours_of_named_object_are_promoted()
    {
        var ctx = await BuildAsync(CreateOrdersCache(), "describe Orders");

        Assert.Contains("dbo.Orders", ctx.DetailedObjectNames);
        Assert.Contains("dbo.Customers", ctx.DetailedObjectNames);

        // The unconnected table stays at inventory level: no column detail baked in.
        var products = Assert.Single(ctx.Objects, o => o.Name == "Products");
        Assert.Null(products.Columns);

        // The relationship is rendered (both sides are in the kept inventory).
        var text = SchemaContextFormatter.Format(ctx);
        Assert.Contains("Relationships:", text);
        Assert.Contains("dbo.Orders.CustomerId -> dbo.Customers.CustomerId", text);
        // Detail block for the promoted neighbour, inventory line for the rest.
        Assert.Contains("dbo.Customers(", text);
        Assert.Contains("dbo.Products", text);
    }

    // ── FR-026: budget exceeded → explicit truncation ───────────────────────

    [Fact]
    public async Task Exceeding_the_budget_sets_truncation_and_signals_it_in_the_text()
    {
        var objects = Enumerable.Range(1, 12)
            .Select(i => Table("dbo", $"Table{i:D2}"))
            .ToArray();
        var cache = CreateCache(objects);

        var ctx = await BuildAsync(cache, "what tables do I have", maxObjects: 5);

        Assert.True(ctx.Truncated);
        Assert.Equal(12, ctx.TotalObjectCount);
        Assert.Equal(5, ctx.Objects.Count);

        var text = SchemaContextFormatter.Format(ctx);
        Assert.Contains("NOTE: showing 5 of 12 objects", text);
        Assert.Contains("incomplete", text);
    }

    [Fact]
    public async Task Promoted_objects_survive_budget_truncation()
    {
        var cache = CreateOrdersCache();
        // Fill the inventory well past the budget so unpromoted objects are dropped.
        foreach (var extra in Enumerable.Range(1, 10).Select(i => Table("dbo", $"Filler{i:D2}")))
        {
            cache.Schemas["dbo"].Objects.Add(extra);
        }

        var ctx = await BuildAsync(cache, "describe Orders", maxObjects: 4);

        Assert.True(ctx.Truncated);
        Assert.Equal(13, ctx.TotalObjectCount);
        // Orders + its FK neighbour Customers are kept even though the budget is tight.
        Assert.Contains(ctx.Objects, o => o.Name == "Orders");
        Assert.Contains(ctx.Objects, o => o.Name == "Customers");
        Assert.Contains("dbo.Orders", ctx.DetailedObjectNames);
        Assert.Contains("dbo.Customers", ctx.DetailedObjectNames);
    }

    // ── FR-028: unbound vs connected-but-empty render distinctly ────────────

    [Fact]
    public async Task Unbound_context_renders_distinctly_from_a_connected_but_empty_database()
    {
        // Unbound: the session lookup finds no connection at all.
        var unbound = await new SchemaContextBuilder((_, _) => null).BuildAsync(
            sessionId: "unknown",
            sessionLookup: _ => (null, null),
            prompt: "what tables do I have",
            compressionLevel: 3);

        // Connected but the database exposes no objects.
        var emptyDb = await BuildAsync(new DatabaseCache { CacheKey = "session-1:EmptyDb" }, null);

        var unboundText = SchemaContextFormatter.Format(unbound);
        var emptyText = SchemaContextFormatter.Format(emptyDb);

        Assert.NotEqual(unboundText, emptyText);
        Assert.Contains("No database connection", unboundText);
        Assert.DoesNotContain("No database connection", emptyText);
        Assert.Contains("TestDb", emptyText);
    }

    // ── T009: ghost text stays at detail level 1 (latency path unchanged) ───

    [Fact]
    public async Task Ghost_text_path_assembles_at_detail_level_1()
    {
        var ctx = await BuildAsync(CreateOrdersCache(), "select * from Orders", compressionLevel: 1);

        Assert.Equal(1, ctx.CompressionLevel);
        // No promotion at level 1: names only, no columns/PK detail baked in.
        Assert.Empty(ctx.DetailedObjectNames);
        var orders = Assert.Single(ctx.Objects, o => o.Name == "Orders");
        Assert.Null(orders.Columns);

        var text = SchemaContextFormatter.Format(ctx);
        Assert.Contains("dbo.Orders", text);
        Assert.DoesNotContain("PK:", text);
    }

    // ── T023: perf — assembly on a 500-object database adds < 200 ms ────────

    [Fact]
    public async Task Assembly_on_a_500_object_database_stays_under_200ms()
    {
        // Synthetic 500-object database: 10 schemas x 50 tables, 8 columns each,
        // a FK chain linking consecutive tables (250 FKs total).
        var tables = new List<DatabaseObject>();
        for (var s = 1; s <= 10; s++)
        {
            for (var t = 1; t <= 50; t++)
            {
                tables.Add(Table($"schema{s}", $"Table{t:D2}",
                    Col("Id", "int", pk: true, nullable: false),
                    Col("ParentId", "int"),
                    Col("Name", "nvarchar", maxLength: 200),
                    Col("Code", "varchar", maxLength: 40),
                    Col("Amount", "decimal", precision: 18, scale: 2),
                    Col("CreatedAt", "datetime2", nullable: false),
                    Col("IsActive", "bit", nullable: false),
                    Col("Notes", "nvarchar", maxLength: -1)));
            }
        }
        var cache = CreateCache(tables.ToArray());
        for (var s = 1; s <= 10; s++)
        {
            for (var t = 2; t <= 26; t++)
            {
                cache.ForeignKeys.Add(new ForeignKey
                {
                    FkName = $"FK_S{s}_T{t}",
                    ParentSchema = $"schema{s}", ParentTable = $"Table{t:D2}", ParentColumns = ["ParentId"],
                    ReferencedSchema = $"schema{s}", ReferencedTable = $"Table{t - 1:D2}", ReferencedColumns = ["Id"],
                });
            }
        }
        cache.RebuildFkIndex();
        Assert.Equal(500, cache.GetAllObjects().Count());

        var builder = new SchemaContextBuilder((_, _) => cache);
        const string prompt = "show orders in schema3.Table07 and their customers";

        async Task<long> TimeOnceAsync()
        {
            var sw = Stopwatch.StartNew();
            var ctx = await builder.BuildAsync(
                "session-1", _ => ("Server=localhost", "BigDb"), prompt, compressionLevel: 3);
            var text = SchemaContextFormatter.Format(ctx);
            sw.Stop();
            Assert.Equal(500, ctx.Objects.Count);
            Assert.False(string.IsNullOrEmpty(text));
            return sw.ElapsedMilliseconds;
        }

        await TimeOnceAsync(); // warmup (JIT)

        var timings = new List<long>();
        for (var i = 0; i < 10; i++)
        {
            timings.Add(await TimeOnceAsync());
        }

        var average = timings.Average();
        _output.WriteLine(
            $"Schema context assembly, 500 objects, level 3 + format: " +
            $"avg {average:0.0} ms, max {timings.Max()} ms over {timings.Count} runs " +
            $"[{string.Join(", ", timings)}]");

        Assert.True(average < 200,
            $"Assembly must add < 200 ms per request (contract/schema-context.md); average was {average:0.0} ms");
    }
}
