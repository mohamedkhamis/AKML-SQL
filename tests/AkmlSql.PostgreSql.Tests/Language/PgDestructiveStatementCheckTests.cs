using AkmlSql.Core.Models.Safety;
using AkmlSql.PostgreSql.Safety;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Language;

/// <summary>Spec 042 (task T096, T105, FR-069) - DROP, TRUNCATE, and DELETE or UPDATE with no WHERE.</summary>
public class PgDestructiveStatementCheckTests
{
    private static SafetyWarningType[] Types(string sql, string? server = null, bool production = false) =>
        PgDestructiveStatementCheck.Analyze(sql, server, production).Select(w => (SafetyWarningType)w.WarningType).ToArray();

    [Fact]
    public void Delete_without_where_is_flagged_with_the_table()
    {
        var w = Assert.Single(PgDestructiveStatementCheck.Analyze("DELETE FROM public.orders;"));

        Assert.Equal((int)SafetyWarningType.DeleteWithoutWhere, w.WarningType);
        Assert.Equal("public.orders", w.ObjectName);
        Assert.Equal(2, w.Severity);
        Assert.Contains("ALL rows", w.Message);
    }

    [Fact]
    public void Delete_with_where_is_not()
    {
        Assert.Empty(Types("DELETE FROM orders WHERE order_id = 1;"));
        Assert.Empty(Types("DELETE FROM orders o USING customers c WHERE o.customer_id = c.customer_id"));
        Assert.Empty(Types("DELETE FROM orders WHERE CURRENT OF cur"));
    }

    [Fact]
    public void Update_without_where_is_flagged_and_with_where_is_not()
    {
        Assert.Equal(new[] { SafetyWarningType.UpdateWithoutWhere }, Types("UPDATE orders SET freight = 0"));
        Assert.Equal(new[] { SafetyWarningType.UpdateWithoutWhere }, Types("UPDATE ONLY sales.orders AS o SET total = 0 RETURNING *"));
        Assert.Empty(Types("UPDATE orders SET freight = 0 WHERE order_id = 1"));
    }

    [Fact]
    public void Update_inside_on_conflict_or_for_update_is_not_an_update_statement()
    {
        Assert.Empty(Types("INSERT INTO orders (order_id) VALUES (1) ON CONFLICT (order_id) DO UPDATE SET freight = 1"));
        Assert.Empty(Types("SELECT * FROM orders FOR UPDATE"));
        Assert.Empty(Types("SELECT * FROM orders FOR NO KEY UPDATE"));
        Assert.Empty(Types("CREATE TABLE t (a int REFERENCES x (id) ON UPDATE CASCADE ON DELETE CASCADE)"));
        Assert.Empty(Types("MERGE INTO orders o USING customers c ON o.customer_id = c.customer_id WHEN MATCHED THEN UPDATE SET freight = 1 WHEN NOT MATCHED THEN DELETE"));
    }

    [Fact]
    public void Drop_table_database_and_schema_are_serious()
    {
        var table = Assert.Single(PgDestructiveStatementCheck.Analyze("DROP TABLE IF EXISTS sales.invoices CASCADE;"));
        var database = Assert.Single(PgDestructiveStatementCheck.Analyze("DROP DATABASE northwind"));
        var schema = Assert.Single(PgDestructiveStatementCheck.Analyze("DROP SCHEMA sales CASCADE"));

        Assert.Equal((int)SafetyWarningType.DropTable, table.WarningType);
        Assert.Equal("sales.invoices", table.ObjectName);
        Assert.Equal(2, table.Severity);
        Assert.Equal((int)SafetyWarningType.DropDatabase, database.WarningType);
        Assert.Equal("northwind", database.ObjectName);
        Assert.Equal(2, schema.Severity);
    }

    [Fact]
    public void Other_drops_are_flagged_with_a_lower_severity()
    {
        var view = Assert.Single(PgDestructiveStatementCheck.Analyze("DROP VIEW vw_customer_orders"));
        var matview = Assert.Single(PgDestructiveStatementCheck.Analyze("DROP MATERIALIZED VIEW mv_product_sales"));
        var function = Assert.Single(PgDestructiveStatementCheck.Analyze("DROP FUNCTION order_total(int)"));

        Assert.Equal(1, view.Severity);
        Assert.Contains("DROP VIEW", view.Message);
        Assert.Equal("mv_product_sales", matview.ObjectName);
        Assert.Equal(1, function.Severity);
    }

