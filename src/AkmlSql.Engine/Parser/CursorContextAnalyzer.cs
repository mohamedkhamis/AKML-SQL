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
    With
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

        // Check for dot prefix
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
                case TSqlTokenType.Select: return ClauseType.Select;
                case TSqlTokenType.From: return ClauseType.From;
                case TSqlTokenType.Where: return ClauseType.Where;
                case TSqlTokenType.Join: return ClauseType.From;
                case TSqlTokenType.On: return ClauseType.JoinOn;
                case TSqlTokenType.Having: return ClauseType.Having;
                case TSqlTokenType.Delete: return ClauseType.Delete;
                case TSqlTokenType.Create: return ClauseType.Create;
                case TSqlTokenType.Alter: return ClauseType.Alter;
                case TSqlTokenType.With: return ClauseType.With;
                case TSqlTokenType.Set:
                    return ClauseType.UpdateSet;
                case TSqlTokenType.Execute: return ClauseType.Exec;
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

                    if (upper == "BY")
                    {
                        // "BY" alone doesn't tell us which clause; continue scanning
                        // to find the preceding GROUP or ORDER keyword.
                        continue;
                    }
                    if (upper is "CROSS" or "INNER" or "LEFT" or "RIGHT" or "FULL" or "OUTER")
                    {
                        // JOIN qualifiers — continue scanning to find JOIN/FROM
                        continue;
                    }
                    break;
                case TSqlTokenType.Insert:
                    return ClauseType.InsertColumns;
                case TSqlTokenType.Values:
                    return ClauseType.InsertValues;
                case TSqlTokenType.Update:
                    return ClauseType.UpdateSet;
            }
        }

        return ClauseType.Unknown;
    }

    private static bool IsWhitespaceOrComment(TSqlParserToken t)
    {
        return t.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment or TSqlTokenType.EndOfFile;
    }
}
