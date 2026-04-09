using Microsoft.SqlServer.TransactSql.ScriptDom;
// ReSharper disable UnusedMember.Global

namespace AkmlSql.Engine.Parser;

public enum ClauseType
{
    Unknown,
    Select,
    From,
    Where,
    JoinOn,
    GroupBy,
    Having,
    OrderBy,
    InsertColumns,
    InsertValues,
    UpdateSet,
    Delete,
    Create,
    Alter,
    Exec,
    With,
    JoinTable,   // After JOIN/INNER JOIN/LEFT JOIN etc. — expects table name, not more JOIN keywords
    UpdateTable, // After UPDATE keyword — expects table name, before SET
    Over,        // After OVER( — window specification context
    Option,      // After OPTION( — query hint context
    Set,         // After SET — SET option context (SET NOCOUNT, SET ANSI_NULLS, etc.)
    Declare,     // After DECLARE — variable/cursor/table declaration
    Drop,        // After DROP — object type context
    Grant,       // After GRANT/REVOKE/DENY — permission context
    ForXml,      // After FOR XML — XML output mode
    ForJson,     // After FOR JSON — JSON output mode
    Use          // After USE keyword — expects a database name (to be inserted as [Name])
}

public class CursorContext
{
    public int CursorOffset { get; set; }
    public ClauseType ClauseType { get; set; } = ClauseType.Unknown;
    public TSqlParserToken? PrecedingToken { get; set; }
    public bool PrecedingDot { get; set; }
    public string DotPrefix { get; set; } = string.Empty;
    public string PartialText { get; set; } = string.Empty;
    public bool InComment { get; set; }
    public bool InString { get; set; }
    public bool InSqlcmdDirective { get; set; }
    public Dictionary<string, string> AvailableAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> AvailableCtes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> AvailableTempTables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> AvailableVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class CursorContextAnalyzer
{
    public CursorContext Analyze(IList<TSqlParserToken> tokens, int cursorOffset)
    {
        var context = new CursorContext { CursorOffset = cursorOffset };

        if (tokens == null || tokens.Count == 0)
        {
            return context;
        }

        try
        {
            return AnalyzeCore(tokens, cursorOffset, context);
        }
        catch
        {
            // Return basic context on any parse error — completions degrade gracefully
            return context;
        }
    }

    private CursorContext AnalyzeCore(IList<TSqlParserToken> tokens, int cursorOffset, CursorContext context)
    {

        // Find token at/before cursor
        TSqlParserToken? tokenAtCursor = null;
        TSqlParserToken? prevToken = null;
        int tokenIndex = -1;

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Text == null) continue;
            if (t.Offset + t.Text.Length >= cursorOffset)
            {
                tokenAtCursor = t;
                tokenIndex = i;
                if (i > 0)
                {
                    prevToken = tokens[i - 1];
                }

                break;
            }
        }

        if (tokenAtCursor == null && tokens.Count > 0)
        {
            // ReSharper disable once UseIndexFromEndExpression
            tokenAtCursor = tokens[tokens.Count - 1];
            tokenIndex = tokens.Count - 1;
            if (tokenIndex > 0)
            {
                prevToken = tokens[tokenIndex - 1];
            }
        }

        // Check if in comment or string
        if (tokenAtCursor != null)
        {
            context.InComment = tokenAtCursor.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment;
            context.InString = tokenAtCursor.TokenType is TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral;
        }

        if (context.InComment || context.InString)
        {
            return context;
        }

