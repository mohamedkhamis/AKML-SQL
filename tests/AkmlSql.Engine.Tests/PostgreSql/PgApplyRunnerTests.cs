using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.PostgreSql;
using Npgsql;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T135, FR-131, FR-132, FR-133, SC-014). Against a real server: an Apply writes by the row's original key values,
/// each edit touches exactly one row, the whole batch is all-or-nothing, values are read as PostgreSQL displays them and converted
/// by the server, and the written rows come back as the server stored them. A transaction the user has open is joined and left
/// open; an aborted one is refused.
/// </summary>
[Collection("PostgreSqlLive")]
[Trait("Category", "PostgreSql")]
public class PgApplyRunnerTests
{
    private const string Seed = """
        CREATE TABLE items (
            id serial PRIMARY KEY, name text NOT NULL, price numeric(10,2), added date, tags text[], meta jsonb,
            flag boolean, cnt integer DEFAULT 7, uid uuid, ts timestamptz);
        CREATE TABLE items_expected (LIKE items INCLUDING DEFAULTS);
        INSERT INTO items (name, price, added, tags, meta, flag, cnt, uid, ts)
            SELECT 'item ' || g, g, DATE '2026-01-01' + g, ARRAY['a' || g, 'b'], jsonb_build_object('n', g), g % 2 = 0, g,
                   ('00000000-0000-0000-0000-' || lpad(g::text, 12, '0'))::uuid, TIMESTAMPTZ '2026-01-01 00:00:00+00' + (g || ' hours')::interval
            FROM generate_series(1, 20) g;
        INSERT INTO items_expected SELECT * FROM items;

        CREATE TABLE "Order Details" ("Order ID" int, "Product ID" int, "Unit Price" numeric(10,2), PRIMARY KEY ("Order ID", "Product ID"));
        INSERT INTO "Order Details" VALUES (1, 1, 10), (1, 2, 20);

        CREATE TABLE shouty (id int PRIMARY KEY, name text);
        INSERT INTO shouty VALUES (1, 'a');
        CREATE FUNCTION shout() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN NEW.name := upper(NEW.name); RETURN NEW; END $$;
        CREATE TRIGGER shout_t BEFORE INSERT OR UPDATE ON shouty FOR EACH ROW EXECUTE FUNCTION shout();

        CREATE TABLE strict_t (id int PRIMARY KEY, code text UNIQUE NOT NULL, qty int CHECK (qty >= 0));
        INSERT INTO strict_t VALUES (1, 'a', 1), (2, 'b', 2);
        """;

    private static readonly string[] ItemColumns = { "id", "name", "price", "added", "tags", "meta", "flag", "cnt", "uid", "ts" };

    private sealed class Db : IAsyncDisposable
    {
        private readonly PgTestDatabase _database;

        private Db(PgTestDatabase database, NpgsqlConnection connection)
        {
            _database = database;
            Connection = connection;
        }

        public NpgsqlConnection Connection { get; }

        public static async Task<Db> OpenAsync()
        {
            var database = await PgTestServer.CreateDatabaseAsync(Seed);
            var connection = PgConnectionFactory.Create(database.ConnectionString);
            await connection.OpenAsync();
            return new Db(database, connection);
        }

        public Task<ApplyChangesResult> ApplyAsync(ApplyChangesRequest request, TransactionState state = TransactionState.None) =>
            new PgApplyRunner().RunAsync(Connection, request, state, CancellationToken.None);

