using Xunit;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Profiles;

public class FormattingProfileTests
{
    // ── FormattingProfile ─────────────────────────────────────────────────

    [Fact]
    public void FormattingProfile_AllSectionNotNull()
    {
        var p = new FormattingProfile();
        Assert.NotNull(p.Metadata);
        Assert.NotNull(p.Whitespace);
        Assert.NotNull(p.Casing);
        Assert.NotNull(p.List);
        Assert.NotNull(p.Parenthesis);
        Assert.NotNull(p.Dml);
        Assert.NotNull(p.Join);
        Assert.NotNull(p.Ddl);
        Assert.NotNull(p.ControlFlow);
        Assert.NotNull(p.Case);
        Assert.NotNull(p.Cte);
        Assert.NotNull(p.Expression);
        Assert.NotNull(p.FormatActions);
        Assert.Null(p.ExtensionData);
    }

    // ── WhitespaceOptions ─────────────────────────────────────────────────

    [Fact]
    public void WhitespaceOptions_Defaults()
    {
        var o = new WhitespaceOptions();
        Assert.Equal("spaces", o.TabStyle);
        Assert.Equal(4, o.TabSize);
        Assert.Equal("block", o.IndentStyle);
        Assert.Equal(120, o.MaxLineWidth);
        Assert.True(o.LineBreakBeforeClause);
        Assert.False(o.LineBreakAfterClause);
        Assert.False(o.LineBreakBeforeComma);
        Assert.True(o.LineBreakAfterComma);
        Assert.Equal(1, o.EmptyLineBetweenStatements);
        Assert.True(o.EmptyLineBeforeGo);
        Assert.True(o.EmptyLineAfterGo);
        Assert.True(o.PreserveEmptyLines);
        Assert.Equal(2, o.MaxConsecutiveEmptyLines);
        Assert.Equal("remove", o.TrailingWhitespace);
        Assert.Equal("ensure", o.FinalNewline);
        Assert.True(o.SpaceAfterComma);
        Assert.True(o.SpaceAroundOperators);
        Assert.True(o.SpaceAroundBooleanOperators);
        Assert.False(o.SpaceInsideParentheses);
        Assert.False(o.SpaceBeforeParentheses);
        Assert.True(o.LineBreakAfterSemicolon);
    }

    [Fact]
    public void WhitespaceOptions_Mutations()
    {
        var o = new WhitespaceOptions { TabStyle = "tabs", TabSize = 2, MaxLineWidth = 80 };
        Assert.Equal("tabs", o.TabStyle);
        Assert.Equal(2, o.TabSize);
        Assert.Equal(80, o.MaxLineWidth);
    }

    // ── CasingOptions ─────────────────────────────────────────────────────

    [Fact]
    public void CasingOptions_Defaults()
    {
        var o = new CasingOptions();
        Assert.Equal("UPPERCASE", o.ReservedKeywords);
        Assert.Equal("UPPERCASE", o.BuiltInFunctions);
        Assert.Equal("lowercase", o.BuiltInDataTypes);
        Assert.Equal("lowercase", o.SystemObjects);
        Assert.Equal("lowercase", o.GlobalVariables);
        Assert.Equal("AsIs", o.LocalVariables);
        Assert.Equal("AsIs", o.Identifiers);
        Assert.False(o.SyncWithDatabase);
        Assert.True(o.CamelCaseDictionary);
        Assert.True(o.ApplyOnTyping);
    }

    [Fact]
    public void CasingOptions_Mutations()
    {
        var o = new CasingOptions { ReservedKeywords = "lowercase", SyncWithDatabase = true };
        Assert.Equal("lowercase", o.ReservedKeywords);
        Assert.True(o.SyncWithDatabase);
    }

    // ── ListOptions ───────────────────────────────────────────────────────

    [Fact]
    public void ListOptions_Defaults()
    {
        var o = new ListOptions();
        Assert.Equal("trailing", o.CommaPosition);
        Assert.True(o.AlignItemsAcrossClauses);
        Assert.True(o.AlignAliases);
        Assert.True(o.OneItemPerLine);
        Assert.True(o.CollapseShortLists);
        Assert.Equal(60, o.CollapseThreshold);
        Assert.True(o.IndentListItems);
        Assert.True(o.AlignDataTypesInDdl);
        Assert.True(o.AlignValuesInInsert);
        Assert.True(o.SpaceAfterListComma);
    }

    [Fact]
    public void ListOptions_Mutations()
    {
        var o = new ListOptions { CommaPosition = "leading", CollapseThreshold = 80 };
        Assert.Equal("leading", o.CommaPosition);
        Assert.Equal(80, o.CollapseThreshold);
    }

    // ── ParenthesisOptions ────────────────────────────────────────────────

    [Fact]
    public void ParenthesisOptions_Defaults()
    {
        var o = new ParenthesisOptions();
        Assert.True(o.OpenOnSameLine);
        Assert.Equal("false", o.CloseOnNewLine);
        Assert.True(o.CollapseShort);
        Assert.Equal(40, o.CollapseThreshold);
        Assert.True(o.IndentContents);
        Assert.False(o.SpaceInside);
        Assert.False(o.RemoveRedundant);
        Assert.Equal("newLine", o.CreateTableColumns);
        Assert.Equal("newLine", o.ProcedureParameters);
        Assert.Equal("indent", o.SubqueryStyle);
    }

    // ── DmlOptions ────────────────────────────────────────────────────────