    [Fact]
    public void Truncate_is_flagged_with_the_table()
    {
        var w = Assert.Single(PgDestructiveStatementCheck.Analyze("TRUNCATE TABLE ONLY orders RESTART IDENTITY CASCADE"));

        Assert.Equal((int)SafetyWarningType.TruncateTable, w.WarningType);
        Assert.Equal("orders", w.ObjectName);
        Assert.Equal((int)SafetyWarningType.TruncateTable, Assert.Single(PgDestructiveStatementCheck.Analyze("TRUNCATE orders, order_details")).WarningType);
    }

    [Fact]
    public void Every_statement_of_a_script_is_checked()
    {
        var types = Types("DELETE FROM a; UPDATE b SET x = 1; DROP TABLE c; SELECT 1; DELETE FROM d WHERE x = 1;");

        Assert.Equal(new[] { SafetyWarningType.DeleteWithoutWhere, SafetyWarningType.UpdateWithoutWhere, SafetyWarningType.DropTable }, types);
    }

    [Fact]
    public void A_data_modifying_cte_is_checked()
    {
        Assert.Equal(new[] { SafetyWarningType.DeleteWithoutWhere }, Types("WITH d AS (DELETE FROM orders RETURNING *) SELECT count(*) FROM d"));
        Assert.Empty(Types("WITH d AS (DELETE FROM orders WHERE order_id = 1 RETURNING *) SELECT count(*) FROM d"));
    }

    [Fact]
    public void A_dollar_quoted_function_body_is_not_a_statement_of_the_script()
    {
        Assert.Empty(Types("CREATE FUNCTION wipe() RETURNS void LANGUAGE sql AS $$ DELETE FROM orders; TRUNCATE orders; $$;"));
        Assert.Empty(Types("DO $body$ BEGIN DELETE FROM orders; END $body$"));
    }

    [Fact]
    public void Text_in_strings_and_comments_is_not_a_statement()
    {
        Assert.Empty(Types("SELECT 'DELETE FROM orders'"));
        Assert.Empty(Types("-- DROP TABLE orders\nSELECT 1"));
        Assert.Empty(Types("/* TRUNCATE orders */ SELECT 1"));
        Assert.Empty(Types("COMMENT ON TABLE orders IS 'DROP TABLE it'"));
    }

    [Fact]
    public void A_production_server_adds_a_warning_for_data_and_for_schema_changes()
    {
        var dml = Types("INSERT INTO orders (order_id) VALUES (1)", "prod-db", production: true);
        var ddl = Types("CREATE TABLE t (id int)", "prod-db", production: true);
        var warning = PgDestructiveStatementCheck.Analyze("CREATE TABLE t (id int)", "prod-db", true).Single();

        Assert.Equal(new[] { SafetyWarningType.ProductionDml }, dml);
        Assert.Equal(new[] { SafetyWarningType.ProductionDdl }, ddl);
        Assert.Contains("'prod-db'", warning.Message);
        Assert.Empty(Types("SELECT 1", "prod-db", production: true));
        Assert.Empty(Types("INSERT INTO orders (order_id) VALUES (1)", "dev-db", production: false));
    }

    [Fact]
    public void Empty_and_odd_input_never_throws_and_has_no_warnings()
    {
        Assert.Empty(PgDestructiveStatementCheck.Analyze(null));
        Assert.Empty(PgDestructiveStatementCheck.Analyze("   "));
        Assert.Empty(PgDestructiveStatementCheck.Analyze("\\d customers"));
        Assert.NotNull(PgDestructiveStatementCheck.Analyze("DELETE"));
        Assert.NotNull(PgDestructiveStatementCheck.Analyze("DROP"));
        Assert.NotNull(PgDestructiveStatementCheck.Analyze("(((( DELETE FROM"));
    }

    [Fact]
    public void T_sql_only_forms_are_not_postgresql_statements()
    {
        // GO is a client batch word in SQL Server; PostgreSQL has none, so it is just an unknown statement here
        Assert.Empty(Types("SELECT 1\nGO\nSELECT 2"));
    }
}
