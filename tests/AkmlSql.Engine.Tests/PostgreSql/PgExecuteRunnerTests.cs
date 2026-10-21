using AkmlSql.Core;
using AkmlSql.Core.Connections;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.PostgreSql;
using Npgsql;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T043, FR-010 to FR-019, SC-007): the PostgreSQL runner against a real server. It needs
/// <c>AKML_PG_TEST_CONNECTION</c> (see scripts/pg-test-server.ps1) and skips without it. Each test works in its own
/// database and keeps one connection across its runs, as a workspace session does.
/// </summary>
[Collection("PostgreSqlLive")]
[Trait("Category", "PostgreSql")]
public class PgExecuteRunnerTests
{
    private sealed class Session : IAsyncDisposable
    {
        private readonly PgTestDatabase _database;

        private Session(PgTestDatabase database, NpgsqlConnection connection)
        {
            _database = database;
            Connection = connection;
        }

        public NpgsqlConnection Connection { get; }
        public TransactionState State { get; private set; }

        /// <summary>What the session carries between runs, as the execute handler does (FR-043).</summary>
        public PgExecuteRunner.PgSchemaChangeState SchemaChange { get; } = new();

        public bool SchemaChangePending => SchemaChange.Pending;

        public static async Task<Session> OpenAsync(string? seed = null)
        {
            var database = await PgTestServer.CreateDatabaseAsync(seed);
            var connection = PgConnectionFactory.Create(database.ConnectionString);
            await connection.OpenAsync();
            return new Session(database, connection);
        }

        public async Task<ExecuteQueryResult> RunAsync(string sql, int maxRows = 1000, int timeoutSeconds = 30, CancellationToken cancel = default,
            Action? schemaChanged = null)
        {
            var result = await new PgExecuteRunner().RunAsync(Connection, sql, State, maxRows, timeoutSeconds, TimeSpan.FromMinutes(5), cancel, schemaChanged,
                schemaChange: SchemaChange);
            State = (TransactionState)result.TransactionState;
            return result;
        }

