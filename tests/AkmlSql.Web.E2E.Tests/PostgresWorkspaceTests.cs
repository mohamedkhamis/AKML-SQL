using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 042 (task T051, quickstart user story 1, SC-007): the PostgreSQL workspace in a real browser against a throwaway
/// PostgreSQL server - the status bar's kind, result sets and command tags, notices, an error that goes to its line, the
/// twenty-error script, an aborted transaction and its rollback, the GO hint and the wrong-kind test message.
///
/// <para><c>BridgeE2E</c> and <see cref="SkippableFactAttribute"/>: skipped without <c>AKML_PG_TEST_CONNECTION</c>
/// (<c>scripts/pg-test-server.ps1 -Action Start</c>), DEBUG builds of AkmlSql.Web and AkmlSql.Engine, or Playwright Chromium.
/// Run serially (<c>-- xUnit.ParallelizeTestCollections=false</c>).</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
[Trait("Category", "PostgreSql")]
public sealed class PostgresWorkspaceTests
{
    private readonly ITestOutputHelper _output;

    public PostgresWorkspaceTests(ITestOutputHelper output) => _output = output;

    private static ILocator Tab(IPage page, string name) => page.Locator($"[data-testid='results-tab-{name}']");

    private static async Task<string[]> MessageLinesAsync(IPage page) =>
        (await page.Locator("[data-testid^='messages-line-']").AllInnerTextsAsync()).Select(t => t.Trim()).ToArray();

    private static Task<string> ActiveEditorLineAsync(IPage page) =>
        page.Locator("[data-testid='sql-editor'] .cm-activeLine").InnerTextAsync();

    [SkippableFact]
    public async Task The_status_bar_says_PostgreSQL_with_the_version_and_the_toolbar_runs_a_script()
    {
        await using var s = await PgWebSession.StartOrSkipAsync(_output);

        var kind = s.Page.Locator("[data-testid='status-kind']");
        await Assertions.Expect(kind).ToContainTextAsync("PostgreSQL");
        Assert.Matches(@"^PostgreSQL \d+\.\d+", await kind.GetAttributeAsync("title") ?? string.Empty);
        await Assertions.Expect(s.Page.Locator("[data-testid='status-login']")).ToHaveAttributeAsync("title", "Login");

        await s.ExecuteAsync("SELECT 1 AS a;");
        await Assertions.Expect(s.Page.Locator("[data-testid='status-outcome']")).ToContainTextAsync("Query executed successfully.");
    }

    [SkippableFact]
    public async Task Two_SELECTs_and_an_UPDATE_stack_two_grids_and_list_the_servers_tags()
    {
        await using var s = await PgWebSession.StartOrSkipAsync(_output);
        await s.ExecuteAsync(
            "CREATE TEMP TABLE m (n int);\nINSERT INTO m VALUES (1), (2);\nSELECT n FROM m;\nUPDATE m SET n = n + 1;\nSELECT n FROM m WHERE n > 2;");

        await Assertions.Expect(s.Page.Locator("[data-testid='results-set-1']")).ToBeVisibleAsync();
        await Assertions.Expect(s.Page.Locator("[data-testid='results-set-header-0']")).ToHaveTextAsync("Result 1 (2 rows)");
        await Assertions.Expect(s.Page.Locator("[data-testid='results-set-header-1']")).ToHaveTextAsync("Result 2 (1 row)");
        await Tab(s.Page, "messages").ClickAsync();
        var lines = await MessageLinesAsync(s.Page);
        Assert.Equal(new[] { "CREATE TABLE", "INSERT 0 2", "SELECT 2", "UPDATE 2", "SELECT 1" }, lines.Take(5).ToArray());
    }

