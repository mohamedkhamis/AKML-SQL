using AkmlSql.Core.Ipc.Messages;
using AkmlSql.PostgreSql.Completion;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Completion;

/// <summary>Spec 042 (task T096, FR-053, FR-063, scenario US3-5) - what hovering a word shows.</summary>
public class PgQuickInfoServiceTests
{
    private static readonly PgQuickInfoService Service = new();

    /// <summary>"|" marks the pointer, which sits on the character after it.</summary>
    private static QuickInfoResponse Hover(string doc, int serverMajor = 18, bool withCache = true)
    {
        var at = doc.IndexOf('|');
        Assert.True(at >= 0, "no pointer marker in: " + doc);
        return Service.Get(doc.Remove(at, 1), at, withCache ? NorthwindPgTestCacheFactory.Create() : null, serverMajor);
    }

    private static string Detail(QuickInfoResponse r, string label) => r.Details.First(d => d.Label == label).Value;

    private static bool Has(QuickInfoResponse r, string label) => r.Details.Any(d => d.Label == label);

    // ── Built-in functions ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_built_in_function_shows_signatures_description_and_the_manual_page()
    {
        var r = Hover("SELECT |string_agg(company_name, ',') FROM customers");

        Assert.Equal("Aggregate", r.ObjectType);
        Assert.Equal("string_agg", r.Header);
        Assert.Equal("Concatenates the non-null input values into a string", r.Description);
        Assert.Contains(r.Details, d => d.Label == "Signature" && d.Value == "string_agg(value text, delimiter text) -> text");
        Assert.Contains(r.Details, d => d.Label == "Signature" && d.Value.EndsWith("-> bytea", StringComparison.Ordinal));
        Assert.Equal("https://www.postgresql.org/docs/18/functions-aggregate.html", Detail(r, "Documentation"));
        Assert.False(Has(r, "Since"));
    }

    [Fact]
    public void A_function_that_arrived_after_the_oldest_release_says_since_when()
    {
        var r = Hover("SELECT |json_exists('{}', '$') FROM customers");

        Assert.Equal("Function", r.ObjectType);
        Assert.Equal("PostgreSQL 17", Detail(r, "Since"));
    }

    [Fact]
    public void A_function_the_server_does_not_have_is_not_described()
    {
        var r = Hover("SELECT |json_exists('{}', '$') FROM customers", serverMajor: 14);

        Assert.Equal(string.Empty, r.ObjectType);
        Assert.Equal(string.Empty, r.Header);
    }

    [Fact]
    public void A_set_returning_function_and_a_window_function_are_named_for_what_they_are()
    {
        Assert.Equal("Window function", Hover("SELECT |row_number() OVER () FROM orders").ObjectType);
        Assert.Equal("Function", Hover("SELECT * FROM |generate_series(1, 3)").ObjectType);
    }

    [Fact]
    public void A_function_written_as_syntax_has_its_manual_signature()
    {
        var r = Hover("SELECT |coalesce(region, 'n/a') FROM customers");

        Assert.Equal("Function", r.ObjectType);
        Assert.Contains(r.Details, d => d.Label == "Signature" && d.Value.StartsWith("coalesce(value", StringComparison.Ordinal));
    }

    [Fact]
    public void A_schema_qualified_built_in_is_found_under_pg_catalog()
    {
        var r = Hover("SELECT pg_catalog.|lower(company_name) FROM customers");

        Assert.Equal("lower", r.Header);
    }

    // ── User-defined functions and procedures ──────────────────────────────────────────────────

    [Fact]
    public void A_user_function_shows_its_schema_and_parameters()
    {
        var r = Hover("SELECT |order_total(1)");

        Assert.Equal("Function", r.ObjectType);
        Assert.Equal("public.order_total", r.Header);
        Assert.Equal("public", Detail(r, "Schema"));
        Assert.Equal("order_total(order_id integer)", Detail(r, "Signature"));
    }

    [Fact]
    public void A_procedure_in_another_schema_is_found_by_its_qualified_name()
    {
        var r = Hover("CALL sales.|mark_invoice_paid(1)");

        Assert.Equal("Procedure", r.ObjectType);
        Assert.Equal("sales.mark_invoice_paid", r.Header);
    }

