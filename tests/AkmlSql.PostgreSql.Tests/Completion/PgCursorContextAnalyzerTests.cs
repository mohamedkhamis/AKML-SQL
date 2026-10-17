using AkmlSql.PostgreSql.Completion;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Completion;

/// <summary>Spec 042 (task T096, FR-064) - where the caret is: clause, prefix, dot chain, aliases, CTEs, calls, dollar bodies.</summary>
public class PgCursorContextAnalyzerTests
{
    /// <summary>The document with "|" marking the caret.</summary>
    private static PgCursorContext At(string doc)
    {
        var caret = doc.IndexOf('|');
        Assert.True(caret >= 0, "no caret marker in: " + doc);
        return PgCursorContextAnalyzer.Analyze(doc.Remove(caret, 1), caret);
    }

    private static string[] Qualifiers(PgCursorContext c) => c.VisibleTables.Select(t => t.Qualifier).ToArray();

    // ── Clauses ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("|", PgClause.StatementStart)]
    [InlineData("SELECT 1; |", PgClause.StatementStart)]
    [InlineData("SELECT | FROM customers", PgClause.SelectList)]
    [InlineData("SELECT customer_id, | FROM customers", PgClause.SelectList)]
    [InlineData("SELECT * FROM |", PgClause.From)]
    [InlineData("SELECT * FROM customers c |", PgClause.From)]
    [InlineData("SELECT * FROM customers, |", PgClause.From)]
    [InlineData("SELECT * FROM customers c JOIN |", PgClause.Join)]
    [InlineData("SELECT * FROM customers c LEFT JOIN orders o ON |", PgClause.JoinOn)]
    [InlineData("SELECT * FROM customers c JOIN orders o USING (|", PgClause.JoinUsing)]
    [InlineData("SELECT * FROM customers WHERE |", PgClause.Where)]
    [InlineData("SELECT * FROM customers WHERE country = 'UK' AND |", PgClause.Where)]
    [InlineData("SELECT count(*) FROM customers GROUP BY |", PgClause.GroupBy)]
    [InlineData("SELECT count(*) FROM customers GROUP BY country HAVING |", PgClause.Having)]
    [InlineData("SELECT * FROM customers ORDER BY |", PgClause.OrderBy)]
    [InlineData("SELECT * FROM customers LIMIT |", PgClause.Limit)]
    [InlineData("SELECT * FROM customers OFFSET 5 |", PgClause.Limit)]
    [InlineData("SELECT 1 UNION |", PgClause.SetOperation)]
    [InlineData("SELECT * FROM customers WINDOW w AS (|", PgClause.OrderBy)]
    public void Select_clauses(string doc, PgClause expected) => Assert.Equal(expected, At(doc).Clause);

    [Theory]
    [InlineData("INSERT INTO |", PgClause.InsertTarget)]
    [InlineData("INSERT INTO orders (|", PgClause.InsertColumns)]
    [InlineData("INSERT INTO orders (order_id, |", PgClause.InsertColumns)]
    [InlineData("INSERT INTO orders (order_id) VALUES (|", PgClause.Values)]
    [InlineData("INSERT INTO orders (order_id) VALUES (1) ON |", PgClause.OnConflict)]
    [InlineData("INSERT INTO orders (order_id) VALUES (1) ON CONFLICT |", PgClause.OnConflict)]
    [InlineData("INSERT INTO orders (order_id) VALUES (1) ON CONFLICT (|", PgClause.OnConflictTarget)]
    [InlineData("INSERT INTO orders (order_id) VALUES (1) ON CONFLICT (order_id) |", PgClause.OnConflict)]
    [InlineData("INSERT INTO orders (order_id) VALUES (1) ON CONFLICT (order_id) DO |", PgClause.OnConflictAction)]
    [InlineData("INSERT INTO orders (order_id) VALUES (1) ON CONFLICT (order_id) DO UPDATE |", PgClause.OnConflictAction)]
    [InlineData("INSERT INTO orders (order_id) VALUES (1) ON CONFLICT (order_id) DO UPDATE SET |", PgClause.Set)]
    [InlineData("INSERT INTO orders (order_id) VALUES (1) RETURNING |", PgClause.Returning)]
    [InlineData("INSERT INTO orders (order_id) SELECT | FROM customers", PgClause.SelectList)]
    public void Insert_clauses(string doc, PgClause expected) => Assert.Equal(expected, At(doc).Clause);

