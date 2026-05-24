using System.Diagnostics.CodeAnalysis;
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
[SuppressMessage("ReSharper", "NotAccessedVariable")]
// ReSharper disable once UnusedMember.Global
public class ControlFlowRules : IRuleSet
{
    public void Apply(List<LayoutNode> nodes, FormattingProfile profile)
    {
        ApplyIfElseRules(nodes, profile.ControlFlow);
        ApplyTryCatchRules(nodes, profile.ControlFlow);
        ApplyBeginEndRules(nodes, profile.ControlFlow);
        ApplyCaseRules(nodes, profile.Case);
        ApplyCteRules(nodes, profile.Cte, profile.Whitespace);
        ApplyExpressionRules(nodes, profile.Expression);
        // Spec 020 T083 / T084 — operator alignment + BETWEEN + IN-list alignment
        ApplyOperatorRules(nodes, profile.Operators, profile.Expression);
        ApplyInStatementsAlignment(nodes, profile.InStatements);
        // Phase B closure — additional layout passes for newly-mapped SQL Prompt settings
        ApplyCteAsOnNewLine(nodes, profile.Cte);
        ApplyCaseEndAlignment(nodes, profile.Case);
        ApplyFunctionCallParameters(nodes, profile.FunctionCalls, profile.Whitespace);
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

                // T082 — Determine WHEN indent strategy from the new WhenAlignment enum,
                // falling back to legacy IndentWhen / toCase behaviour.
                int whenIndent = ResolveWhenIndent(caseOpts, caseIndent);

                // T082 — `ExpressionOnNewLine`: for simple CASE (`CASE expr WHEN ...`), put
                // any tokens between CASE and the first WHEN on their own line so the expression
                // sits below the CASE keyword.
                if (caseOpts.ExpressionOnNewLine)
                {
                    int firstWhen = FindFirstWhen(nodes, caseStart, caseEnd);
                    // Only apply if there's at least one token between CASE and the first WHEN
                    if (firstWhen > caseStart + 1)
                    {
                        var exprTok = nodes[caseStart + 1];
                        if (!exprTok.IsInNoformatRegion && exprTok.PrecedingBreak == BreakType.None)
                        {
                            exprTok.PrecedingBreak = BreakType.NewLine;
                            exprTok.IndentLevel = caseIndent + 1;
                            exprTok.PrecedingSpaces = 0;
                        }
                    }
                }

                bool sawFirstWhen = false;

                // Apply formatting within CASE...END
                for (int j = caseStart + 1; j < caseEnd; j++)
                {
                    if (nodes[j].IsInNoformatRegion)
                        continue;

                    // WHEN on new line — honour T082 FirstWhenOnNewLine override for the first WHEN.
                    if (nodes[j].TokenType == TSqlTokenType.When)
                    {
                        bool isFirstWhen = !sawFirstWhen;
                        sawFirstWhen = true;

                        bool placeOnNewLine = caseOpts.WhenOnNewLine;
                        if (isFirstWhen)
                        {
                            var first = (caseOpts.FirstWhenOnNewLine ?? "auto").Trim().ToLowerInvariant();
                            placeOnNewLine = first switch
                            {
                                "always" => true,
                                "never" => false,
                                _ => placeOnNewLine,    // auto / default — inherit WhenOnNewLine
                            };
                        }

                        if (placeOnNewLine)
                        {
                            if (nodes[j].PrecedingBreak == BreakType.None)
                            {
                                nodes[j].PrecedingBreak = BreakType.NewLine;
                                nodes[j].PrecedingSpaces = 0;
                            }
                            nodes[j].IndentLevel = whenIndent;
                        }
                        else if (isFirstWhen && nodes[j].PrecedingBreak != BreakType.None)
                        {
                            // FirstWhenOnNewLine = "never": force inline with CASE expression.
                            nodes[j].PrecedingBreak = BreakType.None;
                            nodes[j].PrecedingSpaces = 1;
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
                                nodes[j].IndentLevel = whenIndent + 1;
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
                            nodes[j].IndentLevel = whenIndent;
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
    /// Spec 020 T082 — resolves the indent level for WHEN keywords inside a CASE expression
    /// from <see cref="CaseOptions.WhenAlignment"/>, falling back to the legacy
    /// <see cref="CaseOptions.IndentWhen"/> behaviour when the alignment is "toCase".
    /// </summary>
    /// <remarks>
    /// "toFirstItem" is treated like "toCase" at the layout-engine level — true first-item
    /// column alignment requires post-emission column measurement, which the existing
    /// indentation model doesn't expose. The setting still round-trips losslessly through
    /// import/export, and future refinements can plug into <see cref="TextEmitter"/>.
    /// </remarks>
    private static int ResolveWhenIndent(CaseOptions caseOpts, int caseIndent)
    {
        var alignment = (caseOpts.WhenAlignment ?? "toCase").Trim().ToLowerInvariant();
        return alignment switch
        {
            "indentedfromcase" => caseIndent + 1,
            "tofirstitem" => caseIndent,                  // approximated as "toCase" (see remarks)
            "tocase" => caseIndent,
            _ => caseOpts.IndentWhen ? caseIndent + 1 : caseIndent,    // legacy fall-back path
        };
    }

    /// <summary>
    /// Spec 020 T082 — returns the index of the first WHEN token between caseStart and caseEnd
    /// (or caseEnd if no WHEN found inside the bounds).
    /// </summary>
    private static int FindFirstWhen(List<LayoutNode> nodes, int caseStart, int caseEnd)
    {
        for (int j = caseStart + 1; j < caseEnd; j++)
        {
            if (nodes[j].IsInNoformatRegion) continue;
            if (nodes[j].TokenType == TSqlTokenType.When) return j;
        }
        return caseEnd;
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

    private static void ApplyCteRules(List<LayoutNode> nodes, CteOptions cte, WhitespaceOptions ws)
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

        // T080 — place CTE column list per cte.PlaceColumnsOnNewLine
        ApplyCteColumnListPlacement(nodes, cte, ws);
    }

    /// <summary>
    /// Spec 020 T080 — places the optional CTE column list ((col1, col2, ...) between the
    /// CTE name and AS) according to <see cref="CteOptions.PlaceColumnsOnNewLine"/>:
    /// <list type="bullet">
    ///   <item><c>always</c> — opening paren on a new line, indented one level from WITH.</item>
    ///   <item><c>never</c> — opening paren stays inline (single space after the name).</item>
    ///   <item><c>ifLongerThanWrap</c> (default) — opening paren on a new line only when the
    ///     column list's measured length would push the containing line past
    ///     <c>Whitespace.MaxLineWidth</c>.</item>
    /// </list>
    /// </summary>
    private static void ApplyCteColumnListPlacement(List<LayoutNode> nodes, CteOptions cte, WhitespaceOptions ws)
    {
        var mode = (cte.PlaceColumnsOnNewLine ?? string.Empty).Trim().ToLowerInvariant();
        if (mode != "always" && mode != "never" && mode != "iflongerthanwrap")
            mode = "iflongerthanwrap";

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.With) continue;
            if (!IsCteWith(nodes, i)) continue;

            int cteRegionEnd = FindCteRegionEnd(nodes, i);
            int withIndent = nodes[i].IndentLevel;

            // Walk each top-level CTE definition (parenDepth==0) and look for: <Identifier> '(' ... ')' 'AS'
            int parenDepth = 0;
            int j = i + 1;
            while (j < cteRegionEnd)
            {
                if (nodes[j].IsInNoformatRegion) { j++; continue; }

                if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis) { parenDepth++; j++; continue; }
                if (nodes[j].TokenType == TSqlTokenType.RightParenthesis) { parenDepth--; j++; continue; }

                // Identifier at top level only
                if (parenDepth == 0 && nodes[j].TokenType == TSqlTokenType.Identifier)
                {
                    // Next non-noformat token: '(' = column list, 'AS' = no list, anything else = give up
                    int k = j + 1;
                    while (k < cteRegionEnd && nodes[k].IsInNoformatRegion) k++;
                    if (k >= cteRegionEnd) break;

                    if (nodes[k].TokenType == TSqlTokenType.LeftParenthesis)
                    {
                        // Find the matching close paren
                        int close = FindMatchingParen(nodes, k);
                        if (close < 0) { j = k + 1; continue; }

                        ApplyPlacementToOpenParen(nodes, k, close, mode, withIndent, ws);
                        j = close + 1;
                        continue;
                    }
                }
                j++;
            }
        }
    }

    private static void ApplyPlacementToOpenParen(
        List<LayoutNode> nodes, int openParen, int closeParen, string mode, int withIndent, WhitespaceOptions ws)
    {
        switch (mode)
        {
            case "always":
                if (nodes[openParen].PrecedingBreak == BreakType.None)
                {
                    nodes[openParen].PrecedingBreak = BreakType.NewLine;
                    nodes[openParen].IndentLevel = withIndent + 1;
                    nodes[openParen].PrecedingSpaces = 0;
                }
                break;

            case "never":
                if (nodes[openParen].PrecedingBreak != BreakType.None)
                {
                    nodes[openParen].PrecedingBreak = BreakType.None;
                    nodes[openParen].PrecedingSpaces = 1;
                }
                break;

            case "iflongerthanwrap":
            default:
                // If the (col1, col2, ...) plus the preceding CTE name doesn't fit on a line
                // shorter than MaxLineWidth, place the opening paren on a new line.
                int listLength = MeasureLength(nodes, openParen, closeParen + 1);
                int width = ws.MaxLineWidth > 0 ? ws.MaxLineWidth : 120;
                // Heuristic: include ~20 chars for "WITH " + CTE name margin.
                if (listLength + 20 > width && nodes[openParen].PrecedingBreak == BreakType.None)
                {
                    nodes[openParen].PrecedingBreak = BreakType.NewLine;
                    nodes[openParen].IndentLevel = withIndent + 1;
                    nodes[openParen].PrecedingSpaces = 0;
                }
                break;
        }
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
            if (tt is TSqlTokenType.Where or TSqlTokenType.On)
                return true;
            if (tt is TSqlTokenType.Select or TSqlTokenType.From or TSqlTokenType.Semicolon or TSqlTokenType.Go or TSqlTokenType.Case or TSqlTokenType.When)
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
            if (tt is TSqlTokenType.And or TSqlTokenType.Or or TSqlTokenType.Comma or TSqlTokenType.Semicolon or TSqlTokenType.RightParenthesis or TSqlTokenType.Then or TSqlTokenType.When or TSqlTokenType.Else)
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

    // -----------------------------------------------------------------------
    // T083 — Operators rules (alignment + BETWEEN placement)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Spec 020 T083 — applies operator-group settings on top of the existing
    /// <see cref="ApplyBooleanOperatorNewLine"/> + DmlRules AND/OR placement:
    /// <list type="bullet">
    ///   <item><c>Alignment = inlineWithStatement</c> (default) — no-op (existing behaviour).</item>
    ///   <item><c>Alignment = indentedFromStatement</c> — when AND/OR already sits on its own
    ///     line, bumps its indent up by one so it visibly indents from the clause keyword.</item>
    ///   <item><c>Alignment = rightAligned</c> — not implementable without post-emission column
    ///     measurement; falls back to <c>indentedFromStatement</c> at the layout layer (the
    ///     setting still round-trips losslessly through import/export).</item>
    ///   <item><c>BetweenOnNewLine = true</c> — places the <c>BETWEEN</c> keyword on a new line
    ///     when not already broken. Pairs with the existing
    ///     <see cref="ExpressionOptions.BetweenOnOneLine"/> (which would otherwise pull it back).</item>
    /// </list>
    /// </summary>
    private static void ApplyOperatorRules(List<LayoutNode> nodes, OperatorsOptions ops, ExpressionOptions expr)
    {
        if (ops == null) return;

        var alignment = (ops.Alignment ?? "inlineWithStatement").Trim().ToLowerInvariant();
        bool bumpIndent = alignment == "indentedfromstatement" || alignment == "rightaligned";

        if (bumpIndent)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n.IsInNoformatRegion) continue;
                if (n.TokenType != TSqlTokenType.And && n.TokenType != TSqlTokenType.Or) continue;
                if (n.PrecedingBreak == BreakType.None) continue;   // operator is inline — alignment is moot
                n.IndentLevel += 1;
            }
        }

