using Xunit;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Tests.Layout;

public class LineBreakDeciderTests
{
    private static LineBreakDecider Create(FormattingProfile? profile = null)
    {
        return new LineBreakDecider(profile ?? new FormattingProfile());
    }

    private static BreakDecision Decide(
        LineBreakDecider decider,
        TSqlTokenType tokenType,
        string text,
        ClauseContext clause = ClauseContext.None,
        bool isFirst = false,
        bool isFirstInStatement = false,
        TSqlTokenType? prevType = null,
        string? prevText = null)
    {
        return decider.Decide(tokenType, text, clause, isFirst, isFirstInStatement, prevType, prevText);
    }

    // ── isFirstToken ─────────────────────────────────────────────────────

    [Fact]
    public void Decide_FirstToken_ReturnsNone()
    {
        var d = Create();
        var result = Decide(d, TSqlTokenType.Select, "SELECT", isFirst: true);
        Assert.Equal(BreakType.None, result.Break);
        Assert.Equal(0, result.IndentDelta);
        Assert.Equal(0, result.PrecedingSpaces);
    }

    // ── isFirstInStatement ────────────────────────────────────────────────

    [Fact]
    public void Decide_FirstInStatement_WithEmptyLineBetween_ReturnsEmptyLine()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                EmptyLineBetweenStatements = 1
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Select, "SELECT", isFirstInStatement: true);
        Assert.Equal(BreakType.EmptyLine, result.Break);
    }

    [Fact]
    public void Decide_FirstInStatement_ZeroEmptyLines_NoBreak()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                EmptyLineBetweenStatements = 0
            }
        };
        var d = Create(profile);
        // Should fall through to other rules
        var result = Decide(d, TSqlTokenType.Select, "SELECT", isFirstInStatement: true);
        // Not first token, not first in statement with empty lines configured
        // Falls through to SELECT handling
        Assert.NotEqual(BreakType.EmptyLine, result.Break);
    }

    // ── GO keyword ────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Go_WithEmptyLineBefore_ReturnsEmptyLine()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                EmptyLineBeforeGO = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Go, "GO");
        Assert.Equal(BreakType.EmptyLine, result.Break);
    }

    [Fact]
    public void Decide_Go_WithoutEmptyLineBefore_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Whitespace =
            {
                EmptyLineBeforeGO = false
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Go, "GO");
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    // ── SELECT keyword ────────────────────────────────────────────────────

    [Fact]
    public void Decide_Select_NotFirstInStatement_ReturnsNewLine()
    {
        var d = Create();
        var result = Decide(d, TSqlTokenType.Select, "SELECT", clause: ClauseContext.None, isFirstInStatement: false);
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    // ── FROM clause ───────────────────────────────────────────────────────

    [Fact]
    public void Decide_From_OnNewLine_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                FromOnNewLine = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.From, "FROM", clause: ClauseContext.Select);
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    [Fact]
    public void Decide_From_NotOnNewLine_ReturnsNoneWithSpace()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                FromOnNewLine = false
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.From, "FROM", clause: ClauseContext.Select);
        Assert.Equal(BreakType.None, result.Break);
        Assert.Equal(1, result.PrecedingSpaces);
    }

    // ── WHERE clause ──────────────────────────────────────────────────────

    [Fact]
    public void Decide_Where_OnNewLine_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                WhereOnNewLine = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Where, "WHERE");
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    [Fact]
    public void Decide_Where_NotOnNewLine_ReturnsSpace()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                WhereOnNewLine = false
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Where, "WHERE");
        Assert.Equal(BreakType.None, result.Break);
        Assert.Equal(1, result.PrecedingSpaces);
    }

    // ── GROUP BY ──────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Group_OnNewLine_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                GroupByOnNewLine = true
            }
        };
        var d = Create(profile);
        // GROUP is an Identifier token in ScriptDom
        var result = Decide(d, TSqlTokenType.Identifier, "GROUP");
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    // ── ORDER BY ──────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Order_OnNewLine_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                OrderByOnNewLine = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Identifier, "ORDER");
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    // ── HAVING ────────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Having_OnNewLine_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                HavingOnNewLine = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Having, "HAVING");
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    // ── JOIN ──────────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Join_OnNewLine_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                OnNewLine = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Join, "JOIN");
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    [Fact]
    public void Decide_Inner_InFromContext_JoinOnNewLine_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                OnNewLine = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Inner, "INNER", clause: ClauseContext.From);
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    [Fact]
    public void Decide_Left_InJoinContext_JoinOnNewLine_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                OnNewLine = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Left, "LEFT", clause: ClauseContext.Join);
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    [Fact]
    public void Decide_Outer_InJoinContext_ReturnsNoBreak()
    {
        // OUTER follows LEFT/RIGHT/FULL, stays on same line
        var profile = new FormattingProfile
        {
            Join =
            {
                OnNewLine = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Outer, "OUTER", clause: ClauseContext.Join);
        // Outer falls through to default
        Assert.Equal(BreakType.None, result.Break);
    }

    // ── ON after JOIN ─────────────────────────────────────────────────────

    [Fact]
    public void Decide_OnInJoinContext_OnConditionNewLine_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                OnConditionNewLine = true,
                OnConditionIndent = "none"
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.On, "ON", clause: ClauseContext.Join);
        Assert.Equal(BreakType.NewLine, result.Break);
        Assert.Equal(0, result.IndentDelta);
    }

    [Fact]
    public void Decide_OnInJoinContext_OnConditionIndent_ReturnsNewLineWithIndent()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                OnConditionNewLine = true,
                OnConditionIndent = "indent"
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.On, "ON", clause: ClauseContext.Join);
        Assert.Equal(BreakType.NewLine, result.Break);
        Assert.Equal(1, result.IndentDelta);
    }

    [Fact]
    public void Decide_OnInJoinContext_NotOnConditionNewLine_ReturnsSpace()
    {
        var profile = new FormattingProfile
        {
            Join =
            {
                OnConditionNewLine = false
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.On, "ON", clause: ClauseContext.Join);
        Assert.Equal(BreakType.None, result.Break);
        Assert.Equal(1, result.PrecedingSpaces);
    }

    // ── AND / OR ──────────────────────────────────────────────────────────

    [Fact]
    public void Decide_And_AndOrNewLine_Before_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                AndOrNewLine = "before"
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.And, "AND");
        Assert.Equal(BreakType.NewLine, result.Break);
        Assert.Equal(1, result.IndentDelta);
    }

    [Fact]
    public void Decide_Or_AndOrNewLine_Before_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                AndOrNewLine = "before"
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Or, "OR");
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    [Fact]
    public void Decide_And_AndOrNewLine_Default_ReturnsSpace()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                AndOrNewLine = "inline"
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.And, "AND");
        Assert.Equal(BreakType.None, result.Break);
    }

    // ── BY keyword ────────────────────────────────────────────────────────

    [Fact]
    public void Decide_By_ReturnsNoneWithSpace()
    {
        var d = Create();
        var result = Decide(d, TSqlTokenType.By, "BY");
        Assert.Equal(BreakType.None, result.Break);
        Assert.Equal(1, result.PrecedingSpaces);
    }

    // ── TOP ───────────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Top_InSelectClause_TopOnSameLine_ReturnsSpace()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                TopOnSameLine = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Top, "TOP", clause: ClauseContext.Select);
        Assert.Equal(BreakType.None, result.Break);
        Assert.Equal(1, result.PrecedingSpaces);
    }

    [Fact]
    public void Decide_Top_InSelectClause_NotOnSameLine_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                TopOnSameLine = false
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Top, "TOP", clause: ClauseContext.Select);
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    // ── DISTINCT ──────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Distinct_InSelectClause_OnSameLine_ReturnsSpace()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                DistinctOnSameLine = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Distinct, "DISTINCT", clause: ClauseContext.Select);
        Assert.Equal(BreakType.None, result.Break);
    }

    // ── Comma ─────────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Comma_ReturnsNoneNoSpace()
    {
        var d = Create();
        var result = Decide(d, TSqlTokenType.Comma, ",");
        Assert.Equal(BreakType.None, result.Break);
        Assert.Equal(0, result.PrecedingSpaces);
    }

    // ── Token after comma ─────────────────────────────────────────────────

    [Fact]
    public void Decide_AfterComma_InSelectClause_OnNewLine_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                SelectItemsOnNewLine = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Identifier, "col2",
            clause: ClauseContext.Select,
            prevType: TSqlTokenType.Comma, prevText: ",");
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    [Fact]
    public void Decide_AfterComma_DefaultSpaceAfterComma_ReturnsSpace()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                SelectItemsOnNewLine = false
            },
            Whitespace =
            {
                SpaceAfterComma = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Identifier, "col2",
            clause: ClauseContext.Where,
            prevType: TSqlTokenType.Comma, prevText: ",");
        Assert.Equal(BreakType.None, result.Break);
        Assert.Equal(1, result.PrecedingSpaces);
    }

    [Fact]
    public void Decide_AfterComma_NoSpaceAfterComma_ReturnsZeroSpace()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                SelectItemsOnNewLine = false
            },
            Whitespace =
            {
                SpaceAfterComma = false
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Identifier, "col2",
            clause: ClauseContext.Where,
            prevType: TSqlTokenType.Comma, prevText: ",");
        Assert.Equal(0, result.PrecedingSpaces);
    }

    // ── SelectPendingFirstItem ────────────────────────────────────────────

    [Fact]
    public void Decide_InSelectPending_OnNewLine_ReturnsNewLine()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                SelectItemsOnNewLine = true,
                SelectStarOnSameLine = false
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Identifier, "col1",
            clause: ClauseContext.SelectPendingFirstItem);
        Assert.Equal(BreakType.NewLine, result.Break);
    }

    [Fact]
    public void Decide_InSelectPending_StarOnSameLine_ReturnsSpace()
    {
        var profile = new FormattingProfile
        {
            Dml =
            {
                SelectItemsOnNewLine = true,
                SelectStarOnSameLine = true
            }
        };
        var d = Create(profile);
        var result = Decide(d, TSqlTokenType.Star, "*",
            clause: ClauseContext.SelectPendingFirstItem);
        Assert.Equal(BreakType.None, result.Break);
        Assert.Equal(1, result.PrecedingSpaces);
    }

    // ── Semicolon ─────────────────────────────────────────────────────────

    [Fact]
    public void Decide_Semicolon_ReturnsNoneNoSpace()
    {
        var d = Create();
        var result = Decide(d, TSqlTokenType.Semicolon, ";");
        Assert.Equal(BreakType.None, result.Break);
        Assert.Equal(0, result.PrecedingSpaces);
    }

    // ── Default (single space) ────────────────────────────────────────────

    [Fact]
    public void Decide_Default_ReturnsNoneWithOneSpace()
    {
        var d = Create();
        var result = Decide(d, TSqlTokenType.Identifier, "col1");
        Assert.Equal(BreakType.None, result.Break);
        Assert.Equal(1, result.PrecedingSpaces);
    }

    // ── ClauseContext enum ────────────────────────────────────────────────

    [Fact]
    public void ClauseContext_Values()
    {
        Assert.Equal(0, (int)ClauseContext.None);
        Assert.Equal(1, (int)ClauseContext.Select);
        Assert.True((int)ClauseContext.From > 0);
        Assert.True((int)ClauseContext.Where > 0);
        Assert.True((int)ClauseContext.Join > 0);
    }
}