    [Theory]
    [InlineData("UPDATE |", PgClause.UpdateTarget)]
    [InlineData("UPDATE orders SET |", PgClause.Set)]
    [InlineData("UPDATE orders o SET freight = 1, |", PgClause.Set)]
    [InlineData("UPDATE orders SET freight = 1 FROM customers c WHERE |", PgClause.Where)]
    [InlineData("UPDATE orders SET freight = 1 RETURNING |", PgClause.Returning)]
    [InlineData("DELETE FROM |", PgClause.DeleteTarget)]
    [InlineData("DELETE FROM orders o USING |", PgClause.Using)]
    [InlineData("DELETE FROM orders WHERE |", PgClause.Where)]
    [InlineData("DELETE FROM orders RETURNING |", PgClause.Returning)]
    [InlineData("MERGE INTO |", PgClause.MergeTarget)]
    [InlineData("MERGE INTO orders o USING |", PgClause.MergeSource)]
    [InlineData("MERGE INTO orders o USING customers c ON |", PgClause.JoinOn)]
    [InlineData("MERGE INTO orders o USING customers c ON o.customer_id = c.customer_id WHEN |", PgClause.MergeWhen)]
    [InlineData("MERGE INTO orders o USING customers c ON o.customer_id = c.customer_id WHEN MATCHED THEN |", PgClause.MergeAction)]
    [InlineData("MERGE INTO orders o USING customers c ON o.customer_id = c.customer_id WHEN MATCHED THEN UPDATE SET |", PgClause.Set)]
    public void Update_delete_merge_clauses(string doc, PgClause expected) => Assert.Equal(expected, At(doc).Clause);

    [Fact]
    public void Verb_is_the_statements_not_the_with_lists()
    {
        Assert.Equal("SELECT", At("WITH x AS (SELECT 1) SELECT | FROM x").Verb);
        Assert.Equal("DELETE", At("WITH x AS (SELECT 1) DELETE FROM orders WHERE |").Verb);
        Assert.Equal("INSERT", At("INSERT INTO orders (a) VALUES (|").Verb);
        Assert.Equal("UPDATE", At("UPDATE orders SET |").Verb);
    }

    // ── The word being typed and the dot chain ──────────────────────────────────────────────────

    [Fact]
    public void The_word_being_typed_is_the_prefix()
    {
        var c = At("SELECT cust| FROM customers");

        Assert.Equal("cust", c.Prefix);
        Assert.Equal(7, c.PrefixStart);
        Assert.False(c.PrefixQuoted);
        Assert.False(c.AfterDot);
    }

    [Fact]
    public void Nothing_typed_gives_an_empty_prefix_at_the_caret()
    {
        var c = At("SELECT * FROM |");

        Assert.Equal(string.Empty, c.Prefix);
        Assert.Equal(c.Caret, c.PrefixStart);
    }

    [Fact]
    public void A_quoted_prefix_is_without_its_opening_quote()
    {
        var c = At("SELECT * FROM \"Cust|");

        Assert.Equal("Cust", c.Prefix);
        Assert.True(c.PrefixQuoted);
        Assert.Equal(14, c.PrefixStart);
    }

    [Fact]
    public void A_dot_chain_lists_the_names_before_the_dot()
    {
        var one = At("SELECT o.| FROM orders o");
        var two = At("SELECT * FROM sales.|");
        var three = At("SELECT sales.orders.| FROM sales.orders");
        var typed = At("SELECT o.freig| FROM orders o");

        Assert.Equal(new[] { "o" }, one.DotChain.Select(n => n.Text).ToArray());
        Assert.Equal(new[] { "sales" }, two.DotChain.Select(n => n.Text).ToArray());
        Assert.Equal(new[] { "sales", "orders" }, three.DotChain.Select(n => n.Text).ToArray());
        Assert.Equal(new[] { "o" }, typed.DotChain.Select(n => n.Text).ToArray());
        Assert.Equal("freig", typed.Prefix);
    }

