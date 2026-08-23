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
    InsertTarget,     // Spec 032 C2: INSERT [INTO] | — expects an insertable object (table/view), never procs/functions
    InsertColumnList, // Spec 032 C1: INSERT INTO t (| — expects t's columns (target injected into AvailableAliases)
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
    Use,         // After USE keyword — expects a database name (to be inserted as [Name])
    CreateTableColumnDef, // Inside CREATE TABLE ( ) after a column-name identifier — expects a data type
    // Spec 032 US5 (B2–B6) — dedicated-token contexts that previously fell through to From/Unknown:
    OrderKeyword,  // ORDER |  → BY
    GroupKeyword,  // GROUP |  → BY
    SetOperator,   // UNION/INTERSECT/EXCEPT |  → SELECT (and ALL)
    JoinQualifier, // LEFT/RIGHT/INNER/CROSS/FULL/OUTER |  → JOIN/OUTER JOIN/APPLY (per qualifier)
    CaseStart,     // CASE |          → WHEN (+ expression for the simple-CASE form)
    CaseWhen,      // WHEN <cond> |   → THEN (+ expression)
    CaseThen,      // THEN <value> |  → WHEN/ELSE/END (+ expression)
    CaseElse       // ELSE <value> |  → END (+ expression)
}

public class CursorContext
{
    public int CursorOffset { get; set; }
    public ClauseType ClauseType { get; set; } = ClauseType.Unknown;
    public TSqlParserToken? PrecedingToken { get; set; }
    public bool PrecedingDot { get; set; }
    public string DotPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Spec 032 (A6): the full dot-qualifier chain before the caret's segment, outermost
    /// first — e.g. <c>OtherDb.dbo.|</c> → ["OtherDb", "dbo"]. <see cref="DotPrefix"/>
    /// remains the NEAREST qualifier (the last element) for existing consumers; providers
    /// that care about multi-part scoping read the chain. Empty when there is no dot.
    /// </summary>
    public List<string> DotPrefixChain { get; set; } = [];
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

    /// <summary>
    /// When <see cref="ClauseType"/> is <see cref="ClauseType.JoinOn"/>, the key into
    /// <see cref="AvailableAliases"/> (alias when present, else the bare table name) of the table
    /// being joined on the CURRENT JOIN — the table reference between the owning <c>JOIN</c> keyword
    /// and the <c>ON</c> that holds the cursor. Providers scope ON-clause suggestions to predicates
    /// that involve this table, so a third table already in scope never contributes a predicate that
    /// ignores the join being written.
    /// <para>
    /// Empty when the target cannot be resolved (<c>MERGE … ON</c>, <c>CREATE INDEX … ON</c>, an
    /// aliasless derived table, or a malformed fragment). Consumers must treat empty as "unknown"
    /// and fall back to their unscoped behaviour.
    /// </para>
    /// </summary>
    public string CurrentJoinTargetAlias { get; set; } = string.Empty;

    /// <summary>
    /// The <c>schema.table</c> name behind <see cref="CurrentJoinTargetAlias"/> when the join target
    /// is a real table; empty for derived tables and unresolved targets.
    /// </summary>
    public string CurrentJoinTargetFullName { get; set; } = string.Empty;

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

        // Check if in comment or string. Spec 032 G3: a "string" token DIRECTLY after a
        // member-access dot is a double-quoted IDENTIFIER being typed (`"dbo"."|`), not a
        // string literal — treating it as InString killed the dot-scoping entirely.
        if (tokenAtCursor != null)
        {
            context.InComment = tokenAtCursor.TokenType is TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment;

            var isStringToken = tokenAtCursor.TokenType is TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral;
            var isQuotedIdentifierAfterDot = isStringToken &&
                tokenAtCursor.Text?.StartsWith("\"") == true &&
                prevToken is { TokenType: TSqlTokenType.Dot };
            context.InString = isStringToken && !isQuotedIdentifierAfterDot;
        }

        if (context.InComment || context.InString)
        {
            return context;
        }

