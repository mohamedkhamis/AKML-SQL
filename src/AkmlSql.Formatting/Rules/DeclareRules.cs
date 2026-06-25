using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Rules;

/// <summary>
/// Spec 030 — DECLARE / SET variable layout rule.
///
/// <para><b>Opt-in only.</b> The gate flag <see cref="DeclareOptions.OneDeclarationPerLine"/>
/// defaults to <c>false</c>, so the default profile code path early-returns without touching
/// any node — preserving byte-identical output for all 709 format goldens.</para>
///
/// <para>When enabled, splits comma-separated DECLARE lists so each variable occupies its own
/// line, and (optionally) aligns data-type and default-value tokens across declarations.</para>
///
/// <para>Thread-safety: this class is stateless (no instance fields; all helpers are
/// <c>private static</c>), safe for the BulkFormatter's parallel threads.</para>
/// </summary>
// ReSharper disable once UnusedMember.Global
public class DeclareRules : IRuleSet
{
    public void Apply(List<LayoutNode> nodes, FormattingProfile profile)
    {
        var opt = profile.Declare;

        // ── DEFAULT-PROFILE GATE ────────────────────────────────────────────
        // When OneDeclarationPerLine is false (the default), this rule is a
        // complete no-op: no node is ever read or mutated. This is the guarantee
        // that the 709 format goldens remain byte-identical.
        if (!opt.OneDeclarationPerLine)
            return;

        ApplyOnePerLine(nodes, opt);

        if (opt.AlignDataTypes || opt.AlignDefaultValues)
            ApplyAlignment(nodes, opt);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // One-per-line expansion
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds every DECLARE statement and, for each top-level comma (paren-depth 0, so commas
    /// inside VARCHAR(10) are skipped), ensures the token immediately after the comma starts
    /// on a new line with its own DECLARE "context" line-break.
    ///
    /// The actual DECLARE keyword is already on its own line (from the pipeline's LayoutEngine);
    /// what we split here is the multi-variable form:
    ///   DECLARE @a INT, @b INT   →   DECLARE @a INT\n        , @b INT
    /// The leading-comma choice is NOT honoured here — we always put the following @variable
    /// on a new line at the DECLARE's indent level. (A trailing-comma variant would require
    /// the comma itself to keep a trailing space; one-per-line expansion only cares about
    /// the NEXT variable's break.)
    ///
    /// Note: we emit the variable that follows the comma at IndentLevel = DECLARE's indent + 1
    /// so it visually indents one level, matching SQL Prompt's default.
    /// </summary>
    private static void ApplyOnePerLine(List<LayoutNode> nodes, DeclareOptions opt)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.Declare)
                continue;

            int declareIndent = nodes[i].IndentLevel;
            int stmtEnd = FindDeclareEnd(nodes, i + 1);

            // Walk the declaration list; break on each top-level comma
            int parenDepth = 0;
            for (int j = i + 1; j < stmtEnd; j++)
            {
                if (nodes[j].IsInNoformatRegion) continue;

                if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis) { parenDepth++; continue; }
                if (nodes[j].TokenType == TSqlTokenType.RightParenthesis) { parenDepth--; continue; }

                if (parenDepth == 0 && nodes[j].TokenType == TSqlTokenType.Comma)
                {
                    // The variable token that follows the comma
                    int next = j + 1;
                    while (next < stmtEnd && nodes[next].IsInNoformatRegion) next++;
                    if (next >= stmtEnd) break;

                    if (nodes[next].PrecedingBreak == BreakType.None)
                    {
                        nodes[next].PrecedingBreak = BreakType.NewLine;
                        nodes[next].IndentLevel = declareIndent + 1;
                        nodes[next].PrecedingSpaces = 0;
                    }
                }
            }