    [Fact]
    public void A_quoted_name_in_a_dot_chain_keeps_its_case()
    {
        var c = At("SELECT \"Orders\".| FROM \"Orders\"");

        var name = Assert.Single(c.DotChain);
        Assert.True(name.Quoted);
        Assert.Equal("Orders", name.Text);
        Assert.False(name.Matches("orders"));
        Assert.True(new AkmlSql.PostgreSql.Completion.PgName("Orders", false).Matches("orders"));
    }

    // ── Aliases, tables and CTEs ────────────────────────────────────────────────────────────────

    [Fact]
    public void Aliases_after_the_caret_are_visible()
    {
        var c = At("SELECT o.| FROM orders o JOIN customers AS c ON c.customer_id = o.customer_id");

        Assert.Equal(new[] { "o", "c" }, Qualifiers(c));
        Assert.Equal("orders", c.VisibleTables[0].Name);
        Assert.Equal("customers", c.VisibleTables[1].Name);
    }

    [Fact]
    public void A_table_without_an_alias_is_qualified_by_its_name_and_schema()
    {
        var c = At("SELECT | FROM sales.orders, customers");

        Assert.Equal(new[] { "orders", "customers" }, Qualifiers(c));
        Assert.Equal("sales", c.VisibleTables[0].Schema);
        Assert.Null(c.VisibleTables[1].Schema);
    }

    [Fact]
    public void Join_words_and_clause_keywords_are_not_taken_for_aliases()
    {
        var c = At("SELECT | FROM orders LEFT JOIN customers ON true WHERE true");

        Assert.Equal(new[] { "orders", "customers" }, Qualifiers(c));
    }

    [Fact]
    public void Functions_and_derived_tables_in_from_are_visible()
    {
        var c = At("SELECT | FROM generate_series(1, 3) g, (SELECT customer_id AS cid, country FROM customers) d");

        Assert.Equal(new[] { "g", "d" }, Qualifiers(c));
        Assert.Equal(PgTableRefKind.Function, c.VisibleTables[0].Kind);
        Assert.Equal(PgTableRefKind.Derived, c.VisibleTables[1].Kind);
        Assert.Equal(new[] { "cid", "country" }, c.VisibleTables[1].Columns.ToArray());
    }

    [Fact]
    public void An_alias_column_list_names_the_columns()
    {
        var c = At("SELECT | FROM unnest(ARRAY[1,2]) AS u(n)");

        var u = Assert.Single(c.VisibleTables);
        Assert.Equal("u", u.Alias);
        Assert.Equal(new[] { "n" }, u.Columns.ToArray());
    }

    [Fact]
    public void A_subquery_sees_its_own_tables_first_and_the_outer_ones_after()
    {
        var c = At("SELECT * FROM customers c WHERE EXISTS (SELECT 1 FROM orders o WHERE o.customer_id = |)");

        Assert.Equal(new[] { "o", "c" }, Qualifiers(c));
    }

    [Fact]
    public void An_outer_query_does_not_see_the_tables_of_a_subquery()
    {
        var c = At("SELECT | FROM customers c WHERE c.customer_id IN (SELECT customer_id FROM orders o)");

        Assert.Equal(new[] { "c" }, Qualifiers(c));
    }

    [Fact]
    public void Ctes_that_are_complete_before_the_caret_are_visible_with_their_columns()
    {
        var c = At("WITH recent AS (SELECT order_id, freight AS cost FROM orders), big (a, b) AS (SELECT 1, 2) SELECT * FROM |");

        Assert.Equal(new[] { "recent", "big" }, c.VisibleCtes.Select(x => x.Name).ToArray());
        Assert.Equal(new[] { "order_id", "cost" }, c.VisibleCtes[0].Columns.ToArray());
        Assert.Equal(new[] { "a", "b" }, c.VisibleCtes[1].Columns.ToArray());
    }

    [Fact]
    public void A_cte_still_being_written_is_not_visible_unless_recursive()
    {
        var plain = At("WITH a AS (SELECT 1 AS x), b AS (SELECT | FROM a) SELECT 1");
        var recursive = At("WITH RECURSIVE tree AS (SELECT 1 AS n UNION ALL SELECT n + 1 FROM |) SELECT * FROM tree");

        Assert.Equal(new[] { "a" }, plain.VisibleCtes.Select(x => x.Name).ToArray());
        Assert.Equal(new[] { "tree" }, recursive.VisibleCtes.Select(x => x.Name).ToArray());
    }