        // Check for dot prefix.
        // Case 1: cursor is past the dot — tokenAtCursor is after the dot, prevToken IS the dot.
        // Case 2: cursor is immediately after the dot — tokenAtCursor IS the dot itself.
        //   This happens when user types "BomItems." and cursor is at the end with no further text.
        // Spec 032 (A6): the whole id(.id)* chain is consumed, not just one identifier, so
        // `db.dbo.|` scopes to dbo (with the chain exposed) instead of ignoring the db part.
        if (prevToken is { TokenType: TSqlTokenType.Dot })
        {
            context.PrecedingDot = true;
            ExtractDotPrefixChain(tokens, IndexOfBackNonTrivia(tokens, tokenIndex - 1), context);
        }
        else if (tokenAtCursor is { TokenType: TSqlTokenType.Dot })
        {
            // Cursor is right at or immediately after the dot token itself
            context.PrecedingDot = true;
            ExtractDotPrefixChain(tokens, tokenIndex, context);
        }

        context.PrecedingToken = prevToken;

        // Extract partial text being typed. Spec 032 C4: Variable tokens included so a
        // typed `@Cust` produces PartialText "@Cust" — without it VariableProvider (and the
        // spec-032 ParameterProvider) can never trigger on an @-prefixed caret.
        // Spec 032 B2-adjacent: word-like KEYWORD tokens count too — a partial like `to|`
        // lexes as the TO keyword, and with no PartialText nothing filtered (KW-011/015).
        if (tokenAtCursor != null && cursorOffset > tokenAtCursor.Offset &&
            (tokenAtCursor.TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier
                 or TSqlTokenType.Variable or TSqlTokenType.AsciiStringOrQuotedIdentifier ||
             IsWordLikeToken(tokenAtCursor)))
        {
            var len = Math.Min(cursorOffset - tokenAtCursor.Offset, tokenAtCursor.Text.Length);
            // Spec 032 G2: strip opening delimiters — `[Cust` must filter as `Cust`
            // (FuzzyMatcher can never match a bracketed partial, which blanked the list).
            context.PartialText = tokenAtCursor.Text.Substring(0, len).TrimStart('[', '"');
        }

        // Determine clause type by walking backwards through tokens
        context.ClauseType = DetermineClauseType(tokens, tokenIndex, context);

