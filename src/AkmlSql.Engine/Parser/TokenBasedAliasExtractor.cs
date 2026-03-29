using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Parser;

/// <summary>
/// Fallback alias extractor that works on the raw token stream when the AST parser
/// cannot parse incomplete SQL (e.g., "SELECT * FROM BomItems b JOIN ").
/// Scans for FROM/JOIN ... tableName [alias] patterns and extracts them.
/// </summary>
public static class TokenBasedAliasExtractor
{
    /// <summary>
    /// Extracts alias→"schema.table" mappings from the token stream.
    /// Only considers tokens before <paramref name="cursorOffset"/>.
    /// </summary>
    public static Dictionary<string, string> Extract(IList<TSqlParserToken> tokens, int cursorOffset)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tokens == null || tokens.Count == 0) return result;

        // Scan for patterns: FROM/JOIN <identifier> [<identifier>]
        // We look for FROM/JOIN keyword, then expect:
        //   [schemaName DOT] tableName [alias]
        // where alias is an identifier that is NOT a keyword.
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Offset >= cursorOffset) break;

            if (!IsFromOrJoinKeyword(t)) continue;

            // Skip whitespace after FROM/JOIN
            int j = SkipWhitespace(tokens, i + 1);
            if (j >= tokens.Count || tokens[j].Offset >= cursorOffset) continue;

            // Check for optional schema prefix: schema.table
            string schemaName = "dbo";
            string? tableName = null;

            if (tokens[j].TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
            {
                var firstId = tokens[j].Text.Trim('[', ']', '"');
                int k = SkipWhitespace(tokens, j + 1);

                if (k < tokens.Count && tokens[k].TokenType == TSqlTokenType.Dot)
                {
                    // schema.table pattern
                    schemaName = firstId;
                    int m = SkipWhitespace(tokens, k + 1);
                    if (m < tokens.Count &&
                        tokens[m].TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier &&
                        tokens[m].Offset < cursorOffset)
                    {
                        tableName = tokens[m].Text.Trim('[', ']', '"');
                        j = m;
                    }
                    else
                    {
                        continue; // Incomplete schema.table
                    }
                }
                else
                {
                    tableName = firstId;
                }
            }

            if (tableName == null || SuffixCompletionHelper.IsDummyIdentifier(tableName))
                continue;

            // Look for optional alias after the table name
            int next = SkipWhitespace(tokens, j + 1);
            string? alias = null;

            if (next < tokens.Count && tokens[next].Offset < cursorOffset)
            {
                var nextToken = tokens[next];
                // Skip optional AS keyword
                if (nextToken.TokenType == TSqlTokenType.As)
                {
                    next = SkipWhitespace(tokens, next + 1);
                    if (next < tokens.Count && tokens[next].Offset < cursorOffset)
                        nextToken = tokens[next];
                    else
                        nextToken = null;
                }

                if (nextToken != null &&
                    nextToken.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier &&
                    !IsKeyword(nextToken.Text))
                {
                    alias = nextToken.Text.Trim('[', ']', '"');
                    if (SuffixCompletionHelper.IsDummyIdentifier(alias))
                        alias = null;
                }
            }

            var key = alias ?? tableName;
            var fullName = $"{schemaName}.{tableName}";

            // Don't overwrite earlier entries (first occurrence wins)
            if (!result.ContainsKey(key))
                result[key] = fullName;
        }

        return result;
    }

    private static bool IsFromOrJoinKeyword(TSqlParserToken t)
    {
        return t.TokenType is TSqlTokenType.From or TSqlTokenType.Join;
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
