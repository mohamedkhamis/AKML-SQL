using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.PostgreSql;
using Xunit;

namespace AkmlSql.Engine.Tests.PostgreSql;

/// <summary>
/// Spec 042 (task T134, FR-131, FR-132, research R11). One grid edit becomes one parameterised PostgreSQL statement: the values
/// are parameters (never text in the statement), each cast to the column's own type so the server reads them as it displays them,
/// identifiers are quoted only when they need it, a NULL key is IS NULL, and a write with no key is refused so a full-table write
/// can never be produced. The cast type is the one part of the statement that is text, so it must pass a strict grammar.
/// </summary>
public class PgCrudWriteGeneratorTests
{
    private static ApplyChangesRequest Request(string schema = "public", string table = "orders", params string[] returning) =>
        new() { SessionId = "s", BaseSchema = schema, BaseTable = table, ReturnColumns = returning.Length == 0 ? null : returning };

    private static CrudCellDto Cell(string column, string? value, string type) =>
        new() { BaseColumnName = column, Value = value, TypeName = type };

    private static CrudEditDto Update(CrudCellDto[] set, CrudCellDto[] key) => new() { Op = CrudOp.Update, SetCells = set, KeyCells = key };

    // ---- UPDATE ------------------------------------------------------------------------------------------------

    [Fact]
    public void An_update_sets_by_parameter_with_casts_and_is_keyed_by_the_original_key()
    {
        var edit = Update(
            new[] { Cell("freight", "32.50", "numeric(10,2)"), Cell("note", "rush", "text") },
            new[] { Cell("order_id", "10248", "integer") });

        var statement = PgCrudWriteGenerator.Build(Request(), edit);

        Assert.Equal("UPDATE public.orders SET freight = $1::numeric(10,2), note = $2::text WHERE order_id = $3::integer", statement.Sql);
        Assert.Equal(new string?[] { "32.50", "rush", "10248" }, statement.Parameters.Select(p => p.Value).ToArray());
    }

    [Fact]
    public void A_composite_key_is_every_column_joined_with_AND()
    {
        var edit = Update(
            new[] { Cell("quantity", "3", "smallint") },
            new[] { Cell("order_id", "10248", "integer"), Cell("product_id", "11", "integer") });

        var statement = PgCrudWriteGenerator.Build(Request(table: "order_details"), edit);

        Assert.Equal("UPDATE public.order_details SET quantity = $1::smallint WHERE order_id = $2::integer AND product_id = $3::integer", statement.Sql);
    }

    [Fact]
    public void A_NULL_key_cell_is_IS_NULL_and_takes_no_parameter()
    {
        var edit = Update(
            new[] { Cell("note", "x", "text") },
            new[] { Cell("a", "1", "integer"), Cell("b", null, "text") });

        var statement = PgCrudWriteGenerator.Build(Request(), edit);

        Assert.Equal("UPDATE public.orders SET note = $1::text WHERE a = $2::integer AND b IS NULL", statement.Sql);
        Assert.Equal(2, statement.Parameters.Count);
    }

    [Fact]
    public void Setting_a_value_to_NULL_is_a_NULL_parameter_still_cast_to_the_type()
    {
        var statement = PgCrudWriteGenerator.Build(Request(), Update(new[] { Cell("note", null, "text") }, new[] { Cell("order_id", "1", "integer") }));

        Assert.Equal("UPDATE public.orders SET note = $1::text WHERE order_id = $2::integer", statement.Sql);
        Assert.Null(statement.Parameters[0].Value);
        Assert.Equal("text", statement.Parameters[0].TypeName);
    }

    [Fact]
    public void A_value_that_looks_like_SQL_is_a_parameter_and_never_part_of_the_statement()
    {
        var evil = "x'; DROP TABLE orders; --";

        var statement = PgCrudWriteGenerator.Build(Request(), Update(new[] { Cell("note", evil, "text") }, new[] { Cell("order_id", "1", "integer") }));

        Assert.DoesNotContain("DROP", statement.Sql);
        Assert.Equal(evil, statement.Parameters[0].Value);
    }

    // ---- quoting -----------------------------------------------------------------------------------------------