    [SkippableFact]
    public async Task A_notice_is_listed_as_information_and_the_run_still_succeeds()
    {
        await using var s = await PgWebSession.StartOrSkipAsync(_output);
        await s.ExecuteAsync("DO $$ BEGIN RAISE NOTICE 'hello %', 42; END $$;");

        await Assertions.Expect(s.Page.Locator("[data-testid='status-outcome']")).ToContainTextAsync("Query executed successfully.");
        await Tab(s.Page, "messages").ClickAsync();
        Assert.Contains("NOTICE: hello 42", await MessageLinesAsync(s.Page));
        await Assertions.Expect(s.Page.Locator("button.akml-messages-error")).ToHaveCountAsync(0);
    }

    [SkippableFact]
    public async Task An_error_opens_Messages_and_clicking_it_puts_the_caret_on_the_failing_statement()
    {
        await using var s = await PgWebSession.StartOrSkipAsync(_output);
        await s.ExecuteAsync("SELECT 1 AS a;\nSELECT * FROM akml_no_such_042;\nSELECT 3 AS c;");

        await Assertions.Expect(Tab(s.Page, "messages")).ToHaveAttributeAsync("aria-selected", "true");
        var error = s.Page.Locator("[data-testid^='messages-error-']").First;
        await Assertions.Expect(error).ToContainTextAsync("SQLSTATE 42P01");
        await Assertions.Expect(error).ToHaveAttributeAsync("title", "Go to line 2, column 15");
        await error.ClickAsync();

        Assert.Equal("SELECT * FROM akml_no_such_042;", (await ActiveEditorLineAsync(s.Page)).Trim());
        await Assertions.Expect(s.Page.Locator("[data-testid='sql-editor'] .cm-lintRange-error")).ToHaveCountAsync(1);
        await Assertions.Expect(s.Page.Locator("[data-testid='status-outcome']")).ToContainTextAsync("Query completed with errors.");
        var lines = await MessageLinesAsync(s.Page);
        Assert.Contains("1 of 3 statements ran.", lines);
        Assert.Contains("Earlier statements stay applied unless they ran inside an explicit transaction.", lines);

        // The same error is in the Problems tab, at the same place.
        await Tab(s.Page, "problems").ClickAsync();
        var row = s.Page.Locator("[data-testid='problem-execution']");
        await Assertions.Expect(row).ToHaveCountAsync(1);
        await Assertions.Expect(row).ToContainTextAsync("L2:15");
    }

    [SkippableFact]
    public async Task Twenty_different_errors_each_jump_to_their_statements_line()
    {
        await using var s = await PgWebSession.StartOrSkipAsync(_output);
        await s.ExecuteAsync(
            "CREATE TEMP TABLE t (id int PRIMARY KEY, name varchar(5) NOT NULL, qty int CHECK (qty > 0));\n" +
            "CREATE TEMP TABLE c (id int PRIMARY KEY, t_id int REFERENCES t(id));\n" +
            "INSERT INTO t VALUES (1, 'a', 1);");

        var cases = new (string Sql, string SqlState)[]
        {
            ("SELEC 1", "42601"),
            ("SELECT * FROM akml_nope", "42P01"),
            ("SELECT nope FROM t", "42703"),
            ("SELECT nofunc(1)", "42883"),
            ("SELECT 1/0", "22012"),
            ("INSERT INTO t VALUES (1, 'b', 1)", "23505"),
            ("INSERT INTO t VALUES (2, NULL, 1)", "23502"),
            ("INSERT INTO c VALUES (1, 99)", "23503"),
            ("INSERT INTO t VALUES (3, 'c', 0)", "23514"),
            ("INSERT INTO t VALUES (4, 'toolongvalue', 1)", "22001"),
            ("SELECT 'abc'::int", "22P02"),
            ("CREATE TEMP TABLE t (a int)", "42P07"),
            ("CREATE TEMP TABLE d (a int, a int)", "42701"),
            ("INSERT INTO t (id) SELECT true", "42804"),
            ("SELECT id FROM t, c", "42702"),
            ("SELECT name, count(*) FROM t", "42803"),
            ("SELECT 2147483648::int", "22003"),
            ("SELECT '2020-13-45'::date", "22008"),
            ("DROP TABLE t", "2BP01"),
            ("SELECT 1 LIMIT -1", "2201W"),
        };

        foreach (var (sql, sqlState) in cases)
        {
            // The failing statement is the fourth line of the script.
            await s.ExecuteAsync("SELECT 1;\n\nSELECT 2;\n" + sql + ";");
            var error = s.Page.Locator("[data-testid^='messages-error-']").First;
            await Assertions.Expect(error).ToContainTextAsync("SQLSTATE " + sqlState);
            await error.ClickAsync();
            Assert.Equal(sql + ";", (await ActiveEditorLineAsync(s.Page)).Trim());
        }
    }

