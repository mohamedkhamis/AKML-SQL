using AkmlSql.Web.E2E.Tests.Harness;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace AkmlSql.Web.E2E.Tests;

/// <summary>
/// Spec 042 (task T098, quickstart user story 3, SC-005): writing PostgreSQL with help, in a real browser against a throwaway
/// PostgreSQL server - completion after FROM (search path, qualified, double-quoted), columns with types, ON CONFLICT, hover with
/// a link to the manual, signature help, no T-SQL sigils, and an older server (step 5, only with <c>AKML_PG14_TEST_CONNECTION</c>).
///
/// <para>Same gates as <see cref="PostgresWorkspaceTests"/>: <c>AKML_PG_TEST_CONNECTION</c>, DEBUG builds of AkmlSql.Web and
/// AkmlSql.Engine, Playwright Chromium, run serially. Each test creates objects named <c>akml_us3_*</c> and drops them again.</para>
/// </summary>
[Trait("Category", "BridgeE2E")]
[Trait("Category", "PostgreSql")]
public sealed class PostgresEditingTests
{
    private readonly ITestOutputHelper _output;

    public PostgresEditingTests(ITestOutputHelper output) => _output = output;

    private const string Setup =
        "DROP SCHEMA IF EXISTS akml_us3_sales CASCADE;\n" +
        "DROP TABLE IF EXISTS public.akml_us3_orders, public.akml_us3_customers, public.\"Akml US3 Details\" CASCADE;\n" +
        "CREATE TABLE public.akml_us3_customers (customer_id text PRIMARY KEY, company_name text NOT NULL, country text);\n" +
        "CREATE TABLE public.akml_us3_orders (order_id integer PRIMARY KEY, customer_id text REFERENCES public.akml_us3_customers, freight numeric(10,2), order_date date);\n" +
        "CREATE TABLE public.\"Akml US3 Details\" (log_id bigint PRIMARY KEY, \"Event Name\" text);\n" +
        "CREATE SCHEMA akml_us3_sales;\n" +
        "CREATE TABLE akml_us3_sales.akml_us3_orders (order_id integer PRIMARY KEY, total numeric(12,2));";

    private const string Teardown =
        "DROP SCHEMA IF EXISTS akml_us3_sales CASCADE;\n" +
        "DROP TABLE IF EXISTS public.akml_us3_orders, public.akml_us3_customers, public.\"Akml US3 Details\" CASCADE;";

    private static async Task<SqlSession> StartWithObjectsAsync(ITestOutputHelper output)
    {
        var s = await PgWebSession.StartOrSkipAsync(output);
        await s.ExecuteAsync(Setup);
        await Assertions.Expect(s.Page.Locator("[data-testid='status-outcome']")).ToContainTextAsync("Query executed successfully.");
        return s;
    }

    private static async Task FinishAsync(SqlSession s)
    {
        try { await s.ExecuteAsync(Teardown); }
        catch { /* the cluster is private and thrown away; a failed clean-up must not hide the test's own result */ }
    }

