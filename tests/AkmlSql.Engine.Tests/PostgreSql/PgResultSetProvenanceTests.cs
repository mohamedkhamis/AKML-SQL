using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.PostgreSql;
using Npgsql;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T133, FR-130, research R11). Against a real server: a result set is editable when it is the columns of one table
/// and includes every column of that table's primary key; otherwise it is read-only and says why. Provenance is collected only
/// when asked for, and only for statements that return a table's rows.
/// </summary>
[Collection("PostgreSqlLive")]
[Trait("Category", "PostgreSql")]
public class PgResultSetProvenanceTests
{
    private const string Seed = """
        CREATE TABLE people (id serial PRIMARY KEY, name text NOT NULL, born date);
        CREATE TABLE pairs (a int, b int, note text, PRIMARY KEY (a, b));
        CREATE TABLE loose (x int, y text);
        CREATE TABLE gen (id int GENERATED ALWAYS AS IDENTITY PRIMARY KEY, a int, b int GENERATED ALWAYS AS (a * 2) STORED);
        CREATE SCHEMA sales;
        CREATE TABLE sales."Order Details" ("Order ID" int PRIMARY KEY, "Unit Price" numeric(10,2));
        CREATE TABLE parted (id int, region text, PRIMARY KEY (id, region)) PARTITION BY LIST (region);
        CREATE TABLE parted_eu PARTITION OF parted FOR VALUES IN ('eu');
        CREATE VIEW people_v AS SELECT id, name FROM people;
        CREATE MATERIALIZED VIEW people_m AS SELECT id, name FROM people;
        INSERT INTO people (name, born) VALUES ('Ada', '1815-12-10'), ('Alan', '1912-06-23');
        INSERT INTO pairs VALUES (1, 1, 'x');
        INSERT INTO sales."Order Details" VALUES (1, 9.5);
        INSERT INTO parted VALUES (1, 'eu');
        """;

    /// <summary>One database with the seed, and one session connection to it, for one test.</summary>
    private sealed class Db : IAsyncDisposable
    {
        private readonly PgTestDatabase _database;
        private readonly NpgsqlConnection _connection;

        private Db(PgTestDatabase database, NpgsqlConnection connection)
        {
            _database = database;
            _connection = connection;
        }

        public static async Task<Db> OpenAsync()
        {
            var database = await PgTestServer.CreateDatabaseAsync(Seed);
            var connection = PgConnectionFactory.Create(database.ConnectionString);
            await connection.OpenAsync();
            return new Db(database, connection);
        }

        public async Task<ExecuteQueryResult> RunAsync(string sql, bool provenance = true) =>
            await new PgExecuteRunner().RunAsync(_connection, sql, TransactionState.None, 1000, 30, TimeSpan.FromMinutes(1), CancellationToken.None,
                schemaChanged: null, includeProvenance: provenance);

        public async Task<ExecuteResultSet> SetOfAsync(string sql)
        {
            var result = await RunAsync(sql);
            Assert.Equal(ExecuteStatus.Ok, result.Status);
            return Assert.Single(result.ResultSets);
        }