    [Fact]
    public void Names_are_quoted_only_when_they_need_it()
    {
        var edit = Update(
            new[] { Cell("Unit Price", "9.5", "numeric"), Cell("user", "bob", "text"), Cell("plain_name", "p", "text"), Cell("say \"hi\"", "q", "text") },
            new[] { Cell("Order ID", "7", "integer") });

        var statement = PgCrudWriteGenerator.Build(Request(schema: "Sales", table: "Order Details"), edit);

        Assert.Equal(
            "UPDATE \"Sales\".\"Order Details\" SET \"Unit Price\" = $1::numeric, \"user\" = $2::text, plain_name = $3::text, \"say \"\"hi\"\"\" = $4::text WHERE \"Order ID\" = $5::integer",
            statement.Sql);
    }

    [Fact]
    public void A_table_without_a_schema_is_written_unqualified()
    {
        var statement = PgCrudWriteGenerator.Build(Request(schema: ""), Update(new[] { Cell("n", "1", "integer") }, new[] { Cell("k", "1", "integer") }));

        Assert.StartsWith("UPDATE orders SET", statement.Sql);
    }

    // ---- INSERT ------------------------------------------------------------------------------------------------

    [Fact]
    public void An_insert_lists_the_columns_and_casts_each_value()
    {
        var edit = new CrudEditDto { Op = CrudOp.Insert, SetCells = new[] { Cell("customer_id", "ALFKI", "character(5)"), Cell("freight", "1", "numeric(10,2)") } };

        var statement = PgCrudWriteGenerator.Build(Request(), edit);

        Assert.Equal("INSERT INTO public.orders (customer_id, freight) VALUES ($1::character(5), $2::numeric(10,2))", statement.Sql);
        Assert.False(statement.ReturnsRow);
    }

    [Fact]
    public void An_insert_returns_the_written_row_by_the_columns_the_grid_names()
    {
        var edit = new CrudEditDto { Op = CrudOp.Insert, SetCells = new[] { Cell("customer_id", "ALFKI", "character(5)") } };

        var statement = PgCrudWriteGenerator.Build(Request("public", "orders", "order_id", "customer_id", "freight"), edit);

        Assert.Equal("INSERT INTO public.orders (customer_id) VALUES ($1::character(5)) RETURNING order_id, customer_id, freight", statement.Sql);
        Assert.True(statement.ReturnsRow);
        Assert.Equal(new[] { "order_id", "customer_id", "freight" }, statement.ReturnedColumns);
    }

    [Fact]
    public void An_update_returns_the_written_row_too_so_what_a_trigger_changed_is_shown()
    {
        var edit = Update(new[] { Cell("freight", "5", "numeric(10,2)") }, new[] { Cell("order_id", "1", "integer") });

        var statement = PgCrudWriteGenerator.Build(Request("public", "orders", "order_id", "freight"), edit);

        Assert.Equal("UPDATE public.orders SET freight = $1::numeric(10,2) WHERE order_id = $2::integer RETURNING order_id, freight", statement.Sql);
        Assert.True(statement.ReturnsRow);
    }

    [Fact]
    public void The_returned_columns_are_quoted_like_any_other_name_and_a_name_cannot_end_the_list()
    {
        var edit = new CrudEditDto { Op = CrudOp.Insert, SetCells = new[] { Cell("n", "1", "integer") } };

        var statement = PgCrudWriteGenerator.Build(Request("public", "orders", "Order ID", "x\" ; DROP TABLE orders; --"), edit);

        Assert.EndsWith("RETURNING \"Order ID\", \"x\"\" ; DROP TABLE orders; --\"", statement.Sql);
    }

    [Fact]
    public void A_delete_returns_nothing()
    {
        var edit = new CrudEditDto { Op = CrudOp.Delete, KeyCells = new[] { Cell("order_id", "1", "integer") } };

        var statement = PgCrudWriteGenerator.Build(Request("public", "orders", "order_id"), edit);

        Assert.False(statement.ReturnsRow);
        Assert.DoesNotContain("RETURNING", statement.Sql);
    }

    // ---- DELETE ------------------------------------------------------------------------------------------------

    [Fact]
    public void A_delete_is_keyed_by_the_original_key()
    {
        var edit = new CrudEditDto { Op = CrudOp.Delete, KeyCells = new[] { Cell("order_id", "10248", "integer") } };

        var statement = PgCrudWriteGenerator.Build(Request(), edit);

        Assert.Equal("DELETE FROM public.orders WHERE order_id = $1::integer", statement.Sql);
        Assert.Equal(new string?[] { "10248" }, statement.Parameters.Select(p => p.Value).ToArray());
    }