    [Fact]
    public void A_cte_that_selects_star_remembers_what_the_star_covers()
    {
        var c = At("WITH x AS (SELECT * FROM orders o) SELECT x.| FROM x");

        var x = Assert.Single(c.VisibleCtes);
        Assert.Empty(x.Columns);
        var source = Assert.Single(x.StarSources);
        Assert.Equal("orders", source.Name);
    }

    [Fact]
    public void A_data_modifying_cte_exposes_its_returning_list()
    {
        var c = At("WITH ins AS (INSERT INTO orders (order_id) VALUES (1) RETURNING order_id, freight) SELECT | FROM ins");

        var ins = Assert.Single(c.VisibleCtes);
        Assert.Equal(new[] { "order_id", "freight" }, ins.Columns.ToArray());
    }

    [Fact]
    public void Materialized_hints_are_skipped_in_a_cte_definition()
    {
        var c = At("WITH a AS MATERIALIZED (SELECT 1 AS x), b AS NOT MATERIALIZED (SELECT 2 AS y) SELECT * FROM |");

        Assert.Equal(new[] { "a", "b" }, c.VisibleCtes.Select(x => x.Name).ToArray());
    }

    // ── Targets and joins ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_target_of_a_write_is_found()
    {
        Assert.Equal("orders", At("INSERT INTO orders (|").Target!.Name);
        Assert.Equal("orders", At("UPDATE orders o SET |").Target!.Name);
        Assert.Equal("o", At("UPDATE orders o SET |").Target!.Alias);
        Assert.Equal("orders", At("DELETE FROM orders WHERE |").Target!.Name);
        Assert.Equal("orders", At("MERGE INTO orders o USING customers c ON |").Target!.Name);
        Assert.Equal("sales", At("INSERT INTO sales.orders (|").Target!.Schema);
    }

    [Fact]
    public void An_insert_target_is_not_something_the_select_reads_from()
    {
        var c = At("INSERT INTO orders (customer_id) SELECT | FROM customers c");

        Assert.Equal(new[] { "c" }, Qualifiers(c));
        Assert.Equal("orders", c.Target!.Name);
    }

    [Fact]
    public void The_table_being_joined_and_the_ones_before_it_are_known_in_the_on_clause()
    {
        var c = At("SELECT * FROM customers c JOIN orders o ON |");

        Assert.Equal("orders", c.JoinTarget!.Name);
        Assert.Equal(new[] { "customers" }, c.JoinLeft.Select(x => x.Name).ToArray());
    }

    [Fact]
    public void A_later_join_is_the_target_of_the_last_on()
    {
        var c = At("SELECT * FROM customers c JOIN orders o ON o.customer_id = c.customer_id JOIN order_details d ON |");

        Assert.Equal("order_details", c.JoinTarget!.Name);
        Assert.Equal(new[] { "customers", "orders" }, c.JoinLeft.Select(x => x.Name).ToArray());
    }

    [Fact]
    public void A_merge_on_clause_joins_the_source_to_the_target()
    {
        var c = At("MERGE INTO orders o USING customers c ON |");

        Assert.Equal("customers", c.JoinTarget!.Name);
        Assert.Equal(new[] { "orders" }, c.JoinLeft.Select(x => x.Name).ToArray());
    }

    [Fact]
    public void On_conflict_makes_excluded_available()
    {
        Assert.True(At("INSERT INTO orders (a) VALUES (1) ON CONFLICT (a) DO UPDATE SET a = |").ExcludedAvailable);
        Assert.False(At("INSERT INTO orders (a) VALUES (|").ExcludedAvailable);
    }

    [Fact]
    public void An_insert_column_list_belongs_to_the_target()
    {
        var c = At("INSERT INTO sales.orders (order_id, |");

        Assert.Equal("orders", c.ListOwner!.Name);
        Assert.Equal("sales", c.ListOwner.Schema);
    }