    // ── Tables, views, columns ──────────────────────────────────────────────────────────────────

    [Fact]
    public void A_table_shows_its_schema_its_columns_and_its_key()
    {
        var r = Hover("SELECT * FROM |customers");

        Assert.Equal("Table", r.ObjectType);
        Assert.Equal("public.customers", r.Header);
        Assert.Equal("public", Detail(r, "Schema"));
        Assert.Equal("11", Detail(r, "Columns"));
        Assert.Equal("customer_id", Detail(r, "Primary key"));
        Assert.StartsWith("customer_id text (PK)", Detail(r, "Column list"));
        Assert.Contains("company_name text NOT NULL", Detail(r, "Column list"));
    }

    [Fact]
    public void A_long_column_list_is_cut_and_says_how_many_more()
    {
        var r = Hover("SELECT * FROM |employees");

        Assert.Equal("15", Detail(r, "Columns"));
        var lines = Detail(r, "Column list").Split((char)10);
        Assert.Equal(15, lines.Length);                       // 14 columns and the note
        Assert.Equal("... and 1 more", lines[^1]);
        Assert.DoesNotContain("reports_to", Detail(r, "Column list"));
    }

    [Fact]
    public void A_view_a_materialized_view_and_a_sequence_are_named_by_kind()
    {
        Assert.Equal("View", Hover("SELECT * FROM |vw_customer_orders").ObjectType);
        Assert.Equal("Materialized View", Hover("SELECT * FROM |mv_product_sales").ObjectType);
        Assert.Equal("Sequence", Hover("SELECT * FROM |orders_order_id_seq").ObjectType);
    }

    [Fact]
    public void An_alias_says_which_table_it_stands_for()
    {
        var r = Hover("SELECT * FROM customers c WHERE |c.country = 'UK'");

        Assert.Equal("c (alias for public.customers)", r.Header);
        Assert.Equal("Table", r.ObjectType);
    }

    [Fact]
    public void A_column_shows_its_type_nullability_and_key()
    {
        var r = Hover("SELECT * FROM customers c WHERE c.|customer_id = 'A'");

        Assert.Equal("Column", r.ObjectType);
        Assert.Equal("c.customer_id (public.customers)", r.Header);
        Assert.Equal("text", Detail(r, "Type"));
        Assert.Equal("no", Detail(r, "Nullable"));
        Assert.Equal("yes", Detail(r, "Primary key"));
    }

    [Fact]
    public void A_foreign_key_column_says_what_it_references()
    {
        var r = Hover("SELECT o.|customer_id FROM orders o");

        Assert.Equal("public.customers(customer_id) - orders_customer_id_fkey", Detail(r, "References"));
    }

    [Fact]
    public void A_bare_column_name_is_found_in_the_tables_of_the_query()
    {
        var r = Hover("SELECT |freight FROM orders");

        Assert.Equal("Column", r.ObjectType);
        Assert.Equal("numeric(10,2)", Detail(r, "Type"));
    }

    [Fact]
    public void A_schema_qualified_table_and_the_schema_itself_are_described()
    {
        Assert.Equal("sales.orders", Hover("SELECT * FROM sales.|orders").Header);
        var schema = Hover("SELECT * FROM |sales.orders");
        Assert.Equal("Schema", schema.ObjectType);
        Assert.Equal("3", Detail(schema, "Objects"));   // orders, invoices and the mark_invoice_paid procedure
        Assert.Equal("no", Detail(schema, "On the search path"));
    }

    [Fact]
    public void A_table_with_a_name_that_needs_quotes_is_found()
    {
        var r = Hover("SELECT * FROM |\"Audit Log\"");

        Assert.Equal("public.Audit Log", r.Header);
    }

    [Fact]
    public void A_cte_and_a_derived_table_list_their_columns()
    {
        var cte = Hover("WITH x AS (SELECT order_id AS id FROM orders) SELECT * FROM |x");
        var derived = Hover("SELECT * FROM (SELECT customer_id AS cid FROM customers) |d");

        Assert.Equal("CTE", cte.ObjectType);
        Assert.Equal("id", Detail(cte, "Columns"));
        Assert.Equal("Subquery", derived.ObjectType);
        Assert.Equal("cid", Detail(derived, "Columns"));
    }

