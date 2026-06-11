using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Layout;

/// <summary>
/// Spec 030 T013 — true right-alignment for <c>operators.alignment: rightAligned</c> and
/// <c>inStatements.alignment: rightAligned</c>. Right-alignment needs per-space columns the tab
/// grid can't hit, so it runs as a finalization pass (after every rule + the alignment/spacing
/// finalizers) and writes <see cref="LayoutNode.AbsoluteLeadingSpaces"/>, which the emitter honors
/// for line-start tokens in spaces mode. Tabs mode can't sub-align, so this is a no-op there.
/// <list type="bullet">
///   <item>Operators: line-start <c>AND</c>/<c>OR</c> at the same indent share a right edge
///     (the wider keyword's right edge), so <c>OR</c> tucks one space in under <c>AND</c>.</item>
///   <item>IN items: when an IN list is multi-line, each item is right-justified to the widest
///     item's right edge.</item>
/// </list>
/// Idempotent: every column is recomputed from the final token geometry, which re-derives
/// identically on a re-format.
/// </summary>
internal static class RightAligner
{
    public static void Align(List<LayoutNode> nodes, FormattingProfile profile)
    {
        // Sub-tab alignment is impossible with tabs; leave the indent-driven layout untouched.
        if (profile.Whitespace.TabStyle == "tabs") return;
        int tabSize = profile.Whitespace.TabSize > 0 ? profile.Whitespace.TabSize : 4;

        if (string.Equals(profile.Operators?.Alignment, "rightAligned", StringComparison.OrdinalIgnoreCase))
            AlignOperators(nodes, tabSize);

        if (string.Equals(profile.InStatements?.Alignment, "rightAligned", StringComparison.OrdinalIgnoreCase))
            AlignInItems(nodes, tabSize);
    }

    /// <summary>
    /// Right-align line-start AND/OR operators that share an indent level to a common right edge.
    /// Grouping by indent keeps a nested subquery's operators in their own column.
    /// </summary>
    private static void AlignOperators(List<LayoutNode> nodes, int tabSize)
    {
        // Bucket line-start AND/OR nodes by their indent column.
        var byColumn = new Dictionary<int, List<int>>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n.IsInNoformatRegion) continue;
            if (n.PrecedingBreak == BreakType.None) continue;
            if (n.TokenType is not (TSqlTokenType.And or TSqlTokenType.Or)) continue;

            int col = n.IndentLevel * tabSize;
            if (!byColumn.TryGetValue(col, out var list)) byColumn[col] = list = new List<int>();
            list.Add(i);
        }

        foreach (var (col, indices) in byColumn)
        {
            if (indices.Count < 2) continue;   // nothing to align a lone operator against
            int maxWidth = 0;
            foreach (var idx in indices)
                maxWidth = Math.Max(maxWidth, nodes[idx].FormattedText.Length);

            foreach (var idx in indices)
                nodes[idx].AbsoluteLeadingSpaces = col + (maxWidth - nodes[idx].FormattedText.Length);
        }
    }

    /// <summary>
    /// Right-justify the items of each multi-line IN list to the widest item's right edge.
    /// </summary>
    private static void AlignInItems(List<LayoutNode> nodes, int tabSize)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.In) continue;

            int open = -1;
            for (int j = i + 1; j < nodes.Count && j <= i + 3; j++)
                if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis) { open = j; break; }
            if (open < 0) continue;

            int close = FindMatchingParen(nodes, open);
            if (close < 0) continue;

            // Skip subqueries — right-justifying a SELECT body is meaningless.
            bool hasSubquery = false;
            for (int j = open + 1; j < close; j++)
                if (nodes[j].TokenType == TSqlTokenType.Select) { hasSubquery = true; break; }
            if (hasSubquery) continue;

            // Collect each item as (firstNodeIndex, renderedWidth); only items that start their
            // own line participate (i.e. the list was expanded multi-line).
            var items = new List<(int first, int width)>();
            int itemStart = open + 1;
            int depth = 0;
            for (int j = open + 1; j <= close; j++)
            {
                if (j < close && nodes[j].TokenType == TSqlTokenType.LeftParenthesis) { depth++; continue; }
                if (nodes[j].TokenType == TSqlTokenType.RightParenthesis) { if (j == close) { AddItem(); break; } depth--; continue; }
                if (depth == 0 && nodes[j].TokenType == TSqlTokenType.Comma) { AddItem(); itemStart = j + 1; }
                continue;

                void AddItem()
                {
                    if (itemStart >= j) return;
                    if (nodes[itemStart].PrecedingBreak == BreakType.None) return;  // not on its own line
                    int width = 0;
                    for (int k = itemStart; k < j; k++)
                        width += nodes[k].FormattedText.Length + (k == itemStart ? 0 : nodes[k].PrecedingSpaces);
                    items.Add((itemStart, width));
                }
            }

            if (items.Count < 2) continue;
            int baseCol = nodes[items[0].first].IndentLevel * tabSize;
            int maxItem = 0;
            foreach (var it in items) maxItem = Math.Max(maxItem, it.width);

            foreach (var (first, width) in items)
                nodes[first].AbsoluteLeadingSpaces = baseCol + (maxItem - width);

            i = close;
        }
    }

    private static int FindMatchingParen(List<LayoutNode> nodes, int openIndex)
    {
        int depth = 0;
        for (int i = openIndex; i < nodes.Count; i++)
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
}