    // ── Operands, calls and casts ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SELECT * FROM customers WHERE country |", true)]
    [InlineData("SELECT * FROM customers WHERE country = |", false)]
    [InlineData("SELECT * FROM customers WHERE |", false)]
    [InlineData("SELECT * FROM customers WHERE country = 'UK' |", true)]
    [InlineData("SELECT * FROM customers WHERE id IN (1, 2) |", true)]
    [InlineData("SELECT * FROM customers WHERE a IS NULL |", true)]
    [InlineData("SELECT * FROM customers WHERE a AND |", false)]
    [InlineData("SELECT count(*) |", true)]
    [InlineData("SELECT * |", true)]
    [InlineData("SELECT 1, * |", true)]
    [InlineData("SELECT 2 * |", false)]
    [InlineData("SELECT x::|", false)]
    [InlineData("SELECT o.|", false)]
    [InlineData("SELECT * FROM customers ORDER BY country DESC |", true)]
    public void After_operand(string doc, bool expected) => Assert.Equal(expected, At(doc).AfterOperand);

    [Fact]
    public void Preceding_words_are_the_last_tokens_before_the_word_being_typed()
    {
        var c = At("SELECT * FROM customers ORDER BY name, cou|");

        Assert.Equal(new[] { "CUSTOMERS", "ORDER", "BY", "NAME", "," }, c.PrecedingWords.Skip(1).ToArray());
        Assert.Equal("cou", c.Prefix);
    }

    [Fact]
    public void A_function_call_is_known_with_its_name_and_argument_index()
    {
        var first = At("SELECT date_trunc(|");
        var second = At("SELECT date_trunc('day', |");
        var nested = At("SELECT date_trunc('day', coalesce(a, |");
        var qualified = At("SELECT pg_catalog.date_trunc('day', |");
        var none = At("SELECT * FROM customers WHERE country = |");

        Assert.Equal(("date_trunc", 0), (first.CallName, first.CallArgument));
        Assert.Equal(("date_trunc", 1), (second.CallName, second.CallArgument));
        Assert.Equal(("coalesce", 1), (nested.CallName, nested.CallArgument));
        Assert.Equal(("date_trunc", "pg_catalog", 1), (qualified.CallName, qualified.CallSchema, qualified.CallArgument));
        Assert.Null(none.CallName);
    }

    [Fact]
    public void In_lists_and_groups_are_not_calls()
    {
        Assert.Null(At("SELECT * FROM customers WHERE country IN (|").CallName);
        Assert.Null(At("SELECT * FROM customers WHERE (a = 1 OR |").CallName);
        Assert.Null(At("SELECT * FROM customers WHERE EXISTS (SELECT |").CallName);
        Assert.Null(At("INSERT INTO orders (|").CallName);
    }

    [Fact]
    public void A_column_expression_inside_a_call_keeps_the_clause_of_the_query()
    {
        Assert.Equal(PgClause.SelectList, At("SELECT upper(|").Clause);
        Assert.Equal(PgClause.Where, At("SELECT * FROM customers WHERE lower(|").Clause);
        Assert.Equal(PgClause.OrderBy, At("SELECT * FROM customers ORDER BY coalesce(|").Clause);
    }

    [Theory]
    [InlineData("SELECT created::|")]
    [InlineData("SELECT created::dat|")]
    [InlineData("SELECT CAST(created AS |")]
    [InlineData("SELECT * FROM customers WHERE id = $1 AND created::|")]
    public void Casts_ask_for_a_type(string doc) => Assert.Equal(PgClause.TypePosition, At(doc).Clause);

    // ── Filters, windows, ordering inside parentheses ───────────────────────────────────────────

    [Fact]
    public void Parentheses_of_window_and_filter_keep_a_column_clause()
    {
        Assert.Equal(PgClause.OrderBy, At("SELECT row_number() OVER (|").Clause);
        Assert.Equal(PgClause.OrderBy, At("SELECT row_number() OVER (PARTITION BY country ORDER BY |").Clause);
        Assert.Equal(PgClause.Where, At("SELECT count(*) FILTER (WHERE |").Clause);
        Assert.Equal(PgClause.OrderBy, At("SELECT string_agg(name, ',' ORDER BY |").Clause);
    }

    // ── Dollar quotes, strings and comments ─────────────────────────────────────────────────────