    [SkippableFact]
    public async Task A_failure_in_a_transaction_shows_it_aborted_and_one_click_rolls_it_back()
    {
        await using var s = await PgWebSession.StartOrSkipAsync(_output);
        await s.ExecuteAsync("BEGIN;\nSELECT 1/0;");

        var transaction = s.Page.Locator("[data-testid='status-txn']");
        await Assertions.Expect(transaction).ToContainTextAsync("Transaction aborted");
        await Assertions.Expect(s.Page.Locator("[data-testid='status-txn-note']")).ToHaveTextAsync("Every statement fails until ROLLBACK.");

        await s.ExecuteAsync("SELECT 1;");
        await Tab(s.Page, "messages").ClickAsync();
        await Assertions.Expect(s.Page.Locator("[data-testid^='messages-error-']").First).ToContainTextAsync("SQLSTATE 25P02");

        await s.Page.ClickAsync("[data-testid='status-txn-rollback']");
        await Assertions.Expect(transaction).ToHaveCountAsync(0, new() { Timeout = 15_000 });

        await s.ExecuteAsync("SELECT 'after rollback' AS note;");
        await Assertions.Expect(s.Page.Locator("[data-testid='status-outcome']")).ToContainTextAsync("Query executed successfully.");
    }

    [SkippableFact]
    public async Task A_GO_line_is_sent_as_it_is_and_the_error_says_it_belongs_to_SQL_Server()
    {
        await using var s = await PgWebSession.StartOrSkipAsync(_output);
        await s.ExecuteAsync("SELECT 1\nGO\nSELECT 2");

        var error = s.Page.Locator("[data-testid^='messages-error-']").First;
        await Assertions.Expect(error).ToContainTextAsync("SQLSTATE 42601");
        await Assertions.Expect(error).ToContainTextAsync("HINT:");
        await Assertions.Expect(error).ToContainTextAsync("SQL Server");
    }

    [SkippableFact]
    public async Task Testing_a_PostgreSQL_form_aimed_at_a_SQL_Server_port_asks_to_check_the_kind()
    {
        Skip.IfNot(await SandboxEngine.LocalSqlServerAnswersAsync(), "No SQL Server answering on localhost:1433.");
        await using var s = await PgWebSession.StartOrSkipAsync(_output, connect: false);

        await PgWebSession.FillFormAsync(s.Page, "localhost", 1433, "postgres", "someone", "whatever");
        await s.Page.ClickAsync("[data-testid='conn-test-btn']");

        await Assertions.Expect(s.Page.Locator("[data-testid='conn-message']"))
            .ToContainTextAsync("did not answer as a PostgreSQL server. Check the database kind.", new() { Timeout = 30_000 });
    }

    // ── User story 2: browsing ──────────────────────────────────────────────────────────────────

