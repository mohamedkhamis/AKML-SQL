using System.Diagnostics.CodeAnalysis;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Rules;

/// <summary>
/// Implements list formatting rules as a post-processing pass:
/// commaPosition (trailing/leading), alignItemsAcrossClauses, alignAliases,
/// oneItemPerLine, collapseShortLists, collapseThreshold, indentListItems,
/// alignDataTypesInDDL, alignValuesInInsert, spaceAfterListComma.
/// </summary>
[SuppressMessage("ReSharper", "UnusedParameter.Local")]
[SuppressMessage("ReSharper", "UnusedVariable")]
public class ListRules : IRuleSet
{
    public void Apply(List<LayoutNode> nodes, FormattingProfile profile)
    {
        var list = profile.List;

        ApplyOneItemPerLine(nodes, list, profile);
        ApplyIndentListItems(nodes, list, profile);
        ApplyCollapseShortLists(nodes, list);
        // After the break-affecting passes: ApplyCommaPosition moves a comma onto the break of
        // the item that follows it, so it must see the FINAL item breaks — run first (its
        // original slot) it saw none (ApplyOneItemPerLine hadn't created them yet) and
        // commaPosition "leading" never took effect (spec 030 T011).
        ApplyCommaPosition(nodes, list);
        // AlignAliases is NOT applied here: at ListRules time the function-call parens are still
        // exploded (ParenthesisRules re-joins them later), so every AS line measured as starting
        // at the lone ")" and the alignment was inert. FormatterPipeline.ApplyLayoutRules calls
        // ListRules.AlignAliases at the post-collapse finalization instead (spec 030 T011).
        ApplyAlignItemsAcrossClauses(nodes, list, profile);
        ApplySpaceAfterListComma(nodes, list);
    }

    /// <summary>
    /// Handles commaPosition: "trailing" keeps commas at end of line (default),
    /// "leading" moves commas to the start of the next line.
    /// </summary>
    private static void ApplyCommaPosition(List<LayoutNode> nodes, ListOptions list)
    {
        if (list.CommaPosition != "leading")
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion || node.TokenType != TSqlTokenType.Comma)
                continue;