        return context;
    }

    /// <summary>
    /// Spec 032 (C1/C2) — classifies the caret's position within an INSERT statement via a
    /// forward scan from the INSERT keyword: <c>INSERT [INTO] [TOP (n)] name(.name)* [( cols )]</c>.
    /// Caret inside the column-list parens → <see cref="ClauseType.InsertColumnList"/> with the
    /// target table injected into <see cref="CursorContext.AvailableAliases"/> (the proven
    /// ALTER TABLE pattern); caret at the (empty or partially typed) name position after INTO →
    /// <see cref="ClauseType.InsertTarget"/>; otherwise <see cref="ClauseType.InsertColumns"/>
    /// (keyword position — VALUES/SELECT/INTO…).
    /// </summary>
    private static ClauseType DetectInsertClauseType(
        IList<TSqlParserToken> tokens, int insertIndex, CursorContext context)
    {
        int cursor = context.CursorOffset;
        static int TokenEnd(TSqlParserToken t) => t.Offset + (t.Text?.Length ?? 0);
        bool BeforeCursor(int idx) => idx < tokens.Count && TokenEnd(tokens[idx]) <= cursor;

        int f = SkipForwardTrivia(tokens, insertIndex + 1);
        bool sawInto = false;

        if (BeforeCursor(f) && tokens[f].TokenType == TSqlTokenType.Into)
        {
            sawInto = true;
            f = SkipForwardTrivia(tokens, f + 1);
        }

        // Optional TOP (n)
        if (BeforeCursor(f) && tokens[f].TokenType == TSqlTokenType.Top)
        {
            f = SkipForwardTrivia(tokens, f + 1);
            if (BeforeCursor(f) && tokens[f].TokenType == TSqlTokenType.LeftParenthesis)
            {
                int depth = 1;
                f++;
                while (f < tokens.Count && depth > 0 && TokenEnd(tokens[f]) <= cursor)
                {
                    if (tokens[f].TokenType == TSqlTokenType.LeftParenthesis) depth++;
                    else if (tokens[f].TokenType == TSqlTokenType.RightParenthesis) depth--;
                    f++;
                }
                f = SkipForwardTrivia(tokens, f);
            }
        }

        // Multi-part target name, fully typed before the caret.
        var parts = new List<string>();
        while (BeforeCursor(f) &&
               tokens[f].TokenType is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
        {
            parts.Add(tokens[f].Text.Trim('[', ']', '"'));
            int k = SkipForwardTrivia(tokens, f + 1);
            if (k < tokens.Count && tokens[k].TokenType == TSqlTokenType.Dot && TokenEnd(tokens[k]) <= cursor)
            {
                f = SkipForwardTrivia(tokens, k + 1);
                continue;
            }
            f = k;
            break;
        }

        if (parts.Count == 0)
        {
            // Caret at (or typing) the target name. After INTO the position is unambiguous;
            // bare `INSERT |` keeps the keyword position (AfterInsert now offers INTO) with
            // ObjectProvider's insertable-object list alongside — matches SSMS.
            return sawInto ? ClauseType.InsertTarget : ClauseType.InsertColumns;
        }

        var tableName = parts[parts.Count - 1];
        var schemaName = parts.Count >= 2 ? parts[parts.Count - 2] : "dbo";

        // Column-list parens after the name, with the caret inside them?
        if (f < tokens.Count && tokens[f].TokenType == TSqlTokenType.LeftParenthesis && tokens[f].Offset < cursor)
        {
            int depth = 1;
            int close = -1;
            for (int g = f + 1; g < tokens.Count; g++)
            {
                if (tokens[g].TokenType == TSqlTokenType.LeftParenthesis) depth++;
                else if (tokens[g].TokenType == TSqlTokenType.RightParenthesis && --depth == 0)
                {
                    close = g;
                    break;
                }
            }

            if (close < 0 || cursor <= tokens[close].Offset)
            {
                if (!context.AvailableAliases.ContainsKey(tableName))
                    context.AvailableAliases[tableName] = $"{schemaName}.{tableName}";
                return ClauseType.InsertColumnList;
            }
        }

        // Name typed, caret past it (and past any closed column list) → keyword position.
        return ClauseType.InsertColumns;
    }

    private static int SkipForwardTrivia(IList<TSqlParserToken> tokens, int start)
    {
        while (start < tokens.Count && IsWhitespaceOrComment(tokens[start])) start++;
        return start;
    }

    /// <summary>
    /// Spec 032 B6: true when the WHEN/THEN/ELSE at <paramref name="index"/> belongs to an
    /// OPEN CASE expression — i.e. walking further back finds an unmatched CASE before any
    /// MERGE / IF / statement boundary. Closed inner CASE…END pairs are balanced out.
    /// </summary>
    private static bool IsInsideCaseExpression(IList<TSqlParserToken> tokens, int index)
    {
        int closedCases = 0;
        for (int j = index - 1; j >= 0; j--)
        {
            var t = tokens[j];
            if (IsWhitespaceOrComment(t)) continue;
            switch (t.TokenType)
            {
                case TSqlTokenType.End:
                    closedCases++;
                    continue;
                case TSqlTokenType.Case:
                    if (closedCases > 0) { closedCases--; continue; }
                    return true;
                case TSqlTokenType.Merge:
                case TSqlTokenType.If:
                case TSqlTokenType.Semicolon:
                    return false;
            }
        }
        return false;
    }

    /// <summary>Spec 032: a token whose text reads as a word being typed (letters/digits/_,
    /// starting with a letter or underscore) — keyword tokens like TO/AS/ON qualify.</summary>
    private static bool IsWordLikeToken(TSqlParserToken token)
    {
        var text = token.Text;
        if (string.IsNullOrEmpty(text)) return false;
        if (!(char.IsLetter(text[0]) || text[0] == '_')) return false;
        for (int i = 1; i < text.Length; i++)
        {
            if (!(char.IsLetterOrDigit(text[i]) || text[i] == '_')) return false;
        }
        return true;
    }

    /// <summary>Index of the nearest non-trivia token at or before <paramref name="start"/>, or -1.</summary>
    private static int IndexOfBackNonTrivia(IList<TSqlParserToken> tokens, int start)
    {
        int i = start;
        while (i >= 0 && IsWhitespaceOrComment(tokens[i])) i--;
        return i;
    }

    /// <summary>
    /// Spec 032 (A6): walks the <c>identifier (. identifier)*</c> chain backwards from the
    /// dot at <paramref name="dotIndex"/>, populating <see cref="CursorContext.DotPrefix"/>
    /// (nearest qualifier — existing semantics) and <see cref="CursorContext.DotPrefixChain"/>
    /// (outermost → nearest).
    /// </summary>
    private static void ExtractDotPrefixChain(IList<TSqlParserToken> tokens, int dotIndex, CursorContext context)
    {
        if (dotIndex < 1 || tokens[dotIndex].TokenType != TSqlTokenType.Dot) return;

        var parts = new List<string>(); // nearest-first while walking back
        int i = dotIndex;
        while (i >= 1 && tokens[i].TokenType == TSqlTokenType.Dot)
        {
            int idIdx = IndexOfBackNonTrivia(tokens, i - 1);
            if (idIdx < 0) break;
            var idToken = tokens[idIdx];
            // Spec 032 G3: double-quoted names lex as AsciiStringOrQuotedIdentifier —
            // `"dbo"."|` must keep its dot-scoping.
            if (idToken.TokenType is not (TSqlTokenType.Identifier
                or TSqlTokenType.QuotedIdentifier
                or TSqlTokenType.AsciiStringOrQuotedIdentifier)) break;

            parts.Add(idToken.Text.Trim('[', ']', '"'));
            i = IndexOfBackNonTrivia(tokens, idIdx - 1); // another dot → keep walking
            if (i < 0) break;
        }

        if (parts.Count == 0) return;
        context.DotPrefix = parts[0];
        parts.Reverse();
        context.DotPrefixChain = parts;
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
        // Spec 032 B6: once an END is crossed going back, any CASE/WHEN/THEN/ELSE further
        // back belongs to a CLOSED case expression (or a BEGIN…END block) — suppress the
        // Case* classifications and keep walking (pre-032 behavior).
        bool sawEndToken = false;

        // Spec 032: when the CARET's own token happens to lex as a keyword because an
        // identifier is being typed (`FROM Order|` lexes as TSqlTokenType.Order,
        // `FROM Exec|` as Exec), the keyword-position cases below must not classify it —
        // it's a partial identifier, not a completed keyword.
        bool caretTokenIsPartial = !string.IsNullOrEmpty(context.PartialText);

        for (int i = fromIndex; i >= 0; i--)
        {
            var t = tokens[i];
            if (IsWhitespaceOrComment(t))
            {
                continue;
            }

            bool isCaretToken = i == fromIndex && caretTokenIsPartial;

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
                case TSqlTokenType.On:
                    // `i` is the ON that owns the cursor. Resolve the table it joins so
                    // providers can scope ON-clause suggestions to that table.
                    ResolveCurrentJoinTarget(tokens, i, context);
                    return ClauseType.JoinOn;
                case TSqlTokenType.Having: return ClauseType.Having;
                case TSqlTokenType.Delete: return ClauseType.Delete;
                case TSqlTokenType.Create:
                    // If the cursor is inside the first-level paren of a CREATE TABLE statement
                    // (checkedEnclosingParen == true, meaning we exited exactly one paren level)
                    // and the token immediately before the cursor is a column-name identifier,
                    // classify as CreateTableColumnDef so data-type keywords rank first.
                    if (checkedEnclosingParen)
                    {
                        int fwdCreate = i + 1;
                        while (fwdCreate < tokens.Count && IsWhitespaceOrComment(tokens[fwdCreate])) fwdCreate++;
                        if (fwdCreate < tokens.Count && tokens[fwdCreate].TokenType == TSqlTokenType.Table
                            && context.PrecedingToken?.TokenType is TSqlTokenType.Identifier
                                                                 or TSqlTokenType.QuotedIdentifier)
                        {
                            return ClauseType.CreateTableColumnDef;
                        }
                    }
                    return ClauseType.Create;
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
                        // Spec 032 B7: a balanced (…) preceded by TOP is part of the UPDATE
                        // target (`UPDATE TOP (5) dbo.Orders SET |`) — without this skip the
                        // `)` aborted the scan and the caret got the SET-options list.
                        if (tj.TokenType == TSqlTokenType.RightParenthesis)
                        {
                            int depth = 1;
                            j--;
                            while (j >= 0 && depth > 0)
                            {
                                if (tokens[j].TokenType == TSqlTokenType.RightParenthesis) depth++;
                                else if (tokens[j].TokenType == TSqlTokenType.LeftParenthesis) depth--;
                                j--;
                            }
                            while (j >= 0 && IsWhitespaceOrComment(tokens[j])) j--;
                            if (j >= 0 && tokens[j].TokenType == TSqlTokenType.Top) continue; // for-loop steps past TOP
                            break;
                        }
                        break; // Hit a keyword or punctuation that can't be part of UPDATE table
                    }
                    return ClauseType.Set;
                // Spec 032 B1: `EXEC` lexes as the DEDICATED TSqlTokenType.Exec — the
                // Identifier-text arm below never sees it. Without this case the walk fell
                // through to From/Unknown and proc-name completion after `EXEC ` was dead
                // (clause=Exec fired 7× across the whole 1,500-request campaign).
                case TSqlTokenType.Exec:
                    if (isCaretToken) break; // `FROM Exec|` — identifier being typed
                    return ClauseType.Exec;
                case TSqlTokenType.Execute:
                    if (isCaretToken) break;
                    return ClauseType.Exec;

                // Spec 032 B2: ORDER/GROUP also lex as dedicated tokens — the Identifier-text
                // arms below are dead for them, so `ORDER |` misclassified as From (tables +
                // HAVING offered, BY never). After `ORDER BY |` the By case above wins (nearer).
                case TSqlTokenType.Order:
                    if (isCaretToken) break; // `FROM Order|` — partial identifier (CTE-039)
                    return ClauseType.OrderKeyword;
                case TSqlTokenType.Group:
                    if (isCaretToken) break;
                    return ClauseType.GroupKeyword;

                // Spec 032 B4: dedicated set-operator tokens — statement boundary AND a
                // keyword context (SELECT/ALL).
                case TSqlTokenType.Union:
                case TSqlTokenType.Intersect:
                case TSqlTokenType.Except:
                    if (isCaretToken) break;
                    return ClauseType.SetOperator;

                // Spec 032 B3: join qualifiers (dedicated tokens). LEFT( / RIGHT( are the
                // string functions — when followed by an open paren, keep walking.
                case TSqlTokenType.Inner:
                case TSqlTokenType.Left:
                case TSqlTokenType.Right:
                case TSqlTokenType.Full:
                case TSqlTokenType.Cross:
                case TSqlTokenType.Outer:
                {
                    if (isCaretToken) break;
                    int nf = SkipForwardTrivia(tokens, i + 1);
                    if (nf < tokens.Count && tokens[nf].TokenType == TSqlTokenType.LeftParenthesis)
                        break;
                    // Spec 032 H3: `CROSS/OUTER APPLY |` — APPLY lexes as an Identifier;
                    // when it's already typed before the caret this is a TABLE/FUNCTION
                    // position (TVFs valid), not a join-qualifier keyword position.
                    if (nf < tokens.Count &&
                        tokens[nf].TokenType == TSqlTokenType.Identifier &&
                        string.Equals(tokens[nf].Text, "APPLY", StringComparison.OrdinalIgnoreCase) &&
                        tokens[nf].Offset + (tokens[nf].Text?.Length ?? 0) <= context.CursorOffset)
                        return ClauseType.JoinTable;
                    return ClauseType.JoinQualifier;
                }

                // Spec 032 B6: CASE expression states (suppressed once an END was crossed).
                case TSqlTokenType.End:
                    sawEndToken = true;
                    break;
                case TSqlTokenType.Case:
                    if (isCaretToken || sawEndToken) break;
                    return ClauseType.CaseStart;
                case TSqlTokenType.When:
                    // WHEN also belongs to MERGE (WHEN MATCHED) — only a CASE's WHEN counts.
                    if (isCaretToken || sawEndToken || !IsInsideCaseExpression(tokens, i)) break;
                    return ClauseType.CaseWhen;
                case TSqlTokenType.Then:
                    if (isCaretToken || sawEndToken || !IsInsideCaseExpression(tokens, i)) break;
                    return ClauseType.CaseThen;
                case TSqlTokenType.Else:
                    // ELSE also belongs to IF…ELSE — only a CASE's ELSE counts.
                    if (isCaretToken || sawEndToken || !IsInsideCaseExpression(tokens, i)) break;
                    return ClauseType.CaseElse;
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
                    // Spec 032 C1/C2: INSERT has three distinct positions (target name /
                    // column list / keyword). A forward scan disambiguates and injects the
                    // target table for the column-list case (mirrors the ALTER TABLE path).
                    return DetectInsertClauseType(tokens, i, context);
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

    /// <summary>
    /// Given the index of the <c>ON</c> that classified the cursor as <see cref="ClauseType.JoinOn"/>,
    /// walks back to the <c>JOIN</c> keyword that owns it and parses the table reference between the
    /// two, populating <see cref="CursorContext.CurrentJoinTargetAlias"/> and
    /// <see cref="CursorContext.CurrentJoinTargetFullName"/>.
    /// <para>
    /// Leaves both empty (target unknown) when the <c>ON</c> has no owning <c>JOIN</c> in scope —
    /// <c>MERGE … ON</c>, <c>CREATE INDEX … ON</c>, a preceding statement, or a malformed fragment —
    /// and when the target is an aliasless derived table. Parenthesis depth is tracked so a JOIN
    /// nested inside a derived table never claims ownership of an outer ON.
    /// </para>
    /// </summary>
    private static void ResolveCurrentJoinTarget(
        IList<TSqlParserToken> tokens, int onIndex, CursorContext context)
    {
        // 1) Back to the owning JOIN, at paren depth 0.
        int depth = 0;
        int joinIndex = -1;
        for (int i = onIndex - 1; i >= 0; i--)
        {
            var t = tokens[i];
            if (IsWhitespaceOrComment(t)) continue;

            if (t.TokenType == TSqlTokenType.RightParenthesis) { depth++; continue; }
            if (t.TokenType == TSqlTokenType.LeftParenthesis) { if (depth > 0) depth--; continue; }
            if (depth > 0) continue;

            if (t.TokenType == TSqlTokenType.Join) { joinIndex = i; break; }

            // Anything that proves this ON is not a JOIN's ON (or that we have left the
            // current join): give up rather than attribute a stale table to the cursor.
            if (t.TokenType is TSqlTokenType.Semicolon or TSqlTokenType.On or TSqlTokenType.From
                            or TSqlTokenType.Where or TSqlTokenType.Select or TSqlTokenType.Merge
                            or TSqlTokenType.Create)
                return;
        }
        if (joinIndex < 0) return;

        // 2) Forward from JOIN to ON: `(derived) [AS] alias` | `[db.][schema.]table [[AS] alias]`.
        int j = joinIndex + 1;
        while (j < onIndex && IsWhitespaceOrComment(tokens[j])) j++;
        if (j >= onIndex) return;

        if (tokens[j].TokenType == TSqlTokenType.LeftParenthesis)
        {
            // A derived table contributes only its alias — there is no schema.table behind it.
            j = SkipParenGroup(tokens, j, onIndex);
            context.CurrentJoinTargetAlias = ReadAlias(tokens, j, onIndex) ?? string.Empty;
            return;
        }

        if (!IsIdentifierToken(tokens[j]))
            return;

        // Multi-part name: consume `part [. part]*`, tolerating the omitted-schema form `db..table`.
        var parts = new List<string> { TrimIdentifier(tokens[j].Text) };
        j++;
        while (true)
        {
            int resume = j;
            while (j < onIndex && IsWhitespaceOrComment(tokens[j])) j++;
            if (j < onIndex && tokens[j].TokenType == TSqlTokenType.Dot)
            {
                j++;
                while (j < onIndex && IsWhitespaceOrComment(tokens[j])) j++;
                if (j < onIndex && IsIdentifierToken(tokens[j]))
                {
                    parts.Add(TrimIdentifier(tokens[j].Text));
                    j++;
                    continue;
                }
                if (j < onIndex && tokens[j].TokenType == TSqlTokenType.Dot)
                {
                    // `db..Table` — the elided schema is an empty part; the next loop pass
                    // consumes this second dot and the table name behind it.
                    parts.Add(string.Empty);
                    continue;
                }
            }
            j = resume;
            break;
        }

        var table = parts[parts.Count - 1];
        var schema = parts.Count >= 2 && parts[parts.Count - 2].Length > 0
            ? parts[parts.Count - 2]
            : "dbo";
        if (table.Length == 0) return;

        // Step over a trailing parenthesised group so the alias behind it is still found:
        // a table-valued function (`dbo.fn(1) f`) or a legacy table hint (`t (NOLOCK)`).
        int afterName = j;
        while (afterName < onIndex && IsWhitespaceOrComment(tokens[afterName])) afterName++;
        if (afterName < onIndex && tokens[afterName].TokenType == TSqlTokenType.LeftParenthesis)
            j = SkipParenGroup(tokens, afterName, onIndex);

        // AvailableAliases keys on the alias when present, else the bare table name —
        // mirror that convention exactly so providers can index straight into it.
        context.CurrentJoinTargetAlias = ReadAlias(tokens, j, onIndex) ?? table;
        context.CurrentJoinTargetFullName = $"{schema}.{table}";
    }

    /// <summary>
    /// Reads an optional `[AS] alias` starting at <paramref name="start"/>, stopping before
    /// <paramref name="end"/>. Returns null when the next real token is not an identifier (a table
    /// hint such as <c>WITH (NOLOCK)</c>, or the ON itself). <c>AS</c> is matched by text because
    /// ScriptDom tokenizes it as a keyword in some positions and an identifier in others.
    /// </summary>
    private static string? ReadAlias(IList<TSqlParserToken> tokens, int start, int end)
    {
        int k = start;
        while (k < end && IsWhitespaceOrComment(tokens[k])) k++;
        if (k < end && string.Equals(tokens[k].Text, "AS", StringComparison.OrdinalIgnoreCase))
        {
            k++;
            while (k < end && IsWhitespaceOrComment(tokens[k])) k++;
        }
        if (k < end && IsIdentifierToken(tokens[k]))
            return TrimIdentifier(tokens[k].Text);
        return null;
    }

    /// <summary>
    /// Identifier tokens as they appear in a table reference. Double-quoted names arrive as
    /// <see cref="TSqlTokenType.AsciiStringOrQuotedIdentifier"/> — the tokenizer cannot know whether
    /// <c>QUOTED_IDENTIFIER</c> is on — and between JOIN and ON such a token is always an identifier.
    /// </summary>
    private static bool IsIdentifierToken(TSqlParserToken t) =>
        t.TokenType is TSqlTokenType.Identifier
                    or TSqlTokenType.QuotedIdentifier
                    or TSqlTokenType.AsciiStringOrQuotedIdentifier;

    /// <summary>
    /// Given <paramref name="openIndex"/> pointing at a <c>(</c>, returns the index just past its
    /// matching <c>)</c>, or <paramref name="end"/> if the group is unterminated before it. Used to
    /// step over a derived table's body or a trailing argument/hint list while scanning a table
    /// reference between JOIN and ON.
    /// </summary>
    private static int SkipParenGroup(IList<TSqlParserToken> tokens, int openIndex, int end)
    {
        int depth = 1;
        int i = openIndex + 1;
        while (i < end && depth > 0)
        {
            if (tokens[i].TokenType == TSqlTokenType.LeftParenthesis) depth++;
            else if (tokens[i].TokenType == TSqlTokenType.RightParenthesis) depth--;
            i++;
        }
        return i;
    }

    private static string TrimIdentifier(string text) => text.Trim('[', ']', '"');

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
