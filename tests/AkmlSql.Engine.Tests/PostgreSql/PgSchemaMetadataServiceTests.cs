using System.Diagnostics;
using AkmlSql.Core;
using AkmlSql.Engine.PostgreSql;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using Npgsql;
using Xunit;
using Index = AkmlSql.Engine.Schema.Models.Index;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T073, FR-030 to FR-046, contracts/catalog-queries.md): the PostgreSQL catalog reader against a real server.
/// Phase A names the objects (with row estimates and descriptions); Phase B gives the columns, keys, foreign keys, indexes and
/// parameters; the search path, the hidden system schemas and the inaccessible schemas are recorded on the cache.
/// </summary>
[Collection("PostgreSqlLive")]
[Trait("Category", "PostgreSql")]
public class PgSchemaMetadataServiceTests
{
    private const string Seed = @"
CREATE SCHEMA sales;
CREATE SCHEMA hr;
CREATE TABLE sales.customers (
    id serial PRIMARY KEY,
    name varchar(50) NOT NULL,
    balance numeric(10,2) DEFAULT 0,
    created timestamptz DEFAULT now(),
    full_name text GENERATED ALWAYS AS (name || '!') STORED);
COMMENT ON TABLE sales.customers IS 'People who buy';
COMMENT ON COLUMN sales.customers.name IS 'Full name';
CREATE TABLE sales.orders (
    id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    customer_id int NOT NULL REFERENCES sales.customers(id) ON DELETE CASCADE,
    total numeric(12,2),
    note text);
CREATE INDEX orders_customer ON sales.orders (customer_id DESC);
CREATE UNIQUE INDEX orders_note ON sales.orders (note);
CREATE VIEW sales.big_orders AS SELECT id, total FROM sales.orders WHERE total > 100;
CREATE MATERIALIZED VIEW sales.order_totals AS SELECT customer_id, sum(total) AS t FROM sales.orders GROUP BY customer_id;
CREATE SEQUENCE sales.invoice_seq;
CREATE FUNCTION sales.add_numbers(a int, b int DEFAULT 1) RETURNS int LANGUAGE sql AS 'SELECT a + b';
CREATE FUNCTION sales.orders_of(cid int) RETURNS TABLE(order_id bigint, order_total numeric) LANGUAGE sql
    AS 'SELECT id, total FROM sales.orders WHERE customer_id = cid';
CREATE PROCEDURE sales.archive(IN cutoff date, INOUT n int) LANGUAGE sql AS 'SELECT 0';
CREATE TABLE public.plain (x int);
CREATE TABLE hr.staff (id int PRIMARY KEY, boss int REFERENCES hr.staff(id));
";

    private static async Task<(DatabaseCache Cache, PgSchemaMetadataService Service, PgTestDatabase Database)> LoadAsync(bool phaseB = true, string seed = Seed)
    {
        var database = await PgTestServer.CreateDatabaseAsync(seed);
        var service = new PgSchemaMetadataService();
        var cache = new DatabaseCache { CacheKey = "pg://localhost:" + database.Name };
        await service.PopulatePhaseAAsync(cache, database.ConnectionString, CancellationToken.None);
        if (phaseB) await service.PopulatePhaseBAsync(cache, database.ConnectionString, CancellationToken.None);
        return (cache, service, database);
    }

    private static DatabaseObject Object(DatabaseCache cache, string schema, string name) =>
        cache.FindObject(schema, name) ?? throw new Xunit.Sdk.XunitException($"{schema}.{name} is not in the cache.");

    [Fact]
    public void The_service_is_the_PostgreSQL_provider()
    {
        Assert.Equal(DatabaseKind.PostgreSql, new PgSchemaMetadataService().Kind);
    }

    // ── Phase A ─────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Phase_A_names_every_kind_of_object_with_its_type_and_description()
    {
        var (cache, _, database) = await LoadAsync(phaseB: false);
        await using var _ = database;

        Assert.Equal(PopulationPhase.PhaseA, cache.Phase);
        Assert.Equal(DbObjectType.Table, Object(cache, "sales", "customers").ObjectType);
        Assert.Equal(DbObjectType.View, Object(cache, "sales", "big_orders").ObjectType);
        Assert.Equal(DbObjectType.MaterializedView, Object(cache, "sales", "order_totals").ObjectType);
        Assert.Equal(DbObjectType.Sequence, Object(cache, "sales", "invoice_seq").ObjectType);
        Assert.Equal(DbObjectType.ScalarFunction, Object(cache, "sales", "add_numbers").ObjectType);
        Assert.Equal(DbObjectType.TableFunction, Object(cache, "sales", "orders_of").ObjectType);
        Assert.Equal(DbObjectType.Procedure, Object(cache, "sales", "archive").ObjectType);
        Assert.Equal("People who buy", Object(cache, "sales", "customers").Description);
        Assert.Contains("hr", cache.GetSchemaNames());
        Assert.Contains("public", cache.GetSchemaNames());
        Assert.False(Object(cache, "sales", "customers").ColumnsLoaded);
    }