    private const string BrowseSeed =
        "CREATE SCHEMA sales;\n" +
        "CREATE TABLE sales.customers (id serial PRIMARY KEY, name varchar(50) NOT NULL, balance numeric(10,2) DEFAULT 0);\n" +
        "COMMENT ON TABLE sales.customers IS 'People who buy';\n" +
        "CREATE TABLE sales.orders (id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, customer_id int NOT NULL REFERENCES sales.customers(id), total numeric(12,2));\n" +
        "CREATE VIEW sales.big_orders AS SELECT id, total FROM sales.orders WHERE total > 100;\n" +
        "CREATE MATERIALIZED VIEW sales.order_totals AS SELECT customer_id, sum(total) AS t FROM sales.orders GROUP BY customer_id;\n" +
        "CREATE SEQUENCE sales.invoice_seq;\n" +
        "CREATE FUNCTION sales.add_numbers(a int, b int DEFAULT 1) RETURNS int LANGUAGE sql AS 'SELECT a + b';\n" +
        "CREATE PROCEDURE sales.archive(IN cutoff date) LANGUAGE sql AS 'SELECT 1';";

    private static ILocator TreeNode(IPage page, string text) =>
        page.Locator("li.akml-schema-tree-node").Filter(new() { HasText = text });

    private static async Task ExpandAsync(IPage page, string text) =>
        await TreeNode(page, text).First.Locator(".akml-schema-tree-expand").ClickAsync();

    private static async Task SwitchDatabaseAsync(IPage page, string database)
    {
        await page.ClickAsync("[data-testid='db-selector']");
        await page.Locator("[data-testid='db-selector-list']").GetByText(database, new() { Exact = true }).ClickAsync();
        var confirm = page.Locator("[data-dialog-id='db-switch-confirm']");
        await Assertions.Expect(confirm).ToBeVisibleAsync();
        await Assertions.Expect(confirm.Locator("[data-testid='dialog-message']")).ToContainTextAsync("reconnects");
        await page.ClickAsync("[data-testid='dialog-confirm']");
        await Assertions.Expect(page.Locator("[data-testid='db-selector']")).ToContainTextAsync(database, new() { Timeout = 30_000 });
    }

