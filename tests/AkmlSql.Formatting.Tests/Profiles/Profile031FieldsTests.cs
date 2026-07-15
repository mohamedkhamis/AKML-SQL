using AkmlSql.Formatting.Profiles;
using Xunit;

namespace AkmlSql.Formatting.Tests.Profiles;

public class Profile031FieldsTests
{
    [Fact]
    public void New_031_fields_roundtrip_through_akmlstyle_json()
    {
        var p = new FormattingProfile();
        p.Whitespace.SemicolonPlacement = "spaceBefore";
        p.Whitespace.EmptyLinesAfterBatchSeparator = 3;
        p.List.SpaceBeforeComma = true;
        p.List.CommaAlignment = "toList";
        p.List.AlignItemsToTabStops = true;
        p.Parenthesis.Style = "expandedToStatement";
        p.Ddl.ParenthesisStyle = "expandedToStatement";
        p.Cte.ParenthesisStyle = "expandedToStatement";
        p.Dml.NewLineAfterDistinctTop = true;
        p.InsertStatements.Columns.ParenthesisStyle = "expandedSimple";
        p.InsertStatements.Columns.IndentContents = false;
        p.InsertStatements.Values.ParenthesisStyle = "expandedSimple";
        p.InsertStatements.Values.IndentContents = true;
        p.InsertStatements.Values.PlaceSubsequentItemsOnNewLines = "always";
        p.ControlFlow.IndentBeginEndKeywords = true;
        p.Cte.PlaceNameOnNewLine = true;
        p.Cte.IndentName = true;
        p.Cte.ColumnAlignment = "rightAligned";
        p.Declare.EqualsOnNewLine = true;
        p.FunctionCalls.SpaceAroundParentheses = true;
        p.FunctionCalls.SpaceAroundArgumentList = true;
        p.FunctionCalls.SpaceBetweenEmptyParentheses = true;
        p.Case.ThenAlignment = "toWhen";
        p.Operators.BetweenAndAlignment = "rightAlignedToBetween";
        p.InStatements.SpaceAroundContents = true;

        var back = ProfileSerializer.Deserialize(ProfileSerializer.Serialize(p));

        Assert.Equal("spaceBefore", back.Whitespace.SemicolonPlacement);
        Assert.Equal(3, back.Whitespace.EmptyLinesAfterBatchSeparator);
        Assert.True(back.List.SpaceBeforeComma);
        Assert.Equal("toList", back.List.CommaAlignment);
        Assert.True(back.List.AlignItemsToTabStops);
        Assert.Equal("expandedToStatement", back.Parenthesis.Style);
        Assert.Equal("expandedSimple", back.InsertStatements.Columns.ParenthesisStyle);
        Assert.False(back.InsertStatements.Columns.IndentContents);
        Assert.True(back.InsertStatements.Values.IndentContents);
        Assert.Equal("always", back.InsertStatements.Values.PlaceSubsequentItemsOnNewLines);
        Assert.True(back.ControlFlow.IndentBeginEndKeywords);
        Assert.True(back.Cte.PlaceNameOnNewLine);
        Assert.Equal("rightAligned", back.Cte.ColumnAlignment);
        Assert.True(back.Declare.EqualsOnNewLine);
        Assert.True(back.FunctionCalls.SpaceBetweenEmptyParentheses);
        Assert.Equal("toWhen", back.Case.ThenAlignment);
        Assert.Equal("rightAlignedToBetween", back.Operators.BetweenAndAlignment);
        Assert.True(back.InStatements.SpaceAroundContents);
    }

    [Fact]
    public void Format_setting_schema_discovers_insertStatements_group_and_new_fields()
    {
        var schema = FormatSettingSchema.BuildDefault();
        Assert.Contains(schema.Groups, g => g.Id == "insertStatements");
        Assert.Contains(schema.Settings, s => s.Id == "whitespace.semicolonPlacement");
        Assert.Contains(schema.Settings, s => s.Id == "list.commaAlignment");
    }
}
