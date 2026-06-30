using System.Collections.Generic;
using AkmlSql.Engine.Refactoring.Operations.Heavyweight;
using Xunit;

namespace AkmlSql.Engine.Tests.Refactoring.Operations.Heavyweight;

/// <summary>
/// Spec 030 T063 — unit tests for the pure <see cref="InlineStoredProcRewriter.Inline"/> transform.
/// No live DB: the operation fetches the definition; this exercises body-extraction, parameter
/// mapping (named/positional/defaults), token-aware substitution and the conservative refusals.
/// </summary>
public sealed class InlineStoredProcRewriterTests
{
    private static InlineCallArg Pos(string value) => new() { ValueText = value };
    private static InlineCallArg Named(string name, string value) => new() { Name = name, ValueText = value };

    // ── Happy paths ──────────────────────────────────────────────────────────

    [Fact]
    public void Positional_argument_is_substituted_into_the_body()
    {
        const string def = "CREATE PROCEDURE dbo.P @id int AS SELECT * FROM dbo.t WHERE id = @id";
        var r = InlineStoredProcRewriter.Inline(def, [Pos("5")]);
        Assert.True(r.Ok, r.Error);
        Assert.Equal("SELECT * FROM dbo.t WHERE id = 5", r.InlinedSql);
    }

    [Fact]
    public void Named_argument_is_substituted()
    {
        const string def = "CREATE PROCEDURE dbo.P @id int AS SELECT * FROM dbo.t WHERE id = @id";
        var r = InlineStoredProcRewriter.Inline(def, [Named("@id", "42")]);
        Assert.True(r.Ok, r.Error);
        Assert.Equal("SELECT * FROM dbo.t WHERE id = 42", r.InlinedSql);
    }

    [Fact]
    public void Omitted_argument_falls_back_to_the_declared_default()
    {
        const string def = "CREATE PROCEDURE dbo.P @id int = 99 AS SELECT * FROM dbo.t WHERE id = @id";
        var r = InlineStoredProcRewriter.Inline(def, []);
        Assert.True(r.Ok, r.Error);
        Assert.Equal("SELECT * FROM dbo.t WHERE id = 99", r.InlinedSql);
    }

    [Fact]
    public void Multiple_named_arguments_map_by_name_not_position()
    {
        const string def = "CREATE PROCEDURE dbo.P @a int, @b nvarchar(50) AS SELECT @b, @a";
        var r = InlineStoredProcRewriter.Inline(def, [Named("@b", "N'x'"), Named("@a", "1")]);
        Assert.True(r.Ok, r.Error);
        Assert.Equal("SELECT N'x', 1", r.InlinedSql);
    }

    [Fact]
    public void Value_text_preserves_quotes_and_n_prefix()
    {
        const string def = "CREATE PROCEDURE dbo.P @name nvarchar(50) AS SELECT * FROM dbo.t WHERE nm = @name";
        var r = InlineStoredProcRewriter.Inline(def, [Pos("N'Alice'")]);
        Assert.True(r.Ok, r.Error);
        Assert.Equal("SELECT * FROM dbo.t WHERE nm = N'Alice'", r.InlinedSql);
    }

    [Fact]
    public void A_param_inside_a_string_literal_is_not_substituted()
    {
        const string def = "CREATE PROCEDURE dbo.P @id int AS SELECT '@id' AS c, @id";
        var r = InlineStoredProcRewriter.Inline(def, [Pos("5")]);
        Assert.True(r.Ok, r.Error);
        Assert.Equal("SELECT '@id' AS c, 5", r.InlinedSql);
    }

