using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// SQL Prompt-style smart GROUP BY action: when the cursor is in a GROUP BY clause,
/// yields a top-priority "Add columns from SELECT" item whose <see cref="CompletionItem.InsertText"/>
/// is the comma-separated list of non-aggregated SELECT-list expressions. Clicking it
/// auto-fills the GROUP BY with exactly the columns SQL Server requires.
/// </summary>
public class SmartGroupByProvider : ICompletionProvider
{
    public string Name => "SmartGroupBy";

    /// <summary>
    /// SQL aggregate / window functions whose presence in a SELECT-list expression
    /// excludes that expression from automatic GROUP BY inclusion.
    /// </summary>
    private static readonly HashSet<string> AggregateFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "COUNT", "COUNT_BIG", "SUM", "AVG", "MIN", "MAX",
        "STDEV", "STDEVP", "VAR", "VARP",
        "CHECKSUM_AGG", "GROUPING", "GROUPING_ID", "STRING_AGG",
        "APPROX_COUNT_DISTINCT",
        // Window/ranking functions are also incompatible with bare GROUP BY
        "ROW_NUMBER", "RANK", "DENSE_RANK", "NTILE",
        "LAG", "LEAD", "FIRST_VALUE", "LAST_VALUE",
        "PERCENT_RANK", "CUME_DIST", "PERCENTILE_CONT", "PERCENTILE_DISC",
        // 2022+
        "JSON_OBJECTAGG", "JSON_ARRAYAGG"
    };

    public bool CanHandle(CursorContext context, DatabaseCache? cache)
    {
        // Only fire in a GROUP BY context, and only when the user hasn't started
        // typing a partial token yet (so we don't shadow normal column completions
        // once they begin typing).
        return context.ClauseType == ClauseType.GroupBy
               && string.IsNullOrEmpty(context.PartialText);
    }

    public IEnumerable<CompletionItem> GetCompletions(CursorContext context, DatabaseCache? cache)
    {
        // CursorContext doesn't carry the document text, so the CompletionEngine
        // attaches the already-tokenized stream to the context via a side channel
        // (SmartGroupByContextExtensions) right after analysis. The item itself is
        // produced by BuildSmartItem so the web offline completion path — which has
        // a token stream but no CompletionEngine — can emit the identical item.
        var tokens = SmartGroupByContextExtensions.GetTokens(context);
        if (tokens == null || tokens.Count == 0)
        {
            yield break;
        }

        var item = BuildSmartItem(tokens, context.CursorOffset);
        if (item != null)
        {
            yield return item;
        }
    }

    /// <summary>
    /// Builds the SQL-Prompt-style "Add columns from SELECT" completion item from a token
    /// stream, or returns <c>null</c> when the enclosing statement exposes no eligible
    /// (non-aggregated, non-wildcard) SELECT-list expressions. The item is purely text
    /// based — no <see cref="DatabaseCache"/> needed — so both the engine completion path
    /// (via <see cref="GetCompletions"/>) and the web offline completion path share it.
    /// The caller is responsible for confirming the cursor sits in a GROUP BY clause.
    /// </summary>
    public static CompletionItem? BuildSmartItem(IList<TSqlParserToken> tokens, int cursorOffset)
    {
        var expressions = ExtractNonAggregatedSelectExpressions(tokens, cursorOffset);
        if (expressions.Count == 0)
        {
            return null;
        }

        var insertText = string.Join(", ", expressions);
        var preview = expressions.Count > 3
            ? string.Join(", ", expressions.Take(3)) + ", …"
            : insertText;

        return new CompletionItem
        {
            DisplayText = "▶ Add columns from SELECT",
            InsertText = insertText,
            ObjectType = (int)CompletionObjectType.Snippet,
            SecondaryText = preview,
            SourceObject = "GroupBy",
            // Negative priority so it sorts above all column suggestions
            SortPriority = -1000
        };
    }

    /// <summary>
    /// Walks the token stream within the current statement to find the SELECT list,
    /// then returns each comma-separated expression that does NOT contain an aggregate
    /// or window function. Wildcard <c>*</c> entries are skipped (we can't expand them
    /// without a schema lookup at this layer).
    /// Public for unit testing.
    /// </summary>
    public static List<string> ExtractNonAggregatedSelectExpressions(IList<TSqlParserToken> tokens, int cursorOffset)
    {
        var result = new List<string>();

        // Compute statement bounds via surrounding semicolons.
        int statementStart = 0;
        int statementEnd = int.MaxValue;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.TokenType != TSqlTokenType.Semicolon) continue;
            if (t.Offset < cursorOffset) statementStart = t.Offset + 1;
            else { statementEnd = t.Offset; break; }
        }

        // Find SELECT and FROM token indices within the current statement.
        int selectIdx = -1;
        int fromIdx = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Offset < statementStart) continue;
            if (t.Offset >= statementEnd) break;

            if (t.TokenType == TSqlTokenType.Select && selectIdx < 0)
            {
                selectIdx = i;
            }
            else if (t.TokenType == TSqlTokenType.From && selectIdx >= 0 && fromIdx < 0)
            {
                fromIdx = i;
                break;
            }
        }

        if (selectIdx < 0 || fromIdx < 0 || fromIdx <= selectIdx + 1)
        {
            return result;
        }

        // Walk SELECT list, splitting by top-level commas (ignore commas in nested parens).
        var current = new List<TSqlParserToken>();
        int parenDepth = 0;

        for (int i = selectIdx + 1; i < fromIdx; i++)
        {
            var t = tokens[i];
            if (t.TokenType == TSqlTokenType.LeftParenthesis) parenDepth++;
            else if (t.TokenType == TSqlTokenType.RightParenthesis) parenDepth--;
            else if (t.TokenType == TSqlTokenType.Comma && parenDepth == 0)
            {
                AddExpressionIfEligible(current, result);
                current.Clear();
                continue;
            }
            current.Add(t);
        }
        AddExpressionIfEligible(current, result);

        return result;
    }

    private static void AddExpressionIfEligible(List<TSqlParserToken> exprTokens, List<string> result)
    {
        // Strip leading/trailing whitespace and comments
        int start = 0;
        int end = exprTokens.Count - 1;
        while (start <= end && IsWhitespaceOrComment(exprTokens[start])) start++;
        while (end >= start && IsWhitespaceOrComment(exprTokens[end])) end--;
        if (start > end) return;

        // Check for SELECT modifiers (TOP, DISTINCT, ALL) at the very beginning of the
        // SELECT list — those are not column expressions, skip them.
        var firstText = exprTokens[start].Text?.ToUpperInvariant();
        if (firstText is "DISTINCT" or "ALL" or "TOP")
        {
            // Move past TOP <n>, DISTINCT, etc.
            start++;
            // For TOP, also skip the number/PERCENT/WITH TIES
            while (start <= end && (IsWhitespaceOrComment(exprTokens[start]) ||
                                    exprTokens[start].TokenType == TSqlTokenType.Integer ||
                                    exprTokens[start].TokenType == TSqlTokenType.Numeric))
            {
                start++;
            }
            while (start <= end && IsWhitespaceOrComment(exprTokens[start])) start++;
            if (start > end) return;
        }

        // Skip wildcards and aggregated/window expressions.
        if (ContainsWildcard(exprTokens, start, end)) return;
        if (ContainsAggregate(exprTokens, start, end)) return;

        // Strip an "[AS] alias" suffix.
        int aliasStart = FindAliasStart(exprTokens, start, end);
        int exprEnd = aliasStart >= 0 ? aliasStart - 1 : end;
        while (exprEnd >= start && IsWhitespaceOrComment(exprTokens[exprEnd])) exprEnd--;
        if (exprEnd < start) return;

        // Reconstruct the expression text from the original tokens (preserves casing).
        var sb = new System.Text.StringBuilder();
        for (int i = start; i <= exprEnd; i++)
        {
            sb.Append(exprTokens[i].Text);
        }
        var text = sb.ToString().Trim();
        if (text.Length > 0)
        {
            result.Add(text);
        }
    }

    private static bool IsWhitespaceOrComment(TSqlParserToken t) =>
        t.TokenType is TSqlTokenType.WhiteSpace
            or TSqlTokenType.SingleLineComment
            or TSqlTokenType.MultilineComment;

    private static bool ContainsWildcard(List<TSqlParserToken> tokens, int start, int end)
    {
        for (int i = start; i <= end; i++)
        {
            if (tokens[i].TokenType == TSqlTokenType.Star) return true;
        }
        return false;
    }

    private static bool ContainsAggregate(List<TSqlParserToken> tokens, int start, int end)
    {
        for (int i = start; i <= end; i++)
        {
            var t = tokens[i];
            if (t.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
            {
                // Identifier directly followed by '(' is a function call.
                int j = i + 1;
                while (j <= end && IsWhitespaceOrComment(tokens[j])) j++;
                if (j <= end && tokens[j].TokenType == TSqlTokenType.LeftParenthesis)
                {
                    if (AggregateFunctions.Contains(t.Text)) return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the start index of an "[AS] alias" suffix, or -1 if none. The alias
    /// is the trailing identifier after an optional AS keyword that does not look
    /// like a SQL keyword and is preceded by an expression-terminating token.
    /// </summary>
    private static int FindAliasStart(List<TSqlParserToken> tokens, int start, int end)
    {
        // Walk from end backward, skipping trailing whitespace/comments.
        int i = end;
        while (i >= start && IsWhitespaceOrComment(tokens[i])) i--;
        if (i < start) return -1;

        // Pattern A: ... <expr> AS <ident>   → alias is at i, AS is somewhere before
        // Pattern B: ... <expr> <ident>      → alias is at i (no AS)
        if (tokens[i].TokenType is not (TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier))
        {
            return -1;
        }

        int aliasIdx = i;
        // Look behind aliasIdx for whitespace + (AS or another identifier-terminator)
        int j = aliasIdx - 1;
        while (j >= start && IsWhitespaceOrComment(tokens[j])) j--;
        if (j < start) return -1;

        if (tokens[j].TokenType == TSqlTokenType.As)
        {
            return j; // Strip from "AS" onward
        }

        // Pattern B (no AS): require the alias to be immediately preceded by an
        // identifier or right-paren (function call result), and not be a comma/operator.
        if (tokens[j].TokenType is TSqlTokenType.RightParenthesis
            or TSqlTokenType.Identifier
            or TSqlTokenType.QuotedIdentifier)
        {
            // Heuristic: if both tokens are identifiers without an operator between
            // them, the trailing one is an alias. e.g. "Customers c"
            return aliasIdx;
        }

        return -1;
    }
}

/// <summary>
/// Extension carrying the parser token stream from <see cref="CompletionEngine"/> to
/// <see cref="SmartGroupByProvider"/>. The engine attaches the tokens to the
/// <see cref="CursorContext"/> right after analysis so providers that need re-scanning
/// don't have to re-tokenize.
/// </summary>
internal static class SmartGroupByContextExtensions
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CursorContext, IList<TSqlParserToken>> _table = new();

    public static void AttachTokens(CursorContext context, IList<TSqlParserToken> tokens)
    {
        _table.AddOrUpdate(context, tokens);
    }

    public static IList<TSqlParserToken>? GetTokens(CursorContext context)
    {
        return _table.TryGetValue(context, out var tokens) ? tokens : null;
    }
}
