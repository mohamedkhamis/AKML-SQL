using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Parser;

/// <summary>
/// Fallback alias extractor that works on the raw token stream when the AST parser
/// cannot parse incomplete SQL (e.g., "SELECT * FROM BomItems b JOIN ").
/// Scans for FROM/JOIN ... tableName [alias] patterns and extracts them.
/// <para>
/// Spec 032 (US2, clusters A1/A2/A5/A6/F4) — the scan is CURSOR-SCOPE aware:
/// </para>
/// <list type="bullet">
/// <item>Parenthesized scopes (subqueries, CTE bodies, derived tables) that contain the
/// caret contribute their own FROM/JOIN tables, merged with every enclosing scope
/// (inner wins on alias conflicts). Sibling paren groups the caret is NOT inside stay
/// excluded — otherwise <c>WITH cte AS (SELECT * FROM Inner) SELECT * FROM cte</c>
/// would surface "Inner" at the outer statement's caret.</item>
/// <item>Registration is two-pass: FROM/JOIN-introduced tables win over UPDATE/DELETE
/// <em>target</em> tokens, so <c>UPDATE o SET … FROM Orders o</c> resolves the alias to
/// the real table instead of registering a phantom <c>dbo.o</c>. The deliberate FROM-less
/// DML injection (<c>UPDATE Orders SET |</c> → Orders in scope) is preserved.</item>
/// <item>Set-operator tokens (UNION/INTERSECT/EXCEPT) at a scope's own depth bound that
/// scope to the branch containing the caret.</item>
/// <item>Multi-part names (<c>db.schema.table alias</c>) are consumed as a full chain;
/// the last two parts become schema.table (the db part is dropped — the schema cache is
/// per-database) and no bogus intermediate aliases are registered.</item>
/// </list>
/// Statement bounds are detected via the surrounding semicolons; FROM/JOIN tokens both
/// BEFORE and AFTER the cursor (within the active scope segments) are considered, so
/// partial expressions like <c>SELECT COUNT(DISTINCT |) FROM Terminals</c> still resolve.
/// </summary>
public static class TokenBasedAliasExtractor
{
    private readonly record struct ParenPair(int OpenOffset, int CloseOffset);

    private readonly record struct Candidate(string Key, string FullName, int Level, int Pass, int Order);