        public async ValueTask DisposeAsync()
        {
            await _connection.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    // ---- editable ----------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_single_table_select_with_its_primary_key_is_editable_with_the_tables_real_name()
    {
        await using var db = await Db.OpenAsync();
        var set = await db.SetOfAsync("SELECT id, name, born FROM people");

        Assert.True(set.IsEditable, set.ReadOnlyReason);
        Assert.Null(set.ReadOnlyReason);
        Assert.Equal("public", set.BaseSchema);
        Assert.Equal("people", set.BaseTable);
        Assert.Equal(new[] { "id", "name", "born" }, set.Provenance.Select(p => p.BaseColumnName).ToArray());
        var id = set.Provenance[0];
        Assert.True(id.IsKey);
        Assert.True(id.IsTruePrimaryKey);
        Assert.True(id.IsAutoIncrement);     // a serial column
        Assert.False(set.Provenance[1].IsKey);
        Assert.False(set.Provenance[1].IsReadOnly);
        Assert.False(set.Provenance[1].IsExpression);
        Assert.False(set.Provenance[1].AllowDBNull);   // NOT NULL
        Assert.True(set.Provenance[2].AllowDBNull);
    }

    [SkippableFact]
    public async Task ORDER_BY_and_LIMIT_do_not_change_what_a_set_is()
    {
        await using var db = await Db.OpenAsync();
        var set = await db.SetOfAsync("SELECT id, name FROM people ORDER BY name DESC LIMIT 1");

        Assert.True(set.IsEditable, set.ReadOnlyReason);
    }

    [SkippableFact]
    public async Task An_alias_is_still_the_real_column_and_an_expression_beside_the_key_is_read_only_but_does_not_stop_editing()
    {
        await using var db = await Db.OpenAsync();
        var set = await db.SetOfAsync("SELECT id AS person_id, name, upper(name) AS shout FROM people");

        Assert.True(set.IsEditable, set.ReadOnlyReason);
        Assert.Equal("id", set.Provenance[0].BaseColumnName);
        Assert.True(set.Provenance[0].IsKey);
        Assert.Null(set.Provenance[2].BaseColumnName);
        Assert.True(set.Provenance[2].IsExpression);
        Assert.True(set.Provenance[2].IsReadOnly);
    }

    [SkippableFact]
    public async Task A_composite_key_with_every_column_present_is_editable()
    {
        await using var db = await Db.OpenAsync();
        var set = await db.SetOfAsync("SELECT a, b, note FROM pairs");

        Assert.True(set.IsEditable, set.ReadOnlyReason);
        Assert.True(set.Provenance[0].IsTruePrimaryKey);
        Assert.True(set.Provenance[1].IsTruePrimaryKey);
        Assert.False(set.Provenance[2].IsTruePrimaryKey);
    }

    [SkippableFact]
    public async Task Quoted_mixed_case_names_in_another_schema_come_back_exactly()
    {
        await using var db = await Db.OpenAsync();
        var set = await db.SetOfAsync("SELECT \"Order ID\", \"Unit Price\" FROM sales.\"Order Details\"");

        Assert.True(set.IsEditable, set.ReadOnlyReason);
        Assert.Equal("sales", set.BaseSchema);
        Assert.Equal("Order Details", set.BaseTable);
        Assert.Equal("Order ID", set.Provenance[0].BaseColumnName);
        Assert.Equal("Unit Price", set.Provenance[1].BaseColumnName);
    }

    [SkippableFact]
    public async Task A_partitioned_table_is_editable()
    {
        await using var db = await Db.OpenAsync();
        var set = await db.SetOfAsync("SELECT id, region FROM parted");

        Assert.True(set.IsEditable, set.ReadOnlyReason);
        Assert.Equal("parted", set.BaseTable);
    }

    [SkippableFact]
    public async Task Generated_and_always_identity_columns_are_marked_so_the_grid_never_writes_them()
    {
        await using var db = await Db.OpenAsync();
        var set = await db.SetOfAsync("SELECT id, a, b FROM gen");

        Assert.True(set.IsEditable, set.ReadOnlyReason);
        Assert.True(set.Provenance[0].IsAutoIncrement);   // GENERATED ALWAYS AS IDENTITY
        Assert.False(set.Provenance[1].IsReadOnly);
        Assert.True(set.Provenance[2].IsReadOnly);        // a stored generated column
    }

    [SkippableFact]
    public async Task The_column_types_are_the_ones_the_casts_will_use()
    {
        await using var db = await Db.OpenAsync();
        var set = await db.SetOfAsync("SELECT \"Order ID\", \"Unit Price\" FROM sales.\"Order Details\"");

        Assert.Equal(new[] { "integer", "numeric(10,2)" }, set.ColumnSqlTypes);
    }

    // ---- read-only, with the reason ---------------------------------------------------------------------------------

    [SkippableTheory]
    [InlineData("SELECT p.id, p.name, q.note FROM people p JOIN pairs q ON q.a = p.id", "more than one table")]
    [InlineData("SELECT count(*) AS n FROM people", "not a table")]
    [InlineData("SELECT 1 AS one, 'x' AS two", "not a table")]
    [InlineData("SELECT id, name FROM people_v", "view")]
    [InlineData("SELECT id, name FROM people_m", "view")]
    [InlineData("SELECT x, y FROM loose", "no primary key")]
    [InlineData("SELECT a, note FROM pairs", "primary key")]
    [InlineData("SELECT name FROM people", "primary key")]
    [InlineData("SELECT id, id AS again FROM people", "twice")]
    public async Task A_set_that_cannot_be_edited_says_why(string sql, string reasonContains)
    {
        await using var db = await Db.OpenAsync();
        var set = await db.SetOfAsync(sql);

        Assert.False(set.IsEditable);
        Assert.NotNull(set.ReadOnlyReason);
        Assert.Contains(reasonContains, set.ReadOnlyReason, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task The_reason_for_a_partial_key_names_the_missing_columns()
    {
        await using var db = await Db.OpenAsync();
        var set = await db.SetOfAsync("SELECT a, note FROM pairs");

        Assert.Contains("b", set.ReadOnlyReason);
    }

    // ---- when nothing is collected ------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Without_the_request_for_it_no_provenance_is_collected_and_the_set_is_read_only()
    {
        await using var db = await Db.OpenAsync();
        var result = await db.RunAsync("SELECT id, name FROM people", provenance: false);

        var set = Assert.Single(result.ResultSets);
        Assert.False(set.IsEditable);
        Assert.Empty(set.Provenance);
        Assert.Null(set.ReadOnlyReason);
    }

    [SkippableFact]
    public async Task A_statement_that_changes_data_returns_a_read_only_set_with_no_provenance()
    {
        await using var db = await Db.OpenAsync();
        var result = await db.RunAsync("INSERT INTO people (name) VALUES ('Grace') RETURNING id, name");

        var set = Assert.Single(result.ResultSets);
        Assert.False(set.IsEditable);
        Assert.Empty(set.Provenance);
    }

    [SkippableFact]
    public async Task Every_set_of_a_script_is_judged_on_its_own()
    {
        await using var db = await Db.OpenAsync();
        var result = await db.RunAsync("SELECT id, name FROM people; SELECT x FROM loose; SELECT a, b, note FROM pairs;");

        Assert.Equal(new[] { true, false, true }, result.ResultSets.Select(s => s.IsEditable).ToArray());
        Assert.Equal(new[] { "people", null, "pairs" }, result.ResultSets.Select(s => s.IsEditable ? s.BaseTable : null).ToArray());
    }

    [SkippableFact]
    public async Task The_script_still_reports_its_command_tags_with_provenance_on()
    {
        await using var db = await Db.OpenAsync();
        var result = await db.RunAsync("SELECT id FROM people;");

        Assert.Contains(result.Messages, m => m.Contains("SELECT 2"));
    }
}