    [SkippableFact]
    public async Task The_explorer_shows_the_kinds_columns_and_comments_follows_a_script_and_a_database_switch()
    {
        await using var s = await PgWebSession.StartOrSkipAsync(_output);
        var database = "akml_e2e_" + Guid.NewGuid().ToString("N")[..8];
        var switched = false;
        try
        {
            await s.ExecuteAsync($"CREATE DATABASE {database};");
            await SwitchDatabaseAsync(s.Page, database);
            switched = true;
            await Assertions.Expect(s.Page.Locator("[data-testid='status-kind']")).ToContainTextAsync("PostgreSQL");

            // Run the seed: the explorer follows without a manual refresh (FR-043).
            await ExpandAsync(s.Page, "Database \"" + database + "\"");
            await s.ExecuteAsync(BrowseSeed);
            await Assertions.Expect(TreeNode(s.Page, "Schema \"sales\"").First).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await ExpandAsync(s.Page, "Schema \"sales\"");
            var kinds = (await s.Page.Locator(".akml-schema-tree-kind .akml-schema-tree-label").AllInnerTextsAsync()).Select(t => t.Trim()).ToArray();
            Assert.Equal(new[] { "Tables", "Views", "Materialized Views", "Sequences", "Procedures", "Functions" },
                kinds.Select(k => k.Substring(0, k.IndexOf(" (", StringComparison.Ordinal))).ToArray());

            // Columns with the types the server shows, and the table's comment.
            await ExpandAsync(s.Page, "Tables (2)");
            var customers = s.Page.Locator("[data-testid='schema-tree-object'][data-qualifier='sales.customers']");
            await Assertions.Expect(customers).ToHaveAttributeAsync("title", "People who buy");
            await customers.ClickAsync();
            await Assertions.Expect(s.Page.Locator("[data-testid='schema-tree-column']").Filter(new() { HasText = "name  (character varying(50))" })).ToHaveCountAsync(1);
            await Assertions.Expect(s.Page.Locator("[data-testid='schema-tree-column']").Filter(new() { HasText = "balance  (numeric(10,2))" })).ToHaveCountAsync(1);

            // System schemas stay out of sight until asked for.
            await Assertions.Expect(s.Page.Locator("li.akml-schema-tree-schema").Filter(new() { HasText = "pg_catalog" })).ToHaveCountAsync(0);
            await s.Page.ClickAsync("[data-testid='schema-system-toggle']");
            await Assertions.Expect(s.Page.Locator("li.akml-schema-tree-schema").Filter(new() { HasText = "pg_catalog" })).ToHaveCountAsync(1);
            await s.Page.ClickAsync("[data-testid='schema-system-toggle']");

            // Another script changes the schema; the explorer shows it without a refresh.
            await s.ExecuteAsync("CREATE TABLE public.qs_demo (id int PRIMARY KEY);");
            await Assertions.Expect(TreeNode(s.Page, "Schema \"public\"").First).ToBeVisibleAsync(new() { Timeout = 60_000 });

            // Back to the first database: the explorer follows it, and the switch asked first.
            await SwitchDatabaseAsync(s.Page, "postgres");
            await Assertions.Expect(s.Page.Locator("[data-testid='schema-header']")).ToContainTextAsync("postgres", new() { Timeout = 30_000 });
            switched = false;
        }
        finally
        {
            // Leave the server as it was found.
            if (switched) await SwitchDatabaseAsync(s.Page, "postgres");
            await s.ExecuteAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE);");
        }
    }

    [SkippableFact]
    public async Task A_role_without_usage_on_a_schema_sees_the_rest_and_the_schema_marked_not_accessible()
    {
        await using var s = await PgWebSession.StartOrSkipAsync(_output);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var database = "akml_e2e_" + suffix;
        var role = "akml_reader_" + suffix;
        var (host, port, _, _) = PgWebSession.Configured()!.Value;
        try
        {
            await s.ExecuteAsync($"CREATE ROLE {role} LOGIN PASSWORD 'pw-{suffix}';");
            await s.ExecuteAsync($"CREATE DATABASE {database};");
            await SwitchDatabaseAsync(s.Page, database);
            await s.ExecuteAsync(
                "CREATE SCHEMA secret; CREATE TABLE secret.vault (k int);\n" +
                "CREATE SCHEMA open_schema; CREATE TABLE open_schema.t (k int);\n" +
                "GRANT USAGE ON SCHEMA open_schema TO PUBLIC; GRANT SELECT ON open_schema.t TO PUBLIC;");

            await PgWebSession.ConnectAsync(s.Page, host, port, database, role, "pw-" + suffix);
            await Assertions.Expect(s.Page.Locator("[data-testid='status-login']")).ToHaveTextAsync(role, new() { Timeout = 30_000 });

            await Assertions.Expect(TreeNode(s.Page, "Database \"" + database + "\"").First).ToBeVisibleAsync(new() { Timeout = 60_000 });
            await ExpandAsync(s.Page, "Database \"" + database + "\"");
            await Assertions.Expect(s.Page.Locator("[data-testid='schema-inaccessible']")).ToHaveCountAsync(1, new() { Timeout = 60_000 });
            await Assertions.Expect(TreeNode(s.Page, "Schema \"open_schema\"").First).ToBeVisibleAsync();
            await Assertions.Expect(TreeNode(s.Page, "Schema \"secret\"").First).ToContainTextAsync("no access");
        }
        finally
        {
            // Back to the administrator, then drop what this test made.
            var (_, _, user, password) = PgWebSession.Configured()!.Value;
            await PgWebSession.ConnectAsync(s.Page, host, port, "postgres", user, password);
            await s.ExecuteAsync($"DROP DATABASE IF EXISTS {database} WITH (FORCE);");
            await s.ExecuteAsync($"DROP ROLE IF EXISTS {role};");
        }
    }
}
