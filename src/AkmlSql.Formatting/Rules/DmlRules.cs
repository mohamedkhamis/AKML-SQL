using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Rules;

/// <summary>
/// Implements all DML-specific layout decisions as a post-processing pass:
/// selectItemsOnNewLine, selectStarOnSameLine, fromOnNewLine, whereOnNewLine,
/// andOrNewLine, andOrIndent, groupByOnNewLine, havingOnNewLine, orderByOnNewLine,
/// topOnSameLine, distinctOnSameLine, intoOnNewLine, valuesOnNewLine, setOnNewLine,
/// deleteFromOnSameLine, mergeWhenOnNewLine, collapseShortStatements, collapseThreshold,
/// collapseShortSubqueries, subqueryCollapseThreshold.
///
/// Note: Most DML formatting is handled during LayoutEngine.BuildLayout via LineBreakDecider.
/// This rule set provides additional corrections and edge-case handling.
/// </summary>
public class DmlRules : IRuleSet
{
    public void Apply(List<LayoutNode> nodes, FormattingProfile profile)
    {
        var dml = profile.Dml;

        ApplyAndOrNewLine(nodes, dml);
        ApplyAndOrIndent(nodes, dml);
        ApplySelectStarOnSameLine(nodes, dml);
        ApplyIntoOnNewLine(nodes, dml);
        ApplySetOnNewLine(nodes, dml);
        ApplyValuesOnNewLine(nodes, dml);
        ApplyTopOnSameLine(nodes, dml);
        ApplyDistinctOnSameLine(nodes, dml);
        ApplyDeleteFromOnSameLine(nodes, dml);
        ApplyMergeWhenOnNewLine(nodes, dml);
        ApplyCollapseShortStatements(nodes, dml);
        ApplyCollapseShortSubqueries(nodes, dml);
    }