    // ---- what is refused ---------------------------------------------------------------------------------------

    [Fact]
    public void A_keyless_update_or_delete_is_refused_so_a_whole_table_is_never_written()
    {
        var update = Update(new[] { Cell("n", "1", "integer") }, Array.Empty<CrudCellDto>());
        var delete = new CrudEditDto { Op = CrudOp.Delete };

        var u = Assert.Throws<InvalidOperationException>(() => PgCrudWriteGenerator.Build(Request(), update));
        var d = Assert.Throws<InvalidOperationException>(() => PgCrudWriteGenerator.Build(Request(), delete));

        Assert.Contains("key", u.Message);
        Assert.Contains("key", d.Message);
    }

    [Fact]
    public void An_update_with_nothing_to_set_and_an_insert_with_no_values_are_refused()
    {
        Assert.Throws<InvalidOperationException>(() => PgCrudWriteGenerator.Build(Request(), Update(Array.Empty<CrudCellDto>(), new[] { Cell("k", "1", "integer") })));
        Assert.Throws<InvalidOperationException>(() => PgCrudWriteGenerator.Build(Request(), new CrudEditDto { Op = CrudOp.Insert }));
    }

    [Fact]
    public void A_missing_table_or_an_unknown_operation_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => PgCrudWriteGenerator.Build(Request(table: ""), Update(new[] { Cell("n", "1", "integer") }, new[] { Cell("k", "1", "integer") })));
        Assert.Throws<InvalidOperationException>(() => PgCrudWriteGenerator.Build(Request(), new CrudEditDto { Op = 99 }));
    }

    [Fact]
    public void A_cell_without_a_type_is_refused_because_the_cast_needs_one()
    {
        var edit = Update(new[] { new CrudCellDto { BaseColumnName = "n", Value = "1", TypeName = null } }, new[] { Cell("k", "1", "integer") });

        var ex = Assert.Throws<InvalidOperationException>(() => PgCrudWriteGenerator.Build(Request(), edit));

        Assert.Contains("n", ex.Message);
    }

    // ---- the cast type is text in the statement, so it is checked -----------------------------------------------

    [Theory]
    [InlineData("integer")]
    [InlineData("bigint")]
    [InlineData("numeric(10,2)")]
    [InlineData("numeric(10, 2)")]
    [InlineData("character varying(40)")]
    [InlineData("character(5)")]
    [InlineData("timestamp without time zone")]
    [InlineData("timestamp(3) with time zone")]
    [InlineData("time with time zone")]
    [InlineData("double precision")]
    [InlineData("integer[]")]
    [InlineData("text[][]")]
    [InlineData("character varying(10)[]")]
    [InlineData("bytea")]
    [InlineData("jsonb")]
    [InlineData("uuid")]
    [InlineData("interval day to second(3)")]
    [InlineData("public.mood")]
    [InlineData("\"Sales\".\"order status\"")]
    [InlineData("\"odd\"\"name\"")]
    [InlineData("\"semi;colon\"")]
    public void Real_type_names_pass(string type) => Assert.True(PgCrudWriteGenerator.IsSafeTypeName(type));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("text); DROP TABLE orders; --")]
    [InlineData("text; SELECT 1")]
    [InlineData("integer -- note")]
    [InlineData("integer /* c */")]
    [InlineData("text'")]
    [InlineData("text\\")]
    [InlineData("$1")]
    [InlineData("\"unterminated")]
    [InlineData("\"a\" \"b\"; DROP")]
    [InlineData("text\nSELECT")]
    public void Anything_that_could_end_the_cast_or_start_another_statement_is_refused(string type) =>
        Assert.False(PgCrudWriteGenerator.IsSafeTypeName(type));

    [Fact]
    public void A_type_that_fails_the_check_is_refused_by_Build_and_never_reaches_the_statement()
    {
        var edit = Update(new[] { Cell("n", "1", "integer); DROP TABLE orders; --") }, new[] { Cell("k", "1", "integer") });

        var ex = Assert.Throws<InvalidOperationException>(() => PgCrudWriteGenerator.Build(Request(), edit));

        Assert.DoesNotContain("DROP TABLE", ex.Message);
    }

    [Fact]
    public void An_absurdly_long_type_name_is_refused()
    {
        Assert.False(PgCrudWriteGenerator.IsSafeTypeName(new string('a', 300)));
    }
}
