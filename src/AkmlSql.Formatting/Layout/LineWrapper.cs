using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Layout;

/// <summary>
/// FR-002 (spec 030 T012) — wraps lines longer than <c>Whitespace.MaxLineWidth</c>, with a
/// one-level continuation indent. Runs as the LAST post-collapse finalization pass in
/// <c>FormatterPipeline.ApplyLayoutRules</c>: wrapping is line geometry, so it must see the final
/// line shapes (after every rule set's collapse passes and the alignment/spacing finalizers).
/// The break point is the last fitting gap that starts a keyword group (WHEN/AND/OR/THEN/JOIN/…)
/// when one exists, else the last fitting gap of any kind — and only gaps with at least one
/// preceding space qualify: a zero-space gap is structural (dots, compound-operator halves,
/// commas, semicolons, unary operands) and must stay joined. Noformat-region tokens are never
/// wrapped. A continuation line is re-scanned, so a line needing several wraps gets them; a line
/// with no usable gap is left as-is.
/// </summary>
internal static class LineWrapper
{
    public static void Wrap(List<LayoutNode> nodes, FormattingProfile profile)
    {
        int maxWidth = profile.Whitespace.MaxLineWidth;
        if (maxWidth <= 0 || nodes.Count == 0)
            return;
        int tabSize = profile.Whitespace.TabSize > 0 ? profile.Whitespace.TabSize : 4;

        int lineStart = 0;
        while (lineStart < nodes.Count)
        {
            int width = nodes[lineStart].IndentLevel * tabSize + nodes[lineStart].FormattedText.Length;
            int lastFit = -1;        // last candidate gap whose kept-line still fits
            int lastFitKeyword = -1; // …preferring one that starts a keyword group
            int firstCandidate = -1; // fallback when even the first gap overflows
            int next = nodes.Count;  // start of the next line when no wrap happens

            for (int j = lineStart + 1; j < nodes.Count; j++)
            {
                if (nodes[j].PrecedingBreak != BreakType.None)
                {
                    next = j;
                    break;
                }

                bool isCandidate = nodes[j].PrecedingSpaces > 0
                    && !nodes[j].IsInNoformatRegion
                    && !nodes[j - 1].IsInNoformatRegion;
                if (isCandidate)
                {
                    if (firstCandidate < 0) firstCandidate = j;
                    if (width <= maxWidth)
                    {
                        lastFit = j;
                        if (IsPreferredWrapToken(nodes[j].TokenType)) lastFitKeyword = j;
                    }
                }

                width += nodes[j].PrecedingSpaces + nodes[j].FormattedText.Length;

                if (width > maxWidth && (lastFit > 0 || firstCandidate > 0))
                {
                    int wrapAt = lastFitKeyword > 0 ? lastFitKeyword
                        : lastFit > 0 ? lastFit
                        : firstCandidate;
                    nodes[wrapAt].PrecedingBreak = BreakType.NewLine;
                    nodes[wrapAt].IndentLevel = nodes[lineStart].IndentLevel + 1;
                    nodes[wrapAt].PrecedingSpaces = 0;
                    next = wrapAt;   // the continuation line is re-scanned for further wraps
                    break;
                }
            }

            lineStart = next;
        }
    }

    /// <summary>
    /// Tokens that read naturally at the start of a wrapped continuation line — clause/branch
    /// keywords and join introducers. Preferring these over an arbitrary mid-expression gap keeps
    /// groups like "NOT matched BY source" together.
    /// </summary>
    private static bool IsPreferredWrapToken(TSqlTokenType tokenType)
    {
        return tokenType switch
        {
            TSqlTokenType.When or TSqlTokenType.Then or TSqlTokenType.Else or
            TSqlTokenType.And or TSqlTokenType.Or or TSqlTokenType.On or
            TSqlTokenType.Join or TSqlTokenType.Inner or TSqlTokenType.Left or
            TSqlTokenType.Right or TSqlTokenType.Full or TSqlTokenType.Cross or
            TSqlTokenType.From or TSqlTokenType.Where or TSqlTokenType.Having or
            TSqlTokenType.Union or TSqlTokenType.Values or TSqlTokenType.Set or
            TSqlTokenType.Order or TSqlTokenType.Group or TSqlTokenType.Case => true,
            _ => false,
        };
    }
}