            // In leading comma style, the comma goes on the next line before the item.
            // Find the next non-whitespace token
            if (i + 1 < nodes.Count)
            {
                var next = nodes[i + 1];

                // If the next token was going to be on a new line, move the comma there
                if (next.PrecedingBreak is BreakType.NewLine or BreakType.EmptyLine)
                {
                    // Transfer the break from next to the comma
                    node.PrecedingBreak = next.PrecedingBreak;
                    node.IndentLevel = next.IndentLevel;
                    node.PrecedingSpaces = 0;

                    // Next token follows the comma with a space, same line
                    next.PrecedingBreak = BreakType.None;
                    next.PrecedingSpaces = 1;
                    next.IndentLevel = 0;
                }
            }
        }
    }

    /// <summary>
    /// When oneItemPerLine is true, ensures each list item in SELECT, GROUP BY, ORDER BY
    /// appears on its own line.
    /// </summary>
    private static void ApplyOneItemPerLine(List<LayoutNode> nodes, ListOptions list, FormattingProfile profile)
    {
        if (!list.OneItemPerLine)
            return;

        var context = ClauseContext.None;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            // Track clause context
            context = UpdateClauseContext(node, context);

            // If we're in a list context and this follows a comma, ensure it's on a new line
            if (IsListClause(context) && i > 0)
            {
                // Find the previous semantic token
                var prevIndex = FindPrevSemanticToken(nodes, i);
                if (prevIndex >= 0 && nodes[prevIndex].TokenType == TSqlTokenType.Comma)
                {
                    if (node.PrecedingBreak == BreakType.None)
                    {
                        node.PrecedingBreak = BreakType.NewLine;
                        node.IndentLevel = Math.Max(node.IndentLevel, 1);
                        node.PrecedingSpaces = 0;
                    }
                }
            }
        }
    }

    /// <summary>
    /// When indentListItems is true, ensures list items under clause keywords are indented.
    /// </summary>
    private static void ApplyIndentListItems(List<LayoutNode> nodes, ListOptions list, FormattingProfile profile)
    {
        if (!list.IndentListItems)
            return;

        var context = ClauseContext.None;

        foreach (var node in nodes)
        {
            if (node.IsInNoformatRegion)
                continue;

            context = UpdateClauseContext(node, context);

            if (IsListClause(context) && node.PrecedingBreak == BreakType.NewLine)
            {
                // Don't indent clause keywords themselves, only their items
                // (IsListBoundary so the ORDER/GROUP keyword stays at clause level, not col +1).
                if (!IsListBoundary(node.TokenType))
                {
                    node.IndentLevel = Math.Max(node.IndentLevel, 1);
                }
            }
        }
    }

    /// <summary>
    /// When collapseShortLists is true and the total length of list items is under
    /// collapseThreshold, collapse the list onto a single line.
    /// </summary>
    private static void ApplyCollapseShortLists(List<LayoutNode> nodes, ListOptions list)
    {
        if (!list.CollapseShortLists)
            return;

        // Find list regions (comma-separated groups within a clause)
        int i = 0;
        while (i < nodes.Count)
        {
            if (nodes[i].IsInNoformatRegion)
            {
                i++;
                continue;
            }

            // Look for the start of a list (first item after a clause keyword)
            if (IsListBoundary(nodes[i].TokenType))
            {
                int listStart = i + 1;
                // A list opened by JOIN stops at its ON keyword, so collapse cannot delete the
                // ON-condition's break (the style's onConditionNewLine). ON is deliberately NOT a
                // universal boundary — a list starting after a MERGE's ON pulls the following WHEN
                // up — so the stop is scoped to JOIN-opened lists (a MERGE has no JOIN keyword).
                bool stopAtOn = nodes[i].TokenType == TSqlTokenType.Join;
                int listEnd = FindListEnd(nodes, listStart, stopAtOn);

                if (listEnd > listStart)
                {
                    int totalLength = MeasureListLength(nodes, listStart, listEnd);

                    if (totalLength <= list.CollapseThreshold)
                    {
                        CollapseRange(nodes, listStart, listEnd);
                    }
                }

                i = listEnd;
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>
    /// When alignAliases is true, aligns AS aliases in SELECT lists to the same column.
    /// Computes the maximum expression width and pads preceding spaces on the AS keyword.
    /// Called from <c>FormatterPipeline.ApplyLayoutRules</c>' post-collapse finalization (NOT from
    /// <see cref="Apply"/>): alignment is line geometry, and the line shapes are final only after
    /// every rule set's collapse passes have run (ParenthesisRules re-joins exploded function-call
    /// parens after ListRules — measured before that, every AS line started at ")" and the
    /// computed padding was always a single space).
    /// </summary>
    internal static void AlignAliases(List<LayoutNode> nodes, ListOptions list)
    {
        if (!list.AlignAliases)
            return;

        // Find AS keywords in SELECT list context and align them
        var asPositions = new List<(int asIndex, int lineStartIndex)>();
        var context = ClauseContext.None;

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            context = UpdateClauseContext(node, context);

            if (context == ClauseContext.Select && node.TokenType == TSqlTokenType.As)
            {
                // Find the start of this line
                int lineStart = FindLineStart(nodes, i);
                asPositions.Add((i, lineStart));
            }
        }

        if (asPositions.Count < 2)
            return;

        // Calculate max width before AS for alignment
        int maxWidth = 0;
        var widths = new List<int>();

        foreach (var (asIndex, lineStartIndex) in asPositions)
        {
            int width = MeasureWidth(nodes, lineStartIndex, asIndex);
            widths.Add(width);
            maxWidth = Math.Max(maxWidth, width);
        }

        // Apply padding
        for (int j = 0; j < asPositions.Count; j++)
        {
            var (asIndex, _) = asPositions[j];
            int currentWidth = widths[j];
            int padding = maxWidth - currentWidth + 1; // +1 for minimum one space
            nodes[asIndex].PrecedingSpaces = Math.Max(padding, 1);
        }
    }

    /// <summary>
    /// When alignItemsAcrossClauses is true, aligns the first item of each clause
    /// (SELECT, FROM, WHERE, etc.) to the same column position.
    /// </summary>
    private static void ApplyAlignItemsAcrossClauses(List<LayoutNode> nodes, ListOptions list, FormattingProfile profile)
    {
        if (!list.AlignItemsAcrossClauses)
            return;

        // Find clause keywords and their first items
        var clauseItems = new List<(int keywordIndex, int firstItemIndex, int keywordLength)>();

        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            if (IsClauseKeyword(node.TokenType) && i + 1 < nodes.Count)
            {
                int firstItem = i + 1;
                // Skip BY in GROUP BY / ORDER BY
                if (firstItem < nodes.Count && nodes[firstItem].TokenType == TSqlTokenType.By)
                    firstItem++;
                if (firstItem < nodes.Count)
                {
                    int kwLen = GetClauseKeywordLength(nodes, i);
                    clauseItems.Add((i, firstItem, kwLen));
                }
            }
        }

        if (clauseItems.Count < 2)
            return;

        // Find the maximum clause keyword length
        int maxKeywordLen = 0;
        foreach (var (_, _, kwLen) in clauseItems)
            maxKeywordLen = Math.Max(maxKeywordLen, kwLen);

        // Adjust spacing so first items align
        foreach (var (keywordIndex, firstItemIndex, kwLen) in clauseItems)
        {
            var firstItem = nodes[firstItemIndex];
            if (firstItem.PrecedingBreak == BreakType.None)
            {
                firstItem.PrecedingSpaces = maxKeywordLen - kwLen + 1;
            }
        }
    }

    /// <summary>
    /// When spaceAfterListComma is true/false, adjusts spacing after commas within list contexts.
    /// </summary>
    private static void ApplySpaceAfterListComma(List<LayoutNode> nodes, ListOptions list)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion || node.TokenType != TSqlTokenType.Comma)
                continue;

            if (i + 1 < nodes.Count)
            {
                var next = nodes[i + 1];
                if (next.PrecedingBreak == BreakType.None)
                {
                    next.PrecedingSpaces = list.SpaceAfterListComma ? 1 : 0;
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helper methods
    // -----------------------------------------------------------------------

    private static ClauseContext UpdateClauseContext(LayoutNode node, ClauseContext current)
    {
        return node.TokenType switch
        {
            TSqlTokenType.Select => ClauseContext.Select,
            TSqlTokenType.From => ClauseContext.From,
            TSqlTokenType.Where => ClauseContext.Where,
            TSqlTokenType.Having => ClauseContext.Having,
            TSqlTokenType.Join => ClauseContext.Join,
            TSqlTokenType.Insert => ClauseContext.Insert,
            TSqlTokenType.Update => ClauseContext.Update,
            TSqlTokenType.Delete => ClauseContext.Delete,
            TSqlTokenType.Set => ClauseContext.Set,
            TSqlTokenType.Values => ClauseContext.Values,
            TSqlTokenType.Semicolon => ClauseContext.None,
            _ when node.FormattedText.Equals("GROUP", StringComparison.OrdinalIgnoreCase) => ClauseContext.GroupBy,
            _ when node.FormattedText.Equals("ORDER", StringComparison.OrdinalIgnoreCase) => ClauseContext.OrderBy,
            _ => current,
        };
    }

    private static bool IsListClause(ClauseContext context)
    {
        return context is ClauseContext.Select or ClauseContext.GroupBy or ClauseContext.OrderBy or ClauseContext.Set or ClauseContext.Values;
    }

    private static bool IsClauseKeyword(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.Select or TSqlTokenType.From or TSqlTokenType.Where or
            TSqlTokenType.Having or TSqlTokenType.Join or
            TSqlTokenType.Insert or TSqlTokenType.Update or TSqlTokenType.Delete or
            TSqlTokenType.Set or TSqlTokenType.Values => true,
            _ => false,
        };
    }

    /// <summary>
    /// Clause-boundary predicate for the list collapse + indent paths. Extends
    /// <see cref="IsClauseKeyword"/> with ORDER and GROUP, which carry their own ScriptDom token
    /// types but were historically recognised only by <see cref="UpdateClauseContext"/>'s
    /// FormattedText match. Without ORDER/GROUP here, <see cref="FindListEnd"/> over-extends a
    /// WHERE/HAVING list across the ORDER&#160;BY / GROUP&#160;BY boundary and
    /// <see cref="CollapseRange"/> deletes the line break before it — merging ORDER&#160;BY /
    /// GROUP&#160;BY onto the previous clause line (spec 030 T008). Deliberately kept separate from
    /// <see cref="IsClauseKeyword"/> so cross-clause first-item alignment
    /// (<c>ApplyAlignItemsAcrossClauses</c>) is unaffected — folding ORDER&#160;BY into that pass
    /// would re-pad <c>maxKeywordLen</c> across every clause.
    /// </summary>
    private static bool IsListBoundary(TSqlTokenType tokenType)
        => IsClauseKeyword(tokenType)
           || tokenType is TSqlTokenType.Order or TSqlTokenType.Group
           || IsJoinBoundary(tokenType);

    /// <summary>
    /// Join-type modifiers, treated as list boundaries (spec 030 T010) so <see cref="FindListEnd"/>
    /// stops at them: otherwise the FROM "list" — and each JOIN body — over-runs the trailing
    /// <c>INNER</c>/<c>LEFT</c>/… into the preceding segment, and <see cref="CollapseRange"/> pulls
    /// the join modifier up onto the prior line (<c>FROM orders o INNER</c> ⏎ <c>JOIN …</c>) or
    /// collapses the whole FROM+JOIN region. <c>Join</c> itself is already covered by
    /// <see cref="IsClauseKeyword"/>. <c>ON</c> is deliberately NOT included: as a universal
    /// boundary it makes a MERGE's <c>ON</c> start a collapsible list that pulls the following
    /// <c>WHEN</c> up — keeping the JOIN's ON-condition on its own line needs clause-context
    /// awareness (a separate follow-up); with modifiers-only the ON condition simply stays inline.
    /// </summary>
    private static bool IsJoinBoundary(TSqlTokenType tokenType)
        => tokenType is TSqlTokenType.Inner or TSqlTokenType.Left or TSqlTokenType.Right
            or TSqlTokenType.Full or TSqlTokenType.Cross or TSqlTokenType.Outer;

    private static int FindPrevSemanticToken(List<LayoutNode> nodes, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            if (nodes[i].TokenType != TSqlTokenType.WhiteSpace &&
                nodes[i].TokenType != TSqlTokenType.SingleLineComment &&
                nodes[i].TokenType != TSqlTokenType.MultilineComment)
                return i;
        }
        return -1;
    }

    private static int FindListEnd(List<LayoutNode> nodes, int start, bool stopAtOn = false)
    {
        // Track parenthesis depth relative to the list start. A ')' seen at depth 0 closes a paren
        // that was opened BEFORE the list — i.e. a structural subquery/CTE/derived-table close — so
        // the list ends there. A balanced function-call ')' (opened inside the list) stays in the
        // list. Without this, the CTE body's last clause-list over-runs the CTE's closing ')' and
        // CollapseRange deletes its line break, merging ')' + the main SELECT up (spec 030 T009).
        int parenDepth = 0;
        for (int i = start; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion)
                continue;

            var tokenType = nodes[i].TokenType;

            if (tokenType == TSqlTokenType.LeftParenthesis)
            {
                parenDepth++;
                continue;
            }
            if (tokenType == TSqlTokenType.RightParenthesis)
            {
                if (parenDepth == 0)
                    return i;   // closes an enclosing paren → structural list boundary
                parenDepth--;
                continue;
            }

            // End of list: next clause keyword or statement terminator (or, for a JOIN-opened
            // list, the ON keyword — see ApplyCollapseShortLists).
            if (IsListBoundary(tokenType) ||
                (stopAtOn && tokenType == TSqlTokenType.On && parenDepth == 0) ||
                tokenType == TSqlTokenType.Semicolon ||
                tokenType == TSqlTokenType.Go)
                return i;
        }
        return nodes.Count;
    }

    private static int MeasureListLength(List<LayoutNode> nodes, int start, int end)
    {
        int length = 0;
        for (int i = start; i < end; i++)
        {
            length += nodes[i].FormattedText.Length;
            if (nodes[i].PrecedingBreak == BreakType.None)
                length += nodes[i].PrecedingSpaces;
        }
        return length;
    }

    private static void CollapseRange(List<LayoutNode> nodes, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (nodes[i].PrecedingBreak == BreakType.NewLine || nodes[i].PrecedingBreak == BreakType.EmptyLine)
            {
                nodes[i].PrecedingBreak = BreakType.None;
                nodes[i].PrecedingSpaces = nodes[i].TokenType == TSqlTokenType.Comma ? 0 : 1;
                nodes[i].IndentLevel = 0;
            }
        }
    }

    private static int FindLineStart(List<LayoutNode> nodes, int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            if (nodes[i].PrecedingBreak == BreakType.NewLine || nodes[i].PrecedingBreak == BreakType.EmptyLine)
                return i;
        }
        return 0;
    }

    private static int MeasureWidth(List<LayoutNode> nodes, int start, int end)
    {
        int width = 0;
        for (int i = start; i < end; i++)
        {
            width += nodes[i].FormattedText.Length;
            if (i > start)
                width += nodes[i].PrecedingSpaces;
        }
        return width;
    }

    private static int GetClauseKeywordLength(List<LayoutNode> nodes, int keywordIndex)
    {
        int length = nodes[keywordIndex].FormattedText.Length;

        // JoinRules' joinTypeStyle "explicit" rewrites a bare JOIN's text to "INNER JOIN" in the
        // same pass (before this measurement), but a re-format tokenises INNER as its own node —
        // so measuring the full rewritten text makes the cross-clause alignment column differ
        // between the first format and a re-format (non-idempotent). Measure only the keyword
        // itself (the last word) so both passes see the same width.
        if (nodes[keywordIndex].TokenType == TSqlTokenType.Join)
        {
            int lastSpace = nodes[keywordIndex].FormattedText.LastIndexOf(' ');
            if (lastSpace >= 0)
                length -= lastSpace + 1;
        }

        // Check for two-word keywords like GROUP BY, ORDER BY, INSERT INTO
        if (keywordIndex + 1 < nodes.Count)
        {
            var next = nodes[keywordIndex + 1];
            if (next.TokenType is TSqlTokenType.By or TSqlTokenType.Into)
            {
                length += 1 + next.FormattedText.Length; // +1 for space
            }
        }

        return length;
    }
}