    private static Task<string[]> LabelsAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('.cm-completionLabel')).map(e => e.textContent)");

    private static Task<string[]> DetailsAsync(IPage page) =>
        page.EvaluateAsync<string[]>("() => Array.from(document.querySelectorAll('.cm-completionDetail')).map(e => e.textContent)");

    /// <summary>Replace the text with <paramref name="typed"/>, open the popup, and keep asking until a label that satisfies the check appears.</summary>
    private static async Task<string[]> WaitForLabelsAsync(IPage page, string typed, Func<string[], bool> satisfied, int seconds = 60, ITestOutputHelper? output = null)
    {
        var labels = Array.Empty<string>();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            await page.ClickAsync("[data-testid='sql-editor'] .cm-content");
            await page.Keyboard.PressAsync("Control+A");
            await page.Keyboard.PressAsync("Delete");
            await page.Keyboard.TypeAsync(typed);
            await page.Keyboard.PressAsync("Control+Space");
            await page.WaitForTimeoutAsync(2_500);   // the engine completes against the synced document, which is debounced
            labels = await LabelsAsync(page);
            output?.WriteLine($"[{typed}] -> {labels.Length} labels: {string.Join(", ", labels.Take(16))}");
            if (satisfied(labels)) return labels;
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(2_000);
        }
        return labels;
    }

    private static Task<string> EditorTextAsync(IPage page) =>
        page.EvaluateAsync<string>("() => document.querySelector(\"[data-testid='sql-editor'] .cm-content\").innerText.replace(/\\u00a0/g, ' ')");

    // ── Step 1: tables after FROM ───────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task After_FROM_the_search_path_is_unqualified_other_schemas_are_qualified_and_odd_names_are_quoted()
    {
        await using var s = await StartWithObjectsAsync(_output);
        try
        {
            var labels = await WaitForLabelsAsync(s.Page, "SELECT * FROM akml_us3_", l => l.Contains("akml_us3_orders") && l.Contains("akml_us3_sales.akml_us3_orders"), output: _output);
            _output.WriteLine(string.Join(", ", labels.Take(12)));

            Assert.Contains("akml_us3_customers", labels);
            Assert.Contains("akml_us3_orders", labels);                       // public is on the search path
            Assert.Contains("akml_us3_sales.akml_us3_orders", labels);        // another schema is qualified
            Assert.DoesNotContain(labels, l => l.StartsWith("public.", StringComparison.Ordinal));

            // a name that needs quotes is inserted double-quoted, and the quote typed so far is not doubled
            labels = await WaitForLabelsAsync(s.Page, "SELECT * FROM \"Akml", l => l.Contains("\"Akml US3 Details\""), output: _output);
            Assert.Contains("\"Akml US3 Details\"", labels);   // while a quoted name is typed the list shows the quoted forms
            await s.Page.Keyboard.PressAsync("Enter");
            var text = await EditorTextAsync(s.Page);
            Assert.Contains("FROM \"Akml US3 Details\"", text);
            Assert.DoesNotContain("\"\"", text);
        }
        finally { await FinishAsync(s); }
    }

    // ── Step 2: columns with types, ON CONFLICT ─────────────────────────────────────────────────

    [SkippableFact]
    public async Task After_an_alias_dot_the_columns_come_with_their_types()
    {
        await using var s = await StartWithObjectsAsync(_output);
        try
        {
            // prime the schema cache, then write the statement with the caret between SELECT and FROM
            await WaitForLabelsAsync(s.Page, "SELECT * FROM akml_us3_", l => l.Contains("akml_us3_orders"));
            await s.Page.Keyboard.PressAsync("Escape");
            await s.SetEditorTextAsync("SELECT  FROM akml_us3_orders o");
            await s.Page.Keyboard.PressAsync("Control+Home");
            for (var i = 0; i < 7; i++) await s.Page.Keyboard.PressAsync("ArrowRight");
            await s.Page.WaitForTimeoutAsync(2_500);
            await s.Page.Keyboard.TypeAsync("o.");
            await s.Page.WaitForTimeoutAsync(2_500);

            var labels = await LabelsAsync(s.Page);
            var details = await DetailsAsync(s.Page);
            _output.WriteLine(string.Join(", ", labels));

            Assert.Contains("order_id", labels);
            Assert.Contains("customer_id", labels);
            Assert.Contains("freight", labels);
            Assert.DoesNotContain("company_name", labels);
            Assert.Contains(details, d => d.Contains("integer"));
            Assert.Contains(details, d => d.Contains("numeric(10,2)"));
        }
        finally { await FinishAsync(s); }
    }

    [SkippableFact]
    public async Task After_ON_CONFLICT_the_actions_are_PostgreSQLs()
    {
        await using var s = await StartWithObjectsAsync(_output);
        try
        {
            var labels = await WaitForLabelsAsync(s.Page, "INSERT INTO akml_us3_orders (order_id) VALUES (1) ON CONFLICT ",
                l => l.Any(x => x.StartsWith("DO NOTHING", StringComparison.Ordinal)));
            _output.WriteLine(string.Join(", ", labels));

            Assert.Contains(labels, l => l.StartsWith("DO NOTHING", StringComparison.Ordinal));
            Assert.Contains(labels, l => l.StartsWith("DO UPDATE", StringComparison.Ordinal));
        }
        finally { await FinishAsync(s); }
    }

    // ── Step 3: hover and signature help ────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Hovering_string_agg_shows_its_signatures_description_and_the_manual_link()
    {
        await using var s = await StartWithObjectsAsync(_output);
        try
        {
            await s.SetEditorTextAsync("SELECT string_agg(company_name, ',') FROM akml_us3_customers");
            await s.Page.WaitForTimeoutAsync(2_500);

            // the middle of the word string_agg, found in the rendered line
            var point = await s.Page.EvaluateAsync<double[]>(@"() => {
                const root = document.querySelector(""[data-testid='sql-editor'] .cm-content"");
                const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
                for (let n = walker.nextNode(); n; n = walker.nextNode()) {
                    const at = n.textContent.indexOf('string_agg');
                    if (at < 0) continue;
                    const range = document.createRange();
                    range.setStart(n, at + 3); range.setEnd(n, at + 5);
                    const r = range.getBoundingClientRect();
                    return [r.left + r.width / 2, r.top + r.height / 2];
                }
                return null;
            }");
            Assert.NotNull(point);
            await s.Page.Mouse.MoveAsync((float)point[0], (float)point[1]);

            var tooltip = s.Page.Locator(".akml-qi-tooltip");
            await Assertions.Expect(tooltip).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Assertions.Expect(tooltip.Locator(".akml-qi-header")).ToContainTextAsync("string_agg");
            await Assertions.Expect(tooltip.Locator(".akml-qi-desc")).Not.ToBeEmptyAsync();
            await Assertions.Expect(tooltip).ToContainTextAsync("string_agg(value text, delimiter text) -> text");

            var link = tooltip.Locator("a.akml-qi-link");
            await Assertions.Expect(link).ToHaveCountAsync(1);
            Assert.StartsWith("https://www.postgresql.org/docs/", await link.GetAttributeAsync("href"));
            Assert.Equal("_blank", await link.GetAttributeAsync("target"));
            Assert.Contains("noopener", await link.GetAttributeAsync("rel") ?? string.Empty);
        }
        finally { await FinishAsync(s); }
    }

    [SkippableFact]
    public async Task Inside_date_trunc_the_parameters_are_shown_and_the_active_one_follows_the_commas()
    {
        await using var s = await StartWithObjectsAsync(_output);
        try
        {
            await s.SetEditorTextAsync(string.Empty);
            await s.Page.WaitForTimeoutAsync(1_500);
            await s.Page.Keyboard.TypeAsync("SELECT date_trunc(");

            var tip = s.Page.Locator(".akml-sig-tooltip");
            await Assertions.Expect(tip).ToBeVisibleAsync(new() { Timeout = 15_000 });
            await Assertions.Expect(tip.Locator(".akml-sig-label")).ToContainTextAsync("date_trunc(");
            await Assertions.Expect(tip.Locator(".akml-sig-active")).ToHaveCountAsync(1);
        }
        finally { await FinishAsync(s); }
    }

    // ── Step 4: no T-SQL ────────────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task Typing_at_hash_or_bracket_offers_no_t_sql()
    {
        await using var s = await StartWithObjectsAsync(_output);
        try
        {
            foreach (var sigil in new[] { "@", "#", "[" })
            {
                await s.SetEditorTextAsync(string.Empty);
                await s.Page.Keyboard.TypeAsync("SELECT " + sigil);
                await s.Page.WaitForTimeoutAsync(1_500);
                var open = await LabelsAsync(s.Page);
                _output.WriteLine($"after typing '{sigil}': {open.Length} labels open: {string.Join(", ", open.Take(10))}");
                Assert.True(0 == await s.Page.Locator(".cm-tooltip-autocomplete").CountAsync(), $"typing '{sigil}' opened the popup: {string.Join(", ", open.Take(10))}");   // typing the sigil opens nothing

                await s.Page.Keyboard.PressAsync("Control+Space");
                await s.Page.WaitForTimeoutAsync(2_500);
                var labels = await LabelsAsync(s.Page);
                Assert.DoesNotContain(labels, l => l.StartsWith("@", StringComparison.Ordinal) || l.StartsWith("#", StringComparison.Ordinal) || l.StartsWith("[", StringComparison.Ordinal));
                Assert.DoesNotContain(labels, l => l is "GO" or "NOCOUNT" or "sp_help" or "xp_cmdshell" or "GETDATE" or "ISNULL");
                await s.Page.Keyboard.PressAsync("Escape");
            }
        }
        finally { await FinishAsync(s); }
    }

    // ── Step 5: an older server ─────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task An_older_server_is_not_offered_commands_that_arrived_later()
    {
        // MERGE arrived in PostgreSQL 15. Set AKML_PG14_TEST_CONNECTION to a 14 server to run this.
        await using var s = await PgWebSession.StartOrSkipAsync(_output, envVar: "AKML_PG14_TEST_CONNECTION");

        var version = await s.Page.Locator("[data-testid='status-kind']").GetAttributeAsync("title") ?? string.Empty;
        Skip.IfNot(version.StartsWith("PostgreSQL 14", StringComparison.Ordinal), "AKML_PG14_TEST_CONNECTION is not a PostgreSQL 14 server: " + version);

        var labels = await WaitForLabelsAsync(s.Page, "mer", l => l.Length > 0, seconds: 20);
        Assert.DoesNotContain("MERGE", labels);
        labels = await WaitForLabelsAsync(s.Page, "SELECT json_exi", l => true, seconds: 5);
        Assert.DoesNotContain("json_exists", labels);
    }
}
