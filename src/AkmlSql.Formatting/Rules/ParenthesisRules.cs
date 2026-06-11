using System.Diagnostics.CodeAnalysis;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Rules;

/// <summary>
/// Implements parenthesis formatting rules as a post-processing pass:
/// openOnSameLine, closeOnNewLine, collapseShort, collapseThreshold,
/// indentContents, spaceInside, removeRedundant, createTableColumns,
/// procedureParameters, subqueryStyle.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class ParenthesisRules : IRuleSet
{
    public void Apply(List<LayoutNode> nodes, FormattingProfile profile)
    {
        var paren = profile.Parenthesis;

        // Build a mapping of matching parentheses first
        var parenPairs = BuildParenPairs(nodes);

        ApplyOpenOnSameLine(nodes, paren);
        ApplyCloseOnNewLine(nodes, paren, parenPairs);
        ApplyIndentContents(nodes, paren, parenPairs);
        ApplySpaceInside(nodes, paren, parenPairs);
        ApplyCollapseShort(nodes, paren, parenPairs);
        // ApplyRemoveRedundant is FORCE-DISABLED (spec 030 T011): it peels exactly one paren
        // layer per format (only the innermost pair of "((a))" wraps a single token), so it is
        // non-idempotent — and with Stage-6 validation on, the peeled output fails the semantic
        // compare and Format silently returns the ORIGINAL (the option disabled formatting
        // entirely). Re-enable only after a rebuild with a fixpoint loop + semantic guards.
        // The profile option stays in the schema for round-trip compatibility.
        ApplyCreateTableColumns(nodes, paren, parenPairs);
        ApplyProcedureParameters(nodes, paren, parenPairs);
        ApplySubqueryStyle(nodes, paren, parenPairs);
    }

    /// <summary>
    /// Builds a dictionary mapping open paren index to close paren index and vice versa.
    /// </summary>
    private static Dictionary<int, int> BuildParenPairs(List<LayoutNode> nodes)
    {
        var pairs = new Dictionary<int, int>();
        var stack = new Stack<int>();

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.LeftParenthesis)
            {
                stack.Push(i);
            }
            else if (nodes[i].TokenType == TSqlTokenType.RightParenthesis && stack.Count > 0)
            {
                int openIndex = stack.Pop();
                pairs[openIndex] = i;
                pairs[i] = openIndex;
            }
        }

        return pairs;
    }

    /// <summary>
    /// openOnSameLine: when true, the opening parenthesis stays on the same line as the
    /// preceding keyword/identifier. When false, it goes on a new line.
    /// </summary>
    private static void ApplyOpenOnSameLine(List<LayoutNode> nodes, ParenthesisOptions paren)
    {
        for (int i = 1; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion || node.TokenType != TSqlTokenType.LeftParenthesis)
                continue;

            if (paren.OpenOnSameLine)
            {
                // Keep the open paren on the same line
                if (node.PrecedingBreak is BreakType.NewLine or BreakType.EmptyLine)
                {
                    node.PrecedingBreak = BreakType.None;
                    node.PrecedingSpaces = 0;
                }
            }
            else
            {
                // Move open paren to new line
                if (node.PrecedingBreak == BreakType.None)
                {
                    node.PrecedingBreak = BreakType.NewLine;
                    node.PrecedingSpaces = 0;
                }
            }
        }
    }

    /// <summary>
    /// closeOnNewLine: controls whether closing parenthesis is on its own line.
    /// Values: "true" (always), "false" (never), "matchOpen" (align with open).
    /// </summary>
    private static void ApplyCloseOnNewLine(List<LayoutNode> nodes, ParenthesisOptions paren,
        Dictionary<int, int> parenPairs)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (node.IsInNoformatRegion || node.TokenType != TSqlTokenType.RightParenthesis)
                continue;

            switch (paren.CloseOnNewLine)
            {
                case "true":
                    if (node.PrecedingBreak == BreakType.None)
                    {
                        node.PrecedingBreak = BreakType.NewLine;
                        node.PrecedingSpaces = 0;
                    }
                    // Align indent with matching open paren
                    if (parenPairs.TryGetValue(i, out int openIdx))
                    {
                        node.IndentLevel = nodes[openIdx].IndentLevel;
                    }
                    break;

                case "matchOpen":
                    // Close paren goes on a new line at the same indent as the open paren
                    if (parenPairs.TryGetValue(i, out int matchOpenIdx))
                    {
                        node.PrecedingBreak = BreakType.NewLine;
                        node.IndentLevel = nodes[matchOpenIdx].IndentLevel;
                        node.PrecedingSpaces = 0;
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// indentContents: when true, contents within parentheses are indented one level deeper
    /// than the opening parenthesis.
    /// </summary>
    private static void ApplyIndentContents(List<LayoutNode> nodes, ParenthesisOptions paren,
        Dictionary<int, int> parenPairs)
    {
        if (!paren.IndentContents)
            return;

        foreach (var (openIdx, closeIdx) in parenPairs)
        {
            // Only process open->close pairs (skip close->open entries)
            if (nodes[openIdx].TokenType != TSqlTokenType.LeftParenthesis)
                continue;

            int baseIndent = nodes[openIdx].IndentLevel;

            for (int i = openIdx + 1; i < closeIdx; i++)
            {
                if (nodes[i].IsInNoformatRegion)
                    continue;

                if (nodes[i].PrecedingBreak == BreakType.NewLine || nodes[i].PrecedingBreak == BreakType.EmptyLine)
                {
                    // Only bump indent if it's not already deeper (e.g., nested parens)
                    if (nodes[i].IndentLevel <= baseIndent)
                    {
                        nodes[i].IndentLevel = baseIndent + 1;
                    }
                }
            }
        }
    }

    /// <summary>
    /// spaceInside: when true, adds a space after '(' and before ')'.
    /// </summary>
    private static void ApplySpaceInside(List<LayoutNode> nodes, ParenthesisOptions paren,
        Dictionary<int, int> parenPairs)
    {
        foreach (var (openIdx, closeIdx) in parenPairs)
        {
            if (nodes[openIdx].TokenType != TSqlTokenType.LeftParenthesis)
                continue;

            // Space after open paren
            if (openIdx + 1 < nodes.Count && openIdx + 1 <= closeIdx)
            {
                var next = nodes[openIdx + 1];
                if (next.PrecedingBreak == BreakType.None)
                {
                    next.PrecedingSpaces = paren.SpaceInside ? 1 : 0;
                }
            }

            // Space before close paren
            if (closeIdx < nodes.Count && nodes[closeIdx].PrecedingBreak == BreakType.None)
            {
                nodes[closeIdx].PrecedingSpaces = paren.SpaceInside ? 1 : 0;
            }
        }
    }

    /// <summary>
    /// collapseShort: when true and the parenthesized content fits within collapseThreshold
    /// characters, collapse it onto a single line.
    /// </summary>
    private static void ApplyCollapseShort(List<LayoutNode> nodes, ParenthesisOptions paren,
        Dictionary<int, int> parenPairs)
    {
        if (!paren.CollapseShort)
            return;

        foreach (var (openIdx, closeIdx) in parenPairs)
        {
            if (nodes[openIdx].TokenType != TSqlTokenType.LeftParenthesis)
                continue;

            // Don't collapse if it contains a subquery
            if (ContainsSubquery(nodes, openIdx, closeIdx))
                continue;

            int contentLength = MeasureContentLength(nodes, openIdx + 1, closeIdx);
            if (contentLength <= paren.CollapseThreshold)
            {
                // Collapse everything between open and close onto one line
                for (int i = openIdx + 1; i <= closeIdx; i++)
                {
                    if (nodes[i].PrecedingBreak == BreakType.NewLine || nodes[i].PrecedingBreak == BreakType.EmptyLine)
                    {
                        nodes[i].PrecedingBreak = BreakType.None;
                        nodes[i].PrecedingSpaces = nodes[i].TokenType == TSqlTokenType.Comma ? 0 : 1;
                        nodes[i].IndentLevel = 0;
                    }
                }

                // Close paren: no space before it (or 1 if spaceInside)
                nodes[closeIdx].PrecedingSpaces = paren.SpaceInside ? 1 : 0;
            }
        }
    }

    /// <summary>
    /// removeRedundant: removes unnecessary parentheses around simple expressions.
    /// Only removes parens that wrap a single identifier/literal with no operators inside.
    /// </summary>
    private static void ApplyRemoveRedundant(List<LayoutNode> nodes, ParenthesisOptions paren,
        Dictionary<int, int> parenPairs)
    {
        if (!paren.RemoveRedundant)
            return;

        // Collect pairs to remove (process in reverse to preserve indices)
        var toRemove = new List<(int open, int close)>();

        foreach (var (openIdx, closeIdx) in parenPairs)
        {
            if (nodes[openIdx].TokenType != TSqlTokenType.LeftParenthesis)
                continue;

            // Only remove if there's exactly one semantic token inside
            int semanticCount = 0;
            bool hasOperator = false;

            for (int i = openIdx + 1; i < closeIdx; i++)
            {
                if (nodes[i].TokenType == TSqlTokenType.WhiteSpace)
                    continue;

                semanticCount++;
                if (IsOperator(nodes[i].TokenType))
                    hasOperator = true;
            }

            // Don't remove if preceded by a function call or keyword that requires parens
            if (openIdx > 0 && RequiresParentheses(nodes[openIdx - 1].TokenType))
                continue;

            if (semanticCount == 1 && !hasOperator)
            {
                toRemove.Add((openIdx, closeIdx));
            }
        }

        // Remove in reverse order to preserve indices
        toRemove.Sort((a, b) => b.open.CompareTo(a.open));
        foreach (var (open, close) in toRemove)
        {
            // Transfer spacing from what follows the close paren
            if (close + 1 < nodes.Count)
            {
                // Keep the space that was before the open paren on the first content token
                if (open + 1 < close)
                {
                    nodes[open + 1].PrecedingSpaces = nodes[open].PrecedingSpaces;
                    nodes[open + 1].PrecedingBreak = nodes[open].PrecedingBreak;
                    nodes[open + 1].IndentLevel = nodes[open].IndentLevel;
                }
            }

            // Mark parens for removal by clearing their text
            nodes[open].FormattedText = "";
            nodes[open].PrecedingSpaces = 0;
            nodes[close].FormattedText = "";
            nodes[close].PrecedingSpaces = 0;
        }
    }

    /// <summary>
    /// createTableColumns: controls formatting of column definitions in CREATE TABLE.
    /// "newLine" puts each column on a new line, "sameLine" keeps them on one line.
    /// </summary>
    private static void ApplyCreateTableColumns(List<LayoutNode> nodes, ParenthesisOptions paren,
        Dictionary<int, int> parenPairs)
    {
        if (paren.CreateTableColumns != "newLine")
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion)
                continue;

            // Detect CREATE TABLE ... (
            if (nodes[i].TokenType == TSqlTokenType.Create)
            {
                int tableIdx = FindNextToken(nodes, i + 1, TSqlTokenType.Table);
                if (tableIdx < 0)
                    continue;

                int openParen = FindNextToken(nodes, tableIdx + 1, TSqlTokenType.LeftParenthesis);
                if (openParen < 0 || !parenPairs.TryGetValue(openParen, out int closeParen))
                    continue;

                // Put each column definition on a new line — only after TOP-LEVEL commas.
                // Commas nested in type/identity arguments ("decimal(18, 2)", "identity(1, 1)")
                // are not column boundaries (same depth fix as DdlRules.
                // ApplyCreateTableColumnsOnNewLine — this pass duplicates its break loop).
                int parenDepth = 0;
                for (int j = openParen + 1; j < closeParen; j++)
                {
                    if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis)
                    {
                        parenDepth++;
                        continue;
                    }
                    if (nodes[j].TokenType == TSqlTokenType.RightParenthesis)
                    {
                        parenDepth--;
                        continue;
                    }

                    if (parenDepth == 0 && nodes[j].TokenType == TSqlTokenType.Comma && j + 1 < closeParen)
                    {
                        var next = nodes[j + 1];
                        if (next.PrecedingBreak == BreakType.None)
                        {
                            next.PrecedingBreak = BreakType.NewLine;
                            next.IndentLevel = 1;
                            next.PrecedingSpaces = 0;
                        }
                    }
                }

                // First column after open paren on new line
                if (openParen + 1 < closeParen)
                {
                    var firstCol = nodes[openParen + 1];
                    if (firstCol.PrecedingBreak == BreakType.None)
                    {
                        firstCol.PrecedingBreak = BreakType.NewLine;
                        firstCol.IndentLevel = 1;
                        firstCol.PrecedingSpaces = 0;
                    }
                }

                // Close paren on new line
                if (nodes[closeParen].PrecedingBreak == BreakType.None)
                {
                    nodes[closeParen].PrecedingBreak = BreakType.NewLine;
                    nodes[closeParen].IndentLevel = 0;
                    nodes[closeParen].PrecedingSpaces = 0;
                }

                i = closeParen;
            }
        }
    }

    /// <summary>
    /// procedureParameters: controls formatting of procedure parameter lists.
    /// "newLine" puts each parameter on a new line, "sameLine" keeps them on one line.
    /// </summary>
    private static void ApplyProcedureParameters(List<LayoutNode> nodes, ParenthesisOptions paren,
        Dictionary<int, int> parenPairs)
    {
        if (paren.ProcedureParameters != "newLine")
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion)
                continue;

            // Detect CREATE/ALTER PROCEDURE ... (
            bool isProcDef = nodes[i].TokenType == TSqlTokenType.Procedure;
            if (!isProcDef)
                continue;

            // Look for the opening parenthesis after the procedure name
            int openParen = FindNextToken(nodes, i + 1, TSqlTokenType.LeftParenthesis);
            if (openParen < 0 || !parenPairs.TryGetValue(openParen, out int closeParen))
                continue;

            // Put each parameter on a new line (after each comma)
            for (int j = openParen + 1; j < closeParen; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.Comma && j + 1 < closeParen)
                {
                    var next = nodes[j + 1];
                    if (next.PrecedingBreak == BreakType.None)
                    {
                        next.PrecedingBreak = BreakType.NewLine;
                        next.IndentLevel = 1;
                        next.PrecedingSpaces = 0;
                    }
                }
            }

            // First parameter after open paren on new line
            if (openParen + 1 < closeParen)
            {
                var firstParam = nodes[openParen + 1];
                if (firstParam.PrecedingBreak == BreakType.None)
                {
                    firstParam.PrecedingBreak = BreakType.NewLine;
                    firstParam.IndentLevel = 1;
                    firstParam.PrecedingSpaces = 0;
                }
            }

            i = closeParen;
        }
    }

    /// <summary>
    /// subqueryStyle: controls how subqueries within parentheses are formatted.
    /// "indent" indents the subquery body, "alignWithParen" aligns with the paren,
    /// "sameLine" keeps it on the same line if short enough.
    /// </summary>
    private static void ApplySubqueryStyle(List<LayoutNode> nodes, ParenthesisOptions paren,
        Dictionary<int, int> parenPairs)
    {
        foreach (var (openIdx, closeIdx) in parenPairs)
        {
            if (nodes[openIdx].TokenType != TSqlTokenType.LeftParenthesis)
                continue;

            if (!ContainsSubquery(nodes, openIdx, closeIdx))
                continue;

            int baseIndent = nodes[openIdx].IndentLevel;

            switch (paren.SubqueryStyle)
            {
                case "indent":
                    // Subquery SELECT on new line, indented
                    for (int i = openIdx + 1; i < closeIdx; i++)
                    {
                        if (nodes[i].IsInNoformatRegion)
                            continue;

                        if (nodes[i].PrecedingBreak == BreakType.NewLine ||
                            nodes[i].PrecedingBreak == BreakType.EmptyLine)
                        {
                            nodes[i].IndentLevel = Math.Max(nodes[i].IndentLevel, baseIndent + 1);
                        }
                        else if (nodes[i].TokenType == TSqlTokenType.Select)
                        {
                            // Force SELECT to new line inside subquery
                            nodes[i].PrecedingBreak = BreakType.NewLine;
                            nodes[i].IndentLevel = baseIndent + 1;
                            nodes[i].PrecedingSpaces = 0;
                        }
                    }
                    break;

                case "alignWithParen":
                    // Subquery body aligns with the open paren column
                    for (int i = openIdx + 1; i < closeIdx; i++)
                    {
                        if (nodes[i].IsInNoformatRegion)
                            continue;

                        if (nodes[i].PrecedingBreak == BreakType.NewLine ||
                            nodes[i].PrecedingBreak == BreakType.EmptyLine)
                        {
                            nodes[i].IndentLevel = baseIndent;
                        }
                    }
                    break;

                    // "sameLine" — handled by collapseShort
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helper methods
    // -----------------------------------------------------------------------

    private static bool ContainsSubquery(List<LayoutNode> nodes, int openIdx, int closeIdx)
    {
        for (int i = openIdx + 1; i < closeIdx; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Select)
                return true;
        }
        return false;
    }

    private static int MeasureContentLength(List<LayoutNode> nodes, int start, int end)
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

    private static bool IsOperator(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.Plus or TSqlTokenType.Minus or TSqlTokenType.Star or
            TSqlTokenType.Divide or TSqlTokenType.PercentSign or
            TSqlTokenType.EqualsSign or TSqlTokenType.GreaterThan or TSqlTokenType.LessThan or
            TSqlTokenType.And or TSqlTokenType.Or or TSqlTokenType.Not or
            TSqlTokenType.Bang => true,
            _ => false,
        };
    }

    private static bool RequiresParentheses(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.Identifier or // function call
            TSqlTokenType.Coalesce or TSqlTokenType.NullIf or TSqlTokenType.Convert or
            TSqlTokenType.Exists or TSqlTokenType.In or
            TSqlTokenType.Case or TSqlTokenType.When => true,
            _ => false,
        };
    }

    private static int FindNextToken(List<LayoutNode> nodes, int start, TSqlTokenType tokenType)
    {
        for (int i = start; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == tokenType)
                return i;
            // Stop searching at statement boundaries
            if (nodes[i].TokenType == TSqlTokenType.Semicolon || nodes[i].TokenType == TSqlTokenType.Go)
                return -1;
        }
        return -1;
    }
}