    [Fact]
    public void Inside_a_dollar_quoted_function_body_the_body_is_the_text()
    {
        var c = At("CREATE FUNCTION f() RETURNS int LANGUAGE sql AS $$ SELECT * FROM |; SELECT 2; $$;");

        Assert.True(c.InDollarBody);
        Assert.Equal(PgClause.From, c.Clause);
        Assert.False(c.Suppressed);
    }

    [Fact]
    public void A_body_with_semicolons_and_several_statements_is_split_in_the_body()
    {
        var c = At("CREATE FUNCTION f() RETURNS void LANGUAGE plpgsql AS $body$ BEGIN INSERT INTO orders (a) VALUES (1); SELECT | FROM customers c; END; $body$");

        Assert.True(c.InDollarBody);
        Assert.Equal(PgClause.SelectList, c.Clause);
        Assert.Equal(new[] { "c" }, Qualifiers(c));
    }

    [Fact]
    public void A_do_block_body_is_sql()
    {
        var c = At("DO $$ BEGIN PERFORM 1; DELETE FROM | ; END $$");

        Assert.True(c.InDollarBody);
        Assert.Equal(PgClause.DeleteTarget, c.Clause);
    }

    [Fact]
    public void Prefix_start_inside_a_body_is_a_document_offset()
    {
        var doc = "DO $$ SELECT * FROM cust|";
        var c = At(doc);

        Assert.Equal("cust", c.Prefix);
        Assert.Equal(doc.IndexOf("cust|", StringComparison.Ordinal), c.PrefixStart);
    }

    [Fact]
    public void A_dollar_string_that_is_not_a_body_is_a_string()
    {
        Assert.True(At("SELECT $$ some | text $$").Suppressed);
    }

    [Theory]
    [InlineData("SELECT 'abc|'")]
    [InlineData("SELECT 'abc|")]
    [InlineData("SELECT E'a\\'b|")]
    [InlineData("-- comment |")]
    [InlineData("SELECT 1 /* open | */")]
    [InlineData("SELECT 1 /* open |")]
    [InlineData("SELECT $1|")]
    [InlineData("SELECT 12|")]
    [InlineData("\\d cust|")]
    public void Strings_comments_numbers_and_meta_commands_offer_nothing(string doc) => Assert.True(At(doc).Suppressed);

    [Fact]
    public void Right_after_a_closed_string_or_comment_completion_is_back()
    {
        Assert.False(At("SELECT 'abc'|").Suppressed);
        Assert.False(At("SELECT 1 /* x */|").Suppressed);
        Assert.False(At("-- done\nSELECT |").Suppressed);
    }

    [Fact]
    public void A_nested_block_comment_is_one_comment()
    {
        Assert.True(At("SELECT /* outer /* inner */ still | outer */ 1").Suppressed);
        Assert.False(At("SELECT /* outer /* inner */ done */ |").Suppressed);
    }

    // ── DDL ─────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("CREATE TABLE |", PgClause.DdlName)]
    [InlineData("CREATE TABLE t (|", PgClause.DdlColumnName)]
    [InlineData("CREATE TABLE t (id int, |", PgClause.DdlColumnName)]
    [InlineData("CREATE TABLE t (id |", PgClause.DdlColumnType)]
    [InlineData("CREATE TABLE t (id int, name |", PgClause.DdlColumnType)]
    [InlineData("CREATE TABLE t (id int |", PgClause.Other)]
    [InlineData("CREATE TABLE t (id int REFERENCES |", PgClause.DdlRelation)]
    [InlineData("CREATE TABLE t (id int, PRIMARY KEY (|", PgClause.DdlColumnList)]
    [InlineData("CREATE TABLE t (id int, FOREIGN KEY (id) REFERENCES customers (|", PgClause.DdlColumnList)]
    [InlineData("CREATE INDEX ix ON |", PgClause.DdlRelation)]
    [InlineData("CREATE UNIQUE INDEX ix ON orders (|", PgClause.DdlColumnList)]
    [InlineData("CREATE INDEX ix ON orders USING btree (|", PgClause.DdlColumnList)]
    [InlineData("CREATE VIEW v AS SELECT | FROM orders", PgClause.SelectList)]
    [InlineData("CREATE MATERIALIZED VIEW v AS SELECT * FROM |", PgClause.From)]
    [InlineData("CREATE TABLE t AS SELECT * FROM |", PgClause.From)]
    [InlineData("ALTER TABLE |", PgClause.DdlRelation)]
    [InlineData("DROP TABLE |", PgClause.DdlRelation)]
    [InlineData("DROP TABLE IF EXISTS |", PgClause.DdlRelation)]
    [InlineData("TRUNCATE |", PgClause.DdlRelation)]
    [InlineData("TRUNCATE TABLE |", PgClause.DdlRelation)]
    [InlineData("ALTER TABLE orders ADD COLUMN note |", PgClause.DdlColumnType)]
    [InlineData("CREATE FUNCTION f(a |", PgClause.DdlColumnType)]
    [InlineData("CREATE FUNCTION f(a int, OUT b |", PgClause.DdlColumnType)]
    [InlineData("CREATE FUNCTION f(a int) RETURNS |", PgClause.TypePosition)]
    public void Ddl_positions(string doc, PgClause expected) => Assert.Equal(expected, At(doc).Clause);

