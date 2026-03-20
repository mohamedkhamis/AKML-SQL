using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Layout;

/// <summary>
/// Determines PrecedingBreak and indentation for tokens based on clause context and profile rules.
/// </summary>
public class LineBreakDecider
{
    private readonly FormattingProfile _profile;

    public LineBreakDecider(FormattingProfile profile)
    {
        _profile = profile;
    }

    /// <summary>
    /// Decides whether a line break should precede this token, and at what indent level.
    /// </summary>
    public BreakDecision Decide(
        TSqlTokenType tokenType,
        string tokenText,
        ClauseContext currentClause,
        bool isFirstToken,
        bool isFirstInStatement,
        TSqlTokenType? prevSemanticTokenType,
        string? prevSemanticTokenText)
    {
        if (isFirstToken)
            return new BreakDecision(BreakType.None, 0, 0);

        var upperText = tokenText.ToUpperInvariant();
        var dml = _profile.Dml;
        var join = _profile.Join;
        var ws = _profile.Whitespace;

        // New statement: empty line between statements
        if (isFirstInStatement && ws.EmptyLineBetweenStatements > 0)
            return new BreakDecision(BreakType.EmptyLine, 0, 0);

        // GO keyword
        if (tokenType == TSqlTokenType.Go)
        {
            return new BreakDecision(
                ws.EmptyLineBeforeGO ? BreakType.EmptyLine : BreakType.NewLine, 0, 0);
        }

        // SELECT keyword at start of statement
        if (tokenType == TSqlTokenType.Select && currentClause == ClauseContext.None)
            return isFirstInStatement
                ? new BreakDecision(BreakType.None, 0, 0)
                : new BreakDecision(BreakType.NewLine, 0, 0);

        // FROM clause
        if (tokenType == TSqlTokenType.From && currentClause != ClauseContext.None)
            return dml.FromOnNewLine
                ? new BreakDecision(BreakType.NewLine, 0, 0)
                : new BreakDecision(BreakType.None, 0, 1);

        // WHERE clause
        if (tokenType == TSqlTokenType.Where)
            return dml.WhereOnNewLine
                ? new BreakDecision(BreakType.NewLine, 0, 0)
                : new BreakDecision(BreakType.None, 0, 1);

        // GROUP keyword (for GROUP BY)
        if (upperText == "GROUP")
            return dml.GroupByOnNewLine
                ? new BreakDecision(BreakType.NewLine, 0, 0)
                : new BreakDecision(BreakType.None, 0, 1);

        // ORDER keyword (for ORDER BY)
        if (upperText == "ORDER")
            return dml.OrderByOnNewLine
                ? new BreakDecision(BreakType.NewLine, 0, 0)
                : new BreakDecision(BreakType.None, 0, 1);

        // HAVING clause
        if (tokenType == TSqlTokenType.Having)
            return dml.HavingOnNewLine
                ? new BreakDecision(BreakType.NewLine, 0, 0)
                : new BreakDecision(BreakType.None, 0, 1);

        // JOIN keywords
        if (tokenType == TSqlTokenType.Join)
            return join.OnNewLine
                ? new BreakDecision(BreakType.NewLine, 0, 0)
                : new BreakDecision(BreakType.None, 0, 1);

        // JOIN type modifiers (INNER, LEFT, RIGHT, FULL, CROSS) — break before the modifier
        if (IsJoinModifier(tokenType, upperText, currentClause))
            return join.OnNewLine
                ? new BreakDecision(BreakType.NewLine, 0, 0)
                : new BreakDecision(BreakType.None, 0, 1);

        // ON keyword after JOIN
        if (tokenType == TSqlTokenType.On && currentClause == ClauseContext.Join)
        {
            if (join.OnConditionNewLine)
            {
                int indent = join.OnConditionIndent == "indent" ? 1 : 0;
                return new BreakDecision(BreakType.NewLine, indent, 0);
            }
            return new BreakDecision(BreakType.None, 0, 1);
        }

        // AND/OR
        if (tokenType == TSqlTokenType.And || tokenType == TSqlTokenType.Or)
        {
            return dml.AndOrNewLine switch
            {
                "before" => new BreakDecision(BreakType.NewLine, 1, 0),
                // "after" is handled by the token AFTER and/or
                _ => new BreakDecision(BreakType.None, 0, 1),
            };
        }

        // BY keyword — always follows GROUP/ORDER, keep on same line
        if (tokenType == TSqlTokenType.By)
            return new BreakDecision(BreakType.None, 0, 1);

        // TOP keyword after SELECT
        if (tokenType == TSqlTokenType.Top && currentClause == ClauseContext.Select)
            return dml.TopOnSameLine
                ? new BreakDecision(BreakType.None, 0, 1)
                : new BreakDecision(BreakType.NewLine, 1, 0);

        // DISTINCT keyword after SELECT
        if (tokenType == TSqlTokenType.Distinct && currentClause == ClauseContext.Select)
            return dml.DistinctOnSameLine
                ? new BreakDecision(BreakType.None, 0, 1)
                : new BreakDecision(BreakType.NewLine, 1, 0);

        // Comma handling
        if (tokenType == TSqlTokenType.Comma)
            return new BreakDecision(BreakType.None, 0, 0); // No space before comma

        // Token after comma in SELECT list
        if (prevSemanticTokenType == TSqlTokenType.Comma)
        {
            if (currentClause == ClauseContext.Select && dml.SelectItemsOnNewLine)
                return new BreakDecision(BreakType.NewLine, 1, 0);
            if (currentClause == ClauseContext.GroupBy || currentClause == ClauseContext.OrderBy)
                return new BreakDecision(BreakType.NewLine, 1, 0);

            // Default: space after comma
            return ws.SpaceAfterComma
                ? new BreakDecision(BreakType.None, 0, 1)
                : new BreakDecision(BreakType.None, 0, 0);
        }

        // Items in SELECT clause (first item after SELECT/DISTINCT/TOP N)
        if (currentClause == ClauseContext.SelectPendingFirstItem)
        {
            if (dml.SelectItemsOnNewLine && !(upperText == "*" && dml.SelectStarOnSameLine))
                return new BreakDecision(BreakType.NewLine, 1, 0);
            return new BreakDecision(BreakType.None, 0, 1);
        }

        // Semicolons
        if (tokenType == TSqlTokenType.Semicolon)
            return new BreakDecision(BreakType.None, 0, 0);

        // Default: single space
        return new BreakDecision(BreakType.None, 0, 1);
    }

    private static bool IsJoinModifier(TSqlTokenType tokenType, string upperText, ClauseContext currentClause)
    {
        // These tokens often appear immediately before JOIN
        if (currentClause == ClauseContext.From || currentClause == ClauseContext.Join)
        {
            return tokenType switch
            {
                TSqlTokenType.Inner or TSqlTokenType.Left or TSqlTokenType.Right or
                TSqlTokenType.Full or TSqlTokenType.Cross => true,
                TSqlTokenType.Outer => false, // OUTER follows LEFT/RIGHT/FULL, stays on same line
                _ => false,
            };
        }
        return false;
    }
}

public record struct BreakDecision(BreakType Break, int IndentDelta, int PrecedingSpaces);

/// <summary>
/// Tracks the current clause context for formatting decisions.
/// </summary>
public enum ClauseContext
{
    None,
    Select,
    SelectPendingFirstItem,
    From,
    Where,
    GroupBy,
    OrderBy,
    Having,
    Join,
    JoinOn,
    Insert,
    Update,
    Delete,
    Set,
    Values,
    With,
    Other
}