        if (ops.BetweenOnNewLine)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n.IsInNoformatRegion) continue;
                if (n.TokenType != TSqlTokenType.Between) continue;
                if (n.PrecedingBreak != BreakType.None) continue;
                n.PrecedingBreak = BreakType.NewLine;
                n.PrecedingSpaces = 0;
                n.IndentLevel = Math.Max(n.IndentLevel, 1);
            }
        }

        // Phase B closure — `Operators.AndBetweenOnNewLine` places the AND that pairs with a
        // BETWEEN on its own line. Skip when ExpressionOptions.BetweenOnOneLine wins (that rule
        // pulls the AND back inline and would re-collide with this).
        if (ops.AndBetweenOnNewLine && !(expr?.BetweenOnOneLine ?? false))
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].IsInNoformatRegion) continue;
                if (nodes[i].TokenType != TSqlTokenType.Between) continue;

                int andIdx = FindBetweenAnd(nodes, i);
                if (andIdx < 0) continue;
                if (nodes[andIdx].PrecedingBreak != BreakType.None) continue;

                nodes[andIdx].PrecedingBreak = BreakType.NewLine;
                nodes[andIdx].PrecedingSpaces = 0;
                nodes[andIdx].IndentLevel = Math.Max(nodes[i].IndentLevel, 0) + 1;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Phase B closure — additional layout passes for newly-mapped SQL Prompt settings
    // -----------------------------------------------------------------------

    /// <summary>
    /// Phase B closure — SQL Prompt <c>cte.placeAsOnNewLine</c>. Walks each CTE detected by
    /// <see cref="IsCteWith"/> and places the AS keyword that introduces the CTE body on its
    /// own line when <see cref="CteOptions.AsOnNewLine"/> is true.
    /// </summary>
    private static void ApplyCteAsOnNewLine(List<LayoutNode> nodes, CteOptions cte)
    {
        if (cte == null || !cte.AsOnNewLine) return;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.With) continue;
            if (!IsCteWith(nodes, i)) continue;

            int regionEnd = FindCteRegionEnd(nodes, i);
            int withIndent = nodes[i].IndentLevel;

            // Track paren depth so we only act on top-level AS tokens (inside a CTE-name region,
            // not inside the CTE body).
            int parenDepth = 0;
            for (int j = i + 1; j < regionEnd; j++)
            {
                if (nodes[j].IsInNoformatRegion) continue;
                if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis) { parenDepth++; continue; }
                if (nodes[j].TokenType == TSqlTokenType.RightParenthesis) { parenDepth--; continue; }

                if (parenDepth != 0) continue;
                if (nodes[j].TokenType != TSqlTokenType.As) continue;
                if (nodes[j].PrecedingBreak != BreakType.None) continue;

                nodes[j].PrecedingBreak = BreakType.NewLine;
                nodes[j].PrecedingSpaces = 0;
                nodes[j].IndentLevel = withIndent;
            }
        }
    }

    /// <summary>
    /// Phase B closure — SQL Prompt <c>caseExpressions.endAlignment</c>. Walks each CASE block
    /// detected by <see cref="ApplyCaseRules"/> and resets the END token's indent level based on
    /// <see cref="CaseOptions.EndAlignment"/>. When the legacy <see cref="CaseOptions.EndOnNewLine"/>
    /// is false this is a no-op (END stays inline regardless of alignment).
    /// </summary>
    private static void ApplyCaseEndAlignment(List<LayoutNode> nodes, CaseOptions caseOpts)
    {
        if (caseOpts == null) return;
        if (!caseOpts.EndOnNewLine) return;       // END is inline — alignment is moot

        var mode = (caseOpts.EndAlignment ?? "toCase").Trim().ToLowerInvariant();

        // Note: this pass also corrects a pre-Phase-B bug where `ApplyBeginEndRules` would
        // pre-break the END line *before* `ApplyCaseRules` had a chance to set its IndentLevel
        // (the case-rules `EndOnNewLine` branch only runs when PrecedingBreak == None). After
        // this pass, every CASE END is on a known indent regardless of which rule broke it.
        var caseStack = new Stack<int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion) continue;
            if (nodes[i].TokenType == TSqlTokenType.Case) { caseStack.Push(i); continue; }
            if (nodes[i].TokenType == TSqlTokenType.End && caseStack.Count > 0)
            {
                int caseStart = caseStack.Pop();
                if (nodes[i].PrecedingBreak != BreakType.None)
                {
                    nodes[i].IndentLevel = mode == "indented"
                        ? nodes[caseStart].IndentLevel + 1
                        : nodes[caseStart].IndentLevel;
                }
            }
        }
    }

    /// <summary>
    /// Phase B closure — SQL Prompt <c>functionCalls.placeParametersOnNewLine</c> +
    /// <c>functionCalls.indentParameters</c>. Detects parenthesised parameter lists immediately
    /// following an identifier (the function-call shape <c>name(args)</c>) and applies the
    /// configured placement.
    /// </summary>
    private static void ApplyFunctionCallParameters(List<LayoutNode> nodes, FunctionCallsOptions opts, WhitespaceOptions ws)
    {
        if (opts == null) return;
        var mode = (opts.PlaceParametersOnNewLine ?? "ifLongerThanWrap").Trim().ToLowerInvariant();

        for (int i = 1; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion) continue;
            if (nodes[i].TokenType != TSqlTokenType.LeftParenthesis) continue;

            var prev = nodes[i - 1];
            if (prev.IsInNoformatRegion) continue;
            if (prev.TokenType != TSqlTokenType.Identifier) continue;
            // Heuristic: a "real" function-call paren has no space between the name and (
            if (nodes[i].PrecedingBreak != BreakType.None || nodes[i].PrecedingSpaces > 0) continue;

            int close = FindMatchingParen(nodes, i);
            if (close < 0) continue;

            // Skip empty parameter lists — no benefit to breaking those.
            if (close == i + 1) continue;

            bool shouldBreak = mode switch
            {
                "always" => true,
                "never" => false,
                _ => MeasureLength(nodes, i, close + 1) > (ws.MaxLineWidth > 0 ? ws.MaxLineWidth : 120) - 40,
            };

            if (mode == "never")
            {
                // Force inline — collapse any internal breaks within the parameter list.
                CollapseRange(nodes, i + 1, close);
                if (nodes[close].PrecedingBreak != BreakType.None)
                {
                    nodes[close].PrecedingBreak = BreakType.None;
                    nodes[close].PrecedingSpaces = 0;
                }
                continue;
            }

            if (!shouldBreak) continue;

            int parentIndent = prev.IndentLevel;
            int paramIndent = opts.IndentParameters ? parentIndent + 1 : parentIndent;

            // First parameter on its own line; each comma-followed param on its own line; close
            // paren on its own line aligned with the parent.
            if (nodes[i + 1].PrecedingBreak == BreakType.None)
            {
                nodes[i + 1].PrecedingBreak = BreakType.NewLine;
                nodes[i + 1].IndentLevel = paramIndent;
                nodes[i + 1].PrecedingSpaces = 0;
            }
            for (int j = i + 1; j < close; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.Comma && j + 1 < close)
                {
                    if (nodes[j + 1].PrecedingBreak == BreakType.None)
                    {
                        nodes[j + 1].PrecedingBreak = BreakType.NewLine;
                        nodes[j + 1].IndentLevel = paramIndent;
                        nodes[j + 1].PrecedingSpaces = 0;
                    }
                }
            }
            if (nodes[close].PrecedingBreak == BreakType.None)
            {
                nodes[close].PrecedingBreak = BreakType.NewLine;
                nodes[close].IndentLevel = parentIndent;
                nodes[close].PrecedingSpaces = 0;
            }

            i = close;        // skip past this function call
        }
    }

    // -----------------------------------------------------------------------
    // T084 — IN-list alignment
    // -----------------------------------------------------------------------

    /// <summary>
    /// Spec 020 T084 — when <see cref="ExpressionOptions.InListStyle"/> has already expanded an
    /// IN list to multiple lines, this pass re-applies the alignment variant chosen on
    /// <see cref="InStatementsOptions.Alignment"/>:
    /// <list type="bullet">
    ///   <item><c>stacked</c> (default) — one item per line (existing behaviour, no change).</item>
    ///   <item><c>wrapped</c> — pack multiple items per line up to
    ///     <see cref="WhitespaceOptions.MaxLineWidth"/>: each comma stays inline (preceded by a
    ///     space) unless the running line length would exceed the limit, in which case the
    ///     following item gets a line break.</item>
    ///   <item><c>rightAligned</c> — falls back to <c>stacked</c> at the layout level (a true
    ///     right-align would require post-emission column measurement). The setting still
    ///     round-trips losslessly.</item>
    /// </list>
    /// Only IN lists with literal/identifier members are reshaped — IN-subquery lists are left
    /// alone (consistent with <see cref="ApplyInListStyle"/>).
    /// </summary>
    private static void ApplyInStatementsAlignment(List<LayoutNode> nodes, InStatementsOptions inStmt)
    {
        if (inStmt == null) return;

        var alignment = (inStmt.Alignment ?? "stacked").Trim().ToLowerInvariant();
        if (alignment != "wrapped")
            return;   // "stacked" + "rightAligned" + unrecognised — keep current ExpandToMultiLine behaviour

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.In) continue;

            int openParen = -1;
            for (int j = i + 1; j < nodes.Count && j <= i + 3; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis) { openParen = j; break; }
            }
            if (openParen < 0) continue;

            int closeParen = FindMatchingParen(nodes, openParen);
            if (closeParen < 0) continue;

            // Skip IN-subqueries
            bool hasSubquery = false;
            for (int j = openParen + 1; j < closeParen; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.Select) { hasSubquery = true; break; }
            }
            if (hasSubquery) continue;

            // Only reshape if the list is already in multi-line form (otherwise the user / default
            // collapsed it and we shouldn't override).
            bool isMultiLine = false;
            for (int j = openParen + 1; j < closeParen; j++)
            {
                if (nodes[j].PrecedingBreak != BreakType.None) { isMultiLine = true; break; }
            }
            if (!isMultiLine) continue;

            int baseIndent = nodes[openParen].IndentLevel;
            int width = 80;                                    // safe default; the wrapper rolls when running >= width
            int runningWidth = 0;

            for (int j = openParen + 1; j < closeParen; j++)
            {
                // Pull each item back inline first.
                if (nodes[j].PrecedingBreak != BreakType.None)
                {
                    nodes[j].PrecedingBreak = BreakType.None;
                    nodes[j].PrecedingSpaces = nodes[j].TokenType == TSqlTokenType.Comma ? 0 : 1;
                }

                runningWidth += nodes[j].FormattedText.Length + nodes[j].PrecedingSpaces;

                // After a comma, decide whether to wrap. The next item gets a new line when we'd
                // otherwise blow past `width`.
                if (nodes[j].TokenType == TSqlTokenType.Comma && j + 1 < closeParen)
                {
                    int next = nodes[j + 1].FormattedText.Length + 1;
                    if (runningWidth + next > width)
                    {
                        nodes[j + 1].PrecedingBreak = BreakType.NewLine;
                        nodes[j + 1].IndentLevel = baseIndent + 1;
                        nodes[j + 1].PrecedingSpaces = 0;
                        runningWidth = 0;
                    }
                }
            }
        }
    }

    // -----------------------------------------------------------------------

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