        // Check for dot prefix.
        // Case 1: cursor is past the dot — tokenAtCursor is after the dot, prevToken IS the dot.
        // Case 2: cursor is immediately after the dot — tokenAtCursor IS the dot itself.
        //   This happens when user types "BomItems." and cursor is at the end with no further text.
        if (prevToken is { TokenType: TSqlTokenType.Dot })
        {
            context.PrecedingDot = true;
            // Find the identifier before the dot
            if (tokenIndex >= 2)
            {
                var beforeDot = tokens[tokenIndex - 2];
                if (beforeDot.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
                {
                    context.DotPrefix = beforeDot.Text.Trim('[', ']', '"');
                }
            }
        }
        else if (tokenAtCursor is { TokenType: TSqlTokenType.Dot })
        {
            // Cursor is right at or immediately after the dot token itself
            context.PrecedingDot = true;
            if (tokenIndex >= 1)
            {
                var beforeDot = tokens[tokenIndex - 1];
                if (beforeDot.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
                {
                    context.DotPrefix = beforeDot.Text.Trim('[', ']', '"');
                }
            }
        }

        context.PrecedingToken = prevToken;

        // Extract partial text being typed
        if (tokenAtCursor is { TokenType: TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier } &&
            cursorOffset > tokenAtCursor.Offset)
        {
            var len = Math.Min(cursorOffset - tokenAtCursor.Offset, tokenAtCursor.Text.Length);
            context.PartialText = tokenAtCursor.Text.Substring(0, len);
        }

        // Determine clause type by walking backwards through tokens
        context.ClauseType = DetermineClauseType(tokens, tokenIndex);

        return context;
    }

    /// <summary>
    /// Determines the SQL clause type by walking backwards through the token stream.
    /// Covers all clause contexts for alias resolution (T045):
    /// SELECT, FROM, WHERE, JOIN ON, GROUP BY, HAVING, ORDER BY, UPDATE SET,
    /// INSERT (columns/values), DELETE, CREATE, ALTER, EXEC, WITH (CTEs).
    /// </summary>
    private ClauseType DetermineClauseType(IList<TSqlParserToken> tokens, int fromIndex)
    {
        for (int i = fromIndex; i >= 0; i--)
        {
            var t = tokens[i];
            if (IsWhitespaceOrComment(t))
            {
                continue;
            }

            switch (t.TokenType)
            {
                // Statement boundary — stop scanning, treat as new statement (#22)
                case TSqlTokenType.Semicolon: return ClauseType.Unknown;

                case TSqlTokenType.Select: return ClauseType.Select;
                case TSqlTokenType.From: return ClauseType.From;
                case TSqlTokenType.Where: return ClauseType.Where;
                case TSqlTokenType.Join: return ClauseType.JoinTable;
                case TSqlTokenType.On: return ClauseType.JoinOn;
                case TSqlTokenType.Having: return ClauseType.Having;
                case TSqlTokenType.Delete: return ClauseType.Delete;
                case TSqlTokenType.Create: return ClauseType.Create;
                case TSqlTokenType.Alter: return ClauseType.Alter;
                case TSqlTokenType.With: return ClauseType.With;
                case TSqlTokenType.Set:
                    // Distinguish SET in UPDATE ... SET from standalone SET options.
                    // If we already saw UPDATE earlier, this is UpdateSet.
                    // Otherwise it's a standalone SET (SET NOCOUNT, etc.)
                    // For simplicity, walk back to see if UPDATE precedes.
                    for (int j = i - 1; j >= 0; j--)
                    {
                        var tj = tokens[j];
                        if (IsWhitespaceOrComment(tj)) continue;
                        if (tj.TokenType == TSqlTokenType.Update) return ClauseType.UpdateSet;
                        break; // Not preceded by UPDATE — standalone SET
                    }
                    return ClauseType.Set;
                case TSqlTokenType.Execute: return ClauseType.Exec;
                case TSqlTokenType.Grant: return ClauseType.Grant;
                case TSqlTokenType.Deny: return ClauseType.Grant;
                case TSqlTokenType.Revoke: return ClauseType.Grant;
                case TSqlTokenType.Drop: return ClauseType.Drop;
                case TSqlTokenType.Declare: return ClauseType.Declare;
                case TSqlTokenType.Over: return ClauseType.Over;
                case TSqlTokenType.Option: return ClauseType.Option;
                case TSqlTokenType.Use: return ClauseType.Use;

                // TSqlTokenType.By is a dedicated token. When we encounter it scanning
                // backwards, walk back one more non-whitespace token and check its TEXT
                // (ScriptDom may tokenize GROUP/ORDER as dedicated keyword tokens OR as
                // Identifiers depending on version, so we match on text, not token type).
                case TSqlTokenType.By:
                    for (int j = i - 1; j >= 0; j--)
                    {
                        var tj = tokens[j];
                        if (IsWhitespaceOrComment(tj)) continue;
                        var upperBy = tj.Text?.ToUpperInvariant();
                        if (upperBy == "GROUP") return ClauseType.GroupBy;
                        if (upperBy == "ORDER") return ClauseType.OrderBy;
                        break;
                    }
                    // Unrecognized use of BY — continue scanning further back
                    continue;

                case TSqlTokenType.Identifier:
                    var upper = t.Text.ToUpperInvariant();
                    if (upper == "GROUP")
                    {
                        return ClauseType.GroupBy;
                    }

                    if (upper == "ORDER")
                    {
                        return ClauseType.OrderBy;
                    }

                    if (upper == "EXEC")
                    {
                        return ClauseType.Exec;
                    }

                    // (BY is handled explicitly above as TSqlTokenType.By — ScriptDom
                    //  tokenizes it as a dedicated token, never as Identifier.)

                    if (upper is "CROSS" or "INNER" or "LEFT" or "RIGHT" or "FULL" or "OUTER")
                    {
                        // JOIN qualifiers — continue scanning to find JOIN/FROM
                        continue;
                    }
                    if (upper is "UNION" or "INTERSECT" or "EXCEPT")
                    {
                        // Set operators — treat as statement boundary so the next
                        // SELECT gets fresh context, not the FROM/JOIN of the previous query
                        return ClauseType.Unknown;
                    }
                    if (upper == "ALL")
                    {
                        // "ALL" after UNION — continue scanning to find UNION
                        continue;
                    }
                    // FOR XML / FOR JSON detection
                    if (upper is "XML")
                    {
                        // Check if preceded by FOR
                        for (int j = i - 1; j >= 0; j--)
                        {
                            var tj = tokens[j];
                            if (IsWhitespaceOrComment(tj)) continue;
                            if (tj.Text?.Equals("FOR", StringComparison.OrdinalIgnoreCase) == true)
                                return ClauseType.ForXml;
                            break;
                        }
                    }
                    if (upper is "JSON")
                    {
                        for (int j = i - 1; j >= 0; j--)
                        {
                            var tj = tokens[j];
                            if (IsWhitespaceOrComment(tj)) continue;
                            if (tj.Text?.Equals("FOR", StringComparison.OrdinalIgnoreCase) == true)
                                return ClauseType.ForJson;
                            break;
                        }
                    }
                    break;
                case TSqlTokenType.Insert:
                    return ClauseType.InsertColumns;
                case TSqlTokenType.Values:
                    return ClauseType.InsertValues;
                case TSqlTokenType.Update:
                    return ClauseType.UpdateTable; // After UPDATE: expects table name (#7)
            }
        }

        return ClauseType.Unknown;
    }

    private static bool IsWhitespaceOrComment(TSqlParserToken t)
    {
        return t.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment or TSqlTokenType.EndOfFile;
    }
}