        public async Task<string?> ScalarAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, Connection);
            var value = await command.ExecuteScalarAsync();
            return value is null or DBNull ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public async Task ExecAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, Connection);
            await command.ExecuteNonQueryAsync();
        }

        public Task<string?> ChecksumAsync(string table) =>
            ScalarAsync($"SELECT md5(coalesce(string_agg(t::text, E'\\n' ORDER BY id), '')) FROM {table} t");

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private static CrudCellDto C(string column, string? value, string type) => new() { BaseColumnName = column, Value = value, TypeName = type };

    private static ApplyChangesRequest Request(string table, string[]? returning, params CrudEditDto[] edits) =>
        new() { SessionId = "s", BaseSchema = "public", BaseTable = table, ReturnColumns = returning, Edits = edits };

    private static CrudEditDto Update(CrudCellDto[] set, params CrudCellDto[] key) => new() { Op = CrudOp.Update, SetCells = set, KeyCells = key };

    private static CrudEditDto Delete(params CrudCellDto[] key) => new() { Op = CrudOp.Delete, KeyCells = key };

    private static CrudEditDto Insert(params CrudCellDto[] set) => new() { Op = CrudOp.Insert, SetCells = set };

    private static CrudCellDto Id(int id) => C("id", id.ToString(), "integer");

    // ---- SC-014: thirty edits across ten column types ---------------------------------------------------------------

    [SkippableFact]
    public async Task Thirty_edits_inserts_and_deletes_across_ten_column_types_land_exactly()
    {
        await using var db = await Db.OpenAsync();
        var edits = new List<CrudEditDto>();

        // 15 updates, one per kind of value, spread over the first rows.
        for (var i = 1; i <= 15; i++)
        {
            var set = (i % 5) switch
            {
                0 => new[] { C("name", $"renamed {i}", "text"), C("price", $"{i}.50", "numeric(10,2)") },
                1 => new[] { C("added", $"2027-02-{i:00}", "date"), C("flag", i % 2 == 0 ? "true" : "false", "boolean") },
                2 => new[] { C("tags", $"{{x{i},\"y z\"}}", "text[]"), C("cnt", (i * 10).ToString(), "integer") },
                3 => new[] { C("meta", $"{{\"edited\": {i}}}", "jsonb"), C("uid", $"11111111-1111-1111-1111-{i:000000000000}", "uuid") },
                _ => new[] { C("ts", $"2027-03-04 05:06:{i:00}+02", "timestamp with time zone"), C("price", null, "numeric(10,2)") },
            };
            edits.Add(Update(set, Id(i)));
        }
        // 10 inserts (ids 21..30 from the sequence).
        for (var i = 1; i <= 10; i++)
        {
            edits.Add(Insert(
                C("name", $"new {i}", "text"), C("price", $"{i}.25", "numeric(10,2)"), C("added", $"2028-01-{i:00}", "date"),
                C("tags", "{n}", "text[]"), C("meta", $"{{\"new\": {i}}}", "jsonb"), C("flag", "true", "boolean"),
                C("uid", $"22222222-2222-2222-2222-{i:000000000000}", "uuid"), C("ts", "2028-01-01 00:00:00+00", "timestamp with time zone")));
        }
        // 5 deletes.
        for (var i = 16; i <= 20; i++) edits.Add(Delete(Id(i)));
        Assert.Equal(30, edits.Count);

        var result = await db.ApplyAsync(Request("items", ItemColumns, edits.ToArray()));

        Assert.Equal(ExecuteStatus.Ok, result.Status);
        Assert.All(result.Results, r => Assert.True(r.Ok, r.Error));
        Assert.Equal(30, result.Results.Length);

        // The same changes written as plain statements on a twin table; the two tables must be identical, byte for byte of their rows.
        await db.ExecAsync("""
            UPDATE items_expected SET name = 'renamed 5', price = 5.50 WHERE id = 5;
            UPDATE items_expected SET name = 'renamed 10', price = 10.50 WHERE id = 10;
            UPDATE items_expected SET name = 'renamed 15', price = 15.50 WHERE id = 15;
            UPDATE items_expected SET added = '2027-02-01', flag = false WHERE id = 1;
            UPDATE items_expected SET added = '2027-02-06', flag = true WHERE id = 6;
            UPDATE items_expected SET added = '2027-02-11', flag = false WHERE id = 11;
            UPDATE items_expected SET tags = '{x2,"y z"}', cnt = 20 WHERE id = 2;
            UPDATE items_expected SET tags = '{x7,"y z"}', cnt = 70 WHERE id = 7;
            UPDATE items_expected SET tags = '{x12,"y z"}', cnt = 120 WHERE id = 12;
            UPDATE items_expected SET meta = '{"edited": 3}', uid = '11111111-1111-1111-1111-000000000003' WHERE id = 3;
            UPDATE items_expected SET meta = '{"edited": 8}', uid = '11111111-1111-1111-1111-000000000008' WHERE id = 8;
            UPDATE items_expected SET meta = '{"edited": 13}', uid = '11111111-1111-1111-1111-000000000013' WHERE id = 13;
            UPDATE items_expected SET ts = '2027-03-04 05:06:04+02', price = NULL WHERE id = 4;
            UPDATE items_expected SET ts = '2027-03-04 05:06:09+02', price = NULL WHERE id = 9;
            UPDATE items_expected SET ts = '2027-03-04 05:06:14+02', price = NULL WHERE id = 14;
            DELETE FROM items_expected WHERE id BETWEEN 16 AND 20;
            INSERT INTO items_expected (id, name, price, added, tags, meta, flag, uid, ts)
                SELECT 20 + g, 'new ' || g, g + 0.25, DATE '2027-12-31' + g, '{n}', jsonb_build_object('new', g), true,
                       ('22222222-2222-2222-2222-' || lpad(g::text, 12, '0'))::uuid, TIMESTAMPTZ '2028-01-01 00:00:00+00'
                FROM generate_series(1, 10) g;
            """);
        Assert.Equal(await db.ChecksumAsync("items_expected"), await db.ChecksumAsync("items"));
        Assert.Equal("25", await db.ScalarAsync("SELECT count(*) FROM items"));
    }

    // ---- the written row comes back as the server stored it -------------------------------------------------------

    [SkippableFact]
    public async Task An_update_returns_the_row_as_stored_not_as_typed()
    {
        await using var db = await Db.OpenAsync();

        var result = await db.ApplyAsync(Request("items", new[] { "id", "price", "cnt" }, Update(new[] { C("price", "5", "numeric(10,2)") }, Id(2))));

        Assert.Equal(ExecuteStatus.Ok, result.Status);
        Assert.Equal(new string?[] { "2", "5.00", "2" }, result.Results[0].Row);
        Assert.Equal(1, result.Results[0].RowsAffected);
    }

    [SkippableFact]
    public async Task An_insert_returns_the_generated_key_and_the_defaults()
    {
        await using var db = await Db.OpenAsync();

        var result = await db.ApplyAsync(Request("items", new[] { "id", "name", "cnt" }, Insert(C("name", "fresh", "text"))));

        Assert.True(result.Results[0].Ok, result.Results[0].Error);
        Assert.Equal(new string?[] { "21", "fresh", "7" }, result.Results[0].Row);   // the sequence's next value and the column default
    }

    [SkippableFact]
    public async Task What_a_trigger_changed_is_what_comes_back()
    {
        await using var db = await Db.OpenAsync();

        var result = await db.ApplyAsync(Request("shouty", new[] { "id", "name" }, Update(new[] { C("name", "abc", "text") }, Id(1))));

        Assert.Equal(new string?[] { "1", "ABC" }, result.Results[0].Row);
    }

    [SkippableFact]
    public async Task A_delete_and_a_request_that_asks_for_no_columns_return_no_row()
    {
        await using var db = await Db.OpenAsync();

        var deleted = await db.ApplyAsync(Request("items", ItemColumns, Delete(Id(1))));
        var plain = await db.ApplyAsync(Request("items", null, Update(new[] { C("cnt", "99", "integer") }, Id(2))));

        Assert.Null(deleted.Results[0].Row);
        Assert.Null(plain.Results[0].Row);
        Assert.Equal(1, plain.Results[0].RowsAffected);
        Assert.Equal("99", await db.ScalarAsync("SELECT cnt FROM items WHERE id = 2"));
    }

    // ---- keys ------------------------------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_composite_key_and_quoted_names_are_written_by_the_original_key()
    {
        await using var db = await Db.OpenAsync();
        var request = new ApplyChangesRequest
        {
            SessionId = "s", BaseSchema = "public", BaseTable = "Order Details", ReturnColumns = new[] { "Order ID", "Product ID", "Unit Price" },
            Edits = new[] { Update(new[] { C("Unit Price", "99.5", "numeric(10,2)") }, C("Order ID", "1", "integer"), C("Product ID", "2", "integer")) },
        };

        var result = await db.ApplyAsync(request);

        Assert.True(result.Results[0].Ok, result.Results[0].Error);
        Assert.Equal(new string?[] { "1", "2", "99.50" }, result.Results[0].Row);
        Assert.Equal("10.00", await db.ScalarAsync("SELECT \"Unit Price\" FROM \"Order Details\" WHERE \"Product ID\" = 1"));   // the other row is untouched
    }

    [SkippableFact]
    public async Task A_key_that_changed_in_the_meantime_is_not_found_and_nothing_else_is_kept()
    {
        await using var db = await Db.OpenAsync();
        await db.ExecAsync("DELETE FROM items WHERE id = 3");      // someone else removed the row the grid still shows
        var before = await db.ChecksumAsync("items");

        var result = await db.ApplyAsync(Request("items", null,
            Update(new[] { C("cnt", "1000", "integer") }, Id(2)),
            Update(new[] { C("cnt", "2000", "integer") }, Id(3))));

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.False(result.Results[1].Ok);
        Assert.Contains("0 row(s)", result.Results[1].Error);
        Assert.Contains("exactly 1", result.Results[1].Error);
        Assert.False(result.Results[0].Ok);
        Assert.StartsWith("Rolled back", result.Results[0].Error);
        Assert.Contains("Edit 1 failed", result.ErrorMessage);
        Assert.Equal(before, await db.ChecksumAsync("items"));
    }

    // ---- the server's own refusals --------------------------------------------------------------------------------

    [SkippableFact]
    public async Task A_NOT_NULL_column_refuses_NULL_with_the_servers_message_and_nothing_is_applied()
    {
        await using var db = await Db.OpenAsync();
        var before = await db.ChecksumAsync("items");

        var result = await db.ApplyAsync(Request("items", null,
            Update(new[] { C("cnt", "5", "integer") }, Id(1)),
            Update(new[] { C("name", null, "text") }, Id(2))));

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.Contains("null value in column \"name\"", result.Results[1].Error);
        Assert.Contains("not-null constraint", result.Results[1].Error);
        Assert.Equal(before, await db.ChecksumAsync("items"));
    }

    [SkippableFact]
    public async Task Text_the_column_cannot_read_is_the_servers_message_and_nothing_is_applied()
    {
        await using var db = await Db.OpenAsync();
        var before = await db.ChecksumAsync("items");

        var result = await db.ApplyAsync(Request("items", null, Update(new[] { C("price", "abc", "numeric(10,2)") }, Id(1))));

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.Contains("invalid input syntax for type numeric", result.Results[0].Error);
        Assert.Contains("abc", result.Results[0].Error);
        Assert.Equal(before, await db.ChecksumAsync("items"));
    }

    [SkippableFact]
    public async Task A_unique_or_check_violation_names_the_constraint_and_the_failing_row_and_rolls_everything_back()
    {
        await using var db = await Db.OpenAsync();
        var before = await db.ChecksumAsync("strict_t");

        var result = await db.ApplyAsync(Request("strict_t", null,
            Update(new[] { C("qty", "10", "integer") }, Id(1)),
            Update(new[] { C("code", "b", "text") }, Id(1)),           // duplicate of row 2
            Update(new[] { C("qty", "20", "integer") }, Id(2))));

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.Contains("duplicate key value", result.Results[1].Error);
        Assert.Contains("strict_t_code_key", result.Results[1].Error);
        Assert.StartsWith("Rolled back", result.Results[0].Error);
        Assert.StartsWith("Rolled back", result.Results[2].Error);
        Assert.Contains("Edit 1 failed", result.ErrorMessage);
        Assert.Equal(before, await db.ChecksumAsync("strict_t"));

        var check = await db.ApplyAsync(Request("strict_t", null, Update(new[] { C("qty", "-1", "integer") }, Id(1))));
        Assert.Contains("check constraint", check.Results[0].Error);
    }

    [SkippableFact]
    public async Task A_failure_in_the_middle_undoes_the_edits_before_it_and_the_connection_stays_usable()
    {
        await using var db = await Db.OpenAsync();

        var result = await db.ApplyAsync(Request("items", null,
            Insert(C("name", "kept?", "text")),
            Update(new[] { C("price", "x", "numeric(10,2)") }, Id(1)),
            Insert(C("name", "never ran", "text"))));

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.Equal("20", await db.ScalarAsync("SELECT count(*) FROM items"));
        Assert.Equal("1", await db.ScalarAsync("SELECT 1"));
    }

    // ---- refusals of the request itself -------------------------------------------------------------------------------

    [SkippableFact]
    public async Task An_update_or_delete_with_no_key_is_refused_and_nothing_is_written()
    {
        await using var db = await Db.OpenAsync();
        var before = await db.ChecksumAsync("items");

        var result = await db.ApplyAsync(Request("items", null, Update(new[] { C("cnt", "0", "integer") })));

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.Contains("key", result.Results[0].Error);
        Assert.Equal(before, await db.ChecksumAsync("items"));
    }

    [SkippableFact]
    public async Task A_type_name_that_could_end_the_cast_is_refused()
    {
        await using var db = await Db.OpenAsync();
        var before = await db.ChecksumAsync("items");

        var result = await db.ApplyAsync(Request("items", null,
            Update(new[] { C("cnt", "1", "integer); DROP TABLE items; --") }, Id(1))));

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.Equal(before, await db.ChecksumAsync("items"));
        Assert.Equal("20", await db.ScalarAsync("SELECT count(*) FROM items"));
    }

    [SkippableFact]
    public async Task No_edits_is_an_empty_success()
    {
        await using var db = await Db.OpenAsync();

        var result = await db.ApplyAsync(Request("items", null));

        Assert.Equal(ExecuteStatus.Ok, result.Status);
        Assert.Empty(result.Results);
    }

    // ---- the user's own transaction ----------------------------------------------------------------------------------

    [SkippableFact]
    public async Task Inside_an_open_transaction_the_edits_join_it_and_leave_it_open()
    {
        await using var db = await Db.OpenAsync();
        await db.ExecAsync("BEGIN");

        var result = await db.ApplyAsync(Request("items", null, Update(new[] { C("cnt", "4242", "integer") }, Id(1))), TransactionState.Open);

        Assert.Equal(ExecuteStatus.Ok, result.Status);
        Assert.Equal("4242", await db.ScalarAsync("SELECT cnt FROM items WHERE id = 1"));
        await db.ExecAsync("ROLLBACK");
        Assert.Equal("1", await db.ScalarAsync("SELECT cnt FROM items WHERE id = 1"));   // it was never committed: the user's transaction decided
    }

    [SkippableFact]
    public async Task A_failure_inside_an_open_transaction_undoes_only_the_apply_and_the_transaction_stays_usable()
    {
        await using var db = await Db.OpenAsync();
        await db.ExecAsync("BEGIN");
        await db.ExecAsync("UPDATE items SET cnt = 555 WHERE id = 5");      // the user's own earlier work

        var result = await db.ApplyAsync(Request("items", null,
            Update(new[] { C("cnt", "1", "integer") }, Id(1)),
            Update(new[] { C("price", "bad", "numeric(10,2)") }, Id(2))), TransactionState.Open);

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.Equal("1", await db.ScalarAsync("SELECT cnt FROM items WHERE id = 1"));      // the apply is undone ...
        Assert.Equal("555", await db.ScalarAsync("SELECT cnt FROM items WHERE id = 5"));    // ... the user's work and the transaction are not
        await db.ExecAsync("COMMIT");
        Assert.Equal("555", await db.ScalarAsync("SELECT cnt FROM items WHERE id = 5"));
    }

    [SkippableFact]
    public async Task An_aborted_transaction_refuses_the_apply_and_says_to_roll_back()
    {
        await using var db = await Db.OpenAsync();
        var before = await db.ChecksumAsync("items");

        var result = await db.ApplyAsync(Request("items", null, Update(new[] { C("cnt", "1", "integer") }, Id(1))), TransactionState.Aborted);

        Assert.Equal(ExecuteStatus.Error, result.Status);
        Assert.Contains("aborted", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ROLLBACK", result.ErrorMessage);
        Assert.Equal(before, await db.ChecksumAsync("items"));
    }
}
