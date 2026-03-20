using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Rules;

/// <summary>
/// Implements control flow formatting rules as a post-processing pass:
///
/// IF/ELSE: beginOnNewLine, endOnNewLine, indentBetweenBeginEnd, collapseShortIfElse,
///          collapseThreshold, elseOnNewLine, elseAlignWithIf
/// TRY/CATCH: tryCatchOnNewLine
/// CASE: whenOnNewLine, thenOnNewLine, elseOnNewLine, endOnNewLine, indentWhen,
///       alignThen, collapseShortCase, collapseThreshold
/// CTE: withOnNewLine, cteBodyIndent, commaBeforeCte, emptyLineBetweenCtes
/// Expressions: booleanOperatorNewLine, betweenOnOneLine, inListStyle, existsSubqueryIndent
/// </summary>
public class ControlFlowRules : IRuleSet
{
    public void Apply(List<LayoutNode> nodes, FormattingProfile profile)
    {
        ApplyIfElseRules(nodes, profile.ControlFlow);
        ApplyTryCatchRules(nodes, profile.ControlFlow);
        ApplyBeginEndRules(nodes, profile.ControlFlow);
        ApplyCaseRules(nodes, profile.Case);
        ApplyCteRules(nodes, profile.Cte);
        ApplyExpressionRules(nodes, profile.Expression);
    }

    // -----------------------------------------------------------------------
    // IF/ELSE rules
    // -----------------------------------------------------------------------

