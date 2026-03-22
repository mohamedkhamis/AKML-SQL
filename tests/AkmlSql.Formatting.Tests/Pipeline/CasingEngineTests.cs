using System.Diagnostics.CodeAnalysis;
using Xunit;
using AkmlSql.Formatting.Pipeline;

namespace AkmlSql.Formatting.Tests.Pipeline;

[SuppressMessage("ReSharper", "UnusedParameter.Local")]
public class CasingEngineTests
{
    // ── ApplyCasingMode ───────────────────────────────────────────────────

    [Theory]
    [InlineData("select", "UPPERCASE", "SELECT")]
    [InlineData("SELECT", "UPPERCASE", "SELECT")]
    [InlineData("Select", "UPPERCASE", "SELECT")]
    public void ApplyCasingMode_Uppercase(string input, string mode, string expected)
    {
        Assert.Equal(expected, CasingEngine.ApplyCasingMode(input, mode));
    }

    [Theory]
    [InlineData("SELECT", "lowercase", "select")]
    [InlineData("select", "lowercase", "select")]
    [InlineData("From", "lowercase", "from")]
    public void ApplyCasingMode_Lowercase(string input, string mode, string expected)
    {
        Assert.Equal(expected, CasingEngine.ApplyCasingMode(input, mode));
    }

    [Theory]
    [InlineData("select", "PascalCase", "Select")]
    [InlineData("from", "PascalCase", "From")]
    [InlineData("where", "PascalCase", "Where")]
    [InlineData("INNER", "PascalCase", "Inner")]
    [InlineData("JOIN", "PascalCase", "Join")]
    public void ApplyCasingMode_PascalCase_Keywords(string input, string mode, string expected)
    {
        Assert.Equal(expected, CasingEngine.ApplyCasingMode(input, mode));
    }

    [Theory]
    [InlineData("FIRST_VALUE", "PascalCase", "FirstValue")]
    [InlineData("ROW_NUMBER", "PascalCase", "RowNumber")]
    public void ApplyCasingMode_PascalCase_UnderscoreIdentifier(string input, string mode, string expected)
    {
        Assert.Equal(expected, CasingEngine.ApplyCasingMode(input, mode));
    }

    [Theory]
    [InlineData("select", "camelCase", "select")]
    [InlineData("FROM", "camelCase", "from")]
    [InlineData("INNER", "camelCase", "inner")]
    public void ApplyCasingMode_CamelCase_Keywords(string input, string mode, string expected)
    {
        Assert.Equal(expected, CasingEngine.ApplyCasingMode(input, mode));
    }

    [Theory]
    [InlineData("FIRST_VALUE", "camelCase", "firstValue")]
    [InlineData("ROW_NUMBER", "camelCase", "rowNumber")]
    public void ApplyCasingMode_CamelCase_UnderscoreIdentifier(string input, string mode, string expected)
    {
        Assert.Equal(expected, CasingEngine.ApplyCasingMode(input, mode));
    }

    [Theory]
    [InlineData("SELECT", "AsIs", "SELECT")]
    [InlineData("select", "AsIs", "select")]
    [InlineData("myCol", "AsIs", "myCol")]
    public void ApplyCasingMode_AsIs_ReturnsUnchanged(string input, string mode, string expected)
    {
        Assert.Equal(expected, CasingEngine.ApplyCasingMode(input, mode));
    }

    [Fact]
    public void ApplyCasingMode_UnknownMode_ReturnsAsIs()
    {
        Assert.Equal("SELECT", CasingEngine.ApplyCasingMode("SELECT", "UnknownMode"));
    }

    [Fact]
    public void ApplyCasingMode_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", CasingEngine.ApplyCasingMode("", "UPPERCASE"));
        Assert.Equal("", CasingEngine.ApplyCasingMode("", "lowercase"));
        Assert.Equal("", CasingEngine.ApplyCasingMode("", "PascalCase"));
        Assert.Equal("", CasingEngine.ApplyCasingMode("", "camelCase"));
    }

    // ── CasingEngine properties ───────────────────────────────────────────

    [Fact]
    public void CasingEngine_DefaultProperties()
    {
        var engine = new CasingEngine();
        Assert.Null(engine.IdentifierLookup);
        Assert.False(engine.UseCamelCaseDictionary);
    }

    [Fact]
    public void CasingEngine_CanSetIdentifierLookup()
    {
        var engine = new CasingEngine
        {
            IdentifierLookup = (schema, name) => null
        };
        Assert.NotNull(engine.IdentifierLookup);
    }

    [Fact]
    public void CasingEngine_CanSetUseCamelCaseDictionary()
    {
        var engine = new CasingEngine
        {
            UseCamelCaseDictionary = true
        };
        Assert.True(engine.UseCamelCaseDictionary);
    }
}
