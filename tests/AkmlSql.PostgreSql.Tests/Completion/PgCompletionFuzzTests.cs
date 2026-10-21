using System.Text;
using AkmlSql.PostgreSql.Completion;
using AkmlSql.PostgreSql.Safety;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Completion;

/// <summary>
/// Spec 042 (task T096, FR-064) - text being typed is almost never valid, so the analyzers must read anything: random runs of
/// real PostgreSQL pieces, cut at random places, with the caret anywhere. The engine itself catches and logs, which would hide a
/// crash, so the pieces that do not catch are tested directly. The seed is fixed: a failure repeats.
/// </summary>
public class PgCompletionFuzzTests
{
    private static readonly string[] Pieces =
    {
        "SELECT", "FROM", "WHERE", "JOIN", "LEFT", "ON", "USING", "GROUP BY", "ORDER BY", "HAVING", "LIMIT", "OFFSET", "WITH", "AS", "RECURSIVE",
        "MATERIALIZED", "NOT", "INSERT INTO", "VALUES", "ON CONFLICT", "DO UPDATE SET", "DO NOTHING", "RETURNING", "UPDATE", "SET", "DELETE FROM",
        "MERGE INTO", "WHEN MATCHED THEN", "CREATE TABLE", "CREATE INDEX", "ALTER TABLE", "DROP TABLE", "TRUNCATE", "REFERENCES", "PRIMARY KEY",
        "customers", "c", "orders", "o", "sales.orders", "\"Audit Log\"", "\"", "\"\"", "'text'", "'it''s", "$$", "$tag$", "$1", "::", "::int",
        "(", ")", "(", ")", "[", "]", ",", ";", ".", "*", "=", "<", ">=", "->>", "@>", "||", "--", "-- note\n", "/*", "*/", "/* x */", "\n", " ", "  ",
        "count(*)", "date_trunc(", "coalesce(a,", "generate_series(1,3)", "ARRAY[1,2]", "OVER (", "FILTER (WHERE", "LATERAL", "ONLY", "DISTINCT ON (",
        "1", "42.5", "NULL", "TRUE", "DO $$ BEGIN", "END $$", "BEGIN", "RETURNS", "LANGUAGE sql", "\\d", "éè", "😀", "#", "@x",
    };

    private static string Random(Random random, int pieces)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pieces; i++)
        {
            sb.Append(Pieces[random.Next(Pieces.Length)]);
            if (random.Next(3) > 0) sb.Append(' ');
        }
        return sb.ToString();
    }

    [Fact]
    public void The_analyzer_reads_anything_with_the_caret_anywhere()
    {
        var random = new Random(42);
        for (var n = 0; n < 4000; n++)
        {
            var text = Random(random, random.Next(1, 24));
            var caret = random.Next(0, text.Length + 1);

            var context = PgCursorContextAnalyzer.Analyze(text, caret);

            Assert.NotNull(context);
            Assert.InRange(context.PrefixStart, 0, text.Length);
            Assert.True(context.Prefix.Length <= text.Length);
        }
    }

    [Fact]
    public void The_analyzer_reads_every_prefix_of_a_realistic_script()
    {
        const string script =
            "WITH recent AS MATERIALIZED (SELECT o.order_id, o.freight FROM orders o WHERE o.order_date > now() - interval '7 days'),\n" +
            "     big AS (SELECT * FROM recent WHERE freight > 100)\n" +
            "INSERT INTO sales.orders (order_id, total) SELECT b.order_id, b.freight::numeric(12,2) FROM big b\n" +
            "ON CONFLICT (order_id) DO UPDATE SET total = excluded.total WHERE sales.orders.total < excluded.total RETURNING *;\n" +
            "CREATE FUNCTION f(a int, OUT b text) RETURNS text LANGUAGE plpgsql AS $$ BEGIN SELECT \"Event Name\" INTO b FROM \"Audit Log\" WHERE log_id = a; END $$;\n" +
            "MERGE INTO orders o USING customers c ON o.customer_id = c.customer_id WHEN MATCHED THEN UPDATE SET freight = 0 WHEN NOT MATCHED THEN DO NOTHING;";

        for (var caret = 0; caret <= script.Length; caret++)
            Assert.NotNull(PgCursorContextAnalyzer.Analyze(script, caret));
    }

    [Fact]
    public void The_engine_answers_every_prefix_of_a_realistic_script_without_a_cache_and_with_one()
    {
        const string script = "SELECT c.company_name, count(*) FILTER (WHERE o.freight > 1) FROM customers c LEFT JOIN orders o ON o.customer_id = c.customer_id GROUP BY 1 ORDER BY 2 DESC LIMIT 10;";
        var engine = new PgCompletionEngine();
        var cache = NorthwindPgTestCacheFactory.Create();

        for (var caret = 0; caret <= script.Length; caret++)
        {
            Assert.NotNull(engine.GetCompletions(script, caret, null));
            var response = engine.GetCompletions(script, caret, cache);
            Assert.NotNull(response.Items);
            Assert.True(response.Items.Length <= 50);
        }
    }

    [Fact]
    public void The_safety_check_reads_anything()
    {
        var random = new Random(7);
        for (var n = 0; n < 3000; n++)
        {
            var warnings = PgDestructiveStatementCheck.Analyze(Random(random, random.Next(1, 20)), "s", isProduction: random.Next(2) == 0);

            Assert.NotNull(warnings);
        }
    }

    [Fact]
    public void A_very_long_script_is_read_in_reasonable_time()
    {
        var script = string.Concat(Enumerable.Repeat("SELECT a, b, c FROM customers c JOIN orders o ON o.customer_id = c.customer_id WHERE c.country = 'UK';\n", 800));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var response = new PgCompletionEngine().GetCompletions(script, script.Length - 40, NorthwindPgTestCacheFactory.Create());

        Assert.NotNull(response);
        Assert.True(watch.ElapsedMilliseconds < 2_000, $"took {watch.ElapsedMilliseconds} ms for {script.Length:N0} characters");
    }

    [Fact]
    public void Deeply_nested_parentheses_do_not_overflow_the_stack()
    {
        var text = new string('(', 3000) + "SELECT " + new string(')', 3000);

        Assert.NotNull(PgCursorContextAnalyzer.Analyze(text, 3000 + 7));
        Assert.NotNull(PgDestructiveStatementCheck.Analyze(text));
    }
}
