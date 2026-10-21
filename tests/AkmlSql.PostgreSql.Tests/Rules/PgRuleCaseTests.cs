using AkmlSql.Engine.Schema.Models;
using Index = AkmlSql.Engine.Schema.Models.Index;
using AkmlSql.PostgreSql.Analysis;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Rules;

/// <summary>
/// Spec 042 (task T159, SC-016) - one positive and at least one negative script for every rule. A positive script must produce
/// findings of that rule on exactly the marked spans (so the anchoring is pinned, not only the count); a negative script must
/// produce none from that rule.
/// </summary>
[Trait("Category", "Rules")]
public class PgRuleCaseTests
{
    public static IEnumerable<object[]> Ids => PgRuleCases.Ids;

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_positive_script_is_flagged_on_exactly_the_marked_text(string ruleId)
    {
        var rule = PgRuleCases.For(ruleId);
        var (sql, expected) = PgRuleTestKit.Unmark(rule.Positive);
        Assert.NotEmpty(expected);

        var found = PgRuleTestKit.Run(ruleId, sql, rule.NeedsSchema ? PgRuleTestKit.Northwind() : null);

        var actual = found.Select(d => (d.StartOffset, d.EndOffset)).OrderBy(s => s.StartOffset).ToList();
        var want = expected.OrderBy(s => s.Start).Select(s => (s.Start, s.End)).ToList();
        Assert.True(want.SequenceEqual(actual),
            $"{ruleId} should underline {Describe(sql, want)} but underlined {Describe(sql, actual)}");

        Assert.All(found, d =>
        {
            Assert.Equal(ruleId, d.RuleId);
            Assert.True(d.Line >= 1 && d.Column >= 1);
            Assert.False(string.IsNullOrWhiteSpace(d.Message));
            Assert.DoesNotContain("hunter2", d.Message);       // a password is never echoed (PGSE003)
        });
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_negative_scripts_are_left_alone(string ruleId)
    {
        var rule = PgRuleCases.For(ruleId);
        foreach (var sql in rule.Negatives)
        {
            var found = PgRuleTestKit.Run(ruleId, sql, rule.NeedsSchema ? PgRuleTestKit.Northwind() : null);
            Assert.True(found.Count == 0, $"{ruleId} should not flag:\n{sql}\nbut underlined {Describe(sql, found.Select(d => (d.StartOffset, d.EndOffset)).ToList())}");
        }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void A_rule_that_needs_the_schema_reports_nothing_without_one(string ruleId)
    {
        var rule = PgRuleCases.For(ruleId);
        if (!rule.NeedsSchema) return;

        var (sql, _) = PgRuleTestKit.Unmark(rule.Positive);
        var engineRule = PgRuleTestKit.Registry.Find(ruleId)!;

        Assert.True(engineRule.RequiresSchema);
        Assert.Empty(engineRule.Analyze(PgAnalysisContext.Create(sql)));      // even when called directly, with no cache
    }

    private static string Describe(string sql, IEnumerable<(int Start, int End)> spans) =>
        "[" + string.Join(", ", spans.Select(s => $"{s.Start}..{s.End} \"{(s.End <= sql.Length && s.End >= s.Start ? sql.Substring(s.Start, s.End - s.Start) : "?")}\"")) + "]";

    // ── Cases that need more than a script ───────────────────────────────────────────────────────────

    [Fact]
    public void PGDE006_accepts_an_index_the_cache_already_has()
    {
        var cache = PgRuleTestKit.Northwind();
        cache.FindObject("public", "orders")!.Indexes.Add(new Index
        {
            IndexName = "orders_customer_idx",
            Columns = { new IndexColumn { ColumnName = "customer_id" }, new IndexColumn { ColumnName = "order_date" } },
        });

        var found = PgRuleTestKit.Run("PGDE006",
            "ALTER TABLE orders ADD CONSTRAINT fk_customer FOREIGN KEY (customer_id) REFERENCES customers (customer_id);", cache);

        Assert.Empty(found);
    }

    [Fact]
    public void PGDE006_does_not_count_an_index_that_only_contains_the_column_later()
    {
        var cache = PgRuleTestKit.Northwind();
        cache.FindObject("public", "orders")!.Indexes.Add(new Index
        {
            IndexName = "orders_date_customer_idx",
            Columns = { new IndexColumn { ColumnName = "order_date" }, new IndexColumn { ColumnName = "customer_id" } },
        });

        var found = PgRuleTestKit.Run("PGDE006",
            "ALTER TABLE orders ADD CONSTRAINT fk_customer FOREIGN KEY (customer_id) REFERENCES customers (customer_id);", cache);

        Assert.Single(found);
    }

    [Fact]
    public void PGDE006_says_nothing_about_an_existing_table_the_cache_has_not_loaded()
    {
        var found = PgRuleTestKit.Run("PGDE006",
            "ALTER TABLE not_loaded_yet ADD CONSTRAINT fk FOREIGN KEY (a) REFERENCES customers (customer_id);", PgRuleTestKit.Northwind());

        Assert.Empty(found);
    }

    [Fact]
    public void PGDE006_reads_a_column_level_REFERENCES_in_CREATE_TABLE()
    {
        var found = PgRuleTestKit.Run("PGDE006",
            "CREATE TABLE child (id integer PRIMARY KEY, parent_id text REFERENCES customers (customer_id));", PgRuleTestKit.Northwind());

        Assert.Single(found);
        Assert.Contains("parent_id", found[0].Message);
    }

    [Fact]
    public void PGBP005_resolves_the_tables_through_the_search_path()
    {
        var cache = PgRuleTestKit.Northwind("sales", "public");     // sales.orders now comes before public.orders

        var found = PgRuleTestKit.Run("PGBP005",
            "SELECT customer_id FROM orders o JOIN customers c ON o.customer_id = c.customer_id;", cache);

        Assert.Single(found);       // sales.orders and public.customers both have customer_id
    }

    [Fact]
    public void PGBP005_allows_a_name_only_one_table_has()
    {
        var found = PgRuleTestKit.Run("PGBP005",
            "SELECT freight, company_name FROM orders o JOIN customers c ON o.customer_id = c.customer_id;", PgRuleTestKit.Northwind());

        Assert.Empty(found);
    }

    [Fact]
    public void PGBP005_lets_ORDER_BY_name_an_output_column()
    {
        var found = PgRuleTestKit.Run("PGBP005",
            "SELECT c.customer_id AS customer_id FROM customers c JOIN orders o ON o.customer_id = c.customer_id ORDER BY customer_id;", PgRuleTestKit.Northwind());

        Assert.Empty(found);
    }

    [Fact]
    public void PGSE003_never_repeats_the_password_in_its_message()
    {
        var found = PgRuleTestKit.Run("PGSE003", "ALTER ROLE reporter ENCRYPTED PASSWORD 'zq7xv-pw-4417';");

        Assert.Single(found);
        Assert.DoesNotContain("zq7xv", found[0].Message + PgRuleTestKit.Registry.Find("PGSE003")!.Description);
        Assert.DoesNotContain("4417", found[0].Message);
    }

    [Fact]
    public void PGST004_offers_to_insert_AS_before_the_alias()
    {
        var sql = "SELECT customer_id cid FROM customers;";
        var d = Assert.Single(PgRuleTestKit.Run("PGST004", sql));

        var fix = Assert.Single(d.FixActions);
        Assert.Equal("SELECT customer_id AS cid FROM customers;", sql.Remove(fix.ReplacementStart, fix.ReplacementEnd - fix.ReplacementStart).Insert(fix.ReplacementStart, fix.ReplacementText));
    }

    [Fact]
    public void PGST004_reads_an_alias_after_a_function_a_cast_and_a_CASE()
    {
        var found = PgRuleTestKit.Run("PGST004",
            "SELECT upper(company_name) up, customer_id::text t, CASE WHEN city IS NULL THEN 1 ELSE 2 END c, count(*) FILTER (WHERE city = 'x') f FROM customers;");

        Assert.Equal(4, found.Count);
    }

    [Fact]
    public void PGST003_and_PGDEP002_and_PGST002_quick_fixes_produce_valid_replacements()
    {
        string Apply(string sql, AkmlSql.Engine.Analysis.AnalysisDiagnostic d)
        {
            var fix = Assert.Single(d.FixActions);
            return sql.Remove(fix.ReplacementStart, fix.ReplacementEnd - fix.ReplacementStart).Insert(fix.ReplacementStart, fix.ReplacementText);
        }

        var a = "SELECT 1 WHERE 1 != 2;";
        Assert.Equal("SELECT 1 WHERE 1 <> 2;", Apply(a, Assert.Single(PgRuleTestKit.Run("PGST003", a))));

        var b = "SELECT f(a := 1);";
        Assert.Equal("SELECT f(a => 1);", Apply(b, Assert.Single(PgRuleTestKit.Run("PGDEP002", b))));

        var c = "SELECT 1";
        Assert.Equal("SELECT 1;", Apply(c, Assert.Single(PgRuleTestKit.Run("PGST002", c))));
    }

    [Fact]
    public void PGNM001_counts_bytes_not_characters()
    {
        // 32 two-byte letters are 64 bytes but only 32 characters.
        var found = PgRuleTestKit.Run("PGNM001", "CREATE TABLE t (" + new string('é', 32) + " integer);");

        Assert.Single(found);
        Assert.Empty(PgRuleTestKit.Run("PGNM001", "CREATE TABLE t (" + new string('é', 31) + " integer);"));
    }

    [Fact]
    public void Findings_after_a_wide_character_are_placed_at_the_right_offset()
    {
        // The parse tree counts UTF-8 bytes; the editor counts UTF-16 units. The emoji is 4 bytes and 2 units.
        var sql = "SELECT '😀', lower(company_name) FROM customers WHERE lower(company_name) = 'x';";

        var d = Assert.Single(PgRuleTestKit.Run("PGPE003", sql));

        Assert.Equal("lower", PgRuleTestKit.Underlined(sql, d));
        Assert.Equal(sql.LastIndexOf("lower", StringComparison.Ordinal), d.StartOffset);
    }

    [Fact]
    public void A_finding_reports_its_one_based_line_and_column()
    {
        var sql = "SELECT 1;\n\n  DELETE FROM orders;";

        var d = Assert.Single(PgRuleTestKit.Run("PGBP002", sql));

        Assert.Equal(3, d.Line);
        Assert.Equal(sql.Split('\n')[2].IndexOf("orders", StringComparison.Ordinal) + 1, d.Column);
    }
}