    /// <summary>
    /// Post-process AND/OR: "before" puts break before AND/OR, "after" puts break after.
    /// </summary>
    private static void ApplyAndOrNewLine(List<LayoutNode> nodes, DmlOptions dml)
    {
        if (dml.AndOrNewLine == "after")
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i].TokenType == TSqlTokenType.And || nodes[i].TokenType == TSqlTokenType.Or)
                {
                    // Remove break before AND/OR if it was set
                    if (nodes[i].PrecedingBreak == BreakType.NewLine)
                    {
                        nodes[i].PrecedingBreak = BreakType.None;
                        nodes[i].PrecedingSpaces = 1;
                    }

                    // Add break after AND/OR
                    if (i + 1 < nodes.Count && nodes[i + 1].PrecedingBreak == BreakType.None)
                    {
                        nodes[i + 1].PrecedingBreak = BreakType.NewLine;
                        nodes[i + 1].IndentLevel = 1;
                        nodes[i + 1].PrecedingSpaces = 0;
                    }
                }
            }
        }
        else if (dml.AndOrNewLine == "none")
        {
            // Keep AND/OR inline with no forced line breaks
            foreach (var t in nodes)
            {
                if (t.TokenType is TSqlTokenType.And or TSqlTokenType.Or)
                {
                    if (t.PrecedingBreak == BreakType.NewLine)
                    {
                        t.PrecedingBreak = BreakType.None;
                        t.PrecedingSpaces = 1;
                    }
                }
            }
        }
    }

    /// <summary>
    /// andOrIndent: controls indentation of AND/OR conditions.
    /// "indent" — one level of indentation.
    /// "alignWithWhere" — aligns AND/OR under WHERE.
    /// "doubleIndent" — two levels of indentation.
    /// </summary>
    private static void ApplyAndOrIndent(List<LayoutNode> nodes, DmlOptions dml)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion)
                continue;

            if (node.TokenType is TSqlTokenType.And or TSqlTokenType.Or &&
                node.PrecedingBreak == BreakType.NewLine)
            {
                switch (dml.AndOrIndent)
                {
                    case "indent":
                        node.IndentLevel = 1;
                        break;
                    case "alignWithWhere":
                        // Indent level 0 aligns with clause keywords (WHERE is at level 0)
                        node.IndentLevel = 0;
                        break;
                    case "doubleIndent":
                        node.IndentLevel = 2;
                        break;
                }
            }

            // For "after" mode, adjust the token after AND/OR
            if (dml.AndOrNewLine == "after" && i > 0 &&
                (nodes[i - 1].TokenType == TSqlTokenType.And || nodes[i - 1].TokenType == TSqlTokenType.Or) &&
                node.PrecedingBreak == BreakType.NewLine)
            {
                switch (dml.AndOrIndent)
                {
                    case "indent":
                        node.IndentLevel = 1;
                        break;
                    case "alignWithWhere":
                        node.IndentLevel = 1;
                        break;
                    case "doubleIndent":
                        node.IndentLevel = 2;
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Handle SELECT * on same line option.
    /// </summary>
    private static void ApplySelectStarOnSameLine(List<LayoutNode> nodes, DmlOptions dml)
    {
        if (!dml.SelectStarOnSameLine)
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Star &&
                i > 0 && IsSelectKeyword(nodes, i))
            {
                if (nodes[i].PrecedingBreak == BreakType.NewLine)
                {
                    nodes[i].PrecedingBreak = BreakType.None;
                    nodes[i].PrecedingSpaces = 1;
                    nodes[i].IndentLevel = 0;
                }
            }
        }
    }

    /// <summary>
    /// Handle INSERT INTO on new line: INTO stays on same line as INSERT.
    /// </summary>
    private static void ApplyIntoOnNewLine(List<LayoutNode> nodes, DmlOptions dml)
    {
        if (!dml.IntoOnNewLine)
            return;

        for (int i = 1; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Into &&
                i > 0 && nodes[i - 1].TokenType == TSqlTokenType.Insert)
            {
                nodes[i].PrecedingBreak = BreakType.None;
                nodes[i].PrecedingSpaces = 1;
            }
        }
    }

    /// <summary>
    /// Handle SET on new line for UPDATE statements.
    /// </summary>
    private static void ApplySetOnNewLine(List<LayoutNode> nodes, DmlOptions dml)
    {
        if (!dml.SetOnNewLine)
            return;

        bool inUpdate = false;
        foreach (var t in nodes)
        {
            if (t.TokenType == TSqlTokenType.Update)
                inUpdate = true;
            else if (t.TokenType is TSqlTokenType.Semicolon or TSqlTokenType.Select or TSqlTokenType.Go)
                inUpdate = false;

            if (inUpdate && t.TokenType == TSqlTokenType.Set)
            {
                t.PrecedingBreak = BreakType.NewLine;
                t.IndentLevel = 0;
                t.PrecedingSpaces = 0;
            }
        }
    }

    /// <summary>
    /// Handle VALUES on new line for INSERT statements.
    /// </summary>
    private static void ApplyValuesOnNewLine(List<LayoutNode> nodes, DmlOptions dml)
    {
        if (!dml.ValuesOnNewLine)
            return;

        bool inInsert = false;
        foreach (var t in nodes)
        {
            if (t.TokenType == TSqlTokenType.Insert)
                inInsert = true;
            else if (t.TokenType is TSqlTokenType.Semicolon or TSqlTokenType.Go)
                inInsert = false;

            if (!inInsert || t.TokenType != TSqlTokenType.Values) continue;
            if (t.PrecedingBreak != BreakType.None) continue;
            t.PrecedingBreak = BreakType.NewLine;
            t.IndentLevel = 0;
            t.PrecedingSpaces = 0;
        }
    }

    /// <summary>
    /// topOnSameLine: keeps TOP on the same line as SELECT.
    /// </summary>
    private static void ApplyTopOnSameLine(List<LayoutNode> nodes, DmlOptions dml)
    {
        if (dml.TopOnSameLine)
            return; // Default is true, handled by LineBreakDecider

        // When false, put TOP on a new line
        for (int i = 1; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Top)
            {
                // Only if preceded by SELECT
                if (nodes[i - 1].TokenType == TSqlTokenType.Select ||
                    nodes[i - 1].TokenType == TSqlTokenType.Distinct)
                {
                    nodes[i].PrecedingBreak = BreakType.NewLine;
                    nodes[i].IndentLevel = 1;
                    nodes[i].PrecedingSpaces = 0;
                }
            }
        }
    }

    /// <summary>
    /// distinctOnSameLine: keeps DISTINCT on the same line as SELECT.
    /// </summary>
    private static void ApplyDistinctOnSameLine(List<LayoutNode> nodes, DmlOptions dml)
    {
        if (dml.DistinctOnSameLine)
            return;

        for (int i = 1; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Distinct &&
                nodes[i - 1].TokenType == TSqlTokenType.Select)
            {
                nodes[i].PrecedingBreak = BreakType.NewLine;
                nodes[i].IndentLevel = 1;
                nodes[i].PrecedingSpaces = 0;
            }
        }
    }

    /// <summary>
    /// deleteFromOnSameLine: keeps FROM on the same line as DELETE.
    /// </summary>
    private static void ApplyDeleteFromOnSameLine(List<LayoutNode> nodes, DmlOptions dml)
    {
        if (!dml.DeleteFromOnSameLine)
            return;

        for (int i = 1; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.From &&
                i > 0 && nodes[i - 1].TokenType == TSqlTokenType.Delete)
            {
                nodes[i].PrecedingBreak = BreakType.None;
                nodes[i].PrecedingSpaces = 1;
            }
        }
    }

    /// <summary>
    /// mergeWhenOnNewLine: puts WHEN clauses in MERGE statements on new lines.
    /// </summary>
    private static void ApplyMergeWhenOnNewLine(List<LayoutNode> nodes, DmlOptions dml)
    {
        if (!dml.MergeWhenOnNewLine)
            return;

        bool inMerge = false;
        foreach (var t in nodes)
        {
            inMerge = t.TokenType switch
            {
                TSqlTokenType.Merge => true,
                TSqlTokenType.Semicolon or TSqlTokenType.Go => false,
                _ => inMerge
            };

            if (!inMerge || t.TokenType != TSqlTokenType.When) continue;
            if (t.PrecedingBreak != BreakType.None) continue;
            t.PrecedingBreak = BreakType.NewLine;
            t.IndentLevel = 0;
            t.PrecedingSpaces = 0;
        }
    }

    /// <summary>
    /// collapseShortStatements: when true, simple DML statements that fit within
    /// collapseThreshold characters are kept on a single line.
    /// </summary>
    private static void ApplyCollapseShortStatements(List<LayoutNode> nodes, DmlOptions dml)
    {
        if (!dml.CollapseShortStatements)
            return;

        int i = 0;
        while (i < nodes.Count)
        {
            if (nodes[i].IsInNoformatRegion)
            {
                i++;
                continue;
            }

            if (IsStatementStart(nodes[i].TokenType))
            {
                int stmtStart = i;
                int stmtEnd = FindStatementEnd(nodes, i + 1);

                // Don't collapse if statement contains subqueries
                if (!ContainsSubquery(nodes, stmtStart, stmtEnd))
                {
                    int totalLength = MeasureLength(nodes, stmtStart, stmtEnd);
                    if (totalLength <= dml.CollapseThreshold)
                    {
                        CollapseRange(nodes, stmtStart, stmtEnd);
                    }
                }

                i = stmtEnd;
            }
            else
            {
                i++;
            }
        }
    }

    /// <summary>
    /// collapseShortSubqueries: when true, short subqueries that fit within
    /// subqueryCollapseThreshold characters are kept on a single line.
    /// </summary>
    private static void ApplyCollapseShortSubqueries(List<LayoutNode> nodes, DmlOptions dml)
    {
        if (!dml.CollapseShortSubqueries)
            return;

        // Find parenthesized subqueries
        var stack = new Stack<int>();

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.LeftParenthesis)
            {
                stack.Push(i);
            }
            else if (nodes[i].TokenType == TSqlTokenType.RightParenthesis && stack.Count > 0)
            {
                int openIdx = stack.Pop();

                // Check if this contains a subquery (SELECT inside parens)
                bool isSubquery = false;
                for (int j = openIdx + 1; j < i; j++)
                {
                    if (nodes[j].TokenType == TSqlTokenType.Select)
                    {
                        isSubquery = true;
                        break;
                    }
                }

                if (isSubquery)
                {
                    int totalLength = MeasureLength(nodes, openIdx, i + 1);
                    if (totalLength <= dml.SubqueryCollapseThreshold)
                    {
                        // Collapse everything inside the parens
                        for (int j = openIdx + 1; j <= i; j++)
                        {
                            if (nodes[j].PrecedingBreak == BreakType.NewLine ||
                                nodes[j].PrecedingBreak == BreakType.EmptyLine)
                            {
                                nodes[j].PrecedingBreak = BreakType.None;
                                nodes[j].PrecedingSpaces = nodes[j].TokenType == TSqlTokenType.Comma ? 0 : 1;
                                nodes[j].IndentLevel = 0;
                            }
                        }
                    }
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helper methods
    // -----------------------------------------------------------------------

    private static bool IsSelectKeyword(List<LayoutNode> nodes, int starIndex)
    {
        for (int i = starIndex - 1; i >= 0; i--)
        {
            var tt = nodes[i].TokenType;
            if (tt is TSqlTokenType.Select or TSqlTokenType.Distinct or TSqlTokenType.Top)
                return true;
            if (tt == TSqlTokenType.Integer) // TOP N
                continue;
            break;
        }
        return false;
    }

    private static bool IsStatementStart(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.Select or TSqlTokenType.Insert or TSqlTokenType.Update or
            TSqlTokenType.Delete or TSqlTokenType.Merge => true,
            _ => false,
        };
    }

    private static int FindStatementEnd(List<LayoutNode> nodes, int start)
    {
        for (int i = start; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Semicolon ||
                nodes[i].TokenType == TSqlTokenType.Go)
                return i;

            // Next statement start
            if (i > start && IsStatementStart(nodes[i].TokenType) &&
                nodes[i].PrecedingBreak != BreakType.None)
                return i;
        }
        return nodes.Count;
    }

    private static bool ContainsSubquery(List<LayoutNode> nodes, int start, int end)
    {
        int parenDepth = 0;
        for (int i = start; i < end; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.LeftParenthesis)
                parenDepth++;
            else if (nodes[i].TokenType == TSqlTokenType.RightParenthesis)
                parenDepth--;

            if (parenDepth > 0 && nodes[i].TokenType == TSqlTokenType.Select)
                return true;
        }
        return false;
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
                length += 1; // Count newline as 1 space for total width estimation
        }
        return length;
    }

    private static void CollapseRange(List<LayoutNode> nodes, int start, int end)
    {
        for (int i = start + 1; i < end && i < nodes.Count; i++)
        {
            if (nodes[i].PrecedingBreak == BreakType.NewLine || nodes[i].PrecedingBreak == BreakType.EmptyLine)
            {
                nodes[i].PrecedingBreak = BreakType.None;
                nodes[i].PrecedingSpaces = nodes[i].TokenType == TSqlTokenType.Comma ? 0 : 1;
                nodes[i].IndentLevel = 0;
            }
        }
    }
}
