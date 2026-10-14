using System.Data;
using System.Data.Common;
using Npgsql;
using Npgsql.Schema;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T132, SPIKE S6, research R11). Records when Npgsql tells where a result column came from: the base table and
/// schema, the key flag, the identity flag and the table's OID, with and without <see cref="CommandBehavior.KeyInfo"/>, for a
/// plain table, an aliased column, an expression, a join, a view and a table with no primary key. The findings are written in
/// R11 of research.md; the assertions pin the ones the editing code relies on, so a driver upgrade that changes them fails here
/// and not in front of a user.
/// </summary>
[Collection("PostgreSqlLive")]
[Trait("Category", "PostgreSql")]
public class PgColumnSchemaSpikeTests
{
    private const string Seed = """
        CREATE TABLE people (id serial PRIMARY KEY, name text NOT NULL, born date);
        CREATE TABLE pairs (a int, b int, note text, PRIMARY KEY (a, b));
        CREATE TABLE loose (x int, y text);
        CREATE VIEW people_v AS SELECT id, name FROM people;
        INSERT INTO people (name, born) VALUES ('Ada', '1815-12-10'), ('Alan', '1912-06-23');
        """;

    private readonly ITestOutputHelper _output;

    public PgColumnSchemaSpikeTests(ITestOutputHelper output) => _output = output;

    private sealed record Seen(string Name, string? Table, string? Schema, bool? IsKey, bool? IsIdentity, bool? IsAutoIncrement, uint? TableOid, string? BaseColumn);

    private async Task<Seen[]> SchemaAsync(NpgsqlConnection connection, string sql, bool keyInfo)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(keyInfo ? CommandBehavior.KeyInfo : CommandBehavior.Default);
        var columns = await reader.GetColumnSchemaAsync();
        var seen = columns.Cast<NpgsqlDbColumn>().Select(c => new Seen(
            c.ColumnName, c.BaseTableName, c.BaseSchemaName, c.IsKey, c.IsIdentity, c.IsAutoIncrement, c.TableOID, c.BaseColumnName)).ToArray();
        foreach (var s in seen)
            _output.WriteLine($"  [{(keyInfo ? "KeyInfo" : "Default")}] {sql} -> {s}");
        return seen;
    }

    [SkippableFact]
    public async Task What_the_driver_tells_about_a_plain_table_with_and_without_KeyInfo()
    {
        await using var db = await PgTestServer.CreateDatabaseAsync(Seed);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        var without = await SchemaAsync(connection, "SELECT id, name, born FROM people", keyInfo: false);
        var with = await SchemaAsync(connection, "SELECT id, name, born FROM people", keyInfo: true);

        // Whatever KeyInfo changes, the editing code reads the schema from the reader opened WITH it; pin that this works.
        Assert.All(with, c => Assert.Equal("people", c.Table));
        Assert.All(with, c => Assert.Equal("public", c.Schema));
        Assert.All(with, c => Assert.NotNull(c.TableOid));
        Assert.True(with.Single(c => c.Name == "id").IsKey, "the primary key column is a key");
        Assert.False(with.Single(c => c.Name == "name").IsKey);
        _output.WriteLine("base table known without KeyInfo: " + (without.All(c => c.Table == "people")));
    }

    [SkippableFact]
    public async Task The_identity_flag_and_a_composite_key_are_reported()
    {
        await using var db = await PgTestServer.CreateDatabaseAsync(Seed);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        var people = await SchemaAsync(connection, "SELECT id, name FROM people", keyInfo: true);
        var pairs = await SchemaAsync(connection, "SELECT a, b, note FROM pairs", keyInfo: true);

        Assert.True(people.Single(c => c.Name == "id").IsAutoIncrement == true || people.Single(c => c.Name == "id").IsIdentity == true,
            "a serial column is reported as auto-generated");
        Assert.True(pairs.Single(c => c.Name == "a").IsKey);
        Assert.True(pairs.Single(c => c.Name == "b").IsKey);
        Assert.False(pairs.Single(c => c.Name == "note").IsKey);
    }

    [SkippableFact]
    public async Task An_alias_an_expression_a_join_and_a_view_are_told_apart()
    {
        await using var db = await PgTestServer.CreateDatabaseAsync(Seed);
        await using var connection = new NpgsqlConnection(db.ConnectionString);
        await connection.OpenAsync();

        var aliased = await SchemaAsync(connection, "SELECT id AS person_id, name FROM people", keyInfo: true);
        var expression = await SchemaAsync(connection, "SELECT id, upper(name) AS shout, id + 1 AS next FROM people", keyInfo: true);
        var join = await SchemaAsync(connection, "SELECT p.id, p.name, q.note FROM people p JOIN pairs q ON q.a = p.id", keyInfo: true);
        var view = await SchemaAsync(connection, "SELECT id, name FROM people_v", keyInfo: true);
        var noKey = await SchemaAsync(connection, "SELECT x, y FROM loose", keyInfo: true);

        Assert.Equal("id", aliased.Single(c => c.Name == "person_id").BaseColumn);
        Assert.Null(expression.Single(c => c.Name == "shout").Table);          // an expression has no table
        Assert.Null(expression.Single(c => c.Name == "next").Table);
        Assert.Equal(new[] { "people", "people", "pairs" }, join.Select(c => c.Table).ToArray());   // two tables: not editable
        Assert.Equal("people_v", view.First().Table);                          // a view is its own relation: not editable
        Assert.All(noKey, c => Assert.False(c.IsKey));                         // no primary key: not editable
    }
}
