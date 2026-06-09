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

        ApplyCommaPosition(nodes, list);
        ApplyOneItemPerLine(nodes, list, profile);
        ApplyIndentListItems(nodes, list, profile);
        ApplyCollapseShortLists(nodes, list);
        ApplyAlignAliases(nodes, list);
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
                int listEnd = FindListEnd(nodes, listStart);

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
    /// </summary>
    private static void ApplyAlignAliases(List<LayoutNode> nodes, ListOptions list)
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
        => IsClauseKeyword(tokenType) || tokenType is TSqlTokenType.Order or TSqlTokenType.Group;

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

    private static int FindListEnd(List<LayoutNode> nodes, int start)
    {
        for (int i = start; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion)
                continue;

            // End of list: next clause keyword or statement terminator
            if (IsListBoundary(nodes[i].TokenType) ||
                nodes[i].TokenType == TSqlTokenType.Semicolon ||
                nodes[i].TokenType == TSqlTokenType.Go)
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
