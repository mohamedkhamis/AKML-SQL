using AkmlSql.Engine.Productivity;
using Xunit;

namespace AkmlSql.Engine.Tests.Productivity;

/// <summary>
/// Spec 030 T066 — unit tests for the pure <see cref="ScriptAsAlterRewriter.ToAlter"/> transform.
/// No live DB: the handler fetches the definition; this exercises the CREATE→ALTER rewrite alone.
/// </summary>
public sealed class ScriptAsAlterRewriterTests
{
    [Fact]
    public void Plain_create_procedure_becomes_alter()
    {
        var (ok, altered, error) = ScriptAsAlterRewriter.ToAlter("CREATE PROCEDURE dbo.X AS SELECT 1");
        Assert.True(ok, error);
        Assert.Equal("ALTER PROCEDURE dbo.X AS SELECT 1", altered);
    }

    [Fact]
    public void Create_or_alter_collapses_to_alter()
    {
        var (ok, altered, _) = ScriptAsAlterRewriter.ToAlter("CREATE OR ALTER PROCEDURE dbo.X AS SELECT 1");
        Assert.True(ok);
        Assert.Equal("ALTER PROCEDURE dbo.X AS SELECT 1", altered);
    }

    [Fact]
    public void Keyword_match_is_case_insensitive()
    {
        var (ok, altered, _) = ScriptAsAlterRewriter.ToAlter("create view v as select 1");
        Assert.True(ok);
        Assert.Equal("ALTER view v as select 1", altered);
    }

    [Fact]
    public void Leading_comment_is_preserved()
    {
        var (ok, altered, _) = ScriptAsAlterRewriter.ToAlter("-- header\nCREATE VIEW v AS SELECT 1");
        Assert.True(ok);
        Assert.Equal("-- header\nALTER VIEW v AS SELECT 1", altered);
    }

    [Fact]
    public void Only_the_first_create_is_rewritten()
    {
        // A CREATE TABLE #t inside the body must survive untouched.
        var (ok, altered, _) = ScriptAsAlterRewriter.ToAlter("CREATE PROCEDURE p AS CREATE TABLE #t(i int)");
        Assert.True(ok);
        Assert.Equal("ALTER PROCEDURE p AS CREATE TABLE #t(i int)", altered);
    }

    [Fact]
    public void Create_or_alter_tolerates_interleaved_whitespace_and_newlines()
    {
        var (ok, altered, _) = ScriptAsAlterRewriter.ToAlter(
            "CREATE   OR\n  ALTER FUNCTION dbo.f() RETURNS int AS BEGIN RETURN 1 END");
        Assert.True(ok);
        Assert.Equal("ALTER FUNCTION dbo.f() RETURNS int AS BEGIN RETURN 1 END", altered);
    }

    [Fact]
    public void Create_trigger_becomes_alter_trigger()
    {
        var (ok, altered, _) = ScriptAsAlterRewriter.ToAlter(
            "CREATE TRIGGER trg ON dbo.T AFTER INSERT AS SELECT 1");
        Assert.True(ok);
        Assert.Equal("ALTER TRIGGER trg ON dbo.T AFTER INSERT AS SELECT 1", altered);
    }

    [Fact]
    public void Lowercase_create_or_alter_collapses_preserving_alter_casing()
    {
        var (ok, altered, _) = ScriptAsAlterRewriter.ToAlter("create or alter proc p as select 1");
        Assert.True(ok);
        Assert.Equal("alter proc p as select 1", altered);
    }

    [Theory]
    [InlineData("SELECT 1")]                                              // not a CREATE statement
    [InlineData("-- Object 'dbo.X' exists but no definition is available.")] // encrypted-module placeholder
    public void Text_without_leading_create_is_refused(string definition)
    {
        var (ok, altered, error) = ScriptAsAlterRewriter.ToAlter(definition);
        Assert.False(ok);
        Assert.Null(altered);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void Empty_or_whitespace_is_refused(string? definition)
    {
        var (ok, _, error) = ScriptAsAlterRewriter.ToAlter(definition);
        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