            i = stmtEnd - 1; // skip to end of this DECLARE — avoids reprocessing
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Alignment (data-type column and default-value column)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks each DECLARE statement and collects the (variable, dataType, default) token
    /// triplets for each variable that starts its own line (PrecedingBreak != None). For each
    /// group of two or more line-starting variables, pads their data-type tokens (and optionally
    /// their default-value = tokens) to align at a common column using PrecedingSpaces — the
    /// same technique as <c>DdlRules.AlignParameterDataTypes</c> / <c>AlignThenKeywords</c>.
    /// </summary>
    private static void ApplyAlignment(List<LayoutNode> nodes, DeclareOptions opt)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].IsInNoformatRegion || nodes[i].TokenType != TSqlTokenType.Declare)
                continue;

            int stmtEnd = FindDeclareEnd(nodes, i + 1);

            // Collect (variableIdx, dataTypeIdx, equalsIdx) for each variable that is on
            // its own line (the DECLARE keyword line or an already-broken continuation line).
            var entries = new List<(int varIdx, int dtIdx, int eqIdx)>();

            int parenDepth = 0;
            for (int j = i; j < stmtEnd; j++)
            {
                if (nodes[j].IsInNoformatRegion) continue;
                if (nodes[j].TokenType == TSqlTokenType.LeftParenthesis) { parenDepth++; continue; }
                if (nodes[j].TokenType == TSqlTokenType.RightParenthesis) { parenDepth--; continue; }

                if (parenDepth == 0 && nodes[j].TokenType == TSqlTokenType.Variable &&
                    (j == i + 1 || nodes[j].PrecedingBreak != BreakType.None))
                {
                    // Find the first non-variable, non-noformat token on this line → data type
                    int dtIdx = FindDataTypeAfterVar(nodes, j + 1, stmtEnd);
                    int eqIdx = dtIdx >= 0 ? FindEqualsAfterDataType(nodes, dtIdx + 1, stmtEnd) : -1;
                    entries.Add((j, dtIdx, eqIdx));
                }
            }

            if (entries.Count < 2)
            {
                i = stmtEnd - 1;
                continue;
            }

            // Align data types
            if (opt.AlignDataTypes)
            {
                int maxVarWidth = 0;
                var varWidths = new List<int>();
                foreach (var (varIdx, dtIdx, _) in entries)
                {
                    int w = dtIdx >= 0 ? MeasureWidth(nodes, varIdx, dtIdx) : 0;
                    varWidths.Add(w);
                    maxVarWidth = Math.Max(maxVarWidth, w);
                }

                for (int p = 0; p < entries.Count; p++)
                {
                    var (varIdx, dtIdx, _) = entries[p];
                    if (dtIdx >= 0 && varWidths[p] > 0 &&
                        nodes[dtIdx].PrecedingBreak == BreakType.None)
                    {
                        int padding = maxVarWidth - varWidths[p] + 1;
                        nodes[dtIdx].PrecedingSpaces = Math.Max(padding, 1);
                    }
                }
            }

            // Align default-value = tokens
            if (opt.AlignDefaultValues)
            {
                // Compute width from variable to '=' for entries that have one
                int maxEqWidth = 0;
                var eqWidths = new List<int>();
                foreach (var (varIdx, _, eqIdx) in entries)
                {
                    int w = eqIdx >= 0 ? MeasureWidth(nodes, varIdx, eqIdx) : -1;
                    eqWidths.Add(w);
                    if (w > 0) maxEqWidth = Math.Max(maxEqWidth, w);
                }

                for (int p = 0; p < entries.Count; p++)
                {
                    var (_, _, eqIdx) = entries[p];
                    if (eqIdx >= 0 && eqWidths[p] > 0 &&
                        nodes[eqIdx].PrecedingBreak == BreakType.None)
                    {
                        int padding = maxEqWidth - eqWidths[p] + 1;
                        nodes[eqIdx].PrecedingSpaces = Math.Max(padding, 1);
                    }
                }
            }

            i = stmtEnd - 1;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the exclusive end index of the DECLARE statement starting at the token
    /// <em>after</em> the DECLARE keyword (i.e. <paramref name="afterDeclare"/> = declareIndex+1).
    /// Ends at: Semicolon, GO, or the next statement-start keyword at paren-depth 0.
    /// </summary>
    private static int FindDeclareEnd(List<LayoutNode> nodes, int afterDeclare)
    {
        int parenDepth = 0;
        for (int i = afterDeclare; i < nodes.Count; i++)
        {
            if (nodes[i].TokenType == TSqlTokenType.LeftParenthesis) { parenDepth++; continue; }
            if (nodes[i].TokenType == TSqlTokenType.RightParenthesis) { parenDepth--; continue; }

            if (parenDepth > 0) continue;

            if (nodes[i].TokenType is TSqlTokenType.Semicolon or TSqlTokenType.Go)
                return i + 1;

            // Next statement keyword at depth 0 that carries its own line-break signals
            // the end of this DECLARE (multi-statement batch without semicolons).
            if (IsDeclareStatementBoundary(nodes[i].TokenType) && i > afterDeclare &&
                nodes[i].PrecedingBreak != BreakType.None)
                return i;
        }
        return nodes.Count;
    }

    private static bool IsDeclareStatementBoundary(TSqlTokenType t) => t switch
    {
        TSqlTokenType.Select or TSqlTokenType.Insert or TSqlTokenType.Update or
        TSqlTokenType.Delete or TSqlTokenType.Create or TSqlTokenType.Alter or
        TSqlTokenType.Drop or TSqlTokenType.Declare or TSqlTokenType.If or
        TSqlTokenType.While or TSqlTokenType.Begin or TSqlTokenType.Execute or
        TSqlTokenType.Exec or TSqlTokenType.With or TSqlTokenType.Merge or
        TSqlTokenType.Return or TSqlTokenType.Print => true,
        _ => false,
    };

    /// <summary>
    /// Returns the index of the first data-type identifier/keyword token after a variable,
    /// skipping the variable itself and any inline spaces. Returns -1 if not found before
    /// <paramref name="end"/>.
    ///
    /// The data type is the first non-noformat, non-variable token on the same "segment"
    /// (i.e. before the next Variable, Comma at depth-0, or statement boundary).
    /// </summary>
    private static int FindDataTypeAfterVar(List<LayoutNode> nodes, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (nodes[i].IsInNoformatRegion) continue;
            var tt = nodes[i].TokenType;
            // Stop on boundary tokens
            if (tt is TSqlTokenType.Comma or TSqlTokenType.Semicolon or TSqlTokenType.Go)
                return -1;
            if (tt == TSqlTokenType.Variable) return -1;
            // The first non-trivial token is the data type
            return i;
        }
        return -1;
    }

    /// <summary>
    /// Returns the index of the <c>=</c> (EqualsSign) token for a default-value assignment
    /// after the data type, within the same variable declaration segment. Returns -1 if
    /// no default is present before a boundary.
    /// </summary>
    private static int FindEqualsAfterDataType(List<LayoutNode> nodes, int start, int end)
    {
        int parenDepth = 0;
        for (int i = start; i < end; i++)
        {
            if (nodes[i].IsInNoformatRegion) continue;
            if (nodes[i].TokenType == TSqlTokenType.LeftParenthesis) { parenDepth++; continue; }
            if (nodes[i].TokenType == TSqlTokenType.RightParenthesis) { parenDepth--; continue; }

            if (parenDepth > 0) continue;

            var tt = nodes[i].TokenType;
            if (tt is TSqlTokenType.Comma or TSqlTokenType.Semicolon or TSqlTokenType.Go or TSqlTokenType.Variable)
                return -1;
            if (tt == TSqlTokenType.EqualsSign)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Measures the visible width of tokens [start, end) — same approach as
    /// <c>DdlRules.MeasureWidth</c>: sum of text lengths + inline PrecedingSpaces
    /// (breaks are counted as 0 — they start a new column context).
    /// </summary>
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