    [Fact]
    public void The_columns_a_create_table_declares_are_known_for_its_constraints()
    {
        var c = At("CREATE TABLE t (id int, \"Name\" text, note text, PRIMARY KEY (|");

        Assert.Equal(new[] { "id", "Name", "note" }, c.DeclaredColumns.ToArray());
    }

    [Fact]
    public void An_index_and_a_reference_know_their_relation()
    {
        Assert.Equal("orders", At("CREATE INDEX ix ON orders (|").ListOwner!.Name);
        Assert.Equal("customers", At("CREATE TABLE t (cid int REFERENCES customers (|").ListOwner!.Name);
        Assert.Equal("sales", At("CREATE TABLE t (cid int REFERENCES sales.customers (|").ListOwner!.Schema);
    }

    [Fact]
    public void Explain_is_transparent()
    {
        var c = At("EXPLAIN ANALYZE SELECT * FROM |");

        Assert.Equal(PgClause.From, c.Clause);
    }

    // ── Robustness ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("(((((|")]
    [InlineData(")))))|")]
    [InlineData("SELECT ((( FROM |")]
    [InlineData("SELECT * FROM (SELECT * FROM (SELECT |")]
    [InlineData("WITH |")]
    [InlineData("WITH a AS (|")]
    [InlineData("INSERT INTO |")]
    [InlineData("SELECT '|")]
    [InlineData("SELECT \"|")]
    [InlineData("SELECT $a$ |")]
    [InlineData(";;;|;;;")]
    public void Odd_text_never_throws(string doc)
    {
        var caret = doc.IndexOf('|');
        var sql = caret >= 0 ? doc.Remove(caret, 1) : doc;
        var context = PgCursorContextAnalyzer.Analyze(sql, caret < 0 ? sql.Length : caret);

        Assert.NotNull(context);
    }

    [Fact]
    public void The_caret_may_be_out_of_range()
    {
        Assert.NotNull(PgCursorContextAnalyzer.Analyze("SELECT 1", 100));
        Assert.NotNull(PgCursorContextAnalyzer.Analyze("SELECT 1", -5));
        Assert.NotNull(PgCursorContextAnalyzer.Analyze(null, 0));
    }

    [Fact]
    public void Positional_parameters_and_casts_do_not_confuse_the_clause()
    {
        Assert.Equal(PgClause.Where, At("SELECT * FROM customers WHERE customer_id = $1 AND created::date = |").Clause);
        Assert.Equal(PgClause.OrderBy, At("SELECT * FROM customers WHERE country = $1::text ORDER BY |").Clause);
    }

    [Fact]
    public void Statements_before_and_after_the_caret_do_not_leak_in()
    {
        var c = At("SELECT * FROM orders o; SELECT | FROM customers c; SELECT * FROM suppliers s");

        Assert.Equal(new[] { "c" }, Qualifiers(c));
    }

    [Fact]
    public void Array_subscripts_and_json_operators_keep_the_clause()
    {
        Assert.Equal(PgClause.Where, At("SELECT * FROM t WHERE tags[1] = 'a' AND data->>'k' = |").Clause);
        Assert.Equal(PgClause.SelectList, At("SELECT data->|").Clause);
    }
}