    [SkippableFact]
    public async Task Indexes_toast_tables_and_composite_types_are_not_listed_as_objects()
    {
        var (cache, _, database) = await LoadAsync(phaseB: false);
        await using var _ = database;

        var names = cache.GetObjectsInSchema("sales").Select(o => o.ObjectName).ToArray();

        Assert.DoesNotContain("orders_customer", names);
        Assert.DoesNotContain("customers_pkey", names);
        Assert.Contains("customers_id_seq", names);   // the sequence a serial column owns is a sequence like any other
        Assert.DoesNotContain(cache.GetAllObjects(), o => o.SchemaName.StartsWith("pg_toast", StringComparison.Ordinal));
    }

    [SkippableFact]
    public async Task The_search_path_and_the_hidden_system_schemas_are_recorded()
    {
        var (cache, _, database) = await LoadAsync(phaseB: false);
        await using var _ = database;

        Assert.Equal(new[] { "public" }, cache.SearchPath.ToArray());
        Assert.Equal(new[] { "information_schema", "pg_catalog" }, cache.HiddenSchemas.OrderBy(s => s, StringComparer.Ordinal).ToArray());
        // The system views are loaded so names resolve and the explorer can show them on request; their functions are not
        // (the reference carries those).
        Assert.NotNull(cache.FindObject("pg_catalog", "pg_class"));
        Assert.NotNull(cache.FindObject("information_schema", "tables"));
        Assert.DoesNotContain(cache.GetObjectsInSchema("pg_catalog"), o => o.ObjectType is DbObjectType.ScalarFunction or DbObjectType.Procedure);
    }

    [SkippableFact]
    public async Task Row_estimates_come_from_the_planners_statistics()
    {
        var (cache, service, database) = await LoadAsync(phaseB: false, seed: Seed + "INSERT INTO sales.customers (name) SELECT 'n' || g FROM generate_series(1, 500) g; ANALYZE sales.customers;");
        await using var _ = database;

        Assert.Equal(500, Object(cache, "sales", "customers").ApproxRowCount);
        Assert.Equal(0, Object(cache, "sales", "orders").ApproxRowCount);   // never analysed: -1 on newer servers, clamped
    }

    // ── Phase B ─────────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Phase_B_gives_columns_with_types_nullability_defaults_identity_and_keys()
    {
        var (cache, _, database) = await LoadAsync();
        await using var _ = database;

        Assert.Equal(PopulationPhase.PhaseB, cache.Phase);
        var customers = Object(cache, "sales", "customers");
        Assert.True(customers.ColumnsLoaded);
        Assert.Equal(new[] { "id", "name", "balance", "created", "full_name" }, customers.Columns.Select(c => c.ColumnName).ToArray());

        var id = customers.Columns[0];
        Assert.Equal("integer", id.TypeName);
        Assert.True(id.IsPrimaryKey);
        Assert.False(id.IsNullable);
        Assert.StartsWith("nextval(", id.DefaultValue);

        var name = customers.Columns[1];
        Assert.Equal("character varying(50)", name.TypeName);
        Assert.Equal(50, name.MaxLength);
        Assert.False(name.IsNullable);
        Assert.Equal("Full name", name.Description);

        var balance = customers.Columns[2];
        Assert.Equal("numeric(10,2)", balance.TypeName);
        Assert.Equal((10, 2), (balance.Precision, balance.Scale));
        Assert.True(balance.IsNullable);
        Assert.Equal("0", balance.DefaultValue);

        Assert.Equal("timestamp with time zone", customers.Columns[3].TypeName);
        Assert.True(customers.Columns[4].IsComputed);
        Assert.Contains("name", customers.Columns[4].ComputedDefinition);

        var orders = Object(cache, "sales", "orders");
        Assert.True(orders.Columns[0].IsIdentity);
        Assert.True(orders.Columns[0].IsPrimaryKey);
        Assert.Equal("bigint", orders.Columns[0].TypeName);
    }

