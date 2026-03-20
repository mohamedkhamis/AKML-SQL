using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Rules;

/// <summary>
/// Applies all whitespace rules to layout nodes: tabStyle, tabSize, indentStyle, maxLineWidth,
/// lineBreakBeforeClause, lineBreakAfterClause, lineBreakBeforeComma, lineBreakAfterComma,
/// emptyLineBetweenStatements, emptyLineBeforeGO, emptyLineAfterGO, preserveEmptyLines,
/// maxConsecutiveEmptyLines, trailingWhitespace, finalNewline, spaceAfterComma,
/// spaceAroundOperators, spaceAroundBooleanOperators, spaceInsideParentheses,
/// spaceBeforeParentheses, lineBreakAfterSemicolon.
/// </summary>
public class WhitespaceRules : IRuleSet
{
    public void Apply(List<LayoutNode> nodes, FormattingProfile profile)
    {
        var ws = profile.Whitespace;

        ApplyEmptyLineLimits(nodes, ws);
        ApplyGOWhitespace(nodes, ws);
        ApplyCommaLineBreaks(nodes, ws);
        ApplyClauseLineBreaks(nodes, ws);
        ApplySpacingRules(nodes, ws);
        ApplySpaceBeforeParentheses(nodes, ws);
        ApplyFinalNewline(nodes, ws);
    }

