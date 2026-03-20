using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Layout;

/// <summary>
/// Evaluates whether a group of LayoutNodes should be collapsed onto a single line
/// based on total formatted length and configured threshold values.
///
/// Used by rule sets to decide whether multi-line formatting should be collapsed
/// for readability when the content is short enough to fit on one line.
/// </summary>
public class CollapseEvaluator
{
    private readonly FormattingProfile _profile;

    public CollapseEvaluator(FormattingProfile profile)
    {
        _profile = profile;
    }

    /// <summary>
    /// Determines if the nodes in the range [start, end) should be collapsed to a single line.
    /// Returns true if the total formatted length is at or below the threshold.
    /// </summary>
    public bool ShouldCollapse(List<LayoutNode> nodes, int start, int end, int threshold)
    {
        if (start >= end || start < 0 || end > nodes.Count)
            return false;

        int totalLength = MeasureFormattedLength(nodes, start, end);
        return totalLength <= threshold;
    }

    /// <summary>
    /// Determines if a SELECT list should be collapsed based on list options.
    /// </summary>
    public bool ShouldCollapseSelectList(List<LayoutNode> nodes, int start, int end)
    {
        if (!_profile.List.CollapseShortLists)
            return false;

        return ShouldCollapse(nodes, start, end, _profile.List.CollapseThreshold);
    }

    /// <summary>
    /// Determines if a parenthesized group should be collapsed based on parenthesis options.
    /// </summary>
    public bool ShouldCollapseParenthesized(List<LayoutNode> nodes, int start, int end)
    {
        if (!_profile.Parenthesis.CollapseShort)
            return false;

        // Don't collapse if contains subqueries
        for (int i = start; i < end; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Select)
                return false;
        }