    [SkippableFact]
    public async Task Views_and_materialized_views_have_columns_too()
    {
        var (cache, _, database) = await LoadAsync();
        await using var _ = database;

        Assert.Equal(new[] { "id", "total" }, Object(cache, "sales", "big_orders").Columns.Select(c => c.ColumnName).ToArray());
        Assert.Equal(new[] { "customer_id", "t" }, Object(cache, "sales", "order_totals").Columns.Select(c => c.ColumnName).ToArray());
    }

    [SkippableFact]
    public async Task Foreign_keys_carry_both_sides_columns_and_actions_and_the_index_is_built()
    {
        var (cache, _, database) = await LoadAsync();
        await using var _ = database;

        var fk = Assert.Single(cache.ForeignKeys, f => f.ParentTable == "orders");
        Assert.Equal("sales", fk.ParentSchema);
        Assert.Equal(new[] { "customer_id" }, fk.ParentColumns);
        Assert.Equal("sales", fk.ReferencedSchema);
        Assert.Equal("customers", fk.ReferencedTable);
        Assert.Equal(new[] { "id" }, fk.ReferencedColumns);
        Assert.Equal(FkAction.Cascade, fk.DeleteAction);
        Assert.Equal(FkAction.NoAction, fk.UpdateAction);

        var self = Assert.Single(cache.ForeignKeys, f => f.ParentTable == "staff");
        Assert.Equal(("hr", "staff"), (self.ReferencedSchema, self.ReferencedTable));

        Assert.Contains(cache.GetForeignKeysForTable("sales", "customers"), f => f.FkName == fk.FkName);
    }

    [SkippableFact]
    public async Task Indexes_list_their_columns_direction_and_uniqueness()
    {
        var (cache, _, database) = await LoadAsync();
        await using var _ = database;

        var orders = Object(cache, "sales", "orders");
        var byCustomer = orders.Indexes.Single(i => i.IndexName == "orders_customer");
        Assert.False(byCustomer.IsUnique);
        Assert.False(byCustomer.IsPrimaryKey);
        Assert.Equal(IndexType.Nonclustered, byCustomer.IndexType);
        var column = Assert.Single(byCustomer.Columns);
        Assert.Equal(("customer_id", true), (column.ColumnName, column.IsDescending));
        Assert.True(orders.Indexes.Single(i => i.IndexName == "orders_note").IsUnique);
        Assert.True(orders.Indexes.Single(i => i.IsPrimaryKey).IsUnique);
    }

    [SkippableFact]
    public async Task Parameters_come_with_types_direction_and_defaults()
    {
        var (cache, _, database) = await LoadAsync();
        await using var _ = database;

        var add = Object(cache, "sales", "add_numbers");
        Assert.Equal(new[] { "a", "b" }, add.Parameters.Select(p => p.ParameterName).ToArray());
        Assert.Equal(new[] { "integer", "integer" }, add.Parameters.Select(p => p.TypeName).ToArray());
        Assert.Equal(new[] { false, true }, add.Parameters.Select(p => p.HasDefault).ToArray());
        Assert.All(add.Parameters, p => Assert.False(p.IsOutput));

        var archive = Object(cache, "sales", "archive");
        Assert.Equal(new[] { "cutoff", "n" }, archive.Parameters.Select(p => p.ParameterName).ToArray());
        Assert.Equal(new[] { "date", "integer" }, archive.Parameters.Select(p => p.TypeName).ToArray());
        Assert.Equal(new[] { false, true }, archive.Parameters.Select(p => p.IsOutput).ToArray());
    }

    [SkippableFact]
    public async Task A_table_function_lists_its_result_columns_and_only_its_inputs_as_parameters()
    {
        var (cache, _, database) = await LoadAsync();
        await using var _ = database;

        var ordersOf = Object(cache, "sales", "orders_of");
        Assert.Equal(new[] { "cid" }, ordersOf.Parameters.Select(p => p.ParameterName).ToArray());
        Assert.Equal(new[] { "order_id", "order_total" }, ordersOf.Columns.Select(c => c.ColumnName).ToArray());
        Assert.Equal(new[] { "bigint", "numeric" }, ordersOf.Columns.Select(c => c.TypeName).ToArray());
    }

