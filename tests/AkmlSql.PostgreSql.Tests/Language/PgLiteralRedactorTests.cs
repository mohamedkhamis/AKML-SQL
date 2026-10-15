using System.Text;
using System.Text.RegularExpressions;
using AkmlSql.PostgreSql.Language;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Language;

/// <summary>
/// Spec 042 (task T111, FR-081). Literals of PostgreSQL text become placeholders before the text goes to a cloud AI
/// provider; identifiers, keywords, operators, comments and layout stay. Text the scanner cannot read to the end is
/// refused, because the unread part might hold a literal.
/// </summary>
public class PgLiteralRedactorTests
{
    /// <summary>One pass over the redacted text, so a literal that looks like a placeholder is never replaced twice.</summary>
    private static string Restore(string redacted, IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0) return redacted;
        var pattern = string.Join("|", map.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape));
        return Regex.Replace(redacted, pattern, m => map[m.Value]);
    }

    private static PgRedaction Ok(string sql)
    {
        var result = PgLiteralRedactor.Redact(sql);
        Assert.True(result.Ok, result.Error);
        Assert.Null(result.Error);
        return result;
    }

    // ---- the literal kinds ------------------------------------------------------------------------------------

    [Fact]
    public void A_string_literal_becomes_a_quoted_placeholder_and_the_map_keeps_the_original()
    {
        var r = Ok("SELECT * FROM customers WHERE country = 'UK'");

        Assert.Equal("SELECT * FROM customers WHERE country = '__STR_0__'", r.Text);
        Assert.Equal("'UK'", r.Placeholders["'__STR_0__'"]);
    }

    [Fact]
    public void Numbers_become_placeholders()
    {
        var r = Ok("SELECT 1 FROM t WHERE freight > 100.5 AND id = 7 AND x < 1e3");

        Assert.Equal("SELECT __NUM_0__ FROM t WHERE freight > __NUM_1__ AND id = __NUM_2__ AND x < __NUM_3__", r.Text);
        Assert.Equal("100.5", r.Placeholders["__NUM_1__"]);
    }

    [Theory]
    [InlineData("it''s")]
    [InlineData("a\"b")]
    [InlineData("line one\nline two")]
    [InlineData("")]
    public void A_string_with_quotes_newlines_or_nothing_in_it_is_one_literal(string inner)
    {
        var sql = "SELECT '" + inner + "' AS v";

        var r = Ok(sql);

        Assert.Equal("SELECT '__STR_0__' AS v", r.Text);
        Assert.Equal(sql, Restore(r.Text, r.Placeholders));
    }

    [Theory]
    [InlineData("E'tab\\there'")]
    [InlineData("U&'d\\0061t\\+000061'")]
    [InlineData("B'1010'")]
    [InlineData("X'1F'")]
    [InlineData("N'text'")]
    public void Prefixed_strings_are_literals_too(string literal)
    {
        var sql = "SELECT " + literal + " FROM t";

        var r = Ok(sql);

        Assert.Equal("SELECT '__STR_0__' FROM t", r.Text);
        Assert.Equal(literal, r.Placeholders["'__STR_0__'"]);
        Assert.Equal(sql, Restore(r.Text, r.Placeholders));
    }

    [Fact]
    public void A_dollar_quoted_string_keeps_its_tag_and_loses_its_text()
    {
        var r = Ok("SELECT $$secret value$$, $tag$ it's $$ inside $tag$");

        Assert.Equal("SELECT $$__STR_0__$$, $tag$__STR_1__$tag$", r.Text);
        Assert.DoesNotContain("secret", r.Text);
        Assert.Equal("$$secret value$$", r.Placeholders["$$__STR_0__$$"]);
    }

    [Fact]
    public void A_function_body_keeps_its_structure_and_loses_the_literals_in_it()
    {
        const string sql =
            "CREATE FUNCTION f(a int) RETURNS text LANGUAGE plpgsql AS $$\n" +
            "BEGIN\n  IF a > 10 THEN RETURN 'big customer'; END IF;\n  RETURN 'small';\nEND\n$$;";

        var r = Ok(sql);

        Assert.Contains("CREATE FUNCTION f(a int) RETURNS text LANGUAGE plpgsql AS $$", r.Text);
        Assert.Contains("IF a > __NUM_", r.Text);
        Assert.Contains("RETURN '__STR_", r.Text);
        Assert.DoesNotContain("big customer", r.Text);
        Assert.DoesNotContain("small", r.Text);
        Assert.Contains("END IF;", r.Text);
        Assert.Equal(sql, Restore(r.Text, r.Placeholders));
    }

    [Fact]
    public void A_DO_block_is_redacted_inside()
    {
        var r = Ok("DO $do$ BEGIN INSERT INTO audit(who, n) VALUES ('mallory', 42); END $do$;");

        Assert.Equal("DO $do$ BEGIN INSERT INTO audit(who, n) VALUES ('__STR_0__', __NUM_0__); END $do$;", r.Text);
    }

    [Fact]
    public void A_body_that_is_not_readable_as_SQL_is_one_literal_and_the_request_still_succeeds()
    {
        // Python between dollar quotes: the apostrophe never closes, so the body cannot be read as PostgreSQL text.
        const string sql = "CREATE FUNCTION f() RETURNS text LANGUAGE plpython3u AS $py$ x = it's $py$;";

        var r = Ok(sql);

        Assert.Equal("CREATE FUNCTION f() RETURNS text LANGUAGE plpython3u AS $py$__STR_0__$py$;", r.Text);
        Assert.Equal(sql, Restore(r.Text, r.Placeholders));
    }

    // ---- what stays ------------------------------------------------------------------------------------------

    [Fact]
    public void Identifiers_keywords_operators_comments_casts_and_parameters_stay()
    {
        const string sql =
            "-- who's late? it's a comment\n" +
            "SELECT o.\"Order ID\", c.name::text, $1, now() /* block 'x' 5 */\n" +
            "FROM orders o JOIN customers c USING (customer_id)\n" +
            "WHERE o.data @> '{}' AND c.active IS NOT NULL AND o.flag = TRUE;";

        var r = Ok(sql);

        Assert.Contains("-- who's late? it's a comment", r.Text);
        Assert.Contains("o.\"Order ID\", c.name::text, $1, now() /* block 'x' 5 */", r.Text);
        Assert.Contains("FROM orders o JOIN customers c USING (customer_id)", r.Text);
        Assert.Contains("o.data @> '__STR_0__' AND c.active IS NOT NULL AND o.flag = TRUE;", r.Text);
        Assert.Single(r.Placeholders);
    }

    [Fact]
    public void Row_counts_after_LIMIT_OFFSET_and_FETCH_are_structure_and_stay()
    {
        var r = Ok("SELECT id FROM t WHERE x = 5 ORDER BY id LIMIT 10 OFFSET 20;\nSELECT id FROM t FETCH FIRST 3 ROWS ONLY;");

        Assert.Contains("x = __NUM_0__", r.Text);
        Assert.Contains("LIMIT 10 OFFSET 20", r.Text);
        Assert.Contains("FETCH FIRST 3 ROWS ONLY", r.Text);
        Assert.Single(r.Placeholders);
    }

    [Fact]
    public void Layout_is_untouched_when_there_is_nothing_to_redact()
    {
        const string sql = "SELECT  a,\r\n\tb\n  FROM   t\n;\n\n";

        var r = Ok(sql);

        Assert.Equal(sql, r.Text);
        Assert.Empty(r.Placeholders);
    }

    [Fact]
    public void Empty_text_is_fine()
    {
        var r = Ok(string.Empty);

        Assert.Equal(string.Empty, r.Text);
        Assert.Empty(r.Placeholders);
    }

    [Fact]
    public void A_psql_meta_command_stays()
    {
        var r = Ok("\\c northwind\nSELECT 'x';");

        Assert.StartsWith("\\c northwind\n", r.Text);
        Assert.Contains("'__STR_0__'", r.Text);
    }

    // ---- text that cannot be read to the end ------------------------------------------------------------------

    [Theory]
    [InlineData("SELECT 'unfinished")]
    [InlineData("SELECT \"unfinished")]
    [InlineData("SELECT $$unfinished")]
    [InlineData("SELECT $tag$ unfinished $tag")]
    [InlineData("SELECT 1 /* unfinished")]
    [InlineData("SELECT E'unfinished\\'")]
    public void Text_that_ends_inside_a_literal_or_comment_is_refused(string sql)
    {
        var r = PgLiteralRedactor.Redact(sql);

        Assert.False(r.Ok);
        Assert.Equal(string.Empty, r.Text);
        Assert.Empty(r.Placeholders);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }

    [Fact]
    public void The_refusal_never_echoes_the_text()
    {
        var r = PgLiteralRedactor.Redact("SELECT 'hunter2-unfinished");

        Assert.False(r.Ok);
        Assert.DoesNotContain("hunter2", r.Error);
        Assert.DoesNotContain("hunter2", r.Text);
    }

    // ---- properties ------------------------------------------------------------------------------------------

    [Fact]
    public void A_literal_that_looks_like_a_placeholder_is_restored_correctly()
    {
        const string sql = "SELECT '__STR_1__', 'real', 7, '__NUM_0__'";

        var r = Ok(sql);

        Assert.Equal(sql, Restore(r.Text, r.Placeholders));
    }

    [Fact]
    public void Every_original_literal_is_gone_from_the_redacted_text()
    {
        var r = Ok("INSERT INTO people(name, ssn, salary) VALUES ('Ada Lovelace', '078-05-1120', 123456.78) RETURNING id;");

        Assert.DoesNotContain("Ada", r.Text);
        Assert.DoesNotContain("078-05-1120", r.Text);
        Assert.DoesNotContain("123456", r.Text);
        Assert.Contains("INSERT INTO people(name, ssn, salary) VALUES", r.Text);
        Assert.EndsWith("RETURNING id;", r.Text);
    }

    [Fact]
    public void Redaction_is_repeatable()
    {
        const string sql = "SELECT 'a', 1, $$b$$ FROM t";

        Assert.Equal(PgLiteralRedactor.Redact(sql).Text, PgLiteralRedactor.Redact(sql).Text);
    }

    [Fact]
    public void Restoring_what_was_redacted_gives_back_the_text_for_anything_that_can_be_read()
    {
        string[] pieces =
        {
            "SELECT", "FROM", "WHERE", "t", "c", "\"Q n\"", "'a'", "'it''s'", "E'x\\n'", "$$b$$", "$t$ c $t$", "$1", "::int", "(", ")", ",", ";",
            "=", "<>", "42", "1.5", "-- note\n", "/* c */", "\n", " ", "DO $$ BEGIN SELECT 1; END $$", "LIMIT 5", "AS $f$ SELECT 'z' $f$",
        };
        var random = new Random(2024);
        var redactedAtLeastOnce = 0;

        for (var n = 0; n < 3000; n++)
        {
            var sb = new StringBuilder();
            for (var i = random.Next(1, 20); i > 0; i--) sb.Append(pieces[random.Next(pieces.Length)]).Append(random.Next(3) == 0 ? "" : " ");
            var sql = sb.ToString();

            var r = PgLiteralRedactor.Redact(sql);

            if (!r.Ok) continue;
            redactedAtLeastOnce++;
            Assert.Equal(sql, Restore(r.Text, r.Placeholders));
        }

        Assert.True(redactedAtLeastOnce > 1000, "the generator should mostly make readable text");
    }

    [Fact]
    public void A_megabyte_of_text_is_redacted_quickly()
    {
        var script = string.Concat(Enumerable.Repeat("SELECT a, b FROM customers c WHERE c.country = 'UK' AND c.id > 12345 AND c.note = $$n$$;\n", 12_000));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var r = PgLiteralRedactor.Redact(script);

        Assert.True(r.Ok);
        Assert.True(script.Length > 1_000_000);
        Assert.True(watch.ElapsedMilliseconds < 3_000, $"took {watch.ElapsedMilliseconds} ms");
        Assert.Equal(36_000, r.Placeholders.Count);
    }

    // ---- the language of a body decides whether it is read as code ---------------------------------------------

    [Fact]
    public void A_body_in_another_language_that_happens_to_scan_cleanly_is_still_one_literal()
    {
        const string sql = "CREATE FUNCTION f() RETURNS text LANGUAGE plpython3u AS $$ return \"p@ssw0rd\" $$;";

        var r = Ok(sql);

        Assert.Equal("CREATE FUNCTION f() RETURNS text LANGUAGE plpython3u AS $$__STR_0__$$;", r.Text);
        Assert.DoesNotContain("p@ssw0rd", r.Text);
        Assert.Equal(sql, Restore(r.Text, r.Placeholders));
    }

    [Fact]
    public void The_language_named_after_the_body_counts_too()
    {
        var perl = Ok("CREATE FUNCTION f() RETURNS text AS $$ return 'secret'; $$ LANGUAGE plperl;");
        Assert.Equal("CREATE FUNCTION f() RETURNS text AS $$__STR_0__$$ LANGUAGE plperl;", perl.Text);

        var sql = Ok("CREATE FUNCTION g() RETURNS text AS $$ SELECT 'secret' $$ LANGUAGE sql;");
        Assert.Equal("CREATE FUNCTION g() RETURNS text AS $$ SELECT '__STR_0__' $$ LANGUAGE sql;", sql.Text);
    }

    [Fact]
    public void A_quoted_language_name_is_read_and_each_statement_is_judged_on_its_own()
    {
        var r = Ok("CREATE FUNCTION a() RETURNS int LANGUAGE 'plpgsql' AS $$ BEGIN RETURN 1; END $$;\nCREATE FUNCTION b() RETURNS int LANGUAGE plv8 AS $$ return 1; $$;");

        Assert.Contains("AS $$ BEGIN RETURN __NUM_0__; END $$;", r.Text);     // PL/pgSQL: read as code
        Assert.Contains("LANGUAGE plv8 AS $$__STR_", r.Text);                  // JavaScript: one literal
    }

    [Fact]
    public void Two_functions_with_no_semicolon_between_them_are_judged_by_the_language_that_is_not_SQL()
    {
        // Text that does not parse (the "fix my script" case): the first function's semicolon is missing.
        var r = Ok("CREATE FUNCTION a() RETURNS text LANGUAGE plpython3u AS $$ return \"p@ssw0rd\" $$\nCREATE FUNCTION b() RETURNS int LANGUAGE sql AS $$ SELECT 1 $$;");

        Assert.Contains("AS $$__STR_0__$$", r.Text);
        Assert.DoesNotContain("p@ssw0rd", r.Text);
    }

    [Fact]
    public void A_name_spelled_language_does_not_hide_the_LANGUAGE_word_after_it()
    {
        var r = Ok("CREATE FUNCTION f() RETURNS SETOF language LANGUAGE sql AS $$ SELECT * FROM language WHERE name = 'en' $$;");

        Assert.Contains("WHERE name = '__STR_0__' $$", r.Text);        // read as code
    }

    [Fact]
    public void A_psql_meta_command_ends_a_statement_for_the_language_too()
    {
        var r = Ok("CREATE FUNCTION a() RETURNS text LANGUAGE plperl AS $$ return 'x' $$ \\g\nCREATE FUNCTION b() RETURNS int LANGUAGE sql AS $$ SELECT 1 $$;");

        Assert.Contains("AS $$__STR_0__$$ \\g", r.Text);
        Assert.Contains("AS $$ SELECT __NUM_0__ $$;", r.Text);
    }
}