        return ShouldCollapse(nodes, start, end, _profile.Parenthesis.CollapseThreshold);
    }

    /// <summary>
    /// Determines if a DML statement should be collapsed based on DML options.
    /// </summary>
    public bool ShouldCollapseDmlStatement(List<LayoutNode> nodes, int start, int end)
    {
        if (!_profile.Dml.CollapseShortStatements)
            return false;

        // Don't collapse if contains subqueries
        int parenDepth = 0;
        for (int i = start; i < end; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.LeftParenthesis) parenDepth++;
            else if (nodes[i].TokenType == TSqlTokenType.RightParenthesis) parenDepth--;

            if (parenDepth > 0 && nodes[i].TokenType == TSqlTokenType.Select)
                return false;
        }

        return ShouldCollapse(nodes, start, end, _profile.Dml.CollapseThreshold);
    }

    /// <summary>
    /// Determines if a subquery should be collapsed based on DML options.
    /// </summary>
    public bool ShouldCollapseSubquery(List<LayoutNode> nodes, int start, int end)
    {
        if (!_profile.Dml.CollapseShortSubqueries)
            return false;

        return ShouldCollapse(nodes, start, end, _profile.Dml.SubqueryCollapseThreshold);
    }

    /// <summary>
    /// Determines if a CASE expression should be collapsed based on case options.
    /// </summary>
    public bool ShouldCollapseCase(List<LayoutNode> nodes, int start, int end)
    {
        if (!_profile.Case.CollapseShortCase)
            return false;

        return ShouldCollapse(nodes, start, end, _profile.Case.CollapseThreshold);
    }

    /// <summary>
    /// Determines if an IF/ELSE block should be collapsed based on control flow options.
    /// </summary>
    public bool ShouldCollapseIfElse(List<LayoutNode> nodes, int start, int end)
    {
        if (!_profile.ControlFlow.CollapseShortIfElse)
            return false;

        // Don't collapse if contains BEGIN/END
        for (int i = start; i < end; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Begin)
                return false;
        }

        return ShouldCollapse(nodes, start, end, _profile.ControlFlow.CollapseThreshold);
    }

    /// <summary>
    /// Determines if a DDL statement should be collapsed based on DDL options.
    /// </summary>
    public bool ShouldCollapseDdl(List<LayoutNode> nodes, int start, int end)
    {
        if (!_profile.Ddl.CollapseShortDDL)
            return false;

        return ShouldCollapse(nodes, start, end, _profile.Ddl.CollapseThreshold);
    }

    /// <summary>
    /// Collapses the nodes in the range [start, end) onto a single line by removing
    /// line breaks and setting appropriate spacing.
    /// </summary>
    public static void Collapse(List<LayoutNode> nodes, int start, int end)
    {
        for (int i = start; i < end && i < nodes.Count; i++)
        {
            if (nodes[i].PrecedingBreak == BreakType.NewLine || nodes[i].PrecedingBreak == BreakType.EmptyLine)
            {
                nodes[i].PrecedingBreak = BreakType.None;
                // Commas get no space before them, everything else gets 1 space
                nodes[i].PrecedingSpaces = nodes[i].TokenType == TSqlTokenType.Comma ? 0 : 1;
                nodes[i].IndentLevel = 0;
            }
        }
    }

    /// <summary>
    /// Conditionally collapses a range based on threshold. Returns true if collapsed.
    /// </summary>
    public bool CollapseIfShort(List<LayoutNode> nodes, int start, int end, int threshold)
    {
        if (ShouldCollapse(nodes, start, end, threshold))
        {
            Collapse(nodes, start, end);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Measures the total formatted length of nodes in the range [start, end)
    /// as if they were rendered on a single line. This is the key metric used
    /// to determine whether collapsing is appropriate.
    ///
    /// The measurement includes:
    /// - Each node's FormattedText length
    /// - Preceding spaces for inline nodes
    /// - 1 character for each line break (as a proxy for the space it would become)
    /// </summary>
    public static int MeasureFormattedLength(List<LayoutNode> nodes, int start, int end)
    {
        int length = 0;

        for (int i = start; i < end && i < nodes.Count; i++)
        {
            // Skip nodes with empty text (removed by other rules)
            if (nodes[i].FormattedText.Length == 0)
                continue;

            if (nodes[i].PrecedingBreak == BreakType.None)
            {
                length += nodes[i].PrecedingSpaces;
            }
            else if (i > start)
            {
                // A line break would become a single space when collapsed
                length += 1;
            }

            length += nodes[i].FormattedText.Length;
        }

        return length;
    }

    /// <summary>
    /// Measures the formatted length of nodes between matching parentheses (exclusive).
    /// Does not include the parentheses themselves.
    /// </summary>
    public static int MeasureParenthesizedLength(List<LayoutNode> nodes, int openParenIndex, int closeParenIndex)
    {
        return MeasureFormattedLength(nodes, openParenIndex + 1, closeParenIndex);
    }

    /// <summary>
    /// Counts the number of distinct lines in a range of nodes.
    /// </summary>
    public static int CountLines(List<LayoutNode> nodes, int start, int end)
    {
        if (start >= end || start < 0)
            return 0;

        int lines = 1;
        for (int i = start + 1; i < end && i < nodes.Count; i++)
        {
            if (nodes[i].PrecedingBreak == BreakType.NewLine || nodes[i].PrecedingBreak == BreakType.EmptyLine)
                lines++;
        }
        return lines;
    }

    /// <summary>
    /// Finds the maximum line width in a range of nodes.
    /// Useful for determining if content exceeds maxLineWidth after formatting.
    /// </summary>
    public static int MaxLineWidth(List<LayoutNode> nodes, int start, int end, int tabSize)
    {
        int maxWidth = 0;
        int currentWidth = 0;

        for (int i = start; i < end && i < nodes.Count; i++)
        {
            if (nodes[i].PrecedingBreak == BreakType.NewLine || nodes[i].PrecedingBreak == BreakType.EmptyLine)
            {
                maxWidth = Math.Max(maxWidth, currentWidth);
                currentWidth = nodes[i].IndentLevel * tabSize;
            }
            else
            {
                currentWidth += nodes[i].PrecedingSpaces;
            }

            currentWidth += nodes[i].FormattedText.Length;
        }

        maxWidth = Math.Max(maxWidth, currentWidth);
        return maxWidth;
    }
}