        public async Task<int> BackendPidAsync()
        {
            await using var command = new NpgsqlCommand("SELECT pg_backend_pid()", Connection);
            return (int)(await command.ExecuteScalarAsync())!;
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    private static ExecuteMessageDto[] Tags(ExecuteQueryResult r) =>
        r.MessageDetails.Where(m => m.Kind == ExecuteMessageKind.RowCount).ToArray();

    private static ExecuteMessageDto[] Errors(ExecuteQueryResult r) =>
        r.MessageDetails.Where(m => m.Kind == ExecuteMessageKind.Error).ToArray();

    private static string[] SystemLines(ExecuteQueryResult r) =>
        r.MessageDetails.Where(m => m.Kind == ExecuteMessageKind.System).Select(m => m.Text).ToArray();

    // ── Result sets and tags (FR-011, FR-012) ───────────────────────────────────────────────────

    [SkippableFact]
    public async Task Each_statement_that_returns_rows_is_its_own_set_with_its_own_batch()
    {
        await using var s = await Session.OpenAsync();

        var r = await s.RunAsync("SELECT 1 AS a, 'x' AS b;\nSELECT 2 AS c");

        Assert.Equal(ExecuteStatus.Ok, r.Status);
        Assert.Equal(2, r.ResultSets.Length);
        Assert.Equal(new[] { "a", "b" }, r.ResultSets[0].ColumnNames);
        Assert.Equal(new string?[] { "1", "x" }, r.ResultSets[0].Rows.Single());
        Assert.Equal(new string?[] { "2" }, r.ResultSets[1].Rows.Single());
        Assert.Equal(0, r.ResultSets[0].BatchIndex);
        Assert.Equal(1, r.ResultSets[1].BatchIndex);
        Assert.Equal(2, r.Batches.Length);
        Assert.All(r.Batches, b => Assert.Equal(ExecuteStatus.Ok, b.Status));
        Assert.False(r.ResultSets[0].IsEditable);
    }

    [SkippableFact]
    public async Task A_select_tag_belongs_to_its_set()
    {
        await using var s = await Session.OpenAsync();

        var r = await s.RunAsync("SELECT generate_series(1, 3)");

        var tag = Assert.Single(Tags(r));
        Assert.Equal("SELECT 3", tag.Text);
        Assert.Equal(0, tag.ResultSetIndex);
        Assert.Equal(3, tag.RowCount);
    }

    [SkippableFact]
    public async Task Statements_without_rows_report_the_servers_command_tag()
    {
        await using var s = await Session.OpenAsync();

        var created = await s.RunAsync("CREATE TABLE t (id int PRIMARY KEY, n text)");
        Assert.Equal("CREATE TABLE", Assert.Single(Tags(created)).Text);
        Assert.Equal(-1, created.TotalRowsAffected);

        var r = await s.RunAsync("INSERT INTO t VALUES (1, 'a');\nUPDATE t SET n = 'b' WHERE id = 1;\nDELETE FROM t WHERE id = 2;");

        Assert.Equal(new[] { "INSERT 0 1", "UPDATE 1", "DELETE 0" }, Tags(r).Select(t => t.Text).ToArray());
        Assert.Equal(2, r.TotalRowsAffected);
        Assert.Empty(r.ResultSets);
        Assert.Equal(ExecuteStatus.Ok, r.Status);
    }

    // ── Notices (FR-013) ────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Notices_and_warnings_arrive_as_information_in_order_and_are_not_errors()
    {
        await using var s = await Session.OpenAsync();

        var r = await s.RunAsync("DO $$ BEGIN RAISE NOTICE 'hello %', 42; RAISE WARNING 'careful'; END $$;\nSELECT 1");

        var infos = r.MessageDetails.Where(m => m.Kind == ExecuteMessageKind.Info).ToArray();
        Assert.Equal(new[] { "NOTICE: hello 42", "WARNING: careful" }, infos.Select(m => m.Text).ToArray());
        Assert.All(infos, m => Assert.Equal(10, m.Severity));
        Assert.Equal(ExecuteStatus.Ok, r.Status);
        Assert.Empty(Errors(r));

        // In the order the server sent them: both before the statement's own tag, which comes before the next statement.
        var order = r.MessageDetails.Select(m => m.Text).ToList();
        Assert.True(order.IndexOf("NOTICE: hello 42") < order.IndexOf("WARNING: careful"));
        Assert.True(order.IndexOf("WARNING: careful") < order.IndexOf("DO"));
        Assert.True(order.IndexOf("DO") < order.IndexOf("SELECT 1"));
    }

    // ── Errors (FR-014, FR-015, spike S3) ───────────────────────────────────────────────────────

    [SkippableFact]
    public async Task An_error_keeps_earlier_results_skips_later_statements_and_says_how_many_ran()
    {
        await using var s = await Session.OpenAsync();

        var r = await s.RunAsync("SELECT 1;\nSELECT * FROM nope;\nSELECT 3;");

        Assert.Equal(ExecuteStatus.Error, r.Status);
        Assert.Single(r.ResultSets);
        var error = Assert.Single(Errors(r));
        Assert.Equal("42P01", error.SqlState);
        Assert.Contains("nope", error.Text);
        Assert.Equal(2, error.Line);
        Assert.Equal(15, error.Column);
        Assert.Equal(new[] { ExecuteStatus.Ok, ExecuteStatus.Error, ExecuteStatus.Skipped }, r.Batches.Select(b => b.Status).ToArray());
        Assert.Contains("1 of 3 statements ran.", SystemLines(r));
        Assert.Contains(PgExecuteRunner.EarlierStatementsStayText, SystemLines(r));
        Assert.Contains("SQLSTATE 42P01", r.Messages.Single(m => m.Contains("nope")));
    }

    [SkippableFact]
    public async Task A_character_outside_the_basic_plane_before_the_error_still_puts_the_caret_in_the_right_place()
    {
        await using var s = await Session.OpenAsync();

        // The server counts code points, an editor counts UTF-16 units: the emoji is one and two.
        var r = await s.RunAsync("SELECT 1;\nSELECT 'a\U0001F642' || nope;");

        var error = Assert.Single(Errors(r));
        Assert.Equal("42703", error.SqlState);
        Assert.Equal(2, error.Line);
        Assert.Equal(17, error.Column);
    }

    [SkippableFact]
    public async Task An_error_carries_the_servers_detail_and_hint()
    {
        await using var s = await Session.OpenAsync("CREATE TABLE t (id int PRIMARY KEY); INSERT INTO t VALUES (1);");

        var duplicate = Assert.Single(Errors(await s.RunAsync("INSERT INTO t VALUES (1)")));
        Assert.Equal("23505", duplicate.SqlState);
        Assert.Equal("Key (id)=(1) already exists.", duplicate.Detail);

        var misspelt = Assert.Single(Errors(await s.RunAsync("SELECT relnme FROM pg_class")));
        Assert.Equal("42703", misspelt.SqlState);
        Assert.Contains("relname", misspelt.Hint);
    }

    [SkippableFact]
    public async Task An_error_without_a_position_points_at_its_statement()
    {
        await using var s = await Session.OpenAsync();

        var r = await s.RunAsync("SELECT 1;\n\n  DO $$ BEGIN RAISE EXCEPTION 'boom'; END $$;");

        var error = Assert.Single(Errors(r));
        Assert.Equal("P0001", error.SqlState);
        Assert.Equal(3, error.Line);
        Assert.Equal(3, error.Column);
    }

    [SkippableFact]
    public async Task Twenty_different_errors_each_carry_their_sqlstate_and_their_statements_line()
    {
        await using var s = await Session.OpenAsync(
            "CREATE TABLE t (id int PRIMARY KEY, name varchar(5) NOT NULL, qty int CHECK (qty > 0));" +
            "CREATE TABLE c (id int PRIMARY KEY, t_id int REFERENCES t(id));" +
            "INSERT INTO t VALUES (1, 'a', 1);");

        var cases = new (string Sql, string SqlState)[]
        {
            ("SELEC 1", "42601"),
            ("SELECT * FROM nope", "42P01"),
            ("SELECT nope FROM t", "42703"),
            ("SELECT nofunc(1)", "42883"),
            ("SELECT 1/0", "22012"),
            ("INSERT INTO t VALUES (1, 'b', 1)", "23505"),
            ("INSERT INTO t VALUES (2, NULL, 1)", "23502"),
            ("INSERT INTO c VALUES (1, 99)", "23503"),
            ("INSERT INTO t VALUES (3, 'c', 0)", "23514"),
            ("INSERT INTO t VALUES (4, 'toolongvalue', 1)", "22001"),
            ("SELECT 'abc'::int", "22P02"),
            ("CREATE TABLE t (a int)", "42P07"),
            ("CREATE TABLE d (a int, a int)", "42701"),
            ("INSERT INTO t (id) SELECT true", "42804"),
            ("SELECT id FROM t, c", "42702"),
            ("SELECT name, count(*) FROM t", "42803"),
            ("SELECT 2147483648::int", "22003"),
            ("SELECT '2020-13-45'::date", "22008"),
            ("DROP TABLE t", "2BP01"),
            ("SELECT 1 LIMIT -1", "2201W"),
            ("SELECT DISTINCT 1 FOR UPDATE", "0A000"),
            ("DO $$ BEGIN RAISE EXCEPTION 'boom'; END $$", "P0001"),
        };
        Assert.True(cases.Select(c => c.SqlState).Distinct().Count() >= 20);

        var failures = new List<string>();
        foreach (var (sql, expected) in cases)
        {
            // The statement is the fourth line of the script: its error must map there.
            var r = await s.RunAsync("SELECT 1;\n\nSELECT 2;\n" + sql + ";");
            var error = Errors(r).SingleOrDefault();
            if (error == null) failures.Add($"{sql}: no error");
            else if (error.SqlState != expected) failures.Add($"{sql}: SQLSTATE {error.SqlState}, expected {expected}");
            else if (error.Line != 4 || error.Column < 1) failures.Add($"{sql}: at {error.Line}:{error.Column}, expected line 4");
            else if (string.IsNullOrEmpty(error.Text)) failures.Add($"{sql}: no message text");
        }
        Assert.Empty(failures);
    }

    // ── The explorer follows a run (FR-043) ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task A_run_that_changes_the_catalog_reports_it_once_even_when_a_later_statement_fails()
    {
        await using var s = await Session.OpenAsync();
        var calls = 0;

        await s.RunAsync("CREATE TABLE a (x int);\nCREATE TABLE b (x int);\nSELECT * FROM nope;", schemaChanged: () => calls++);

        Assert.Equal(1, calls);   // the two tables exist even though the script stopped
    }

    [SkippableFact]
    public async Task Reads_writes_and_transaction_control_do_not_report_a_schema_change()
    {
        await using var s = await Session.OpenAsync("CREATE TABLE t (x int);");
        var calls = 0;

        await s.RunAsync("SELECT 1;\nINSERT INTO t VALUES (1);\nUPDATE t SET x = 2;\nBEGIN;\nCOMMIT;\nTRUNCATE t;", schemaChanged: () => calls++);

        Assert.Equal(0, calls);
    }

    [SkippableFact]
    public async Task A_failing_reporter_never_turns_a_good_run_into_an_error()
    {
        await using var s = await Session.OpenAsync();

        var result = await s.RunAsync("CREATE TABLE c (x int)", schemaChanged: () => throw new InvalidOperationException("no reload"));

        Assert.Equal(ExecuteStatus.Ok, result.Status);
    }

    // ── GO, meta-commands, COPY (FR-010, FR-019) ────────────────────────────────────────────────

    [SkippableFact]
    public async Task A_GO_line_is_sent_as_it_is_and_the_error_says_it_is_SQL_Server_only()
    {
        await using var s = await Session.OpenAsync();

        var r = await s.RunAsync("SELECT 1\nGO\nSELECT 2");

        var error = Assert.Single(Errors(r));
        Assert.Equal("42601", error.SqlState);
        Assert.Contains("SQL Server", error.Hint);
        Assert.Single(r.Batches);
    }

    [SkippableFact]
    public async Task A_psql_meta_command_is_not_sent_and_stops_the_script_with_a_hint()
    {
        await using var s = await Session.OpenAsync();

        var r = await s.RunAsync("SELECT 1;\n\\dt\nSELECT 2;");

        Assert.Equal(ExecuteStatus.Error, r.Status);
        Assert.Single(r.ResultSets);
        Assert.Equal(new[] { ExecuteStatus.Ok, ExecuteStatus.Skipped, ExecuteStatus.Skipped }, r.Batches.Select(b => b.Status).ToArray());
        var error = Assert.Single(Errors(r));
        Assert.Contains("psql client command", error.Text);
        Assert.Equal(2, error.Line);
    }

    [SkippableFact]
    public async Task Copy_through_the_client_is_not_sent_and_names_the_alternative()
    {
        await using var s = await Session.OpenAsync("CREATE TABLE t (id int);");

        var r = await s.RunAsync("COPY t FROM STDIN;\nSELECT 1;");

        Assert.Equal(new[] { ExecuteStatus.Skipped, ExecuteStatus.Skipped }, r.Batches.Select(b => b.Status).ToArray());
        var error = Assert.Single(Errors(r));
        Assert.Contains("STDIN", error.Text);
        Assert.Contains("INSERT", error.Text);
    }

    // ── Transactions (FR-016) ───────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task A_failure_inside_a_transaction_aborts_it_until_rollback()
    {
        await using var s = await Session.OpenAsync();

        var begun = await s.RunAsync("BEGIN");
        Assert.Equal(TransactionState.Open, s.State);
        Assert.DoesNotContain(PgExecuteRunner.AbortedTransactionText, SystemLines(begun));

        var failed = await s.RunAsync("SELECT 1/0;");
        Assert.Equal(TransactionState.Aborted, s.State);
        Assert.Equal("22012", Assert.Single(Errors(failed)).SqlState);
        Assert.Contains(PgExecuteRunner.AbortedTransactionText, SystemLines(failed));

        var refused = await s.RunAsync("SELECT 1");
        Assert.Equal(TransactionState.Aborted, s.State);
        Assert.Equal("25P02", Assert.Single(Errors(refused)).SqlState);
        Assert.Contains(PgExecuteRunner.AbortedTransactionText, SystemLines(refused));

        var rolledBack = await s.RunAsync("ROLLBACK");
        Assert.Equal(TransactionState.None, s.State);
        Assert.Equal(ExecuteStatus.Ok, rolledBack.Status);
        Assert.DoesNotContain(PgExecuteRunner.AbortedTransactionText, SystemLines(rolledBack));

        Assert.Equal(ExecuteStatus.Ok, (await s.RunAsync("SELECT 1")).Status);
    }

    [SkippableFact]
    public async Task The_state_in_the_reply_is_the_state_after_the_script()
    {
        await using var s = await Session.OpenAsync();

        var r = await s.RunAsync("BEGIN;\nSELECT 1;");
        Assert.Equal((int)TransactionState.Open, r.TransactionState);

        r = await s.RunAsync("COMMIT");
        Assert.Equal((int)TransactionState.None, r.TransactionState);
    }

    // ── Timeout and cancel (FR-017, spike S4) ───────────────────────────────────────────────────

    [SkippableFact]
    public async Task A_timeout_is_reported_and_the_same_connection_works_afterwards()
    {
        await using var s = await Session.OpenAsync();
        var pidBefore = await s.BackendPidAsync();

        var r = await s.RunAsync("SELECT pg_sleep(30);\nSELECT 2;", timeoutSeconds: 1);

        Assert.Equal(ExecuteStatus.TimedOut, r.Status);
        Assert.Equal(new[] { ExecuteStatus.TimedOut, ExecuteStatus.Skipped }, r.Batches.Select(b => b.Status).ToArray());
        Assert.Contains(Errors(r), e => e.Text.Contains("timed out"));

        Assert.Equal(ExecuteStatus.Ok, (await s.RunAsync("SELECT 1")).Status);
        // Spike S4: the driver cancelled the statement on the server and kept the physical connection.
        Assert.Equal(pidBefore, await s.BackendPidAsync());
    }

    [SkippableFact]
    public async Task A_cancel_stops_the_script_and_leaves_the_connection_usable()
    {
        await using var s = await Session.OpenAsync();
        using var cts = new CancellationTokenSource();

        var running = s.RunAsync("SELECT pg_sleep(30)", cancel: cts.Token);
        await Task.Delay(500);
        cts.Cancel();
        var r = await running;

        Assert.Equal(ExecuteStatus.Cancelled, r.Status);
        Assert.Equal(ExecuteStatus.Ok, (await s.RunAsync("SELECT 1")).Status);
    }

    [SkippableFact]
    public async Task A_connection_the_server_closed_is_reported_as_disconnected()
    {
        await using var s = await Session.OpenAsync();
        var pid = await s.BackendPidAsync();
        await using (var admin = new NpgsqlConnection(PgTestServer.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var kill = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", admin);
            kill.Parameters.AddWithValue("pid", pid);
            await kill.ExecuteScalarAsync();
        }
        await Task.Delay(300);

        var r = await s.RunAsync("SELECT 1;\nSELECT 2;");

        Assert.Equal(ExecuteStatus.Disconnected, r.Status);
        Assert.Empty(r.ResultSets);
        Assert.Equal((int)TransactionState.None, r.TransactionState);
        Assert.Equal(new[] { ExecuteStatus.Disconnected, ExecuteStatus.Skipped }, r.Batches.Select(b => b.Status).ToArray());
    }

    // ── Values and types (FR-018, spike S5) ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Arrays_intervals_json_bytea_and_enums_come_back_as_the_servers_text()
    {
        await using var s = await Session.OpenAsync("CREATE TYPE mood AS ENUM ('sad', 'happy');");

        var r = await s.RunAsync(
            "SELECT ARRAY[1,2,3] AS a, '1 day 2 hours'::interval AS i, '{\"a\": 1}'::json AS j, " +
            "decode('deadbeef', 'hex') AS b, 'happy'::mood AS m, NULL::int AS n");

        var set = Assert.Single(r.ResultSets);
        Assert.Equal(new string?[] { "{1,2,3}", "1 day 02:00:00", "{\"a\": 1}", "\\xdeadbeef", "happy", null }, set.Rows.Single());
        // A type outside pg_catalog carries its schema, so the name is unambiguous wherever the search path points.
        Assert.Equal(new[] { "integer[]", "interval", "json", "bytea", "public.mood", "integer" }, set.ColumnSqlTypes);
    }

    [SkippableFact]
    public async Task Declared_lengths_and_scales_are_part_of_the_type_and_numbers_are_hinted()
    {
        await using var s = await Session.OpenAsync();

        var r = await s.RunAsync("SELECT 'abc'::varchar(5) AS v, 1.5::numeric(10,2) AS n, 1::int AS i, 'x'::text AS t, 1.5::float8 AS f");

        var set = Assert.Single(r.ResultSets);
        Assert.Equal(new[] { "character varying(5)", "numeric(10,2)", "integer", "text", "double precision" }, set.ColumnSqlTypes);
        Assert.Equal(
            new[] { ClrTypeHint.ServerText, ClrTypeHint.ServerNumber, ClrTypeHint.ServerNumber, ClrTypeHint.ServerText, ClrTypeHint.ServerNumber },
            set.ClrTypeHints);
        Assert.Equal(new string?[] { "abc", "1.50", "1", "x", "1.5" }, set.Rows.Single());
    }

    [SkippableFact]
    public async Task The_row_cap_truncates_a_set_and_keeps_the_servers_own_tag()
    {
        await using var s = await Session.OpenAsync();

        var r = await s.RunAsync("SELECT generate_series(1, 50) AS n", maxRows: 10);

        var set = Assert.Single(r.ResultSets);
        Assert.Equal(10, set.Rows.Length);
        Assert.True(set.Truncated);
        Assert.Equal(40, set.RowsOmitted);
        Assert.Equal("SELECT 50", Assert.Single(Tags(r)).Text);
    }

    // ── TLS (FR-101) ────────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Prefer_connects_and_VerifyFull_refuses_a_certificate_it_cannot_verify()
    {
        PgTestServer.SkipUnlessAvailable();
        var admin = new NpgsqlConnectionStringBuilder(PgTestServer.AdminConnectionString);
        string Build(string mode) => PgConnectionString.Build(admin.Host!, admin.Port, "postgres", admin.Username!, admin.Password, mode, null);

        var facts = await PgConnectionFactory.ReadFactsAsync(Build("prefer"), default);
        Assert.Equal("postgres", facts.Database);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => PgConnectionFactory.ReadFactsAsync(Build("verify-full"), default));
        var text = ex.ToString();
        Assert.True(
            text.Contains("certificate", StringComparison.OrdinalIgnoreCase) || text.Contains("SSL", StringComparison.OrdinalIgnoreCase)
            || text.Contains("TLS", StringComparison.OrdinalIgnoreCase),
            "The failure should name the TLS problem: " + ex.Message);
    }

    // ── A change inside a transaction is reported when the transaction ends (FR-043) ─────────────

    [SkippableFact]
    public async Task A_change_inside_an_open_transaction_is_reported_by_the_run_that_ends_the_transaction()
    {
        await using var s = await Session.OpenAsync();
        var calls = 0;

        var first = await s.RunAsync("BEGIN;\nCREATE TABLE a (x int);", schemaChanged: () => calls++);
        Assert.Equal(0, calls);                                   // another connection cannot see the table yet
        Assert.False(first.SchemaChanged);
        Assert.True(s.SchemaChangePending);

        await s.RunAsync("SELECT 1;", schemaChanged: () => calls++);
        Assert.Equal(0, calls);                                   // still open
        Assert.True(s.SchemaChangePending);

        var last = await s.RunAsync("COMMIT;", schemaChanged: () => calls++);
        Assert.Equal(1, calls);
        Assert.True(last.SchemaChanged);
        Assert.False(s.SchemaChangePending);
    }

    [SkippableTheory]
    [InlineData("CREATE TABLE copy_a AS SELECT 1 AS x;")]
    [InlineData("SELECT 1 AS x INTO copy_b;")]
    [InlineData("CREATE MATERIALIZED VIEW copy_c AS SELECT 1 AS x;")]
    public async Task Objects_made_from_a_query_are_reported_although_their_tag_reads_SELECT(string sql)
    {
        PgTestServer.SkipUnlessAvailable();
        await using var s = await Session.OpenAsync();
        var calls = 0;

        var result = await s.RunAsync(sql, schemaChanged: () => calls++);

        Assert.Equal(1, calls);
        Assert.True(result.SchemaChanged);
    }

    [SkippableFact]
    public async Task A_change_committed_in_the_same_run_is_reported_at_once()
    {
        await using var s = await Session.OpenAsync();
        var calls = 0;

        var result = await s.RunAsync("BEGIN;\nCREATE TABLE a (x int);\nCOMMIT;", schemaChanged: () => calls++);

        Assert.Equal(1, calls);
        Assert.True(result.SchemaChanged);
        Assert.False(s.SchemaChangePending);
    }

    [SkippableFact]
    public async Task A_change_made_before_a_transaction_opens_in_the_same_run_is_reported_at_once()
    {
        await using var s = await Session.OpenAsync();
        var calls = 0;

        var result = await s.RunAsync("CREATE TABLE a (x int);\nBEGIN;\nINSERT INTO a VALUES (1);", schemaChanged: () => calls++);

        Assert.Equal(1, calls);                                   // the table was created outside the transaction: visible already
        Assert.True(result.SchemaChanged);
        Assert.Equal(TransactionState.Open, s.State);
        Assert.False(s.SchemaChangePending);
    }

    [SkippableFact]
    public async Task A_change_whose_transaction_ends_before_another_begins_is_reported_in_that_run()
    {
        await using var s = await Session.OpenAsync();
        var calls = 0;

        var result = await s.RunAsync("BEGIN;\nCREATE TABLE b (x int);\nCOMMIT;\nBEGIN;\nSELECT 1;", schemaChanged: () => calls++);

        Assert.Equal(1, calls);
        Assert.True(result.SchemaChanged);
        Assert.Equal(TransactionState.Open, s.State);
        Assert.False(s.SchemaChangePending);
    }

    [SkippableFact]
    public async Task A_change_kept_in_a_transaction_survives_a_run_that_does_not_end_it_and_is_paid_by_ROLLBACK_too()
    {
        await using var s = await Session.OpenAsync();
        var calls = 0;

        await s.RunAsync("BEGIN;\nCREATE TABLE c (x int);\nSELECT * FROM nope;", schemaChanged: () => calls++);   // the failure aborts the transaction
        Assert.Equal(0, calls);
        Assert.Equal(TransactionState.Aborted, s.State);
        Assert.True(s.SchemaChangePending);

        var result = await s.RunAsync("ROLLBACK;", schemaChanged: () => calls++);

        Assert.Equal(1, calls);                                   // the catalog is read again once the transaction is gone, whichever way it ended
        Assert.True(result.SchemaChanged);
        Assert.False(s.SchemaChangePending);
    }
}
