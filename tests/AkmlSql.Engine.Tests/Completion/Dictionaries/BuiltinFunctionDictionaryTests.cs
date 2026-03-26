using Xunit;
using AkmlSql.Engine.Completion.Dictionaries;

namespace AkmlSql.Engine.Tests.Completion.Dictionaries;

public class BuiltinFunctionDictionaryTests
{
    // ── TryGetSignature ────────────────────────────────────────────────────

    [Fact]
    public void TryGetSignature_KnownFunction_ReturnsTrue()
    {
        var found = BuiltinFunctionDictionary.TryGetSignature("CONVERT", out var overloads);
        Assert.True(found);
        Assert.NotNull(overloads);
        Assert.NotEmpty(overloads);
    }

    [Fact]
    public void TryGetSignature_CaseInsensitive_ReturnsTrue()
    {
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("convert", out _));
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("Convert", out _));
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("CONVERT", out _));
    }

    [Fact]
    public void TryGetSignature_UnknownFunction_ReturnsFalse()
    {
        var found = BuiltinFunctionDictionary.TryGetSignature("NONEXISTENT_FUNC", out var overloads);
        Assert.False(found);
    }

    [Fact]
    public void TryGetSignature_Convert_HasTwoOverloads()
    {
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("CONVERT", out var overloads));
        Assert.Equal(2, overloads!.Length);
    }

    [Fact]
    public void TryGetSignature_Cast_HasOneOverload()
    {
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("CAST", out var overloads));
        Assert.Single(overloads!);
    }

    [Fact]
    public void TryGetSignature_Overload_HasLabelAndDocumentation()
    {
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("GETDATE", out var overloads));
        Assert.Single(overloads!);
        var overload = overloads![0];
        Assert.NotEmpty(overload.Label);
        Assert.NotEmpty(overload.Documentation);
        Assert.Contains("GETDATE", overload.Label);
    }

    [Fact]
    public void TryGetSignature_Convert_FirstOverload_HasTwoRequiredParams()
    {
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("CONVERT", out var overloads));
        var first = overloads![0];
        Assert.Equal(2, first.Parameters.Length);
        Assert.False(first.Parameters[0].IsOptional);
        Assert.False(first.Parameters[1].IsOptional);
    }

    [Fact]
    public void TryGetSignature_Convert_SecondOverload_HasOptionalStyle()
    {
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("CONVERT", out var overloads));
        var second = overloads![1];
        Assert.Equal(3, second.Parameters.Length);
        Assert.True(second.Parameters[2].IsOptional);
        Assert.Equal("style", second.Parameters[2].Name);
    }

    [Fact]
    public void TryGetSignature_CharIndex_HasTwoOverloads()
    {
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("CHARINDEX", out var overloads));
        Assert.Equal(2, overloads!.Length);
    }

    [Fact]
    public void TryGetSignature_Round_HasTwoOverloads()
    {
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("ROUND", out var overloads));
        Assert.Equal(2, overloads!.Length);
    }

    [Fact]
    public void TryGetSignature_Format_HasTwoOverloads()
    {
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("FORMAT", out var overloads));
        Assert.Equal(2, overloads!.Length);
    }

    [Fact]
    public void TryGetSignature_GetDate_HasEmptyParameters()
    {
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("GETDATE", out var overloads));
        Assert.Empty(overloads![0].Parameters);
    }

    [Fact]
    public void TryGetSignature_GetUtcDate_HasEmptyParameters()
    {
        Assert.True(BuiltinFunctionDictionary.TryGetSignature("GETUTCDATE", out var overloads));
        Assert.Empty(overloads![0].Parameters);
    }

    // ── IsBuiltinFunction ─────────────────────────────────────────────────

    [Theory]
    [InlineData("CONVERT")]
    [InlineData("CAST")]
    [InlineData("TRY_CONVERT")]
    [InlineData("TRY_CAST")]
    [InlineData("DATEADD")]
    [InlineData("DATEDIFF")]
    [InlineData("DATEPART")]
    [InlineData("FORMAT")]
    [InlineData("GETDATE")]
    [InlineData("GETUTCDATE")]
    [InlineData("COALESCE")]
    [InlineData("ISNULL")]
    [InlineData("NULLIF")]
    [InlineData("IIF")]
    [InlineData("CHOOSE")]
    [InlineData("SUBSTRING")]
    [InlineData("LEFT")]
    [InlineData("RIGHT")]
    [InlineData("LEN")]
    [InlineData("CHARINDEX")]
    [InlineData("REPLACE")]
    [InlineData("STUFF")]
    [InlineData("STRING_AGG")]
    [InlineData("ROUND")]
    [InlineData("ABS")]
    [InlineData("CEILING")]
    [InlineData("FLOOR")]
    public void IsBuiltinFunction_KnownFunctions_ReturnsTrue(string name)
    {
        Assert.True(BuiltinFunctionDictionary.IsBuiltinFunction(name));
    }

    [Fact]
    public void IsBuiltinFunction_CaseInsensitive()
    {
        Assert.True(BuiltinFunctionDictionary.IsBuiltinFunction("getdate"));
        Assert.True(BuiltinFunctionDictionary.IsBuiltinFunction("GetDate"));
        Assert.True(BuiltinFunctionDictionary.IsBuiltinFunction("GETDATE"));
    }

    [Fact]
    public void IsBuiltinFunction_UnknownFunction_ReturnsFalse()
    {
        Assert.False(BuiltinFunctionDictionary.IsBuiltinFunction("MY_CUSTOM_FUNC"));
    }

    [Fact]
    public void IsBuiltinFunction_EmptyString_ReturnsFalse()
    {
        Assert.False(BuiltinFunctionDictionary.IsBuiltinFunction(""));
    }

    // ── GetAllFunctionNames ───────────────────────────────────────────────

    [Fact]
    public void GetAllFunctionNames_ReturnsNonEmpty()
    {
        var names = BuiltinFunctionDictionary.GetAllFunctionNames().ToList();
        Assert.NotEmpty(names);
    }

    [Fact]
    public void GetAllFunctionNames_ContainsKnownFunctions()
    {
        var names = BuiltinFunctionDictionary.GetAllFunctionNames().ToList();
        Assert.Contains("CONVERT", names);
        Assert.Contains("CAST", names);
        Assert.Contains("GETDATE", names);
        Assert.Contains("COALESCE", names);
        Assert.Contains("SUBSTRING", names);
    }

    [Fact]
    public void GetAllFunctionNames_HasExpectedCount()
    {
        var count = BuiltinFunctionDictionary.GetAllFunctionNames().Count();
        Assert.Equal(27, count);
    }
}