    [Fact]
    public void Without_a_schema_the_names_in_the_statement_still_describe_themselves()
    {
        var r = Hover("SELECT * FROM customers |c", withCache: false);

        Assert.Equal("Table", r.ObjectType);
        Assert.Contains("customers", r.Header);
    }

    // ── Commands, key words, types, operators ───────────────────────────────────────────────────

    [Fact]
    public void A_command_shows_its_syntax_and_the_manual_page()
    {
        var r = Hover("|CREATE TABLE t (id int)");

        Assert.Equal("Command", r.ObjectType);
        Assert.Equal("CREATE TABLE", r.Header);
        Assert.StartsWith("CREATE ", Detail(r, "Syntax"));
        Assert.Equal("https://www.postgresql.org/docs/18/sql-createtable.html", Detail(r, "Documentation"));
        Assert.Equal("define a new table", r.Description);
    }

    [Fact]
    public void A_one_word_command_at_the_start_of_a_statement_is_the_command()
    {
        var r = Hover("|SELECT * FROM customers");

        Assert.Equal("Command", r.ObjectType);
        Assert.Equal("SELECT", r.Header);
        Assert.Contains("FROM", Detail(r, "Syntax"));
    }

    [Fact]
    public void A_command_newer_than_the_oldest_release_says_since_when()
    {
        var r = Hover("|MERGE INTO orders o USING customers c ON true WHEN MATCHED THEN DELETE");

        Assert.Equal("Command", r.ObjectType);
        Assert.Equal("PostgreSQL 15", Detail(r, "Since"));
    }

    [Fact]
    public void A_key_word_says_how_it_may_be_used()
    {
        var r = Hover("SELECT * FROM customers c, |LATERAL (SELECT 1) l");

        Assert.Equal("Keyword", r.ObjectType);
        Assert.Equal("LATERAL", r.Header);
        Assert.Contains("reserved", Detail(r, "Kind"));
    }

    [Fact]
    public void A_type_in_a_type_position_is_a_type_with_its_aliases()
    {
        var column = Hover("CREATE TABLE t (id |integer)");
        var cast = Hover("SELECT x::|timestamptz");

        Assert.Equal("Type", column.ObjectType);
        Assert.Equal("int, int4", Detail(column, "Also written"));
        Assert.Equal("Type", cast.ObjectType);
        Assert.Equal("timestamp with time zone", cast.Header);
    }

    [Fact]
    public void An_operator_shows_its_forms()
    {
        var r = Hover("SELECT data |->> 'a' FROM customers");

        Assert.Equal("Operator", r.ObjectType);
        Assert.Equal("->>", r.Header);
        Assert.Contains(r.Details, d => d.Label == "Form" && d.Value.Contains("jsonb ->> text", StringComparison.Ordinal));
        Assert.Contains("functions-json", Detail(r, "Documentation"));
    }

    [Fact]
    public void The_cast_operator_is_described_as_syntax()
    {
        var r = Hover("SELECT x|::int");

        Assert.Equal("Syntax", r.ObjectType);
        Assert.Equal("::", r.Header);
    }

    // ── Nothing to say ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SELECT |  1")]
    [InlineData("SELECT 1|23")]
    [InlineData("SELECT 'te|xt'")]
    [InlineData("-- no|te")]
    [InlineData("SELECT * FROM customers WHERE |nonexistent_word_here = 1")]
    public void Where_there_is_nothing_to_describe_the_response_is_empty(string doc)
    {
        var r = Hover(doc);

        Assert.Equal(string.Empty, r.ObjectType);
        Assert.Equal(string.Empty, r.Header);
        Assert.Empty(r.Details);
    }

    [Fact]
    public void Odd_text_and_out_of_range_offsets_never_throw()
    {
        Assert.NotNull(Service.Get(null, 0, null));
        Assert.NotNull(Service.Get("SELECT", 99, null));
        Assert.NotNull(Service.Get("(((", 1, null));
        Assert.NotNull(Service.Get("SELECT a..b FROM", 8, NorthwindPgTestCacheFactory.Create()));
    }
}
