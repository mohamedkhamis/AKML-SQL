using System.Collections.Generic;
using System.Linq;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

public sealed class SuppressionParserTests
{
    private static readonly TsqlParserService Parser;

    static SuppressionParserTests()
    {
        Parser = new TsqlParserService();
        Parser.SetServerVersion(160);
    }

    private static (SuppressionMap Map, List<AnalysisDiagnostic> Meta) Parse(string sql)
    {
        var tokens = Parser.GetTokenStream(sql);
        var map    = SuppressionParser.Parse(tokens, out var meta);
        return (map, meta);
    }

    [Fact]
    public void InlineNoqaSpecificRule_SuppressesOnlyThatRule()
    {
        var sql = "SELECT * FROM foo  -- noqa: PE001";
        var (map, _) = Parse(sql);

        Assert.True(map.IsSuppressed(1, "PE001"));
        Assert.False(map.IsSuppressed(1, "PE002"));
        Assert.False(map.IsSuppressed(2, "PE001"));
    }

    [Fact]
    public void InlineNoqaMultipleRules_SuppressesAll()
    {
        var sql = "SELECT * FROM foo  -- noqa: PE001, BP004";
        var (map, _) = Parse(sql);

        Assert.True(map.IsSuppressed(1, "PE001"));
        Assert.True(map.IsSuppressed(1, "BP004"));
        Assert.False(map.IsSuppressed(1, "SE001"));
    }

    [Fact]
    public void InlineNoqaNoRuleIds_SuppressesAll()
    {
        var sql = "SELECT * FROM foo  -- noqa";
        var (map, _) = Parse(sql);

        Assert.True(map.IsSuppressed(1, "PE001"));
        Assert.True(map.IsSuppressed(1, "ANY_RULE"));
    }

    [Fact]
    public void NoqaBlock_SuppressesAllInRange()
    {
        var sql = """
            -- noqa-begin
            SELECT * FROM foo
            DELETE FROM bar
            -- noqa-end
            SELECT 1
            """;
        var (map, _) = Parse(sql);

        Assert.True(map.IsSuppressed(2, "PE001"));
        Assert.True(map.IsSuppressed(3, "PE003"));
        Assert.False(map.IsSuppressed(5, "PE001"));
    }

    [Fact]
    public void NoqaBeginWithoutEnd_SuppressesToEof_AndEmitsWarning()
    {
        var sql = """
            -- noqa-begin
            SELECT * FROM foo
            """;
        var (map, meta) = Parse(sql);

        Assert.True(map.IsSuppressed(2, "PE001"));
        Assert.True(map.IsSuppressed(9999, "ANY_RULE"));

        Assert.Single(meta);
        Assert.Equal(DiagnosticSeverity.Warning, meta[0].Severity);
        Assert.Equal("NOQA001", meta[0].RuleId);
    }

    [Fact]
    public void NoqaIsCaseInsensitive()
    {
        var sql = "SELECT * FROM foo  -- NOQA: pe001";
        var (map, _) = Parse(sql);

        Assert.True(map.IsSuppressed(1, "PE001"));
    }

    [Fact]
    public void NoqaWithSpacesAroundColon_Parsed()
    {
        var sql = "SELECT 1  -- noqa :   PE001 , BP004";
        var (map, _) = Parse(sql);

        Assert.True(map.IsSuppressed(1, "PE001"));
        Assert.True(map.IsSuppressed(1, "BP004"));
    }

    [Fact]
    public void NoqaOnDifferentLines_IsolatedSuppression()
    {
        var sql = """
            SELECT * FROM foo  -- noqa: PE001
            DELETE FROM bar
            """;
        var (map, _) = Parse(sql);

        Assert.True(map.IsSuppressed(1, "PE001"));
        Assert.False(map.IsSuppressed(2, "PE001"));
    }

    [Fact]
    public void EmptyNoqaBeginEnd_NoMetaDiagnostics()
    {
        var sql = """
            -- noqa-begin
            -- noqa-end
            """;
        var (_, meta) = Parse(sql);

        Assert.Empty(meta);
    }
}