    public static Dictionary<string, string> Extract(IList<TSqlParserToken> tokens, int cursorOffset)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tokens == null || tokens.Count == 0) return result;

        // ── 1. Statement bounds via semicolons ──────────────────────────────
        int statementStart = 0;
        int statementEnd = int.MaxValue;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.TokenType != TSqlTokenType.Semicolon) continue;
            if (t.Offset < cursorOffset)
            {
                statementStart = t.Offset + 1;
            }
            else
            {
                statementEnd = t.Offset;
                break;
            }
        }

        // ── 2. Matched paren pairs within the statement ─────────────────────
        // Unbalanced opens (mid-typing) are treated as staying open to the end.
        var pairs = new List<ParenPair>();
        var openStack = new Stack<int>();
        foreach (var t in tokens)
        {
            if (t.Offset < statementStart) continue;
            if (t.Offset >= statementEnd) break;
            if (t.TokenType == TSqlTokenType.LeftParenthesis) openStack.Push(t.Offset);
            else if (t.TokenType == TSqlTokenType.RightParenthesis && openStack.Count > 0)
                pairs.Add(new ParenPair(openStack.Pop(), t.Offset));
        }
        while (openStack.Count > 0)
            pairs.Add(new ParenPair(openStack.Pop(), statementEnd));

        // ── 3. The caret's enclosing-paren chain, outer → inner ─────────────
        // Level 0 = the statement itself; level k = the k-th chain pair.
        var chain = pairs
            .Where(p => ContainsOffset(p, cursorOffset))
            .OrderBy(p => p.OpenOffset)
            .ToList();

        // Pairs NOT containing the caret are blockers: everything inside them is invisible.
        var blockers = pairs.Where(p => !ContainsOffset(p, cursorOffset)).ToList();

        bool IsVisible(int offset) => !blockers.Any(p => ContainsOffset(p, offset));
        int LevelOf(int offset) => chain.Count(p => ContainsOffset(p, offset));

        // ── 4. Per-level active segment (set-operator branch bounds, A5) ────
        var segStart = new int[chain.Count + 1];
        var segEnd = new int[chain.Count + 1];
        segStart[0] = statementStart;
        segEnd[0] = statementEnd;
        for (int k = 1; k <= chain.Count; k++)
        {
            segStart[k] = chain[k - 1].OpenOffset + 1;
            segEnd[k] = chain[k - 1].CloseOffset;
        }

        foreach (var t in tokens)
        {
            if (t.Offset < statementStart) continue;
            if (t.Offset >= statementEnd) break;
            if (t.TokenType is not (TSqlTokenType.Union or TSqlTokenType.Intersect or TSqlTokenType.Except)) continue;
            if (!IsVisible(t.Offset)) continue;

            int lvl = LevelOf(t.Offset);
            if (t.Offset < cursorOffset)
                segStart[lvl] = Math.Max(segStart[lvl], t.Offset + 1);
            else
                segEnd[lvl] = Math.Min(segEnd[lvl], t.Offset);
        }

        // ── 5. Two-pass pattern scan ─────────────────────────────────────────
        // Pass 1 = FROM/JOIN-introduced tables; pass 2 = UPDATE/DELETE targets
        // (kept so FROM-less DML injects its target — memory: dml-target-alias-resolution).
        var candidates = new List<Candidate>();
        int order = 0;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Offset < statementStart) continue;
            if (t.Offset >= statementEnd) break;

            int pass;
            if (t.TokenType is TSqlTokenType.From or TSqlTokenType.Join) pass = 1;
            else if (t.TokenType is TSqlTokenType.Update or TSqlTokenType.Delete) pass = 2;
            else continue;

            if (!IsVisible(t.Offset)) continue;
            int lvl = LevelOf(t.Offset);
            if (t.Offset < segStart[lvl] || t.Offset >= segEnd[lvl]) continue;

            if (!TryParseTarget(tokens, i, statementEnd, out var schema, out var table, out var alias))
                continue;
            if (SuffixCompletionHelper.IsDummyIdentifier(table)) continue;

            var key = alias ?? table;
            candidates.Add(new Candidate(key, $"{schema}.{table}", lvl, pass, order++));
        }

        // ── 6. Merge: inner scope wins; within a level FROM/JOIN beats DML targets;
        //       within a level+pass the first occurrence wins (existing semantics). ──
        foreach (var group in candidates.GroupBy(c => c.Key, StringComparer.OrdinalIgnoreCase))
        {
            var winner = group
                .OrderByDescending(c => c.Level)
                .ThenBy(c => c.Pass)
                .ThenBy(c => c.Order)
                .First();
            result[winner.Key] = winner.FullName;
        }

        return result;
    }

    private static bool ContainsOffset(ParenPair p, int offset)
        => offset > p.OpenOffset && offset <= p.CloseOffset;

    /// <summary>
    /// Parses the multi-part table target (and optional alias) that follows the trigger
    /// keyword at <paramref name="triggerIndex"/>: <c>id(.id)* [AS] [alias]</c>.
    /// Returns false when no usable table name follows (e.g. <c>DELETE FROM …</c> — the
    /// FROM branch handles it) or the chain is incomplete (trailing dot).
    /// </summary>
    private static bool TryParseTarget(
        IList<TSqlParserToken> tokens, int triggerIndex, int statementEnd,
        out string schema, out string table, out string? alias)
    {
        schema = "dbo";
        table = string.Empty;
        alias = null;

        var parts = new List<string>();
        int j = SkipWhitespace(tokens, triggerIndex + 1);
        bool endedWithDot = false;

        while (j < tokens.Count && tokens[j].Offset < statementEnd &&
               tokens[j].TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
        {
            parts.Add(tokens[j].Text.Trim('[', ']', '"'));
            endedWithDot = false;

            int k = SkipWhitespace(tokens, j + 1);
            if (k < tokens.Count && tokens[k].Offset < statementEnd && tokens[k].TokenType == TSqlTokenType.Dot)
            {
                endedWithDot = true;
                j = SkipWhitespace(tokens, k + 1);
            }
            else
            {
                j = k;
                break;
            }
        }

        if (parts.Count == 0 || endedWithDot) return false;

        // Last part is the table, second-to-last the schema; a leading db part is
        // dropped (the schema cache is per-database) — A6.
        table = parts[parts.Count - 1];
        if (parts.Count >= 2) schema = parts[parts.Count - 2];

        // Optional alias (skipping AS), same rules as before the rework.
        if (j < tokens.Count && tokens[j].Offset < statementEnd)
        {
            var nextToken = tokens[j];
            if (nextToken.TokenType == TSqlTokenType.As)
            {
                j = SkipWhitespace(tokens, j + 1);
                nextToken = j < tokens.Count && tokens[j].Offset < statementEnd ? tokens[j] : null;
            }

            if (nextToken != null &&
                nextToken.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier &&
                !IsKeyword(nextToken.Text))
            {
                var candidate = nextToken.Text.Trim('[', ']', '"');
                if (!SuffixCompletionHelper.IsDummyIdentifier(candidate))
                    alias = candidate;
            }
        }

        return true;
    }

    private static int SkipWhitespace(IList<TSqlParserToken> tokens, int start)
    {
        while (start < tokens.Count &&
               tokens[start].TokenType is TSqlTokenType.WhiteSpace
                   or TSqlTokenType.SingleLineComment
                   or TSqlTokenType.MultilineComment)
        {
            start++;
        }

        return start;
    }

    /// <summary>
    /// Simple check to avoid treating SQL keywords as aliases.
    /// </summary>
    private static bool IsKeyword(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        switch (text.ToUpperInvariant())
        {
            case "ON":
            case "WHERE":
            case "SET":
            case "JOIN":
            case "INNER":
            case "LEFT":
            case "RIGHT":
            case "CROSS":
            case "FULL":
            case "OUTER":
            case "AND":
            case "OR":
            case "ORDER":
            case "GROUP":
            case "HAVING":
            case "UNION":
            case "EXCEPT":
            case "INTERSECT":
            case "SELECT":
            case "INSERT":
            case "UPDATE":
            case "DELETE":
            case "INTO":
            case "VALUES":
            case "FROM":
            case "AS":
            case "WITH":
            case "GO":
            case "BEGIN":
            case "END":
            case "IF":
            case "ELSE":
            case "WHILE":
            case "RETURN":
            case "NOLOCK":
            case "ROWLOCK":
            case "UPDLOCK":
            case "HOLDLOCK":
            case "TABLOCK":
                return true;
            default:
                return false;
        }
    }
}
