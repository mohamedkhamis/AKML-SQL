using AkmlSql.Core.Ipc.Messages;
using AkmlSql.PostgreSql.Completion;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Completion;

/// <summary>Spec 042 (task T096, FR-067) - SELECT * to the column list, with PostgreSQL quoting.</summary>
public class PgWildcardExpanderTests
{
    private static readonly PgWildcardExpander Expander = new();

    /// <summary>"|" marks the caret, normally right after the star.</summary>
    private static WildcardExpansionResponse Expand(string doc, string? qualifier = null, bool withCache = true)
    {
        var caret = doc.IndexOf('|');
        Assert.True(caret >= 0, "no caret marker in: " + doc);
        return Expander.Handle(doc.Remove(caret, 1), caret, qualifier, withCache ? NorthwindPgTestCacheFactory.Create() : null);
    }

    private static string[] Names(WildcardTableGroup g) => g.Columns.Select(c => c.ColumnName).ToArray();

    [Fact]
    public void A_star_over_one_table_gives_that_tables_columns_in_order()
    {
        var r = Expand("SELECT *| FROM customers");

        Assert.True(r.Success);
        var group = Assert.Single(r.Tables);
        Assert.Equal("public.customers", group.TableName);
        Assert.Equal("customers", group.Qualifier);
        Assert.Equal(11, group.Columns.Length);
        Assert.Equal("customer_id", group.Columns[0].ColumnName);
        Assert.Equal("text", group.Columns[0].TypeDisplay);
        Assert.Equal("fax", group.Columns[^1].ColumnName);
    }

    [Fact]
    public void A_star_over_a_join_gives_a_group_per_table_in_the_order_of_the_query()
    {
        var r = Expand("SELECT *| FROM customers c JOIN orders o ON o.customer_id = c.customer_id");

        Assert.True(r.Success);
        Assert.Equal(new[] { "c", "o" }, r.Tables.Select(t => t.Qualifier).ToArray());
        Assert.Equal(new[] { 11, 14 }, r.Tables.Select(t => t.Columns.Length).ToArray());
    }

    [Fact]
    public void A_qualified_star_expands_only_that_table()
    {
        var r = Expand("SELECT c.*| FROM customers c JOIN orders o ON o.customer_id = c.customer_id");

        var group = Assert.Single(r.Tables);
        Assert.Equal("c", group.Qualifier);
        Assert.Contains("company_name", Names(group));
        Assert.DoesNotContain("freight", Names(group));
    }

    [Fact]
    public void The_qualifier_may_be_given_by_the_caller()
    {
        var r = Expand("SELECT *| FROM customers c JOIN orders o ON o.customer_id = c.customer_id", qualifier: "o");

        var group = Assert.Single(r.Tables);
        Assert.Equal("o", group.Qualifier);
        Assert.Contains("freight", Names(group));
    }

    [Fact]
    public void A_name_that_needs_quotes_comes_back_quoted_and_the_rest_do_not()
    {
        var r = Expand("SELECT *| FROM \"Audit Log\"");

        var group = Assert.Single(r.Tables);
        Assert.Equal("\"Audit Log\"", group.Qualifier);
        Assert.Equal(new[] { "log_id", "\"Event Name\"", "logged_at" }, Names(group));
    }

    [Fact]
    public void A_star_over_a_derived_table_or_a_cte_expands_its_output_columns()
    {
        var derived = Expand("SELECT *| FROM (SELECT customer_id AS cid, country FROM customers) d");
        var cte = Expand("WITH x AS (SELECT * FROM customers) SELECT *| FROM x");

        Assert.Equal(new[] { "cid", "country" }, Names(Assert.Single(derived.Tables)));
        var group = Assert.Single(cte.Tables);
        Assert.Equal("x", group.Qualifier);
        Assert.Equal(11, group.Columns.Length);
    }

    [Fact]
    public void Returning_star_expands_the_target()
    {
        var r = Expand("INSERT INTO orders (order_id) VALUES (1) RETURNING *|");

        var group = Assert.Single(r.Tables);
        Assert.Equal("public.orders", group.TableName);
        Assert.Equal(14, group.Columns.Length);
    }

    [Theory]
    [InlineData("SELECT count(*|) FROM customers")]
    [InlineData("SELECT 2 *| 3 FROM customers")]
    [InlineData("SELECT * FROM customers WHERE |x = 1")]
    [InlineData("SELECT 'a *|' FROM customers")]
    [InlineData("-- *|")]
    public void A_star_that_is_not_a_column_wildcard_is_not_expanded(string doc)
    {
        Assert.False(Expand(doc).Success);
    }

    [Fact]
    public void Without_a_schema_nothing_is_expanded_and_the_reply_says_why()
    {
        var r = Expand("SELECT *| FROM customers", withCache: false);

        Assert.False(r.Success);
        Assert.Contains("schema", r.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unknown_table_has_nothing_to_expand()
    {
        var r = Expand("SELECT *| FROM no_such_table");

        Assert.False(r.Success);
        Assert.NotNull(r.ErrorMessage);
    }

    [Fact]
    public void An_unknown_qualifier_says_so()
    {
        var r = Expand("SELECT x.*| FROM customers c");

        Assert.False(r.Success);
        Assert.Contains("'x'", r.ErrorMessage);
    }

    [Fact]
    public void The_caret_may_be_on_the_star_as_well_as_after_it()
    {
        Assert.True(Expand("SELECT |* FROM customers").Success);
        Assert.True(Expand("SELECT *| FROM customers").Success);
    }

    // Spec 043 follow-up (review): the web editor sends a quoted qualifier unquoted ('"C".*' as C); the text before the star
    // says it was quoted, so it keeps its case instead of folding to c.
    [Fact]
    public void A_quoted_qualifier_sent_unquoted_keeps_its_case()
    {
        var r = Expand("SELECT \"C\".*| FROM customers AS \"C\" JOIN orders o ON o.customer_id = \"C\".customer_id", qualifier: "C");

        Assert.True(r.Success, r.ErrorMessage);
        var group = Assert.Single(r.Tables);
        Assert.Equal("\"C\"", group.Qualifier);
        Assert.Contains("company_name", Names(group));
        Assert.DoesNotContain("freight", Names(group));
    }

    [Fact]
    public void An_unquoted_qualifier_still_folds_to_lower_case()
    {
        Assert.True(Expand("SELECT C.*| FROM customers c", qualifier: "C").Success);
        Assert.False(Expand("SELECT C.*| FROM customers AS \"C\"", qualifier: "C").Success);
    }

    [Fact]
    public void A_quoted_qualifier_with_a_doubled_quote_inside_is_read_whole()
    {
        var r = Expand("SELECT \"a\"\"b\".*| FROM customers AS \"a\"\"b\"");

        Assert.True(r.Success, r.ErrorMessage);
        Assert.Contains("company_name", Names(Assert.Single(r.Tables)));
    }

    [Fact]
    public void Odd_text_never_throws()
    {
        Assert.False(Expander.Handle(null, 0, null, null).Success);
        Assert.False(Expander.Handle("*", 5, null, NorthwindPgTestCacheFactory.Create()).Success);
        Assert.NotNull(Expander.Handle("SELECT ((( * FROM", 11, null, NorthwindPgTestCacheFactory.Create()));
    }
}
