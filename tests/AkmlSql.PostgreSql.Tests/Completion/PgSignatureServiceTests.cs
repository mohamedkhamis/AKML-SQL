using AkmlSql.Core.Ipc.Messages;
using AkmlSql.PostgreSql.Completion;
using Xunit;

namespace AkmlSql.PostgreSql.Tests.Completion;

/// <summary>Spec 042 (task T096, FR-062, scenario US3-6) - signature help inside a call.</summary>
public class PgSignatureServiceTests
{
    private static readonly PgSignatureService Service = new();

    private static SignatureResponse At(string doc, int serverMajor = 18)
    {
        var caret = doc.IndexOf('|');
        Assert.True(caret >= 0, "no caret marker in: " + doc);
        return Service.Get(doc.Remove(caret, 1), caret, NorthwindPgTestCacheFactory.Create(), serverMajor);
    }

    [Fact]
    public void Inside_date_trunc_the_forms_and_the_first_parameter_are_shown()
    {
        var r = At("SELECT date_trunc(|");

        Assert.Equal("date_trunc", r.FunctionName);
        Assert.Equal(0, r.ActiveParameter);
        Assert.True(r.Overloads!.Length >= 3);
        Assert.Contains(r.Overloads, o => o.Label == "date_trunc(text, timestamp with time zone) -> timestamp with time zone");
        Assert.Contains(r.Overloads, o => o.Label.StartsWith("date_trunc(text, interval)", StringComparison.Ordinal));
        Assert.All(r.Overloads, o => Assert.Equal("Truncate to specified precision", o.Documentation));
    }

    [Fact]
    public void The_active_parameter_follows_the_commas()
    {
        Assert.Equal(1, At("SELECT date_trunc('day', |").ActiveParameter);
        Assert.Equal(2, At("SELECT date_trunc('day', now(), |").ActiveParameter);
        Assert.Equal(0, At("SELECT date_trunc(|'day', now())").ActiveParameter);
    }

    [Fact]
    public void A_commas_inside_nested_parentheses_do_not_move_the_active_parameter()
    {
        var r = At("SELECT date_trunc('day', coalesce(a, b), |");

        Assert.Equal("date_trunc", r.FunctionName);
        Assert.Equal(2, r.ActiveParameter);
    }

    [Fact]
    public void The_nearest_enclosing_call_is_the_one_described()
    {
        var r = At("SELECT date_trunc('day', coalesce(a, |");

        Assert.Equal("coalesce", r.FunctionName);
        Assert.Equal(1, r.ActiveParameter);
    }

    [Fact]
    public void The_form_with_enough_parameters_is_the_active_one()
    {
        var r = At("SELECT date_trunc('day', now(), |");

        var active = r.Overloads![r.ActiveOverload];
        Assert.Equal(3, active.Parameters.Length);
        Assert.Equal("text", active.Parameters[2].Type);
    }

    [Fact]
    public void A_user_function_shows_its_parameters_from_the_schema()
    {
        var r = At("SELECT order_total(|");

        Assert.Equal("order_total", r.FunctionName);
        var overload = Assert.Single(r.Overloads!);
        Assert.Equal("order_total(order_id integer)", overload.Label);
        Assert.Equal("order_id", overload.Parameters[0].Name);
        Assert.Equal("integer", overload.Parameters[0].Type);
    }

    [Fact]
    public void A_user_procedure_is_found_too()
    {
        var r = At("CALL archive_orders(|");

        var overload = Assert.Single(r.Overloads!);
        Assert.Equal("archive_orders(cutoff date)", overload.Label);
    }

    [Fact]
    public void A_procedure_of_another_schema_needs_its_schema()
    {
        Assert.Empty(At("CALL mark_invoice_paid(|").Overloads!);
        Assert.Single(At("CALL sales.mark_invoice_paid(|").Overloads!);
    }

    [Fact]
    public void A_variadic_function_covers_every_position()
    {
        var r = At("SELECT coalesce(a, b, c, |");

        Assert.Equal("coalesce", r.FunctionName);
        Assert.Equal(3, r.ActiveParameter);
        var active = r.Overloads![r.ActiveOverload];
        Assert.True(active.Parameters[^1].IsOptional);
        Assert.Equal("repeats", active.Parameters[^1].Documentation);
    }

    [Fact]
    public void A_schema_qualified_built_in_is_found()
    {
        var r = At("SELECT pg_catalog.lower(|");

        Assert.Equal("lower", r.FunctionName);
        Assert.NotEmpty(r.Overloads!);
    }

    [Theory]
    [InlineData("SELECT |")]
    [InlineData("SELECT * FROM customers WHERE country IN (|")]
    [InlineData("SELECT (1 + |")]
    [InlineData("INSERT INTO orders (|")]
    [InlineData("SELECT 'date_trunc(|'")]
    [InlineData("-- date_trunc(|")]
    public void Outside_a_call_there_is_no_signature(string doc)
    {
        var r = At(doc);

        Assert.Equal(string.Empty, r.FunctionName);
        Assert.Empty(r.Overloads!);
    }

    [Fact]
    public void An_unknown_function_has_no_signature()
    {
        var r = At("SELECT no_such_function(|");

        Assert.Empty(r.Overloads!);
    }

    [Fact]
    public void A_function_the_server_does_not_have_has_no_signature()
    {
        Assert.NotEmpty(At("SELECT json_exists(|").Overloads!);
        Assert.Empty(At("SELECT json_exists(|", serverMajor: 14).Overloads!);
    }

    [Fact]
    public void Odd_text_never_throws()
    {
        Assert.NotNull(Service.Get(null, 0, null));
        Assert.NotNull(Service.Get("SELECT f(", 99, null));
        Assert.NotNull(Service.Get("((((", 2, NorthwindPgTestCacheFactory.Create()));
    }
}
