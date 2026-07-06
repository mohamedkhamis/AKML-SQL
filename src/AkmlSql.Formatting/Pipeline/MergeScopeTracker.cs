using AkmlSql.Formatting.Layout;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>
/// Tracks whether the current token stream position is inside a MERGE statement's top-level match
/// clauses (vs. a nested <c>CASE...END</c>). Feed it each significant node in order via
/// <see cref="Advance"/>; consult <see cref="InMerge"/> / <see cref="CaseDepth"/> for the rest.
/// <para>
/// Single home for logic that was duplicated across <c>DmlRules.ApplyMergeWhenOnNewLine</c>,
/// <c>FormatterPipeline.NormalizeMergeWhenLayout</c>, and
/// <c>CasingEngine.ApplyMergeContextualKeywordCasing</c> — so a fix here reaches all three instead
/// of silently diverging. Also guards the <c>MERGE</c> <i>join hint</i> (<c>INNER MERGE JOIN</c> /
/// <c>OPTION (MERGE JOIN)</c>), which emits a <see cref="TSqlTokenType.Merge"/> token but is NOT a
/// MERGE statement.
/// </para>
/// </summary>
internal sealed class MergeScopeTracker
{
    /// <summary>True while inside a MERGE statement (after its opening MERGE, before its terminator).</summary>
    public bool InMerge { get; private set; }

    /// <summary>Nesting depth of CASE...END within the current MERGE (0 = a top-level match clause).</summary>
    public int CaseDepth { get; private set; }

    /// <summary>The <see cref="LayoutNode.IndentLevel"/> of the MERGE token that opened the scope.</summary>
    public int MergeIndent { get; private set; }

    private TSqlTokenType _prevSignificant = TSqlTokenType.None;

    /// <summary>
    /// Advances the scope by one node. Trivia (whitespace/comments) is ignored. Returns true when the
    /// node was a scope-control token (MERGE / <c>;</c> / GO, or CASE / END inside a MERGE) so the
    /// caller can <c>continue</c> past it.
    /// </summary>
    public bool Advance(LayoutNode node)
    {
        var tt = node.TokenType;
        if (tt is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
            return false;   // trivia never changes scope and is not the previous "significant" token

        bool control = true;
        switch (tt)
        {
            case TSqlTokenType.Merge:
                // Only a statement MERGE opens the scope — not the "INNER MERGE JOIN" /
                // "OPTION (MERGE JOIN)" join hint, which also tokenizes as a Merge token.
                if (!IsJoinHintPredecessor(_prevSignificant))
                {
                    InMerge = true;
                    CaseDepth = 0;
                    MergeIndent = node.IndentLevel;
                }
                break;
            case TSqlTokenType.Semicolon:
            case TSqlTokenType.Go:
                InMerge = false;
                CaseDepth = 0;
                break;
            case TSqlTokenType.Case when InMerge:
                CaseDepth++;
                break;
            case TSqlTokenType.End when InMerge && CaseDepth > 0:
                CaseDepth--;
                break;
            default:
                control = false;
                break;
        }

        _prevSignificant = tt;
        return control;
    }

    // A MERGE token is a join hint (not a statement) when it directly follows a join type or an
    // opening paren (OPTION (MERGE JOIN)).
    private static bool IsJoinHintPredecessor(TSqlTokenType t) => t is
        TSqlTokenType.Inner or TSqlTokenType.Outer or TSqlTokenType.Left or
        TSqlTokenType.Right or TSqlTokenType.Full or TSqlTokenType.Cross or
        TSqlTokenType.LeftParenthesis;
}
