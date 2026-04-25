using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Parser;

/// <summary>
/// Fallback CTE-name extractor that works on the raw token stream when the AST
/// parser cannot parse incomplete SQL (e.g. the trailing CTE body is unfinished).
/// Scans for <c>WITH Name [(cols)] AS ( body ) [, Name AS (...) ]*</c> patterns
/// and returns the names whose bodies start before the cursor — those are the
/// CTEs visible at the cursor position for FROM/JOIN completion.
/// <para>
/// Distinct from the AST-based <see cref="CteResolver"/>, which also resolves
/// column lists but requires a parseable script. Both are complementary: AST
/// populates columns when possible, tokens populate names when not.
/// </para>
/// </summary>
public static class TokenBasedCteExtractor
{
    public static List<string> Extract(IList<TSqlParserToken> tokens, int cursorOffset)
    {
        var result = new List<string>();
        if (tokens == null || tokens.Count == 0) return result;

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.TokenType != TSqlTokenType.With) continue;

            // Disambiguate: `WITH (NOLOCK)` inside FROM is not a CTE clause — the
            // next non-whitespace token is `(`. `WITH XMLNAMESPACES ...` is also
            // not a CTE clause, but we rely on the `AS` check later to reject it.
            int j = i + 1;
            while (j < tokens.Count && IsWhitespaceOrComment(tokens[j])) j++;
            if (j >= tokens.Count || tokens[j].TokenType == TSqlTokenType.LeftParenthesis) continue;

            ParseCteList(tokens, j, cursorOffset, result);
            // Only the first top-level WITH contributes visible CTEs for this scan —
            // a nested WITH inside a subquery isn't visible from sibling CTE bodies.
            break;
        }
        return result;
    }

    private static void ParseCteList(IList<TSqlParserToken> tokens, int start, int cursorOffset, List<string> result)
    {
        int j = start;
        while (j < tokens.Count)
        {
            while (j < tokens.Count && IsWhitespaceOrComment(tokens[j])) j++;
            if (j >= tokens.Count) return;

            // Expect CTE name.
            if (tokens[j].TokenType is not (TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier))
                return;
            var cteName = tokens[j].Text.Trim('[', ']', '"');
            j++;
            while (j < tokens.Count && IsWhitespaceOrComment(tokens[j])) j++;

            // Optional column list: `Name (col1, col2) AS (`
            if (j < tokens.Count && tokens[j].TokenType == TSqlTokenType.LeftParenthesis)
            {
                int depth = 1; j++;
                while (j < tokens.Count && depth > 0)
                {
                    if (tokens[j].TokenType == TSqlTokenType.LeftParenthesis) depth++;
                    else if (tokens[j].TokenType == TSqlTokenType.RightParenthesis) depth--;
                    j++;
                }
                while (j < tokens.Count && IsWhitespaceOrComment(tokens[j])) j++;
            }

            // Expect AS.
            if (j >= tokens.Count ||
                !string.Equals(tokens[j].Text, "AS", StringComparison.OrdinalIgnoreCase))
                return;
            j++;
            while (j < tokens.Count && IsWhitespaceOrComment(tokens[j])) j++;

            // Expect opening paren of the body.
            if (j >= tokens.Count || tokens[j].TokenType != TSqlTokenType.LeftParenthesis)
                return;
            int bodyOpenOffset = tokens[j].Offset;
            int bd = 1; j++;
            while (j < tokens.Count && bd > 0)
            {
                if (tokens[j].TokenType == TSqlTokenType.LeftParenthesis) bd++;
                else if (tokens[j].TokenType == TSqlTokenType.RightParenthesis) bd--;
                j++;
            }
            // bd == 0 → body closed at tokens[j-1] (the matching `)`).
            // bd  > 0 → body never closed (cursor is inside an incomplete CTE).
            int bodyCloseOffset = bd == 0 ? tokens[j - 1].Offset : int.MaxValue;
            bool cursorInThisBody = bodyOpenOffset < cursorOffset && cursorOffset <= bodyCloseOffset;

            // A CTE is visible from the cursor if its body OPENS before the cursor
            // AND the cursor is NOT inside this CTE's own body — non-recursive CTEs
            // can't reference themselves.
            if (bodyOpenOffset < cursorOffset &&
                !cursorInThisBody &&
                !result.Contains(cteName, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(cteName);
            }

            while (j < tokens.Count && IsWhitespaceOrComment(tokens[j])) j++;
            if (j >= tokens.Count) return;
            if (tokens[j].TokenType == TSqlTokenType.Comma) { j++; continue; }
            return;
        }
    }

    private static bool IsWhitespaceOrComment(TSqlParserToken t) =>
        t.TokenType is TSqlTokenType.WhiteSpace
            or TSqlTokenType.SingleLineComment
            or TSqlTokenType.MultilineComment
            or TSqlTokenType.EndOfFile;
}