    [Fact]
    public void Leading_SET_option_is_ignored_with_a_warning()
    {
        const string def = "CREATE PROCEDURE dbo.P @id int AS SET NOCOUNT ON; SELECT * FROM dbo.t WHERE id = @id";
        var r = InlineStoredProcRewriter.Inline(def, [Pos("7")]);
        Assert.True(r.Ok, r.Error);
        Assert.Equal("SELECT * FROM dbo.t WHERE id = 7", r.InlinedSql);
        Assert.Contains(r.Warnings, w => w.Contains("SET", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_param_used_twice_substitutes_both_and_warns()
    {
        const string def = "CREATE PROCEDURE dbo.P @id int AS SELECT * FROM dbo.t WHERE a = @id OR b = @id";
        var r = InlineStoredProcRewriter.Inline(def, [Pos("3")]);
        Assert.True(r.Ok, r.Error);
        Assert.Equal("SELECT * FROM dbo.t WHERE a = 3 OR b = 3", r.InlinedSql);
        Assert.Contains(r.Warnings, w => w.Contains("@id"));
    }

    // Regression for spec-030 sweep finding #5 (MED): a negative argument substituted directly after
    // an operator with no whitespace ("5-@id" with @id = -1) must NOT fuse into "5--1" — that "--"
    // is a line comment that silently truncates the statement. A single separating space is inserted
    // only in this fusing case (positive arguments like "3" above stay flush, so other goldens hold).
    [Fact]
    public void Negative_argument_adjacent_to_operator_does_not_form_a_comment_token()
    {
        const string def = "CREATE PROCEDURE dbo.P @id int AS SELECT 5-@id";
        var r = InlineStoredProcRewriter.Inline(def, [Pos("-1")]);
        Assert.True(r.Ok, r.Error);
        Assert.DoesNotContain("--", r.InlinedSql);
        Assert.Equal("SELECT 5- -1", r.InlinedSql);
    }

    [Fact]
    public void Alter_procedure_definition_is_also_inlinable()
    {
        const string def = "ALTER PROCEDURE dbo.P @id int AS SELECT @id";
        var r = InlineStoredProcRewriter.Inline(def, [Pos("1")]);
        Assert.True(r.Ok, r.Error);
        Assert.Equal("SELECT 1", r.InlinedSql);
    }

    // ── Conservative refusals ────────────────────────────────────────────────

    [Fact]
    public void Output_parameter_is_refused()
    {
        const string def = "CREATE PROCEDURE dbo.P @id int, @out int OUTPUT AS SELECT @out = @id";
        var r = InlineStoredProcRewriter.Inline(def, [Named("@id", "1")]);
        Assert.False(r.Ok);
        Assert.Contains("OUTPUT", r.Error!, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Multi_statement_body_is_refused()
    {
        const string def = "CREATE PROCEDURE dbo.P @id int AS SELECT 1; SELECT @id";
        var r = InlineStoredProcRewriter.Inline(def, [Pos("1")]);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Control_flow_body_is_refused()
    {
        const string def = "CREATE PROCEDURE dbo.P @id int AS IF @id > 0 SELECT 1 ELSE SELECT 2";
        var r = InlineStoredProcRewriter.Inline(def, [Pos("1")]);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Missing_argument_without_default_is_refused()
    {
        const string def = "CREATE PROCEDURE dbo.P @id int AS SELECT @id";
        var r = InlineStoredProcRewriter.Inline(def, []);
        Assert.False(r.Ok);
        Assert.Contains("@id", r.Error!);
    }

    [Fact]
    public void Mixed_named_and_positional_arguments_are_refused()
    {
        const string def = "CREATE PROCEDURE dbo.P @a int, @b int AS SELECT @a, @b";
        var r = InlineStoredProcRewriter.Inline(def, [Pos("1"), Named("@b", "2")]);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Unknown_named_argument_is_refused()
    {
        const string def = "CREATE PROCEDURE dbo.P @a int AS SELECT @a";
        var r = InlineStoredProcRewriter.Inline(def, [Named("@zzz", "1")]);
        Assert.False(r.Ok);
        Assert.Contains("@zzz", r.Error!);
    }

    [Fact]
    public void More_positional_arguments_than_parameters_is_refused()
    {
        const string def = "CREATE PROCEDURE dbo.P @a int AS SELECT @a";
        var r = InlineStoredProcRewriter.Inline(def, [Pos("1"), Pos("2")]);
        Assert.False(r.Ok);
    }

    [Fact]
    public void A_view_definition_is_not_a_procedure_and_is_refused()
    {
        const string def = "CREATE VIEW dbo.v AS SELECT 1 AS c";
        var r = InlineStoredProcRewriter.Inline(def, []);
        Assert.False(r.Ok);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_definition_is_refused(string? def)
    {
        var r = InlineStoredProcRewriter.Inline(def, []);
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
    }
}