    private static void ApplyIfElseRules(List<LayoutNode> nodes, ControlFlowOptions cf)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            // ELSE on new line and alignment with IF
            if (node.TokenType == TSqlTokenType.Else)
            {
                if (cf.ElseOnNewLine && node.PrecedingBreak == BreakType.None)
                {
                    node.PrecedingBreak = BreakType.NewLine;
                    node.PrecedingSpaces = 0;
                }

                if (cf.ElseAlignWithIf && node.PrecedingBreak != BreakType.None)
                {
                    // Find the matching IF and align indent
                    int ifLevel = FindMatchingIfIndentLevel(nodes, i);
                    node.IndentLevel = ifLevel;
                }
            }
        }

        // Collapse short IF/ELSE
        if (cf.CollapseShortIfElse)
        {
            ApplyCollapseShortIfElse(nodes, cf.CollapseThreshold);
        }
    }

    private static void ApplyCollapseShortIfElse(List<LayoutNode> nodes, int threshold)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.If)
                continue;

            // Find the extent of the IF block (up to ELSE or next statement)
            int ifEnd = FindIfBlockEnd(nodes, i);

            // Don't collapse if it contains BEGIN/END
            if (ContainsBeginEnd(nodes, i, ifEnd))
                continue;

            int totalLength = MeasureLength(nodes, i, ifEnd);
            if (totalLength <= threshold)
            {
                // Collapse the IF block onto one line
                for (int j = i + 1; j < ifEnd && j < nodes.Count; j++)
                {
                    if (nodes[j].PrecedingBreak == BreakType.NewLine ||
                        nodes[j].PrecedingBreak == BreakType.EmptyLine)
                    {
                        nodes[j].PrecedingBreak = BreakType.None;
                        nodes[j].PrecedingSpaces = 1;
                        nodes[j].IndentLevel = 0;
                    }
                }
            }
        }
    }

    private static int FindMatchingIfIndentLevel(List<LayoutNode> nodes, int elseIndex)
    {
        // Walk backward to find the matching IF, accounting for nesting
        int depth = 0;
        for (int i = elseIndex - 1; i >= 0; i--)
        {
            if (nodes[i].TokenType == TSqlTokenType.Else)
                depth++;
            if (nodes[i].TokenType == TSqlTokenType.If)
            {
                if (depth == 0)
                    return nodes[i].IndentLevel;
                depth--;
            }
        }
        return 0;
    }

    private static int FindIfBlockEnd(List<LayoutNode> nodes, int ifIndex)
    {
        // Simple heuristic: find the next statement-level keyword or ELSE
        int beginDepth = 0;
        for (int i = ifIndex + 1; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Begin)
                beginDepth++;
            if (nodes[i].TokenType == TSqlTokenType.End)
            {
                beginDepth--;
                if (beginDepth <= 0)
                    return i + 1;
            }

            if (beginDepth == 0)
            {
                if (nodes[i].TokenType == TSqlTokenType.Else ||
                    nodes[i].TokenType == TSqlTokenType.Semicolon ||
                    nodes[i].TokenType == TSqlTokenType.Go)
                    return i;

                // Next statement start at indent level 0
                if (IsStatementStart(nodes[i].TokenType) &&
                    nodes[i].PrecedingBreak != BreakType.None &&
                    i > ifIndex + 1)
                    return i;
            }
        }
        return nodes.Count;
    }

    private static bool ContainsBeginEnd(List<LayoutNode> nodes, int start, int end)
    {
        for (int i = start; i < end && i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Begin)
                return true;
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // TRY/CATCH rules
    // -----------------------------------------------------------------------

    private static void ApplyTryCatchRules(List<LayoutNode> nodes, ControlFlowOptions cf)
    {
        if (!cf.TryCatchOnNewLine)
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            var upper = node.FormattedText.ToUpperInvariant();

            // BEGIN TRY, END TRY, BEGIN CATCH, END CATCH on new lines
            if (upper is "TRY" or "CATCH")
            {
                // The BEGIN/END before TRY/CATCH should already be on a new line
                // Make sure TRY/CATCH stays with its BEGIN/END
                if (node.PrecedingBreak == BreakType.NewLine)
                {
                    node.PrecedingBreak = BreakType.None;
                    node.PrecedingSpaces = 1;
                }
            }

            // Ensure BEGIN CATCH is on a new line
            if (node.TokenType == TSqlTokenType.Begin && i + 1 < nodes.Count)
            {
                var nextUpper = nodes[i + 1].FormattedText.ToUpperInvariant();
                if (nextUpper is "TRY" or "CATCH")
                {
                    if (node.PrecedingBreak == BreakType.None)
                    {
                        node.PrecedingBreak = BreakType.NewLine;
                        node.IndentLevel = 0;
                        node.PrecedingSpaces = 0;
                    }
                }
            }

            // Ensure END TRY and END CATCH are on new lines
            if (node.TokenType == TSqlTokenType.End && i + 1 < nodes.Count)
            {
                var nextUpper = nodes[i + 1].FormattedText.ToUpperInvariant();
                if (nextUpper is "TRY" or "CATCH")
                {
                    if (node.PrecedingBreak == BreakType.None)
                    {
                        node.PrecedingBreak = BreakType.NewLine;
                        node.IndentLevel = 0;
                        node.PrecedingSpaces = 0;
                    }
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // BEGIN/END rules
    // -----------------------------------------------------------------------

    private static void ApplyBeginEndRules(List<LayoutNode> nodes, ControlFlowOptions cf)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            // BEGIN on new line
            if (node.TokenType == TSqlTokenType.Begin)
            {
                // Skip BEGIN TRY / BEGIN CATCH — handled by TryCatch rules
                if (i + 1 < nodes.Count)
                {
                    var nextUpper = nodes[i + 1].FormattedText.ToUpperInvariant();
                    if (nextUpper is "TRY" or "CATCH" or "TRANSACTION" or "TRAN")
                        continue;
                }

                if (cf.BeginOnNewLine && node.PrecedingBreak == BreakType.None)
                {
                    node.PrecedingBreak = BreakType.NewLine;
                    node.PrecedingSpaces = 0;
                }
            }

            // END on new line
            if (node.TokenType == TSqlTokenType.End)
            {
                // Skip END TRY / END CATCH
                if (i + 1 < nodes.Count)
                {
                    var nextUpper = nodes[i + 1].FormattedText.ToUpperInvariant();
                    if (nextUpper is "TRY" or "CATCH")
                        continue;
                }

                if (cf.EndOnNewLine && node.PrecedingBreak == BreakType.None)
                {
                    node.PrecedingBreak = BreakType.NewLine;
                    node.PrecedingSpaces = 0;
                }
            }
        }

        // Indent between BEGIN...END
        if (cf.IndentBetweenBeginEnd)
        {
            ApplyIndentBetweenBeginEnd(nodes);
        }
    }

    private static void ApplyIndentBetweenBeginEnd(List<LayoutNode> nodes)
    {
        // Track BEGIN/END nesting and adjust indent levels
        var beginStack = new Stack<int>(); // stores the indent level of the BEGIN

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            if (node.TokenType == TSqlTokenType.Begin)
            {
                // Skip BEGIN TRANSACTION
                if (i + 1 < nodes.Count)
                {
                    var nextUpper = nodes[i + 1].FormattedText.ToUpperInvariant();
                    if (nextUpper is "TRANSACTION" or "TRAN")
                        continue;
                }

                beginStack.Push(node.IndentLevel);
            }
            else if (node.TokenType == TSqlTokenType.End && beginStack.Count > 0)
            {
                // Skip END TRY / END CATCH for indent purposes
                if (i + 1 < nodes.Count)
                {
                    var nextUpper = nodes[i + 1].FormattedText.ToUpperInvariant();
                    if (nextUpper is "TRY" or "CATCH")
                    {
                        beginStack.Pop();
                        continue;
                    }
                }

                int beginLevel = beginStack.Pop();

                // END should align with BEGIN
                if (node.PrecedingBreak != BreakType.None)
                {
                    node.IndentLevel = beginLevel;
                }
            }
            else if (beginStack.Count > 0 && node.PrecedingBreak != BreakType.None)
            {
                // Content between BEGIN and END: indent relative to BEGIN
                int beginLevel = beginStack.Peek();
                node.IndentLevel = Math.Max(node.IndentLevel, beginLevel + 1);
            }
        }
    }

    // -----------------------------------------------------------------------
    // CASE rules
    // -----------------------------------------------------------------------

    private static void ApplyCaseRules(List<LayoutNode> nodes, CaseOptions caseOpts)
    {
        // Find CASE...END blocks
        var caseStack = new Stack<int>();

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            if (node.TokenType == TSqlTokenType.Case)
            {
                caseStack.Push(i);
                continue;
            }

            if (node.TokenType == TSqlTokenType.End && caseStack.Count > 0)
            {
                int caseStart = caseStack.Pop();
                int caseEnd = i;

                // Check for collapse
                if (caseOpts.CollapseShortCase)
                {
                    int totalLength = MeasureLength(nodes, caseStart, caseEnd + 1);
                    if (totalLength <= caseOpts.CollapseThreshold)
                    {
                        // Collapse the entire CASE onto one line
                        for (int j = caseStart + 1; j <= caseEnd; j++)
                        {
                            if (nodes[j].PrecedingBreak == BreakType.NewLine ||
                                nodes[j].PrecedingBreak == BreakType.EmptyLine)
                            {
                                nodes[j].PrecedingBreak = BreakType.None;
                                nodes[j].PrecedingSpaces = 1;
                                nodes[j].IndentLevel = 0;
                            }
                        }
                        continue; // Skip detailed formatting since we collapsed
                    }
                }

                int caseIndent = nodes[caseStart].IndentLevel;

                // Apply formatting within CASE...END
                for (int j = caseStart + 1; j < caseEnd; j++)
                {
                    if (nodes[j].IsInNoformatRegion)
                        continue;

                    // WHEN on new line
                    if (nodes[j].TokenType == TSqlTokenType.When && caseOpts.WhenOnNewLine)
                    {
                        if (nodes[j].PrecedingBreak == BreakType.None)
                        {
                            nodes[j].PrecedingBreak = BreakType.NewLine;
                            nodes[j].PrecedingSpaces = 0;
                        }

                        if (caseOpts.IndentWhen)
                        {
                            nodes[j].IndentLevel = caseIndent + 1;
                        }
                        else
                        {
                            nodes[j].IndentLevel = caseIndent;
                        }
                    }

                    // THEN on new line or same line
                    if (nodes[j].TokenType == TSqlTokenType.Then)
                    {
                        if (caseOpts.ThenOnNewLine)
                        {
                            if (nodes[j].PrecedingBreak == BreakType.None)
                            {
                                nodes[j].PrecedingBreak = BreakType.NewLine;
                                nodes[j].IndentLevel = caseIndent + 2;
                                nodes[j].PrecedingSpaces = 0;
                            }
                        }
                    }

                    // ELSE on new line (within CASE, not IF/ELSE)
                    if (nodes[j].TokenType == TSqlTokenType.Else && caseOpts.ElseOnNewLine)
                    {
                        if (nodes[j].PrecedingBreak == BreakType.None)
                        {
                            nodes[j].PrecedingBreak = BreakType.NewLine;
                            nodes[j].IndentLevel = caseOpts.IndentWhen ? caseIndent + 1 : caseIndent;
                            nodes[j].PrecedingSpaces = 0;
                        }
                    }
                }

                // END on new line
                if (caseOpts.EndOnNewLine)
                {
                    if (nodes[caseEnd].PrecedingBreak == BreakType.None)
                    {
                        nodes[caseEnd].PrecedingBreak = BreakType.NewLine;
                        nodes[caseEnd].IndentLevel = caseIndent;
                        nodes[caseEnd].PrecedingSpaces = 0;
                    }
                }

                // Align THEN keywords if requested
                if (caseOpts.AlignThen)
                {
                    AlignThenKeywords(nodes, caseStart, caseEnd);
                }
            }
        }
    }

    /// <summary>
    /// Aligns THEN keywords to the same column within a CASE expression.
    /// </summary>
    private static void AlignThenKeywords(List<LayoutNode> nodes, int caseStart, int caseEnd)
    {
        // Find all WHEN...THEN pairs and compute max width before THEN
        var thenPositions = new List<(int thenIdx, int whenIdx)>();

        int lastWhen = -1;
        for (int j = caseStart + 1; j < caseEnd; j++)
        {
            if (nodes[j].TokenType == TSqlTokenType.When)
                lastWhen = j;
            if (nodes[j].TokenType == TSqlTokenType.Then && lastWhen >= 0)
            {
                thenPositions.Add((j, lastWhen));
                lastWhen = -1;
            }
        }

        if (thenPositions.Count < 2)
            return;

        // Calculate max width from WHEN to THEN
        int maxWidth = 0;
        var widths = new List<int>();
        foreach (var (thenIdx, whenIdx) in thenPositions)
        {
            int width = MeasureWidth(nodes, whenIdx, thenIdx);
            widths.Add(width);
            maxWidth = Math.Max(maxWidth, width);
        }

        // Pad THEN tokens to align
        for (int p = 0; p < thenPositions.Count; p++)
        {
            var (thenIdx, _) = thenPositions[p];
            if (nodes[thenIdx].PrecedingBreak == BreakType.None)
            {
                int padding = maxWidth - widths[p] + 1;
                nodes[thenIdx].PrecedingSpaces = Math.Max(padding, 1);
            }
        }
    }

    // -----------------------------------------------------------------------
    // CTE rules
    // -----------------------------------------------------------------------

    private static void ApplyCteRules(List<LayoutNode> nodes, CteOptions cte)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            // WITH on new line
            if (node.TokenType == TSqlTokenType.With && cte.WithOnNewLine)
            {
                // Verify this is a CTE WITH (followed by identifier AS)
                if (!IsCteWith(nodes, i))
                    continue;

                if (node.PrecedingBreak == BreakType.None && i > 0)
                {
                    node.PrecedingBreak = BreakType.NewLine;
                    node.IndentLevel = 0;
                    node.PrecedingSpaces = 0;
                }
            }
        }

        // CTE body indent and comma handling
        ApplyCteBodyFormatting(nodes, cte);
    }

    private static void ApplyCteBodyFormatting(List<LayoutNode> nodes, CteOptions cte)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.With)
                continue;

            if (!IsCteWith(nodes, i))
                continue;

            // Process CTE definitions from WITH to the final SELECT
            int cteRegionEnd = FindCteRegionEnd(nodes, i);
            int withIndent = nodes[i].IndentLevel;

            // Track individual CTEs within the WITH clause
            int parenDepth = 0;
            bool inCteBody = false;
            int cteCount = 0;

            for (int j = i + 1; j < cteRegionEnd; j++)
            {
                if (nodes[j].IsInNoformatRegion)
                    continue;

                if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis)
                {
                    parenDepth++;
                    if (parenDepth == 1)
                        inCteBody = true;
                }
                else if (nodes[j].TokenType == TSqlTokenType.RightParenthesis)
                {
                    parenDepth--;
                    if (parenDepth == 0)
                    {
                        inCteBody = false;
                        cteCount++;
                    }
                }

                // Indent CTE body contents
                if (inCteBody && cte.CteBodyIndent && parenDepth == 1 &&
                    nodes[j].PrecedingBreak != BreakType.None)
                {
                    nodes[j].IndentLevel = Math.Max(nodes[j].IndentLevel, withIndent + 1);
                }

                // Comma between CTEs
                if (parenDepth == 0 && nodes[j].TokenType == TSqlTokenType.Comma)
                {
                    if (cte.CommaBeforeCte)
                    {
                        // Leading comma style: comma on next line before CTE name
                        if (j + 1 < cteRegionEnd && nodes[j + 1].PrecedingBreak != BreakType.None)
                        {
                            // Move comma to next line
                            nodes[j].PrecedingBreak = nodes[j + 1].PrecedingBreak;
                            nodes[j].IndentLevel = nodes[j + 1].IndentLevel;
                            nodes[j + 1].PrecedingBreak = BreakType.None;
                            nodes[j + 1].PrecedingSpaces = 1;
                        }
                    }

                    // Empty line between CTEs
                    if (cte.EmptyLineBetweenCtes && j + 1 < cteRegionEnd)
                    {
                        // The token after the comma (or the comma itself for leading) gets empty line
                        if (cte.CommaBeforeCte)
                        {
                            if (nodes[j].PrecedingBreak == BreakType.NewLine)
                                nodes[j].PrecedingBreak = BreakType.EmptyLine;
                        }
                        else
                        {
                            var next = nodes[j + 1];
                            if (next.PrecedingBreak == BreakType.NewLine)
                                next.PrecedingBreak = BreakType.EmptyLine;
                        }
                    }
                }
            }

            i = cteRegionEnd - 1;
        }
    }

    private static bool IsCteWith(List<LayoutNode> nodes, int withIndex)
    {
        // A CTE WITH is followed by an identifier and then AS (
        // Walk forward to check pattern: WITH <name> AS (
        int j = withIndex + 1;
        while (j < nodes.Count && j <= withIndex + 5)
        {
            if (nodes[j].TokenType == TSqlTokenType.Identifier)
            {
                // Look for AS after the identifier
                for (int k = j + 1; k < nodes.Count && k <= j + 3; k++)
                {
                    if (nodes[k].TokenType == TSqlTokenType.As)
                        return true;
                    if (nodes[k].TokenType == TSqlTokenType.LeftParenthesis)
                        return true; // WITH name (columns) AS (
                }
                return false;
            }
            j++;
        }
        return false;
    }

    private static int FindCteRegionEnd(List<LayoutNode> nodes, int withIndex)
    {
        // CTE region ends at the final SELECT/INSERT/UPDATE/DELETE/MERGE that uses the CTEs
        int parenDepth = 0;

        for (int i = withIndex + 1; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.LeftParenthesis)
                parenDepth++;
            else if (nodes[i].TokenType == TSqlTokenType.RightParenthesis)
                parenDepth--;

            // The final SELECT (outside of CTE parens) marks the end of the CTE region
            if (parenDepth == 0 &&
                (nodes[i].TokenType == TSqlTokenType.Select ||
                 nodes[i].TokenType == TSqlTokenType.Insert ||
                 nodes[i].TokenType == TSqlTokenType.Update ||
                 nodes[i].TokenType == TSqlTokenType.Delete ||
                 nodes[i].TokenType == TSqlTokenType.Merge))
            {
                return i;
            }

            if (nodes[i].TokenType == TSqlTokenType.Semicolon || nodes[i].TokenType == TSqlTokenType.Go)
                return i;
        }

        return nodes.Count;
    }

    // -----------------------------------------------------------------------
    // Expression rules
    // -----------------------------------------------------------------------

    private static void ApplyExpressionRules(List<LayoutNode> nodes, ExpressionOptions expr)
    {
        ApplyBooleanOperatorNewLine(nodes, expr);
        ApplyBetweenOnOneLine(nodes, expr);
        ApplyInListStyle(nodes, expr);
        ApplyExistsSubqueryIndent(nodes, expr);
    }

    /// <summary>
    /// booleanOperatorNewLine: "before" puts break before AND/OR, "after" puts break after,
    /// "none" keeps them inline.
    /// </summary>
    private static void ApplyBooleanOperatorNewLine(List<LayoutNode> nodes, ExpressionOptions expr)
    {
        // This overlaps with DmlRules.AndOrNewLine but applies to all boolean operators,
        // not just those in WHERE clauses. Only adjust if not already handled by DML rules.
        // We skip adjustment here to avoid double-processing — DmlRules handles the primary
        // AND/OR formatting. This method handles boolean operators in other contexts
        // (e.g., CASE WHEN, CHECK constraints, computed columns).

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            if (node.TokenType != TSqlTokenType.And && node.TokenType != TSqlTokenType.Or)
                continue;

            // Only process if in a non-WHERE context (CASE, CHECK, etc.)
            if (IsInWhereOrJoinContext(nodes, i))
                continue;

            switch (expr.BooleanOperatorNewLine)
            {
                case "before":
                    if (node.PrecedingBreak == BreakType.None)
                    {
                        node.PrecedingBreak = BreakType.NewLine;
                        node.IndentLevel = Math.Max(node.IndentLevel, 1);
                        node.PrecedingSpaces = 0;
                    }
                    break;

                case "after":
                    if (node.PrecedingBreak == BreakType.NewLine)
                    {
                        node.PrecedingBreak = BreakType.None;
                        node.PrecedingSpaces = 1;
                    }
                    if (i + 1 < nodes.Count && nodes[i + 1].PrecedingBreak == BreakType.None)
                    {
                        nodes[i + 1].PrecedingBreak = BreakType.NewLine;
                        nodes[i + 1].IndentLevel = 1;
                        nodes[i + 1].PrecedingSpaces = 0;
                    }
                    break;

                case "none":
                    if (node.PrecedingBreak == BreakType.NewLine)
                    {
                        node.PrecedingBreak = BreakType.None;
                        node.PrecedingSpaces = 1;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// betweenOnOneLine: when true, keeps BETWEEN x AND y on a single line.
    /// </summary>
    private static void ApplyBetweenOnOneLine(List<LayoutNode> nodes, ExpressionOptions expr)
    {
        if (!expr.BetweenOnOneLine)
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.Between)
                continue;

            // Find the AND that pairs with this BETWEEN
            int andIdx = FindBetweenAnd(nodes, i);
            if (andIdx < 0)
                continue;

            // Ensure everything from BETWEEN to the end value after AND is on one line
            // Find the end of the AND value (next boolean operator, comma, or clause keyword)
            int endOfAnd = FindBetweenEndValue(nodes, andIdx);

            for (int j = i + 1; j < endOfAnd && j < nodes.Count; j++)
            {
                if (nodes[j].PrecedingBreak == BreakType.NewLine ||
                    nodes[j].PrecedingBreak == BreakType.EmptyLine)
                {
                    nodes[j].PrecedingBreak = BreakType.None;
                    nodes[j].PrecedingSpaces = 1;
                    nodes[j].IndentLevel = 0;
                }
            }
        }
    }

    /// <summary>
    /// inListStyle: controls IN (...) list formatting.
    /// "singleLine" keeps all items on one line.
    /// "multiLine" puts each item on its own line.
    /// "auto" uses threshold to decide.
    /// </summary>
    private static void ApplyInListStyle(List<LayoutNode> nodes, ExpressionOptions expr)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.In)
                continue;

            // Find the opening parenthesis after IN
            int openParen = -1;
            for (int j = i + 1; j < nodes.Count && j <= i + 3; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis)
                {
                    openParen = j;
                    break;
                }
            }
            if (openParen < 0)
                continue;

            // Find matching close paren
            int closeParen = FindMatchingParen(nodes, openParen);
            if (closeParen < 0)
                continue;

            // Skip if contains subquery
            bool hasSubquery = false;
            for (int j = openParen + 1; j < closeParen; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.Select)
                {
                    hasSubquery = true;
                    break;
                }
            }
            if (hasSubquery)
                continue;

            switch (expr.InListStyle)
            {
                case "singleLine":
                    CollapseRange(nodes, openParen + 1, closeParen);
                    break;

                case "multiLine":
                    ExpandToMultiLine(nodes, openParen, closeParen);
                    break;

                case "auto":
                    int length = MeasureLength(nodes, openParen, closeParen + 1);
                    if (length > expr.InListThreshold)
                        ExpandToMultiLine(nodes, openParen, closeParen);
                    else
                        CollapseRange(nodes, openParen + 1, closeParen);
                    break;
            }
        }
    }

    /// <summary>
    /// existsSubqueryIndent: controls indentation of subqueries after EXISTS.
    /// "indent" — indent the subquery, "alignWithExists" — align with EXISTS keyword.
    /// </summary>
    private static void ApplyExistsSubqueryIndent(List<LayoutNode> nodes, ExpressionOptions expr)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.Exists)
                continue;

            int existsIndent = nodes[i].IndentLevel;

            // Find the opening paren after EXISTS
            int openParen = -1;
            for (int j = i + 1; j < nodes.Count && j <= i + 2; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis)
                {
                    openParen = j;
                    break;
                }
            }
            if (openParen < 0)
                continue;

            int closeParen = FindMatchingParen(nodes, openParen);
            if (closeParen < 0)
                continue;

            switch (expr.ExistsSubqueryIndent)
            {
                case "indent":
                    for (int j = openParen + 1; j < closeParen; j++)
                    {
                        if (nodes[j].PrecedingBreak != BreakType.None)
                        {
                            nodes[j].IndentLevel = Math.Max(nodes[j].IndentLevel, existsIndent + 1);
                        }
                    }
                    break;

                case "alignWithExists":
                    for (int j = openParen + 1; j < closeParen; j++)
                    {
                        if (nodes[j].PrecedingBreak != BreakType.None)
                        {
                            nodes[j].IndentLevel = existsIndent;
                        }
                    }
                    break;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helper methods
    // -----------------------------------------------------------------------

    private static bool IsStatementStart(TSqlTokenType tokenType)
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

    private static bool IsInWhereOrJoinContext(List<LayoutNode> nodes, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            var tt = nodes[i].TokenType;
            if (tt == TSqlTokenType.Where || tt == TSqlTokenType.On)
                return true;
            if (tt == TSqlTokenType.Select || tt == TSqlTokenType.From ||
                tt == TSqlTokenType.Semicolon || tt == TSqlTokenType.Go ||
                tt == TSqlTokenType.Case || tt == TSqlTokenType.When)
                return false;
        }
        return false;
    }

    private static int FindBetweenAnd(List<LayoutNode> nodes, int betweenIndex)
    {
        // The AND that pairs with BETWEEN is the first AND after BETWEEN
        // that is not nested in parentheses
        int parenDepth = 0;
        for (int i = betweenIndex + 1; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.LeftParenthesis)
                parenDepth++;
            else if (nodes[i].TokenType == TSqlTokenType.RightParenthesis)
                parenDepth--;

            if (parenDepth == 0 && nodes[i].TokenType == TSqlTokenType.And)
                return i;

            // Stop at clause boundaries
            if (nodes[i].TokenType == TSqlTokenType.Or ||
                nodes[i].TokenType == TSqlTokenType.Semicolon ||
                nodes[i].TokenType == TSqlTokenType.Comma)
                return -1;
        }
        return -1;
    }

    private static int FindBetweenEndValue(List<LayoutNode> nodes, int andIndex)
    {
        // Find the end of the value expression after the AND in BETWEEN
        for (int i = andIndex + 1; i < nodes.Count; i++)
        {
            var tt = nodes[i].TokenType;
            if (tt == TSqlTokenType.And || tt == TSqlTokenType.Or ||
                tt == TSqlTokenType.Comma || tt == TSqlTokenType.Semicolon ||
                tt == TSqlTokenType.RightParenthesis ||
                tt == TSqlTokenType.Then || tt == TSqlTokenType.When ||
                tt == TSqlTokenType.Else)
                return i;
        }
        return nodes.Count;
    }

    private static int FindMatchingParen(List<LayoutNode> nodes, int openIdx)
    {
        int depth = 1;
        for (int i = openIdx + 1; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.LeftParenthesis) depth++;
            else if (nodes[i].TokenType == TSqlTokenType.RightParenthesis)
            {
                depth--;
                if (depth == 0) return i;
            }
        }
        return -1;
    }

    private static void CollapseRange(List<LayoutNode> nodes, int start, int end)
    {
        for (int i = start; i < end && i < nodes.Count; i++)
        {
            if (nodes[i].PrecedingBreak == BreakType.NewLine || nodes[i].PrecedingBreak == BreakType.EmptyLine)
            {
                nodes[i].PrecedingBreak = BreakType.None;
                nodes[i].PrecedingSpaces = nodes[i].TokenType == TSqlTokenType.Comma ? 0 : 1;
                nodes[i].IndentLevel = 0;
            }
        }
    }

    private static void ExpandToMultiLine(List<LayoutNode> nodes, int openParen, int closeParen)
    {
        // First item on new line
        if (openParen + 1 < closeParen)
        {
            nodes[openParen + 1].PrecedingBreak = BreakType.NewLine;
            nodes[openParen + 1].IndentLevel = nodes[openParen].IndentLevel + 1;
            nodes[openParen + 1].PrecedingSpaces = 0;
        }

        // Each item after comma on new line
        for (int j = openParen + 1; j < closeParen; j++)
        {
            if (nodes[j].TokenType == TSqlTokenType.Comma && j + 1 < closeParen)
            {
                nodes[j + 1].PrecedingBreak = BreakType.NewLine;
                nodes[j + 1].IndentLevel = nodes[openParen].IndentLevel + 1;
                nodes[j + 1].PrecedingSpaces = 0;
            }
        }

        // Close paren on new line
        nodes[closeParen].PrecedingBreak = BreakType.NewLine;
        nodes[closeParen].IndentLevel = nodes[openParen].IndentLevel;
        nodes[closeParen].PrecedingSpaces = 0;
    }

    private static int MeasureLength(List<LayoutNode> nodes, int start, int end)
    {
        int length = 0;
        for (int i = start; i < end && i < nodes.Count; i++)
        {
            length += nodes[i].FormattedText.Length;
            if (nodes[i].PrecedingBreak == BreakType.None)
                length += nodes[i].PrecedingSpaces;
            else
                length += 1;
        }
        return length;
    }

    private static int MeasureWidth(List<LayoutNode> nodes, int start, int end)
    {
        int width = 0;
        for (int i = start; i < end; i++)
        {
            width += nodes[i].FormattedText.Length;
            if (i > start && nodes[i].PrecedingBreak == BreakType.None)
                width += nodes[i].PrecedingSpaces;
        }
        return width;
    }
}