    [Fact]
    public void DmlOptions_Defaults()
    {
        var o = new DmlOptions();
        Assert.True(o.SelectItemsOnNewLine);
        Assert.True(o.SelectStarOnSameLine);
        Assert.True(o.FromOnNewLine);
        Assert.True(o.WhereOnNewLine);
        Assert.Equal("before", o.AndOrNewLine);
        Assert.Equal("alignWithWhere", o.AndOrIndent);
        Assert.True(o.GroupByOnNewLine);
        Assert.True(o.HavingOnNewLine);
        Assert.True(o.OrderByOnNewLine);
        Assert.True(o.TopOnSameLine);
        Assert.True(o.DistinctOnSameLine);
        Assert.True(o.IntoOnNewLine);
        Assert.True(o.ValuesOnNewLine);
        Assert.True(o.SetOnNewLine);
        Assert.True(o.DeleteFromOnSameLine);
        Assert.True(o.MergeWhenOnNewLine);
        Assert.True(o.CollapseShortStatements);
        Assert.Equal(80, o.CollapseThreshold);
        Assert.True(o.CollapseShortSubqueries);
        Assert.Equal(60, o.SubqueryCollapseThreshold);
    }

    // ── JoinOptions ───────────────────────────────────────────────────────

    [Fact]
    public void JoinOptions_Defaults()
    {
        var o = new JoinOptions();
        Assert.True(o.OnNewLine);
        Assert.False(o.IndentJoin);
        Assert.True(o.OnConditionNewLine);
        Assert.Equal("indent", o.OnConditionIndent);
        Assert.Equal("newLine", o.MultipleOnConditions);
        Assert.False(o.EmptyLineBeforeJoin);
        Assert.Equal("right", o.AlignJoinKeyword);
        Assert.Equal("explicit", o.JoinTypeStyle);
        Assert.True(o.CrossApplyNewLine);
    }

    // ── DdlOptions ────────────────────────────────────────────────────────

    [Fact]
    public void DdlOptions_Defaults()
    {
        var o = new DdlOptions();
        Assert.True(o.CreateTableColumnsOnNewLine);
        Assert.True(o.AlignDataTypes);
        Assert.True(o.AlignConstraints);
        Assert.False(o.ConstraintsOnNewLine);
        Assert.Equal("sameLine", o.InlineConstraintStyle);
        Assert.True(o.TableConstraintsSeparate);
        Assert.Equal("auto", o.FirstParameterOnNewLine);
        Assert.Equal("aligned", o.ParameterAlignment);
        Assert.True(o.AlignParameterDataTypes);
        Assert.True(o.AlignParameterDefaults);
        Assert.True(o.AsOnNewLine);
        Assert.True(o.BeginOnNewLine);
        Assert.True(o.CollapseShortDdl);
        Assert.Equal(60, o.CollapseThreshold);
    }

    // ── ControlFlowOptions ────────────────────────────────────────────────

    [Fact]
    public void ControlFlowOptions_Defaults()
    {
        var o = new ControlFlowOptions();
        Assert.True(o.BeginOnNewLine);
        Assert.True(o.EndOnNewLine);
        Assert.True(o.IndentBetweenBeginEnd);
        Assert.True(o.CollapseShortIfElse);
        Assert.Equal(60, o.CollapseThreshold);
        Assert.True(o.ElseOnNewLine);
        Assert.True(o.ElseAlignWithIf);
        Assert.True(o.TryCatchOnNewLine);
    }

    // ── CaseOptions ───────────────────────────────────────────────────────

    [Fact]
    public void CaseOptions_Defaults()
    {
        var o = new CaseOptions();
        Assert.True(o.WhenOnNewLine);
        Assert.False(o.ThenOnNewLine);
        Assert.True(o.ElseOnNewLine);
        Assert.True(o.EndOnNewLine);
        Assert.True(o.IndentWhen);
        Assert.True(o.AlignThen);
        Assert.True(o.CollapseShortCase);
        Assert.Equal(60, o.CollapseThreshold);
    }

    // ── CteOptions ────────────────────────────────────────────────────────

    [Fact]
    public void CteOptions_Defaults()
    {
        var o = new CteOptions();
        Assert.True(o.WithOnNewLine);
        Assert.True(o.CteBodyIndent);
        Assert.False(o.CommaBeforeCte);
        Assert.True(o.EmptyLineBetweenCtes);
    }

    // ── ExpressionOptions ─────────────────────────────────────────────────

    [Fact]
    public void ExpressionOptions_Defaults()
    {
        var o = new ExpressionOptions();
        Assert.Equal("before", o.BooleanOperatorNewLine);
        Assert.True(o.BetweenOnOneLine);
        Assert.Equal("multiLine", o.InListStyle);
        Assert.Equal(60, o.InListThreshold);
        Assert.Equal("indent", o.ExistsSubqueryIndent);
    }

    // ── FormatActionConfig ────────────────────────────────────────────────

    [Fact]
    public void FormatActionConfig_Defaults()
    {
        var o = new FormatActionConfig();
        Assert.True(o.ApplyLayout);
        Assert.True(o.ApplyCasing);
        Assert.False(o.InsertSemicolons);
        Assert.False(o.RemoveSemicolons);
        Assert.False(o.ExpandWildcards);
        Assert.False(o.QualifyObjectNames);
        Assert.True(o.AddAsKeyword);
        Assert.False(o.AddSquareBrackets);
    }

    [Fact]
    public void FormatActionConfig_Mutations()
    {
        var o = new FormatActionConfig
        {
            ApplyLayout = false,
            InsertSemicolons = true,
            ExpandWildcards = true
        };
        Assert.False(o.ApplyLayout);
        Assert.True(o.InsertSemicolons);
        Assert.True(o.ExpandWildcards);
    }
}
