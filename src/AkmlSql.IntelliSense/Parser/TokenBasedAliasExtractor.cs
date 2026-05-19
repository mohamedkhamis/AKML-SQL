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
    /// Extracts alias→"schema.table" mappings from the token stream for the SQL
    /// statement containing the cursor. Considers FROM/JOIN tokens both BEFORE and
    /// AFTER the cursor (within the same statement), so that partial expressions
    /// like <c>SELECT COUNT(DISTINCT |) FROM Terminals</c> still resolve table
    /// references that come later in the same statement.
    /// Statement bounds are detected via the surrounding semicolons.
    /// </summary>
    public static Dictionary<string, string> Extract(IList<TSqlParserToken> tokens, int cursorOffset)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (tokens == null || tokens.Count == 0) return result;

        // Compute the [statementStart, statementEnd) byte range that bounds the
        // statement containing the cursor. Semicolons act as boundaries; absent
        // semicolons we use the start/end of the token stream.
        int statementStart = 0;
        int statementEnd = int.MaxValue;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.TokenType != TSqlTokenType.Semicolon) continue;
            if (t.Offset < cursorOffset)
            {
                // The semicolon ends the previous statement; current statement starts after it.
                statementStart = t.Offset + 1;
            }
            else
            {
                // First semicolon at/after the cursor terminates the current statement.
                statementEnd = t.Offset;
                break;
            }
        }

        // Track parenthesis depth as we scan. CTE bodies, derived tables, and
        // subqueries are all wrapped in (...). Their internal FROM/JOIN clauses
        // are at depth >= 1 and must not leak into the outer-statement alias map
        // — otherwise `WITH cte AS (SELECT * FROM Inner) SELECT * FROM cte` would
        // surface "Inner" as an alias for the outer cursor's wildcard expansion.
        int parenDepth = 0;

        // Scan for FROM/JOIN <identifier> [alias] patterns within the statement.
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Offset < statementStart) continue;
            if (t.Offset >= statementEnd) break;

            if (t.TokenType == TSqlTokenType.LeftParenthesis) { parenDepth++; continue; }
            if (t.TokenType == TSqlTokenType.RightParenthesis) { if (parenDepth > 0) parenDepth--; continue; }

            // Only depth-0 FROM/JOIN are at the outer statement scope. Skip ones
            // nested inside CTE bodies / derived tables / scalar subqueries.
            if (parenDepth > 0) continue;

            if (!IsFromOrJoinKeyword(t)) continue;

            // Skip whitespace after FROM/JOIN
            int j = SkipWhitespace(tokens, i + 1);
            if (j >= tokens.Count || tokens[j].Offset >= statementEnd) continue;

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
                        tokens[m].Offset < statementEnd)
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

            if (next < tokens.Count && tokens[next].Offset < statementEnd)
            {
                var nextToken = tokens[next];
                // Skip optional AS keyword
                if (nextToken.TokenType == TSqlTokenType.As)
                {
                    next = SkipWhitespace(tokens, next + 1);
                    if (next < tokens.Count && tokens[next].Offset < statementEnd)
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
        // Include UPDATE so that "UPDATE <table> SET ..." injects the target table
        // into AvailableAliases, enabling column completions after SET.
        return t.TokenType is TSqlTokenType.From or TSqlTokenType.Join or TSqlTokenType.Update;
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
