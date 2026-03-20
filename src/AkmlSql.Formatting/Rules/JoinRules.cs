using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Rules;

/// <summary>
/// Implements all JOIN formatting as a post-processing pass:
/// onNewLine, indentJoin, onConditionNewLine, onConditionIndent,
/// multipleOnConditions, emptyLineBeforeJoin, alignJoinKeyword,
/// joinTypeStyle, crossApplyNewLine.
///
/// Note: Most JOIN formatting is handled during LayoutEngine.BuildLayout via LineBreakDecider.
/// This rule set provides additional corrections and edge-case handling.
/// </summary>
public class JoinRules : IRuleSet
{
    public void Apply(List<LayoutNode> nodes, FormattingProfile profile)
    {
        var join = profile.Join;

        ApplyEmptyLineBeforeJoin(nodes, join);
        ApplyIndentJoin(nodes, join);
        ApplyMultipleOnConditions(nodes, join);
        ApplyAlignJoinKeyword(nodes, join);
        ApplyJoinTypeStyle(nodes, join);
        ApplyCrossApplyNewLine(nodes, join);
    }

    /// <summary>
    /// emptyLineBeforeJoin: inserts an empty line before each JOIN clause for readability.
    /// </summary>
    private static void ApplyEmptyLineBeforeJoin(List<LayoutNode> nodes, JoinOptions join)
    {
        if (!join.EmptyLineBeforeJoin)
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (IsJoinStartToken(nodes, i))
            {
                if (nodes[i].PrecedingBreak == BreakType.NewLine)
                {
                    nodes[i].PrecedingBreak = BreakType.EmptyLine;
                }
            }
        }
    }

    /// <summary>
    /// indentJoin: indents JOIN keywords and their modifiers one level.
    /// </summary>
    private static void ApplyIndentJoin(List<LayoutNode> nodes, JoinOptions join)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            // Indent JOIN keyword itself
            if (join.IndentJoin && node.TokenType == TSqlTokenType.Join)
            {
                if (node.PrecedingBreak == BreakType.NewLine || node.PrecedingBreak == BreakType.EmptyLine)
                {
                    node.IndentLevel = Math.Max(node.IndentLevel, 1);
                }
            }

            // Also indent JOIN modifiers (INNER, LEFT, etc.) if they start the JOIN line
            if (join.IndentJoin && IsJoinModifier(node.TokenType) && IsJoinStartToken(nodes, i))
            {
                if (node.PrecedingBreak == BreakType.NewLine || node.PrecedingBreak == BreakType.EmptyLine)
                {
                    node.IndentLevel = Math.Max(node.IndentLevel, 1);
                }
            }
        }
    }

    /// <summary>
    /// multipleOnConditions: controls formatting of multiple conditions in JOIN ON clauses.
    /// "newLine" puts each AND condition on a new line; "sameLine" keeps them inline.
    /// </summary>
    private static void ApplyMultipleOnConditions(List<LayoutNode> nodes, JoinOptions join)
    {
        if (join.MultipleOnConditions == "sameLine")
        {
            // Collapse AND in join context back to same line
            for (int i = 0; i < nodes.Count; i++)
            {
                if (IsAndInJoinContext(nodes, i) && nodes[i].PrecedingBreak == BreakType.NewLine)
                {
                    nodes[i].PrecedingBreak = BreakType.None;
                    nodes[i].PrecedingSpaces = 1;
                    nodes[i].IndentLevel = 0;
                }
            }
        }
        else if (join.MultipleOnConditions == "newLine")
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (IsAndInJoinContext(nodes, i))
                {
                    nodes[i].PrecedingBreak = BreakType.NewLine;
                    nodes[i].IndentLevel = join.OnConditionIndent == "indent" ? 2 : 1;
                    nodes[i].PrecedingSpaces = 0;
                }
            }
        }
    }

    /// <summary>
    /// alignJoinKeyword: controls alignment of the JOIN keyword relative to join modifiers.
    /// "right" — right-aligns JOIN so that JOIN keywords line up regardless of modifier width.
    /// "left" — left-aligns everything from the modifier.
    /// "none" — no special alignment.
    /// </summary>
    private static void ApplyAlignJoinKeyword(List<LayoutNode> nodes, JoinOptions join)
    {
        if (join.AlignJoinKeyword == "none")
            return;

        if (join.AlignJoinKeyword == "right")
        {
            // Right-align: pad join modifiers so JOIN keywords align vertically.
            // Find the maximum modifier+JOIN keyword width.
            int maxModifierWidth = 0;

            for (int i = 0; i < nodes.Count; i++)
            {
                if (!IsJoinStartToken(nodes, i))
                    continue;

                int width = GetJoinExpressionWidth(nodes, i);
                maxModifierWidth = Math.Max(maxModifierWidth, width);
            }

            if (maxModifierWidth == 0)
                return;

            // Now adjust spacing so all JOIN keywords end at the same column
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!IsJoinStartToken(nodes, i))
                    continue;

                // Find the JOIN token in this expression
                int joinIdx = FindJoinToken(nodes, i);
                if (joinIdx < 0)
                    continue;

                int currentWidth = GetJoinExpressionWidth(nodes, i);
                int padding = maxModifierWidth - currentWidth;

                if (padding > 0 && nodes[i].PrecedingBreak != BreakType.None)
                {
                    // Add extra preceding spaces to align
                    nodes[i].PrecedingSpaces += padding;
                }
            }
        }
    }

    /// <summary>
    /// joinTypeStyle: controls explicit vs implicit join type keywords.
    /// "explicit" — ensures INNER JOIN is written instead of just JOIN.
    /// "implicit" — removes unnecessary INNER keyword.
    /// "asIs" — no changes.
    /// </summary>
    private static void ApplyJoinTypeStyle(List<LayoutNode> nodes, JoinOptions join)
    {
        if (join.JoinTypeStyle == "asIs")
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType != TSqlTokenType.Join)
                continue;

            if (join.JoinTypeStyle == "explicit")
            {
                // If JOIN appears without a modifier (INNER/LEFT/RIGHT/FULL/CROSS),
                // we'd need to insert INNER before it. Since we can't insert new nodes easily,
                // we modify the JOIN token text to include INNER.
                bool hasModifier = i > 0 && IsJoinModifier(nodes[i - 1].TokenType);
                // Check one more back for OUTER (LEFT OUTER JOIN)
                if (!hasModifier && i >= 2 &&
                    nodes[i - 1].TokenType == TSqlTokenType.Outer &&
                    IsJoinModifier(nodes[i - 2].TokenType))
                {
                    hasModifier = true;
                }

                if (!hasModifier)
                {
                    // Prepend INNER to the JOIN text
                    nodes[i].FormattedText = "INNER " + nodes[i].FormattedText;
                }
            }
            else if (join.JoinTypeStyle == "implicit")
            {
                // Remove explicit INNER keyword before JOIN
                if (i > 0 && nodes[i - 1].TokenType == TSqlTokenType.Inner)
                {
                    nodes[i - 1].FormattedText = "";
                    nodes[i - 1].PrecedingSpaces = 0;
                    // Transfer the break from INNER to JOIN
                    if (nodes[i - 1].PrecedingBreak != BreakType.None)
                    {
                        nodes[i].PrecedingBreak = nodes[i - 1].PrecedingBreak;
                        nodes[i].IndentLevel = nodes[i - 1].IndentLevel;
                        nodes[i - 1].PrecedingBreak = BreakType.None;
                    }
                }
            }
        }
    }

    /// <summary>
    /// crossApplyNewLine: puts CROSS APPLY and OUTER APPLY on new lines.
    /// </summary>
    private static void ApplyCrossApplyNewLine(List<LayoutNode> nodes, JoinOptions join)
    {
        if (!join.CrossApplyNewLine)
            return;

        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion)
                continue;

            // Look for CROSS APPLY or OUTER APPLY pattern
            if (nodes[i].FormattedText.Equals("APPLY", StringComparison.OrdinalIgnoreCase) ||
                nodes[i].FormattedText.Equals("apply", StringComparison.OrdinalIgnoreCase))
            {
                // Check if preceded by CROSS or OUTER
                if (i > 0 &&
                    (nodes[i - 1].TokenType == TSqlTokenType.Cross ||
                     nodes[i - 1].TokenType == TSqlTokenType.Outer))
                {
                    // Put the CROSS/OUTER token on a new line
                    if (nodes[i - 1].PrecedingBreak == BreakType.None)
                    {
                        nodes[i - 1].PrecedingBreak = BreakType.NewLine;
                        nodes[i - 1].IndentLevel = 0;
                        nodes[i - 1].PrecedingSpaces = 0;
                    }
                }
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helper methods
    // -----------------------------------------------------------------------

    /// <summary>
    /// Determines if the node at index i is the start of a JOIN clause
    /// (either JOIN itself, or a join modifier like INNER/LEFT/RIGHT/FULL/CROSS).
    /// </summary>
    private static bool IsJoinStartToken(List<LayoutNode> nodes, int index)
    {
        var node = nodes[index];
        if (node.TokenType == TSqlTokenType.Join)
            return true;

        if (IsJoinModifier(node.TokenType))
        {
            for (int j = index + 1; j < nodes.Count; j++)
            {
                if (nodes[j].TokenType == TSqlTokenType.Join)
                    return true;
                if (nodes[j].TokenType == TSqlTokenType.Outer)
                    continue; // LEFT OUTER JOIN
                break;
            }
        }

        return false;
    }

    private static bool IsJoinModifier(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.Inner or TSqlTokenType.Left or TSqlTokenType.Right or
            TSqlTokenType.Full or TSqlTokenType.Cross => true,
            _ => false,
        };
    }

    /// <summary>
    /// Checks if an AND token at position i is within a JOIN ON condition context.
    /// </summary>
    private static bool IsAndInJoinContext(List<LayoutNode> nodes, int index)
    {
        if (nodes[index].TokenType != TSqlTokenType.And)
            return false;

        bool foundOn = false;
        for (int i = index - 1; i >= 0; i--)
        {
            var tt = nodes[i].TokenType;
            if (tt == TSqlTokenType.On)
            {
                foundOn = true;
                continue;
            }
            if (foundOn && tt == TSqlTokenType.Join)
                return true;
            if (tt == TSqlTokenType.Where || tt == TSqlTokenType.Select ||
                tt == TSqlTokenType.Having || tt == TSqlTokenType.Semicolon)
                return false;
        }

        return false;
    }

    /// <summary>
    /// Gets the total text width of a JOIN expression (e.g., "INNER JOIN" = 10, "LEFT OUTER JOIN" = 15).
    /// </summary>
    private static int GetJoinExpressionWidth(List<LayoutNode> nodes, int startIndex)
    {
        int width = 0;
        for (int i = startIndex; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Join)
            {
                width += nodes[i].FormattedText.Length;
                break;
            }
            if (IsJoinModifier(nodes[i].TokenType) || nodes[i].TokenType == TSqlTokenType.Outer)
            {
                if (width > 0) width += 1; // space
                width += nodes[i].FormattedText.Length;
            }
            else
            {
                break;
            }
        }
        return width;
    }

    /// <summary>
    /// Finds the JOIN token starting from a join expression start index.
    /// </summary>
    private static int FindJoinToken(List<LayoutNode> nodes, int startIndex)
    {
        for (int i = startIndex; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.Join)
                return i;
            if (!IsJoinModifier(nodes[i].TokenType) && nodes[i].TokenType != TSqlTokenType.Outer)
                break;
        }
        return -1;
    }
}