    // ── Access, databases, change detection ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task A_schema_the_user_cannot_use_is_marked_and_its_objects_are_left_out()
    {
        PgTestServer.SkipUnlessAvailable();
        var seed = "CREATE SCHEMA secret; CREATE TABLE secret.vault (k int);"
                   + "CREATE SCHEMA open_schema; CREATE TABLE open_schema.t (k int);"
                   + "GRANT USAGE ON SCHEMA open_schema TO PUBLIC; GRANT SELECT ON open_schema.t TO PUBLIC;";
        await using var database = await PgTestServer.CreateDatabaseAsync(seed);
        var role = "akml_reader_" + database.Name.Substring(7);

        // A login role that can connect but has no USAGE on the secret schema.
        await using (var admin = new NpgsqlConnection(PgTestServer.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE ROLE " + role + " LOGIN PASSWORD 'pw'", admin);
            await create.ExecuteNonQueryAsync();
        }
        try
        {
            var asReader = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Username = role, Password = "pw" }.ConnectionString;
            var service = new PgSchemaMetadataService();
            var cache = new DatabaseCache { CacheKey = "restricted" };

            await service.PopulatePhaseAAsync(cache, asReader, CancellationToken.None);
            await service.PopulatePhaseBAsync(cache, asReader, CancellationToken.None);

            Assert.Contains("secret", cache.InaccessibleSchemas);
            Assert.Contains("secret", cache.GetSchemaNames());
            Assert.Empty(cache.GetObjectsInSchema("secret"));
            Assert.NotNull(cache.FindObject("open_schema", "t"));
            Assert.Equal(PopulationPhase.PhaseB, cache.Phase);
            Assert.False(cache.PermissionDenied);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var cleanup = new NpgsqlConnection(PgTestServer.AdminConnectionString);
            await cleanup.OpenAsync();
            await using var drop = new NpgsqlCommand("DROP ROLE IF EXISTS " + role, cleanup);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [SkippableFact]
    public async Task A_wrong_password_marks_the_cache_permission_denied_without_throwing()
    {
        await using var database = await PgTestServer.CreateDatabaseAsync();
        var wrong = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Password = "definitely-wrong" }.ConnectionString;
        var cache = new DatabaseCache { CacheKey = "wrong-password" };

        await new PgSchemaMetadataService().PopulatePhaseAAsync(cache, wrong, CancellationToken.None);

        Assert.True(cache.PermissionDenied);
        Assert.Equal(PopulationPhase.NotLoaded, cache.Phase);
    }

    [SkippableFact]
    public async Task The_databases_list_has_no_templates_and_is_sorted()
    {
        PgTestServer.SkipUnlessAvailable();
        await using var database = await PgTestServer.CreateDatabaseAsync();

        var names = await new PgSchemaMetadataService().ListDatabasesAsync(PgTestServer.AdminConnectionString!, CancellationToken.None);

        Assert.Contains("postgres", names);
        Assert.Contains(database.Name, names);
        Assert.DoesNotContain("template0", names);
        Assert.DoesNotContain("template1", names);
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(), names.ToArray());
    }

    [SkippableFact]
    public async Task The_checksum_changes_when_an_object_is_created_and_not_otherwise()
    {
        await using var database = await PgTestServer.CreateDatabaseAsync(Seed);

        var before = await PgChangeChecksum.ComputeAsync(database.ConnectionString, CancellationToken.None);
        var same = await PgChangeChecksum.ComputeAsync(database.ConnectionString, CancellationToken.None);
        await using (var connection = new NpgsqlConnection(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand("CREATE TABLE public.added (x int)", connection);
            await create.ExecuteNonQueryAsync();
        }
        var after = await PgChangeChecksum.ComputeAsync(database.ConnectionString, CancellationToken.None);

        Assert.Equal(before, same);
        Assert.NotEqual(before, after);
    }

    // ── Speed (SC-008, T088) ────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task A_thousand_objects_load_names_in_two_seconds_and_details_in_ten()
    {
        var seed = PgTestServer.Fixture("thousand-objects.sql");
        await using var database = await PgTestServer.CreateDatabaseAsync(seed);
        var service = new PgSchemaMetadataService();
        var cache = new DatabaseCache { CacheKey = "thousand" };

        var phaseA = Stopwatch.StartNew();
        await service.PopulatePhaseAAsync(cache, database.ConnectionString, CancellationToken.None);
        phaseA.Stop();
        var phaseB = Stopwatch.StartNew();
        await service.PopulatePhaseBAsync(cache, database.ConnectionString, CancellationToken.None);
        phaseB.Stop();

        Assert.True(cache.GetObjectsInSchema("bulk").Count() >= 1000, "The fixture should create at least 1,000 objects.");
        Assert.True(phaseA.Elapsed < TimeSpan.FromSeconds(2), $"Phase A took {phaseA.Elapsed.TotalMilliseconds:F0} ms.");
        Assert.True(phaseB.Elapsed < TimeSpan.FromSeconds(10), $"Phase B took {phaseB.Elapsed.TotalMilliseconds:F0} ms.");
    }
}
