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
    AlterTableColumn, // After ALTER TABLE <name> ALTER COLUMN — yields columns from <name>
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
    /// <summary>
    /// True when the cursor is inside the body of a CTE definition — i.e. directly
    /// inside the parentheses of <c>WITH Name AS ( ... )</c> or <c>, Name AS ( ... )</c>.
    /// Signals that providers should treat the position as a fresh query-start context
    /// (offer SELECT, FROM, etc.) and that prior-CTE names should be suggested for
    /// FROM/JOIN. Does not imply <c>ClauseType</c> — when true the analyzer also
    /// returns <see cref="ClauseType.Unknown"/>.
    /// </summary>
    public bool IsInCteBody { get; set; }
    /// <summary>
    /// The session id of the request that produced this context. Populated by
    /// <see cref="Completion.CompletionEngine.GetCompletions(string, int, Schema.DatabaseCache?, string)"/>
    /// so providers that need per-session state (e.g. <c>DatabaseProvider</c>'s
    /// per-connection database list cache) can look it up without static state.
    /// </summary>
    public string SessionId { get; set; } = string.Empty;
    public Dictionary<string, string> AvailableAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> AvailableCtes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// For each CTE in scope, the list of underlying tables referenced in the CTE
    /// body's FROM/JOIN clauses. Used by JoinOnFkProvider to look up real FK
    /// relationships between CTE pairs by walking through to their source tables.
    /// </summary>
    public Dictionary<string, List<(string Schema, string Table)>> AvailableCteSources { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
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
                    prevToken = SkipBackOverTrivia(tokens, i - 1);
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
                prevToken = SkipBackOverTrivia(tokens, tokenIndex - 1);
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
        context.ClauseType = DetermineClauseType(tokens, tokenIndex, context);

        return context;
    }

    /// <summary>
    /// Determines the SQL clause type by walking backwards through the token stream.
    /// Covers all clause contexts for alias resolution (T045):
    /// SELECT, FROM, WHERE, JOIN ON, GROUP BY, HAVING, ORDER BY, UPDATE SET,
    /// INSERT (columns/values), DELETE, CREATE, ALTER, ALTER TABLE…ALTER COLUMN, EXEC, WITH (CTEs).
    /// When <c>AlterTableColumn</c> is detected, populates <paramref name="context"/>.AvailableAliases
    /// with the ALTER TABLE target table so ColumnProvider can resolve columns.
    /// </summary>
    private static ClauseType DetermineClauseType(IList<TSqlParserToken> tokens, int fromIndex, CursorContext context)
    {
        // Track paren depth across the backward walk so tokens inside SIBLING balanced
        // paren groups (e.g. the body of a preceding CTE) don't leak into the current
        // clause classification. Without this, `WITH c1 AS (... JOIN .. ON ..), c2 AS (|`
        // walks from the cursor, crosses `)` into c1's body and returns JoinOn — wrong.
        int parenDepth = 0;
        bool checkedEnclosingParen = false;
        // True once we've crossed at least one balanced sibling paren group going back.
        // Used when we hit WITH: if a sibling group was crossed, the CTE list is past
        // us (cursor sits AFTER `WITH ... AS (...)`) and the next thing the user wants
        // is a statement keyword (SELECT/INSERT/...), not the AfterWith table-hints.
        bool sawSiblingParenGroup = false;

        for (int i = fromIndex; i >= 0; i--)
        {
            var t = tokens[i];
            if (IsWhitespaceOrComment(t))
            {
                continue;
            }

            if (t.TokenType == TSqlTokenType.RightParenthesis)
            {
                parenDepth++;
                sawSiblingParenGroup = true;
                continue;
            }
            if (t.TokenType == TSqlTokenType.LeftParenthesis)
            {
                parenDepth--;
                // Moment we exit the cursor's enclosing paren: if it opens a CTE body
                // (`[, | WITH] Name [(cols)] AS (`), classify as statement-start so
                // GeneralKeywords (SELECT, WITH, INSERT, ...) are offered inside Cte2's body.
                if (!checkedEnclosingParen && parenDepth == -1)
                {
                    checkedEnclosingParen = true;
                    if (IsCteAsOpenParen(tokens, i))
                    {
                        context.IsInCteBody = true;
                        return ClauseType.Unknown;
                    }
                }
                continue;
            }

            // Inside a sibling balanced group we've already walked past — skip.
            if (parenDepth > 0)
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
                case TSqlTokenType.Alter:
                    return DetectAlterClauseType(tokens, i, context);
                case TSqlTokenType.With:
                    // After a CTE list (`WITH ... AS (body) |`) the cursor wants
                    // statement-start keywords, not the AfterWith table-hint set.
                    // Detect "list complete" by whether we crossed a balanced sibling
                    // group on the way back from the cursor.
                    return sawSiblingParenGroup ? ClauseType.Unknown : ClauseType.With;
                case TSqlTokenType.Set:
                    // Distinguish "UPDATE [schema.]table SET" from standalone SET options.
                    // Scan all the way back through table/schema tokens to find UPDATE.
                    // Stop at any statement boundary or unambiguous non-UPDATE keyword.
                    for (int j = i - 1; j >= 0; j--)
                    {
                        var tj = tokens[j];
                        if (IsWhitespaceOrComment(tj)) continue;
                        if (tj.TokenType == TSqlTokenType.Update) return ClauseType.UpdateSet;
                        // Identifiers and dots are table/schema name tokens — keep scanning.
                        if (tj.TokenType == TSqlTokenType.Identifier ||
                            tj.TokenType == TSqlTokenType.QuotedIdentifier ||
                            tj.TokenType == TSqlTokenType.Dot) continue;
                        break; // Hit a keyword or punctuation that can't be part of UPDATE table
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

    /// <summary>
    /// Inspects the ALTER keyword at <paramref name="alterIndex"/> to decide whether it
    /// is the inner ALTER in an <c>ALTER TABLE [schema.]table ALTER COLUMN</c> statement.
    /// If the pattern is confirmed the target table is injected into
    /// <paramref name="context"/>.AvailableAliases and <c>AlterTableColumn</c> is returned;
    /// otherwise <c>Alter</c> is returned (generic DDL context).
    /// </summary>
    private static ClauseType DetectAlterClauseType(
        IList<TSqlParserToken> tokens, int alterIndex, CursorContext context)
    {
        // Peek forward: if the next non-whitespace token is COLUMN this is "ALTER COLUMN".
        int fwd = alterIndex + 1;
        while (fwd < tokens.Count && IsWhitespaceOrComment(tokens[fwd])) fwd++;

        if (fwd >= tokens.Count || tokens[fwd].TokenType != TSqlTokenType.Column)
            return ClauseType.Alter;

        // Confirmed inner ALTER COLUMN. Scan backward to verify the full pattern:
        //   <outer-ALTER>  TABLE  [schema .]  table  ALTER  COLUMN
        int b = alterIndex - 1;
        while (b >= 0 && IsWhitespaceOrComment(tokens[b])) b--;

        // Expect the table name identifier
        if (b < 0 || tokens[b].TokenType is not (TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier))
            return ClauseType.Alter;

        var rawName = tokens[b].Text.Trim('[', ']', '"');
        b--;
        while (b >= 0 && IsWhitespaceOrComment(tokens[b])) b--;

        string schemaName;
        string tableName;

        if (b >= 0 && tokens[b].TokenType == TSqlTokenType.Dot)
        {
            // schema.table — grab the schema identifier before the dot
            b--;
            while (b >= 0 && IsWhitespaceOrComment(tokens[b])) b--;
            if (b < 0 || tokens[b].TokenType is not (TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier))
                return ClauseType.Alter;
            schemaName = tokens[b].Text.Trim('[', ']', '"');
            tableName  = rawName;
            b--;
            while (b >= 0 && IsWhitespaceOrComment(tokens[b])) b--;
        }
        else
        {
            schemaName = "dbo";
            tableName  = rawName;
            // b already points to the token before the table name
        }

        // Expect TABLE keyword
        if (b < 0 || tokens[b].TokenType != TSqlTokenType.Table)
            return ClauseType.Alter;
        b--;
        while (b >= 0 && IsWhitespaceOrComment(tokens[b])) b--;

        // Expect outer ALTER keyword
        if (b < 0 || tokens[b].TokenType != TSqlTokenType.Alter)
            return ClauseType.Alter;

        // Pattern confirmed — inject the table so ColumnProvider can resolve columns
        if (!context.AvailableAliases.ContainsKey(tableName))
            context.AvailableAliases[tableName] = $"{schemaName}.{tableName}";
        return ClauseType.AlterTableColumn;
    }

    private static bool IsWhitespaceOrComment(TSqlParserToken t)
    {
        return t.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment or TSqlTokenType.EndOfFile;
    }

    /// <summary>
    /// Walks backward from <paramref name="startIndex"/> over whitespace and
    /// comment tokens, returning the first "real" token (or null if the entire
    /// run back to the beginning is trivia). Used to set <c>PrecedingToken</c>
    /// to the meaningful token before the cursor — providers (ObjectProvider,
    /// AliasProvider) need to see "what kind of thing came before me?" without
    /// being misled by interior whitespace or newlines.
    /// </summary>
    private static TSqlParserToken? SkipBackOverTrivia(IList<TSqlParserToken> tokens, int startIndex)
    {
        int j = startIndex;
        while (j >= 0 && IsWhitespaceOrComment(tokens[j])) j--;
        return j >= 0 ? tokens[j] : null;
    }

    /// <summary>
    /// Returns true if the <c>(</c> at <paramref name="lparenIndex"/> opens a CTE body.
    /// Recognizes both the first CTE after <c>WITH</c> and subsequent CTEs after <c>,</c>,
    /// with an optional column-list in between: <c>[WITH | ,] Name [(col, ...)] AS (</c>.
    /// </summary>
    private static bool IsCteAsOpenParen(IList<TSqlParserToken> tokens, int lparenIndex)
    {
        int i = lparenIndex - 1;
        while (i >= 0 && IsWhitespaceOrComment(tokens[i])) i--;
        // Expect 'AS' immediately before the open paren. Match by text — ScriptDom
        // sometimes tokenizes AS as a dedicated keyword and sometimes as an Identifier.
        if (i < 0 || !string.Equals(tokens[i].Text, "AS", StringComparison.OrdinalIgnoreCase))
            return false;
        i--;
        while (i >= 0 && IsWhitespaceOrComment(tokens[i])) i--;

        // Optional column list between the name and AS: `Name (c1, c2) AS (`
        if (i >= 0 && tokens[i].TokenType == TSqlTokenType.RightParenthesis)
        {
            int depth = 1;
            i--;
            while (i >= 0 && depth > 0)
            {
                if (tokens[i].TokenType == TSqlTokenType.RightParenthesis) depth++;
                else if (tokens[i].TokenType == TSqlTokenType.LeftParenthesis) depth--;
                i--;
            }
            while (i >= 0 && IsWhitespaceOrComment(tokens[i])) i--;
        }

        // Expect the CTE name (identifier).
        if (i < 0 ||
            tokens[i].TokenType is not (TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier))
            return false;
        i--;
        while (i >= 0 && IsWhitespaceOrComment(tokens[i])) i--;

        // Before the name must be WITH (first CTE) or ',' (subsequent CTE).
        if (i < 0) return false;
        if (tokens[i].TokenType == TSqlTokenType.With) return true;
        if (tokens[i].TokenType == TSqlTokenType.Comma) return true;
        return false;
    }
}
