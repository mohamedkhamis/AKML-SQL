using Xunit;
using AkmlSql.Engine.Snippets;
using AkmlSql.Engine.Snippets.Models;

namespace AkmlSql.Engine.Tests.Snippets;

public class PlaceholderParserTests
{
    private static SnippetVariable Var(string name, string def = "", string? schemaAware = null)
    {
        return new SnippetVariable { Name = name, Default = def, SchemaAware = schemaAware };
    }

    // ── Parse ──────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyText_ReturnsEmpty()
    {
        var result = PlaceholderParser.Parse("", []);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_NoPlaceholders_ReturnsEmpty()
    {
        var result = PlaceholderParser.Parse("SELECT 1", []);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_BuiltInVariableOnly_ExcludesIt()
    {
        var result = PlaceholderParser.Parse("$CURSOR$", []);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_AllBuiltIns_Excluded()
    {
        var text = "$CURSOR$ $DATE$ $DATETIME$ $TIME$ $USER$ $MACHINE$ $DATABASE$ $SERVER$ $SCHEMA$ $GUID$ $YEAR$ $FILENAME$ $CLIPBOARD$ $SELECTEDTEXT$";
        var result = PlaceholderParser.Parse(text, []);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_CustomVariable_ReturnsPlaceholderInfo()
    {
        var result = PlaceholderParser.Parse("SELECT * FROM $TableName$", [Var("TableName", "MyTable")]);
        Assert.Single(result);
        Assert.Equal("TableName", result[0].VariableName);
        Assert.Equal("MyTable", result[0].DefaultText);
        Assert.Equal(0, result[0].GroupIndex);
        Assert.True(result[0].Length > 0);
    }

    [Fact]
    public void Parse_CustomVariable_NoMatchingDefinition_UsesVarNameAsDefault()
    {
        var result = PlaceholderParser.Parse("SELECT $ColName$", []);
        Assert.Single(result);
        Assert.Equal("ColName", result[0].VariableName);
        Assert.Equal("ColName", result[0].DefaultText); // falls back to varName
    }

    [Fact]
    public void Parse_SameVariableTwice_SameGroupIndex()
    {
        var result = PlaceholderParser.Parse("$Tbl$ JOIN $Tbl$", []);
        Assert.Equal(2, result.Count);
        Assert.Equal(result[0].GroupIndex, result[1].GroupIndex);
    }

    [Fact]
    public void Parse_TwoDifferentVariables_DifferentGroupIndex()
    {
        var result = PlaceholderParser.Parse("$A$ $B$", []);
        Assert.Equal(2, result.Count);
        Assert.NotEqual(result[0].GroupIndex, result[1].GroupIndex);
    }

    [Fact]
    public void Parse_SchemaAwareType_Propagated()
    {
        var result = PlaceholderParser.Parse("$TableName$", [Var("TableName", "T", "table")]);
        Assert.Single(result);
        Assert.Equal("table", result[0].SchemaAwareType);
    }

    [Fact]
    public void Parse_VariableOffset_IsCorrect()
    {
        var text = "SELECT $Col$";
        var result = PlaceholderParser.Parse(text, []);
        Assert.Single(result);
        Assert.Equal(text.IndexOf("$Col$", StringComparison.Ordinal), result[0].Offset);
        Assert.Equal("$Col$".Length, result[0].Length);
    }

    // ── FindCursorOffset ────────────────────────────────────────────────────

    [Fact]
    public void FindCursorOffset_WithCursor_ReturnsIndex()
    {
        var text = "SELECT $CURSOR$ FROM T";
        var idx = PlaceholderParser.FindCursorOffset(text);
        Assert.Equal(text.IndexOf("$CURSOR$", StringComparison.Ordinal), idx);
    }

    [Fact]
    public void FindCursorOffset_CaseInsensitive()
    {
        var text = "prefix$cursor$suffix";
        Assert.Equal(6, PlaceholderParser.FindCursorOffset(text));
    }

    [Fact]
    public void FindCursorOffset_NoCursor_ReturnsMinusOne()
    {
        Assert.Equal(-1, PlaceholderParser.FindCursorOffset("SELECT 1"));
    }

    [Fact]
    public void FindCursorOffset_EmptyString_ReturnsMinusOne()
    {
        Assert.Equal(-1, PlaceholderParser.FindCursorOffset(""));
    }

    // ── ReplacePlaceholdersWithDefaults ─────────────────────────────────────

    [Fact]
    public void ReplacePlaceholders_CustomVariable_ReplacedWithDefault()
    {
        var result = PlaceholderParser.ReplacePlaceholdersWithDefaults(
            "$TableName$", [Var("TableName", "Orders")]);
        Assert.Equal("Orders", result);
    }

    [Fact]
    public void ReplacePlaceholders_NoMatchingVariable_UsesVarName()
    {
        var result = PlaceholderParser.ReplacePlaceholdersWithDefaults("$Col$", []);
        Assert.Equal("Col", result);
    }

    [Fact]
    public void ReplacePlaceholders_BuiltInVariable_NotReplaced()
    {
        var result = PlaceholderParser.ReplacePlaceholdersWithDefaults(
            "$CURSOR$ $DATE$", []);
        // Built-in vars kept as-is
        Assert.Contains("$CURSOR$", result);
        Assert.Contains("$DATE$", result);
    }

    [Fact]
    public void ReplacePlaceholders_MixedText()
    {
        var result = PlaceholderParser.ReplacePlaceholdersWithDefaults(
            "SELECT $Col$ FROM $TableName$ -- $CURSOR$",
            [Var("Col", "Id"), Var("TableName", "Orders")]);
        Assert.Equal("SELECT Id FROM Orders -- $CURSOR$", result);
    }
}
