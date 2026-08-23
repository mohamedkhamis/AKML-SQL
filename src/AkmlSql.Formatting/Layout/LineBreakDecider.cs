using System.Diagnostics.CodeAnalysis;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Layout;

/// <summary>
/// Determines PrecedingBreak and indentation for tokens based on clause context and profile rules.
/// </summary>
[SuppressMessage("ReSharper", "UnusedParameter.Local")]
public class LineBreakDecider(FormattingProfile profile)
{
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
        var dml = profile.Dml;
        var join = profile.Join;
        var ws = profile.Whitespace;

        // New statement: empty line between statements
        if (isFirstInStatement && ws.EmptyLineBetweenStatements > 0)
            return new BreakDecision(BreakType.EmptyLine, 0, 0);

        // GO keyword
        if (tokenType == TSqlTokenType.Go)
        {
            return new BreakDecision(
                ws.EmptyLineBeforeGo ? BreakType.EmptyLine : BreakType.NewLine, 0, 0);
        }

        // SELECT keyword at start of statement, or the main SELECT of a WITH (CTE) statement.
        // The clause tracker freezes inside the parenthesised CTE bodies, so the main SELECT
        // arrives with ClauseContext.With — without that arm it fell through to "single space"
        // and crammed onto the CTE's closing paren (") SELECT …").
        if (tokenType == TSqlTokenType.Select && currentClause is ClauseContext.None or ClauseContext.With)
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
        {
            // A join-type modifier (INNER/LEFT/RIGHT/FULL/CROSS, optionally OUTER) already broke the
            // line before itself; keep JOIN on that same line instead of breaking again — otherwise
            // "INNER JOIN" splits across two lines ("INNER" ⏎ "JOIN").
            if (prevSemanticTokenType is TSqlTokenType.Inner or TSqlTokenType.Left
                or TSqlTokenType.Right or TSqlTokenType.Full or TSqlTokenType.Cross
                or TSqlTokenType.Outer)
                return new BreakDecision(BreakType.None, 0, 1);

            // Spec 032 J1 (FMTA-006): only break in a genuinely TRACKED join context. Inside
            // parenthesized bodies the clause tracker is frozen (With/None/Where…) and the
            // modifier arm below can never fire there — so a bare JOIN that broke on pass 1
            // (then got rewritten to "INNER JOIN") collapsed on pass 2: the campaign's one
            // idempotency failure. The goldens bless INLINE joins in frozen scopes
            // (sp031-10-cte-columns), so both passes now stay inline there.
            if (currentClause is not (ClauseContext.From or ClauseContext.Join or ClauseContext.JoinOn))
                return new BreakDecision(BreakType.None, 0, 1);

            return join.OnNewLine
                ? new BreakDecision(BreakType.NewLine, 0, 0)
                : new BreakDecision(BreakType.None, 0, 1);
        }

        // JOIN type modifiers (INNER, LEFT, RIGHT, FULL, CROSS) — break before the modifier
        if (IsJoinModifier(tokenType, upperText, currentClause, prevSemanticTokenType))
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
        if (tokenType is TSqlTokenType.And or TSqlTokenType.Or)
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
            if (currentClause is ClauseContext.GroupBy or ClauseContext.OrderBy)
                return new BreakDecision(BreakType.NewLine, 1, 0);

            // Default: space after comma
            return ws.SpaceAfterComma
                ? new BreakDecision(BreakType.None, 0, 1)
                : new BreakDecision(BreakType.None, 0, 0);
        }

        // Items in SELECT clause (first item after SELECT/DISTINCT/TOP N). The clause tracker
        // never leaves SelectPendingFirstItem until the next clause keyword (its first-item
        // handoff tests Select after the context already moved on), so this branch also sees
        // every later token of the select list — AS keywords, aliases, operands after operators,
        // subquery internals. Gate the break to tokens that actually follow the SELECT header
        // (plus a subquery's own SELECT after "("), or the list fragments one token per line
        // ("COUNT(x)" ⏎ "AS" ⏎ "alias").
        if (currentClause == ClauseContext.SelectPendingFirstItem)
        {
            bool followsSelectHeader =
                prevSemanticTokenType is TSqlTokenType.Select or TSqlTokenType.Distinct
                    or TSqlTokenType.Top or TSqlTokenType.Integer
                || prevSemanticTokenText?.ToUpperInvariant() is "PERCENT" or "TIES";
            bool isSubquerySelect = tokenType == TSqlTokenType.Select
                && prevSemanticTokenType == TSqlTokenType.LeftParenthesis;

            // As and Semicolon can never start a select item ("SELECT 1;" must not break the
            // terminator off the item when the prev token — the Integer — is in the gate).
            if (dml.SelectItemsOnNewLine
                && (followsSelectHeader || isSubquerySelect)
                && tokenType is not TSqlTokenType.As and not TSqlTokenType.Semicolon
                && !(upperText == "*" && dml.SelectStarOnSameLine))
                return new BreakDecision(BreakType.NewLine, 1, 0);
            return new BreakDecision(BreakType.None, 0, 1);
        }

        // Semicolons
        if (tokenType == TSqlTokenType.Semicolon)
            return new BreakDecision(BreakType.None, 0, 0);

        // Default: single space
        return new BreakDecision(BreakType.None, 0, 1);
    }

    private static bool IsJoinModifier(
        TSqlTokenType tokenType, string upperText, ClauseContext currentClause,
        TSqlTokenType? prevSemanticTokenType)
    {
        // OUTER follows LEFT/RIGHT/FULL, stays on same line; anything else is not a modifier.
        bool isModifierToken = tokenType is TSqlTokenType.Inner or TSqlTokenType.Left
            or TSqlTokenType.Right or TSqlTokenType.Full or TSqlTokenType.Cross;
        if (!isModifierToken) return false;

        // These tokens often appear immediately before JOIN. JoinOn is included so a *chained*
        // join (the LEFT/INNER that starts the next join after a prior "... ON <cond>") also breaks.
        // Only a genuinely tracked join context breaks (spec 032 J1: inside parenthesized
        // bodies the tracker is frozen, and the JOIN arm above stays inline to match — both
        // passes must make the SAME decision or formatting oscillates, per FMTA-006).
        return currentClause is ClauseContext.From or ClauseContext.Join or ClauseContext.JoinOn;
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
    // ReSharper disable UnusedMember.Global
    Other
    // ReSharper restore UnusedMember.Global
}