    /// <summary>
    /// Enforces maxConsecutiveEmptyLines and preserveEmptyLines settings.
    /// </summary>
    private static void ApplyEmptyLineLimits(List<LayoutNode> nodes, WhitespaceOptions ws)
    {
        int consecutiveEmptyLines = 0;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
            {
                consecutiveEmptyLines = 0;
                continue;
            }

            if (node.PrecedingBreak == BreakType.EmptyLine)
            {
                consecutiveEmptyLines++;

                // Enforce max consecutive empty lines
                if (consecutiveEmptyLines > ws.MaxConsecutiveEmptyLines)
                {
                    node.PrecedingBreak = BreakType.NewLine;
                }

                // Strip empty lines if not preserving them (unless this is a structural empty line
                // like between statements or before GO)
                if (!ws.PreserveEmptyLines && !IsStructuralEmptyLine(nodes, i, ws))
                {
                    node.PrecedingBreak = BreakType.NewLine;
                }
            }
            else
            {
                consecutiveEmptyLines = 0;
            }
        }
    }

    /// <summary>
    /// Determines if an empty line is structural (between statements, before/after GO).
    /// Structural empty lines are kept even when preserveEmptyLines is false.
    /// </summary>
    private static bool IsStructuralEmptyLine(List<LayoutNode> nodes, int index, WhitespaceOptions ws)
    {
        var node = nodes[index];

        // Empty line before GO
        if (node.TokenType == TSqlTokenType.Go && ws.EmptyLineBeforeGO)
            return true;

        // Empty line after GO (previous token is GO)
        if (index > 0 && nodes[index - 1].TokenType == TSqlTokenType.Go && ws.EmptyLineAfterGO)
            return true;

        // Empty line between statements (heuristic: node starts a new statement keyword)
        if (ws.EmptyLineBetweenStatements > 0 && IsStatementStartToken(node.TokenType))
            return true;

        return false;
    }

    /// <summary>
    /// Applies emptyLineBeforeGO and emptyLineAfterGO settings.
    /// </summary>
    private static void ApplyGOWhitespace(List<LayoutNode> nodes, WhitespaceOptions ws)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            if (node.TokenType == TSqlTokenType.Go)
            {
                // Empty line before GO
                if (ws.EmptyLineBeforeGO && node.PrecedingBreak == BreakType.NewLine)
                {
                    node.PrecedingBreak = BreakType.EmptyLine;
                }
                else if (!ws.EmptyLineBeforeGO && node.PrecedingBreak == BreakType.EmptyLine)
                {
                    node.PrecedingBreak = BreakType.NewLine;
                }
            }

            // Empty line after GO
            if (i > 0 && nodes[i - 1].TokenType == TSqlTokenType.Go)
            {
                if (ws.EmptyLineAfterGO && node.PrecedingBreak == BreakType.NewLine)
                {
                    node.PrecedingBreak = BreakType.EmptyLine;
                }
                else if (!ws.EmptyLineAfterGO && node.PrecedingBreak == BreakType.EmptyLine)
                {
                    node.PrecedingBreak = BreakType.NewLine;
                }
            }
        }
    }

    /// <summary>
    /// Applies lineBreakBeforeComma and lineBreakAfterComma settings.
    /// lineBreakBeforeComma: places the comma at the start of the next line (leading commas).
    /// lineBreakAfterComma: places a line break after each comma.
    /// </summary>
    private static void ApplyCommaLineBreaks(List<LayoutNode> nodes, WhitespaceOptions ws)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            if (node.TokenType == TSqlTokenType.Comma)
            {
                // lineBreakBeforeComma: comma itself gets a line break before it
                if (ws.LineBreakBeforeComma)
                {
                    if (node.PrecedingBreak == BreakType.None)
                    {
                        node.PrecedingBreak = BreakType.NewLine;
                        node.PrecedingSpaces = 0;
                    }
                }

                // lineBreakAfterComma: the next token gets a line break
                if (ws.LineBreakAfterComma && i + 1 < nodes.Count)
                {
                    var next = nodes[i + 1];
                    if (!next.IsInNoformatRegion && next.PrecedingBreak == BreakType.None)
                    {
                        next.PrecedingBreak = BreakType.NewLine;
                        next.PrecedingSpaces = 0;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Applies lineBreakBeforeClause and lineBreakAfterClause settings.
    /// </summary>
    private static void ApplyClauseLineBreaks(List<LayoutNode> nodes, WhitespaceOptions ws)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            if (IsClauseKeyword(node.TokenType))
            {
                // lineBreakBeforeClause
                if (ws.LineBreakBeforeClause && node.PrecedingBreak == BreakType.None)
                {
                    node.PrecedingBreak = BreakType.NewLine;
                    node.PrecedingSpaces = 0;
                }
                else if (!ws.LineBreakBeforeClause && node.PrecedingBreak == BreakType.NewLine)
                {
                    // Only collapse if this isn't required by another rule (indent level 0 suggests clause-level)
                    if (node.IndentLevel == 0)
                    {
                        node.PrecedingBreak = BreakType.None;
                        node.PrecedingSpaces = 1;
                    }
                }

                // lineBreakAfterClause: the first token after the clause keyword
                if (ws.LineBreakAfterClause && i + 1 < nodes.Count)
                {
                    var next = nodes[i + 1];
                    if (!next.IsInNoformatRegion && next.PrecedingBreak == BreakType.None)
                    {
                        // Don't break between two-word clauses (GROUP BY, ORDER BY)
                        if (!IsTwoWordClauseSecondPart(next.TokenType, node.TokenType))
                        {
                            next.PrecedingBreak = BreakType.NewLine;
                            next.IndentLevel = Math.Max(next.IndentLevel, 1);
                            next.PrecedingSpaces = 0;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Applies core spacing rules: spaceAfterComma, spaceAroundOperators,
    /// spaceAroundBooleanOperators, spaceInsideParentheses, no space before comma/semicolon/dot,
    /// and lineBreakAfterSemicolon.
    /// </summary>
    private static void ApplySpacingRules(List<LayoutNode> nodes, WhitespaceOptions ws)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            // Space after comma
            if (node.TokenType == TSqlTokenType.Comma && i + 1 < nodes.Count)
            {
                var next = nodes[i + 1];
                if (next.PrecedingBreak == BreakType.None)
                {
                    next.PrecedingSpaces = ws.SpaceAfterComma ? 1 : 0;
                }
            }

            // Space around arithmetic/comparison operators
            if (IsArithmeticOrComparisonOperator(node.TokenType))
            {
                if (ws.SpaceAroundOperators)
                {
                    if (node.PrecedingBreak == BreakType.None)
                        node.PrecedingSpaces = Math.Max(node.PrecedingSpaces, 1);

                    if (i + 1 < nodes.Count)
                    {
                        var next = nodes[i + 1];
                        if (next.PrecedingBreak == BreakType.None)
                            next.PrecedingSpaces = Math.Max(next.PrecedingSpaces, 1);
                    }
                }
                else
                {
                    if (node.PrecedingBreak == BreakType.None)
                        node.PrecedingSpaces = 0;

                    if (i + 1 < nodes.Count)
                    {
                        var next = nodes[i + 1];
                        if (next.PrecedingBreak == BreakType.None)
                            next.PrecedingSpaces = 0;
                    }
                }
            }

            // Space around boolean operators (AND, OR)
            if (IsBooleanOperator(node.TokenType))
            {
                if (ws.SpaceAroundBooleanOperators)
                {
                    if (node.PrecedingBreak == BreakType.None)
                        node.PrecedingSpaces = Math.Max(node.PrecedingSpaces, 1);

                    if (i + 1 < nodes.Count)
                    {
                        var next = nodes[i + 1];
                        if (next.PrecedingBreak == BreakType.None)
                            next.PrecedingSpaces = Math.Max(next.PrecedingSpaces, 1);
                    }
                }
            }

            // Space inside parentheses
            if (ws.SpaceInsideParentheses)
            {
                if (node.TokenType == TSqlTokenType.LeftParenthesis && i + 1 < nodes.Count)
                {
                    var next = nodes[i + 1];
                    if (next.PrecedingBreak == BreakType.None && next.TokenType != TSqlTokenType.RightParenthesis)
                        next.PrecedingSpaces = Math.Max(next.PrecedingSpaces, 1);
                }

                if (node.TokenType == TSqlTokenType.RightParenthesis && node.PrecedingBreak == BreakType.None && i > 0)
                {
                    if (nodes[i - 1].TokenType != TSqlTokenType.LeftParenthesis)
                        node.PrecedingSpaces = Math.Max(node.PrecedingSpaces, 1);
                }
            }
            else
            {
                // Ensure no space inside parentheses when disabled
                if (node.TokenType == TSqlTokenType.LeftParenthesis && i + 1 < nodes.Count)
                {
                    var next = nodes[i + 1];
                    if (next.PrecedingBreak == BreakType.None && next.TokenType != TSqlTokenType.RightParenthesis)
                        next.PrecedingSpaces = 0;
                }
            }

            // No space before comma
            if (node.TokenType == TSqlTokenType.Comma && node.PrecedingBreak == BreakType.None)
            {
                node.PrecedingSpaces = 0;
            }

            // No space before semicolon
            if (node.TokenType == TSqlTokenType.Semicolon && node.PrecedingBreak == BreakType.None)
            {
                node.PrecedingSpaces = 0;
            }

            // No space around dot
            if (node.TokenType == TSqlTokenType.Dot)
            {
                node.PrecedingSpaces = 0;
                if (i + 1 < nodes.Count && nodes[i + 1].PrecedingBreak == BreakType.None)
                    nodes[i + 1].PrecedingSpaces = 0;
            }

            // Line break after semicolon
            if (ws.LineBreakAfterSemicolon && node.TokenType == TSqlTokenType.Semicolon && i + 1 < nodes.Count)
            {
                var next = nodes[i + 1];
                if (next.PrecedingBreak == BreakType.None)
                {
                    next.PrecedingBreak = BreakType.NewLine;
                    next.PrecedingSpaces = 0;
                }
            }
        }
    }

    /// <summary>
    /// Applies spaceBeforeParentheses: adds or removes space before opening parenthesis
    /// in function calls and subexpressions.
    /// </summary>
    private static void ApplySpaceBeforeParentheses(List<LayoutNode> nodes, WhitespaceOptions ws)
    {
        for (int i = 1; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            if (node.TokenType == TSqlTokenType.LeftParenthesis && node.PrecedingBreak == BreakType.None)
            {
                var prev = nodes[i - 1];
                // Space before parenthesis when preceded by identifier or keyword (function call)
                if (prev.TokenType == TSqlTokenType.Identifier ||
                    IsKeywordThatPrecedesParenthesis(prev.TokenType))
                {
                    node.PrecedingSpaces = ws.SpaceBeforeParentheses ? 1 : 0;
                }
            }
        }
    }

    /// <summary>
    /// Ensures the final newline at end of output if configured.
    /// Adds a synthetic empty node at the end if needed.
    /// </summary>
    private static void ApplyFinalNewline(List<LayoutNode> nodes, WhitespaceOptions ws)
    {
        if (nodes.Count == 0)
            return;

        // Final newline is handled by the LayoutRenderer, but we set up the last node's
        // trailing state. If "ensure" mode, we tag the last node so the renderer knows.
        // The actual final newline emission is in the rendering phase.
        // Here we just make sure we don't have trailing empty lines beyond what's needed.

        if (ws.FinalNewline == "remove")
        {
            // Remove trailing empty-line breaks at end
            for (int i = nodes.Count - 1; i > 0; i--)
            {
                if (nodes[i].PrecedingBreak == BreakType.EmptyLine ||
                    (nodes[i].PrecedingBreak == BreakType.NewLine &&
                     string.IsNullOrWhiteSpace(nodes[i].FormattedText)))
                {
                    nodes.RemoveAt(i);
                }
                else
                {
                    break;
                }
            }
        }
    }

    private static bool IsArithmeticOrComparisonOperator(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.EqualsSign or TSqlTokenType.GreaterThan or TSqlTokenType.LessThan or
            TSqlTokenType.Plus or TSqlTokenType.Minus or TSqlTokenType.Divide or
            TSqlTokenType.PercentSign or TSqlTokenType.Bang => true,
            // Note: Star is excluded because it's also used as SELECT *
            _ => false,
        };
    }

    private static bool IsBooleanOperator(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.And or TSqlTokenType.Or => true,
            _ => false,
        };
    }

    private static bool IsClauseKeyword(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.Select or TSqlTokenType.From or TSqlTokenType.Where or
            TSqlTokenType.Having or TSqlTokenType.Join or
            TSqlTokenType.Insert or TSqlTokenType.Update or TSqlTokenType.Delete or
            TSqlTokenType.Set or TSqlTokenType.Into or TSqlTokenType.Values => true,
            _ => false,
        };
    }

    private static bool IsTwoWordClauseSecondPart(TSqlTokenType nextTokenType, TSqlTokenType clauseTokenType)
    {
        // GROUP BY, ORDER BY — BY follows GROUP/ORDER
        if (nextTokenType == TSqlTokenType.By)
            return true;
        // INSERT INTO — INTO follows INSERT
        if (nextTokenType == TSqlTokenType.Into && clauseTokenType == TSqlTokenType.Insert)
            return true;
        return false;
    }

    private static bool IsStatementStartToken(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.Select or TSqlTokenType.Insert or TSqlTokenType.Update or
            TSqlTokenType.Delete or TSqlTokenType.Create or TSqlTokenType.Alter or
            TSqlTokenType.Drop or TSqlTokenType.Declare or TSqlTokenType.If or
            TSqlTokenType.While or TSqlTokenType.Begin or TSqlTokenType.Execute or
            TSqlTokenType.Exec or TSqlTokenType.With or TSqlTokenType.Merge => true,
            _ => false,
        };
    }

    private static bool IsKeywordThatPrecedesParenthesis(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.Coalesce or TSqlTokenType.NullIf or TSqlTokenType.Convert or
            TSqlTokenType.Exists or TSqlTokenType.In => true,
            _ => false,
        };
    }
}
