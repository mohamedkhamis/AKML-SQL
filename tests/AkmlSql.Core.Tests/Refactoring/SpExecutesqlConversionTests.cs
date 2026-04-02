using System;
using System.Collections.Generic;
using AkmlSql.Engine.Refactoring;
using AkmlSql.Engine.Refactoring.Operations.Lightweight;
using Xunit;

namespace AkmlSql.Core.Tests.Refactoring;

/// <summary>
/// Tests the <see cref="ConvertSpExecutesqlOperation"/> text-level conversion
/// of sp_executesql calls to static SQL.
/// </summary>
public class SpExecutesqlConversionTests
{
    private readonly ConvertSpExecutesqlOperation _operation = new();

    private (string modifiedText, string[] warnings) Apply(string sql)
    {
        var context = new RefactoringContext
        {
            DocumentText = sql,
            Script = new Microsoft.SqlServer.TransactSql.ScriptDom.TSqlScript(),
            Tokens = []
        };
        return _operation.Apply(context);
    }

    [Fact]
    public void BasicIntParam_ReplacedWithValue()
    {
        var sql = "EXEC sp_executesql N'SELECT @id', N'@id int', @id = 5";

        var (result, warnings) = Apply(sql);

        Assert.Equal("SELECT 5", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void StringParam_PreservesQuotes()
    {
        var sql = "EXEC sp_executesql N'SELECT @name', N'@name nvarchar(50)', @name = N'Smith'";

        var (result, warnings) = Apply(sql);

        Assert.Equal("SELECT 'Smith'", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void NullParam_SubstitutesNull()
    {
        var sql = "EXEC sp_executesql N'SELECT @id', N'@id int', @id = NULL";

        var (result, warnings) = Apply(sql);

        Assert.Equal("SELECT NULL", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void MultipleParams_AllReplaced()
    {
        var sql = "EXEC sp_executesql N'SELECT * FROM Users WHERE Id = @id AND Name = @name', "
                + "N'@id int, @name nvarchar(100)', @id = 42, @name = N'John'";

        var (result, warnings) = Apply(sql);

        Assert.Equal("SELECT * FROM Users WHERE Id = 42 AND Name = 'John'", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Execute_Keyword_AlsoMatches()
    {
        var sql = "EXECUTE sp_executesql N'SELECT @val', N'@val int', @val = 100";

        var (result, warnings) = Apply(sql);

        Assert.Equal("SELECT 100", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void CaseInsensitive_Matching()
    {
        var sql = "exec SP_EXECUTESQL N'SELECT @x', N'@x int', @x = 7";

        var (result, warnings) = Apply(sql);

        Assert.Equal("SELECT 7", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void NoParams_TemplateOnly()
    {
        var sql = "EXEC sp_executesql N'SELECT 1'";

        var (result, warnings) = Apply(sql);

        Assert.Equal("SELECT 1", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void InvalidInput_NoSpExecutesql_ReturnsOriginal()
    {
        var sql = "SELECT * FROM Users";

        var (result, warnings) = Apply(sql);

        Assert.Equal(sql, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        var (result, warnings) = Apply(string.Empty);

        Assert.Equal(string.Empty, result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void EscapedQuotes_InTemplate_HandledCorrectly()
    {
        var sql = "EXEC sp_executesql N'SELECT * FROM Users WHERE Name = ''Admin'''";

        var (result, warnings) = Apply(sql);

        Assert.Equal("SELECT * FROM Users WHERE Name = 'Admin'", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void SchemaPrefix_SysOrDbo_Supported()
    {
        var sql = "EXEC sys.sp_executesql N'SELECT @v', N'@v int', @v = 1";

        var (result, warnings) = Apply(sql);

        Assert.Equal("SELECT 1", result);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ExtractStringLiteral_NPrefix_Removed()
    {
        var result = ConvertSpExecutesqlOperation.ExtractStringLiteral("N'hello world'");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void ExtractStringLiteral_EscapedQuotes_Unescaped()
    {
        var result = ConvertSpExecutesqlOperation.ExtractStringLiteral("N'it''s a test'");
        Assert.Equal("it's a test", result);
    }

    [Fact]
    public void ExtractStringLiteral_NotALiteral_ReturnsNull()
    {
        var result = ConvertSpExecutesqlOperation.ExtractStringLiteral("42");
        Assert.Null(result);
    }

    [Fact]
    public void ParseParameterDefinitions_MultipleParams()
    {
        var result = ConvertSpExecutesqlOperation.ParseParameterDefinitions(
            "@id int, @name nvarchar(50), @amount decimal(10,2)");

        Assert.Equal(3, result.Count);
        Assert.Equal("int", result["@id"]);
        Assert.Equal("nvarchar(50)", result["@name"]);
        Assert.Equal("decimal(10,2)", result["@amount"]);
    }

    [Fact]
    public void ParseParameterValues_MultipleValues()
    {
        var result = ConvertSpExecutesqlOperation.ParseParameterValues(
            "@id = 5, @name = N'Smith', @amount = 99.50");

        Assert.Equal(3, result.Count);
        Assert.Equal("5", result["@id"]);
        Assert.Equal("N'Smith'", result["@name"]);
        Assert.Equal("99.50", result["@amount"]);
    }

    [Fact]
    public void SubstituteParameters_LongerNameFirst_NoPartialMatch()
    {
        // @id and @id2 — @id2 must not be partially matched as @id + "2"
        var paramValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "@id", "1" },
            { "@id2", "2" }
        };

        var result = ConvertSpExecutesqlOperation.SubstituteParameters(
            "SELECT @id, @id2", paramValues);

        Assert.Equal("SELECT 1, 2", result);
    }

    [Fact]
    public void MultipleCallsInDocument_AllConverted()
    {
        var sql = "EXEC sp_executesql N'SELECT @a', N'@a int', @a = 1\n"
                + "EXEC sp_executesql N'SELECT @b', N'@b int', @b = 2";

        var (result, warnings) = Apply(sql);

        Assert.Contains("SELECT 1", result);
        Assert.Contains("SELECT 2", result);
        Assert.DoesNotContain("sp_executesql", result);
        Assert.Empty(warnings);
    }
}
