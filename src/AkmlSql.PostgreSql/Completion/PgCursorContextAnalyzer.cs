using AkmlSql.PostgreSql.Language;

namespace AkmlSql.PostgreSql.Completion;

/// <summary>
/// Spec 042 (task T099, FR-061, FR-064) - works out where the caret is in a PostgreSQL statement: the clause, the word being
/// typed, the dot chain before it, the tables and CTEs it can see, the function call it is inside. It is built on the managed
/// scanner (dollar quotes, nested comments, <c>::</c> and positional parameters are single tokens), not on the T-SQL parser,
/// and it never fails: text that does not parse still yields a context. The T-SQL <c>CursorContext</c> is not reused.
/// </summary>
public static class PgCursorContextAnalyzer
{
    public static PgCursorContext Analyze(string? text, int caret)
    {
        text ??= string.Empty;
        caret = Math.Clamp(caret, 0, text.Length);
        return AnalyzeUnit(text, PgLexicalScanner.Scan(text), caret, inBody: false);
    }

    // ── One unit of text: the document, or the body of a dollar-quoted function ─────────────────

    private static PgCursorContext AnalyzeUnit(string text, IReadOnlyList<PgLexToken> tokens, int caret, bool inBody)
    {
        // The token the caret touches: it starts before the caret and ends at or after it.
        var caretToken = -1;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Start >= caret) break;
            if (caret <= tokens[i].End) { caretToken = i; break; }
        }

        var partial = string.Empty;
        var partialStart = caret;
        var partialQuoted = false;
        var skipStart = -1;

        if (caretToken >= 0)
        {
            var tok = tokens[caretToken];
            switch (tok.Kind)
            {
                case PgTokenKind.LineComment:
                case PgTokenKind.MetaCommand:
                    return Suppressed(caret);
                case PgTokenKind.BlockComment:
                    if (!tok.Terminated || caret < tok.End) return Suppressed(caret);
                    break;
                case PgTokenKind.String:
                    if (!tok.Terminated || caret < tok.End) return Suppressed(caret);
                    break;
                case PgTokenKind.Number:
                case PgTokenKind.Parameter:
                    if (caret == tok.End) return Suppressed(caret);
                    break;
                case PgTokenKind.DollarString:
                {
                    var body = DollarBody(text, tok);
                    if (caret >= body.Start && caret <= body.End)
                    {
                        if (!BodyIsSql(text, tokens, caretToken)) return Suppressed(caret);
                        var inner = text.Substring(body.Start, body.End - body.Start);
                        var nested = AnalyzeUnit(inner, PgLexicalScanner.Scan(inner), caret - body.Start, inBody: true);
                        return nested with { InDollarBody = true, PrefixStart = nested.PrefixStart + body.Start, Caret = caret };
                    }
                    break;
                }
                case PgTokenKind.Identifier:
                    partial = text.Substring(tok.Start, caret - tok.Start);
                    partialStart = tok.Start;
                    skipStart = tok.Start;
                    break;
                case PgTokenKind.QuotedIdentifier:
                    if (!tok.Terminated || caret < tok.End)
                    {
                        partial = text.Substring(tok.Start + 1, caret - tok.Start - 1);
                        partialStart = tok.Start;
                        partialQuoted = true;
                        skipStart = tok.Start;
                    }
                    break;
            }
        }

        // The statement: after the last semicolon that ends before the caret, up to the next one.
        var from = 0;
        var to = text.Length;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Kind != PgTokenKind.Semicolon) continue;
            if (tokens[i].End <= caret) from = tokens[i].End;
            else if (tokens[i].Start >= caret) { to = tokens[i].Start; break; }
        }

        var sig = PgStatementTokens.Build(text, tokens, from, to, skipStart);
        var before = 0;
        while (before < sig.Count && sig[before].Token.End <= caret) before++;

        return new Walker(text, sig, before, caret, partial, partialStart, partialQuoted, inBody).Run();
    }

    private static PgCursorContext Suppressed(int caret) => new() { Suppressed = true, Caret = caret, PrefixStart = caret };

    // The body of $tag$...$tag$: from just after the opening tag to just before the closing tag (or the end if it is not closed).
    private static (int Start, int End) DollarBody(string text, PgLexToken token)
    {
        var tagEnd = text.IndexOf('$', token.Start + 1);
        if (tagEnd < 0 || tagEnd >= token.End) return (token.End, token.End);
        var tagLength = tagEnd - token.Start + 1;
        var start = token.Start + tagLength;
        var end = token.Terminated ? token.End - tagLength : token.End;
        return (start, Math.Max(start, end));
    }

    // A dollar-quoted string is SQL when it is the body of a function (after AS) or a DO block; any other one is a string.
    private static bool BodyIsSql(string text, IReadOnlyList<PgLexToken> tokens, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var t = tokens[i];
            if (t.Kind is PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment) continue;
            if (t.Kind == PgTokenKind.Identifier)
            {
                var word = text.Substring(t.Start, t.End - t.Start).ToUpperInvariant();
                if (word is "AS" or "DO") return true;
                // DO LANGUAGE plpgsql $$ ... $$ : the language name comes between.
                if (i > 0) return DoPrecedes(text, tokens, i);
            }
            return false;
        }
        return false;
    }

    private static bool DoPrecedes(string text, IReadOnlyList<PgLexToken> tokens, int i)
    {
        for (var j = i - 1; j >= 0; j--)
        {
            var t = tokens[j];
            if (t.Kind is PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment) continue;
            if (t.Kind != PgTokenKind.Identifier) return false;
            var word = text.Substring(t.Start, t.End - t.Start).ToUpperInvariant();
            if (word == "LANGUAGE") continue;
            return word == "DO";
        }
        return false;
    }

    // ── The walk over the tokens before the caret ───────────────────────────────────────────────

    private enum FrameKind { Root, Subquery, Call, Group, InsertColumns, ValuesRow, ConflictTarget, DdlBody, ColumnList, FuncParams, Subscript }

    private sealed class Frame
    {
        public FrameKind Kind;
        public int Open = -1;
        public PgClause Clause = PgClause.None;
        public string Verb = string.Empty;
        public string? CallName;
        public string? CallSchema;
        public int Arg;
        public int ItemTokens;                 // tokens since the last top-level comma (DDL bodies, parameter lists)
        public string ItemFirst = string.Empty;
        public int WithIndex = -1;
        public int LastJoin = -1;
        public int QueryStart = -1;            // token index where the query's own tokens begin
        public PgTableRef? ListOwner;
        public readonly List<string> DeclaredColumns = new();
        public string DdlObject = string.Empty;
        public bool ConflictTargetDone;
        public bool IsWindowSpec;
        public bool InConflict;
        public bool AfterConflictKeyword;
        public bool IsQuery => Kind is FrameKind.Root or FrameKind.Subquery;
    }

    // Words after which an operand (a name, a constant, a sub-expression) is expected, not an operator or the next clause.
    private static readonly HashSet<string> ExpectsOperand = new(StringComparer.Ordinal)
    {
        "SELECT", "DISTINCT", "ALL", "FROM", "JOIN", "ON", "WHERE", "AND", "OR", "NOT", "IN", "BY", "HAVING", "LIMIT", "OFFSET", "SET",
        "VALUES", "RETURNING", "INTO", "UPDATE", "DELETE", "USING", "AS", "WITH", "UNION", "INTERSECT", "EXCEPT", "CASE", "WHEN", "THEN",
        "ELSE", "BETWEEN", "LIKE", "ILIKE", "SIMILAR", "IS", "EXISTS", "ANY", "SOME", "ARRAY", "INSERT", "ONLY", "LATERAL", "NATURAL",
        "INNER", "LEFT", "RIGHT", "FULL", "CROSS", "OUTER", "RECURSIVE", "OVER", "FILTER", "PARTITION", "TABLE", "COLUMN", "TO", "IF",
        "CONFLICT", "DO", "FETCH", "FIRST", "NEXT", "MATERIALIZED", "VIEW", "INDEX", "REFERENCES", "CONSTRAINT", "RETURNS", "ADD",
        "RENAME", "TRUNCATE", "WINDOW", "FOR", "OF", "MERGE", "MATCHED", "ESCAPE", "OPERATOR", "COLLATE", "AT", "ROW", "ROWS", "UNBOUNDED",
        "PRECEDING", "FOLLOWING", "CURRENT", "INTERVAL", "ATOMIC", "BEGIN", "EXPLAIN", "ANALYZE", "VERBOSE", "KEY", "PRIMARY", "FOREIGN",
        "UNIQUE", "CHECK", "DEFAULT", "GENERATED", "ALWAYS", "OVERRIDING", "LOCK", "COMMENT", "GRANT", "REVOKE", "CASCADE", "RESTRICT",
        "NOTHING", "EXCLUDED", "TYPE", "OWNER", "SCHEMA", "SEQUENCE", "TRIGGER", "EXTENSION", "DATABASE", "ROLE", "USER", "CREATE", "ALTER",
        "DROP", "DECLARE", "CLOSE", "OPEN", "CALL", "EXECUTE", "PREPARE", "COPY", "REFRESH", "VACUUM", "REINDEX", "CLUSTER", "SHOW", "RESET",
    };

    // Words that end an operand and so can precede an operator, a comma, a closing parenthesis or a new clause.
    private static readonly HashSet<string> CompletesOperand = new(StringComparer.Ordinal)
    {
        "NULL", "TRUE", "FALSE", "END", "ASC", "DESC", "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "LOCALTIME",
        "LOCALTIMESTAMP", "CURRENT_USER", "SESSION_USER", "UNKNOWN", "EXCLUDED", "NOTHING", "ROWS", "ONLY", "TIES", "LAST", "MATCHED",
    };

    // A word before "(" that makes it a grouping parenthesis, a clause parenthesis or a subquery, not a function call.
    private static readonly HashSet<string> NotACall = new(StringComparer.Ordinal)
    {
        "IN", "AS", "ON", "VALUES", "USING", "OVER", "FILTER", "WHERE", "AND", "OR", "NOT", "SELECT", "FROM", "JOIN", "BY", "HAVING",
        "EXISTS", "ANY", "ALL", "SOME", "RETURNING", "SET", "WHEN", "THEN", "ELSE", "CASE", "LIKE", "ILIKE", "BETWEEN", "IS", "INTO",
        "UNION", "INTERSECT", "EXCEPT", "DISTINCT", "LATERAL", "ONLY", "WITH", "WITHIN", "CONFLICT", "KEY", "CHECK", "UNIQUE", "EXCLUDE",
        "WINDOW", "PARTITION", "LIMIT", "OFFSET", "FETCH", "TO", "DO", "ARRAY", "ROW", "TABLE", "REFERENCES", "INDEX", "CONSTRAINT",
        "LANGUAGE", "RETURNS", "OF", "PRIMARY", "FOREIGN", "SIMILAR", "ESCAPE", "THEN", "UPDATE", "INSERT", "DELETE", "MERGE", "SET",
    };

    private sealed class Walker
    {
        private readonly string _text;
        private readonly PgStatementTokens _t;
        private readonly int _before;
        private readonly int _caret;
        private readonly string _partial;
        private readonly int _partialStart;
        private readonly bool _partialQuoted;
        private readonly bool _inBody;
        private readonly List<Frame> _stack = new();

        public Walker(string text, PgStatementTokens t, int before, int caret, string partial, int partialStart, bool partialQuoted, bool inBody)
        {
            _inBody = inBody;
            _text = text;
            _t = t;
            _before = before;
            _caret = caret;
            _partial = partial;
            _partialStart = partialStart;
            _partialQuoted = partialQuoted;
        }

        private Frame Top => _stack[^1];

        public PgCursorContext Run()
        {
            _stack.Add(new Frame { Kind = FrameKind.Root, Clause = PgClause.StatementStart, QueryStart = 0 });

            for (var i = 0; i < _before; i++)
            {
                var s = _t[i];
                var f = Top;

                if (s.IsOpen) { Open(i); continue; }
                if (s.IsClose) { Close(i); continue; }
                if (s.IsComma) { Comma(f, i); continue; }

                if (s.IsWord) Word(f, s, i);
                else Other(f, s, i);
            }

            return Build();
        }

        // ── Parentheses ─────────────────────────────────────────────────────────────────────────

        private void Open(int i)
        {
            var parent = Top;
            parent.ItemTokens++;
            var frame = new Frame { Kind = FrameKind.Group, Open = i, Clause = parent.Clause, Verb = parent.Verb };

            if (_t.StartsSubquery(i) || (parent.IsQuery && parent.Clause == PgClause.With && i > 0 && IsCteBodyOpen(i)))
            {
                frame.Kind = FrameKind.Subquery;
                frame.Clause = PgClause.StatementStart;
                frame.Verb = string.Empty;
                frame.QueryStart = i + 1;
            }
            else if (parent.Kind == FrameKind.DdlBody && parent.ItemTokens > 1 && i > 0 && _t[i - 1].IsWord && IsKeyConstraintWord(i))
            {
                // PRIMARY KEY ( / UNIQUE ( / FOREIGN KEY ( inside CREATE TABLE
                frame.Kind = FrameKind.ColumnList;
                frame.Clause = PgClause.DdlColumnList;
                frame.ListOwner = null;
                frame.DeclaredColumns.AddRange(parent.DeclaredColumns);
            }
            else if (parent.Kind == FrameKind.DdlBody && i > 0 && _t[i - 1].IsWordEqual("REFERENCES") == false && ReferencesBefore(i) is { } referenced)
            {
                frame.Kind = FrameKind.ColumnList;
                frame.Clause = PgClause.DdlColumnList;
                frame.ListOwner = referenced;
            }
            else if (parent.IsQuery && parent.Clause == PgClause.InsertTarget)
            {
                frame.Kind = FrameKind.InsertColumns;
                frame.Clause = PgClause.InsertColumns;
            }
            else if (parent.IsQuery && parent.Clause == PgClause.Values)
            {
                frame.Kind = FrameKind.ValuesRow;
                frame.Clause = PgClause.Values;
            }
            else if (parent.IsQuery && parent.Clause == PgClause.OnConflict && !parent.ConflictTargetDone)
            {
                frame.Kind = FrameKind.ConflictTarget;
                frame.Clause = PgClause.OnConflictTarget;
            }
            else if (parent.IsQuery && parent.Clause == PgClause.JoinUsing)
            {
                frame.Kind = FrameKind.ColumnList;
                frame.Clause = PgClause.JoinUsing;
            }
            else if (parent.IsQuery && parent.Verb == "CREATE" && IsRelationDdl(parent) && parent.Clause is PgClause.DdlName or PgClause.Other && parent.DdlObject == "TABLE")
            {
                frame.Kind = FrameKind.DdlBody;
                frame.Clause = PgClause.DdlColumnName;
                frame.ItemTokens = 0;
            }
            else if (parent.IsQuery && parent.Verb == "CREATE" && parent.DdlObject == "INDEX" && parent.ListOwner != null)
            {
                frame.Kind = FrameKind.ColumnList;
                frame.Clause = PgClause.DdlColumnList;
                frame.ListOwner = parent.ListOwner;
            }
            else if (parent.IsQuery && parent.Verb == "CREATE" && parent.DdlObject is "FUNCTION" or "PROCEDURE" && parent.Clause is PgClause.DdlName or PgClause.Other)
            {
                frame.Kind = FrameKind.FuncParams;
                frame.Clause = PgClause.Other;
            }
            else if (i > 0 && _t[i - 1].IsName && IsCallName(i - 1))
            {
                frame.Kind = FrameKind.Call;
                frame.CallName = _t[i - 1].Name.Folded;
                if (i >= 3 && _t[i - 2].IsDot && _t[i - 3].IsName) frame.CallSchema = _t[i - 3].Name.Folded;
                if (_t[i - 1].IsWord && _t[i - 1].Upper is "OVER" or "FILTER") frame.Kind = FrameKind.Group;
            }
            else if (i > 0 && _t[i - 1].IsWordEqual("OVER") || i > 0 && _t[i - 1].IsWordEqual("AS") && parent.Clause == PgClause.Window)
            {
                frame.Kind = FrameKind.Group;
                frame.Clause = PgClause.OrderBy;       // PARTITION BY / ORDER BY take columns
                frame.IsWindowSpec = true;
            }
            else if (i > 0 && _t[i - 1].IsWordEqual("FILTER"))
            {
                frame.Kind = FrameKind.Group;
                frame.Clause = PgClause.Where;
            }
            else if (i > 0 && _t[i - 1].IsOpen == false && _t[i - 1].Kind == PgTokenKind.Punctuation && _t[i - 1].Text == "[")
            {
                frame.Kind = FrameKind.Subscript;
            }

            _stack.Add(frame);
        }

        // WITH name [(cols)] AS [NOT] [MATERIALIZED] (  : the parenthesis opens the CTE's statement.
        private bool IsCteBodyOpen(int open)
        {
            var k = open - 1;
            if (k >= 0 && _t[k].IsWordEqual("MATERIALIZED")) k--;
            if (k >= 0 && _t[k].IsWordEqual("NOT")) k--;
            return k >= 0 && _t[k].IsWordEqual("AS");
        }

        private bool IsRelationDdl(Frame f) => f.DdlObject is "TABLE" or "INDEX" or "VIEW" or "FUNCTION" or "PROCEDURE";

        // PRIMARY KEY (   UNIQUE (   FOREIGN KEY (   EXCLUDE (: the previous word or the one before it says so.
        private bool IsKeyConstraintWord(int open)
        {
            var w = _t[open - 1];
            if (!w.IsWord) return false;
            if (w.Upper is "UNIQUE") return true;
            return w.Upper == "KEY" && open >= 2 && _t[open - 2].IsWord && _t[open - 2].Upper is "PRIMARY" or "FOREIGN";
        }

        // REFERENCES table (  : returns the referenced table.
        private PgTableRef? ReferencesBefore(int open)
        {
            if (open < 2 || !_t[open - 1].IsName) return null;
            var nameIdx = open - 1;
            string? schema = null;
            if (nameIdx >= 2 && _t[nameIdx - 1].IsDot && _t[nameIdx - 2].IsName)
            {
                schema = _t[nameIdx - 2].Name.Folded;
                nameIdx -= 2;
            }
            if (nameIdx < 1 || !_t[nameIdx - 1].IsWordEqual("REFERENCES")) return null;
            return new PgTableRef(string.Empty, schema, _t[open - 1].Name.Folded, PgTableRefKind.Table, Array.Empty<string>(), Array.Empty<PgTableRef>());
        }

        private bool IsCallName(int nameIndex)
        {
            var w = _t[nameIndex];
            if (w.IsQuoted) return true;
            if (NotACall.Contains(w.Upper))
            {
                // `array(select ...)`/`exists(...)` are subqueries or constructs; filter/over are clauses.
                return false;
            }
            // A table name followed by "(" in INSERT INTO t ( / CREATE TABLE t ( is not a call.
            return true;
        }

        private void Close(int i)
        {
            if (_stack.Count <= 1) return;
            var closed = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            var parent = Top;
            parent.ItemTokens++;
            if (closed.Kind == FrameKind.ConflictTarget) parent.ConflictTargetDone = true;
            if (closed.Kind == FrameKind.InsertColumns) parent.Clause = PgClause.Other;
            if (closed.Kind == FrameKind.ColumnList && parent.Kind == FrameKind.Root && parent.Verb == "CREATE") parent.Clause = PgClause.Other;
            if (closed.Kind == FrameKind.FuncParams) parent.Clause = PgClause.Other;
            if (closed.Kind == FrameKind.DdlBody) parent.Clause = PgClause.Other;
        }

        private void Comma(Frame f, int i)
        {
            switch (f.Kind)
            {
                case FrameKind.Call:
                    f.Arg++;
                    break;
                case FrameKind.DdlBody:
                    f.ItemTokens = 0;
                    f.ItemFirst = string.Empty;
                    f.Clause = PgClause.DdlColumnName;
                    break;
                case FrameKind.FuncParams:
                    f.ItemTokens = 0;
                    f.ItemFirst = string.Empty;
                    f.Clause = PgClause.Other;
                    break;
                case FrameKind.Subscript:
                case FrameKind.Group:
                    break;
            }
            f.ItemTokens = f.Kind is FrameKind.DdlBody or FrameKind.FuncParams ? 0 : f.ItemTokens + 1;
        }

        // ── Tokens that are not words, parentheses or commas ────────────────────────────────────

        private void Other(Frame f, PgSig s, int i)
        {
            f.ItemTokens++;
            if (s.IsQuoted && f.Kind == FrameKind.DdlBody && f.ItemTokens == 1)
            {
                f.ItemFirst = s.Name.Folded;
                f.DeclaredColumns.Add(s.Name.Folded);
                f.Clause = PgClause.DdlColumnType;
            }
            else if (s.IsQuoted && f.Kind == FrameKind.FuncParams && f.ItemTokens == 1)
            {
                f.Clause = PgClause.DdlColumnType;
            }
            else if (f.Kind == FrameKind.DdlBody && f.ItemTokens > 1 && f.Clause is PgClause.DdlColumnType or PgClause.Values)
            {
                f.Clause = PgClause.Other;
            }
        }

        // ── Words: clause keywords and names ────────────────────────────────────────────────────

        private void Word(Frame f, PgSig s, int i)
        {
            var up = s.Upper;
            f.ItemTokens++;
            var prev = i > 0 ? _t[i - 1] : null;

            // Items in a CREATE TABLE body and in a function parameter list.
            if (f.Kind == FrameKind.DdlBody) { DdlItem(f, s, i); return; }
            if (f.Kind == FrameKind.FuncParams) { ParamItem(f, s); return; }

            // Non-query parentheses only react to a few words.
            if (!f.IsQuery)
            {
                NonQueryWord(f, s, i, prev);
                return;
            }

            // In a PL/pgSQL body BEGIN, THEN, ELSE and LOOP each start a new statement.
            if (_inBody && f.Kind == FrameKind.Root && up is "BEGIN" or "THEN" or "ELSE" or "LOOP")
            {
                f.Verb = string.Empty;
                f.Clause = PgClause.StatementStart;
                f.QueryStart = i + 1;
                f.WithIndex = -1;
                f.InConflict = false;
                f.ConflictTargetDone = false;
                f.DdlObject = string.Empty;
                return;
            }

            // The first word of a statement says what it is.
            if (f.Verb.Length == 0 && f.Clause is PgClause.StatementStart or PgClause.None)
            {
                StartStatement(f, s, i);
                return;
            }

            switch (up)
            {
                case "WITH" when f.Clause == PgClause.StatementStart:
                    f.Clause = PgClause.With;
                    f.WithIndex = i;
                    break;
                case "SELECT":
                    if (f.Verb.Length == 0) f.Verb = "SELECT";
                    f.Clause = PgClause.SelectList;
                    break;
                case "INSERT" or "UPDATE" or "DELETE" or "MERGE" when f.Verb.Length == 0:
                    // the statement that follows a WITH list
                    f.Verb = up;
                    f.Clause = up == "UPDATE" ? PgClause.UpdateTarget : PgClause.Other;
                    break;
                case "VALUES":
                    f.Clause = PgClause.Values;
                    if (f.Verb.Length == 0) f.Verb = "VALUES";
                    break;
                case "FROM":
                    if (prev != null && prev.IsWordEqual("DISTINCT") && i >= 2 && (_t[i - 2].IsWordEqual("IS") || _t[i - 2].IsWordEqual("NOT"))) break;
                    if (f.Verb == "DELETE") { f.Clause = PgClause.DeleteTarget; break; }
                    if (f.Verb is "COPY" or "REVOKE") { f.Clause = PgClause.Other; break; }
                    f.Clause = PgClause.From;
                    break;
                case "JOIN":
                    f.Clause = PgClause.Join;
                    f.LastJoin = i;
                    break;
                case "ON":
                    On(f, i);
                    break;
                case "USING":
                    if (f.Verb == "DELETE") f.Clause = PgClause.Using;
                    else if (f.Verb == "MERGE") f.Clause = PgClause.MergeSource;
                    else if (f.Clause is PgClause.Join or PgClause.JoinOn) f.Clause = PgClause.JoinUsing;
                    else f.Clause = PgClause.Other;
                    break;
                case "WHERE":
                    f.Clause = PgClause.Where;
                    break;
                case "BY" when prev != null && prev.IsWordEqual("GROUP"):
                    f.Clause = PgClause.GroupBy;
                    break;
                case "BY" when prev != null && prev.IsWordEqual("ORDER"):
                    f.Clause = PgClause.OrderBy;
                    break;
                case "HAVING":
                    f.Clause = PgClause.Having;
                    break;
                case "WINDOW":
                    f.Clause = PgClause.Window;
                    break;
                case "LIMIT":
                case "OFFSET":
                case "FETCH":
                    f.Clause = PgClause.Limit;
                    break;
                case "UNION":
                case "INTERSECT":
                case "EXCEPT":
                    f.Clause = PgClause.SetOperation;
                    break;
                case "RETURNING":
                    f.Clause = PgClause.Returning;
                    break;
                case "INTO":
                    Into(f);
                    break;
                case "SET":
                    if (f.Verb == "UPDATE" || f.InConflict || (f.Verb == "MERGE" && f.Clause == PgClause.MergeAction)) f.Clause = PgClause.Set;
                    else f.Clause = PgClause.Other;
                    break;
                case "CONFLICT" when prev != null && prev.IsWordEqual("ON"):
                    f.Clause = PgClause.OnConflict;
                    f.InConflict = true;
                    f.AfterConflictKeyword = true;
                    break;
                case "DO" when f.InConflict:
                    f.Clause = PgClause.OnConflictAction;
                    break;
                case "UPDATE" when f.InConflict && prev != null && prev.IsWordEqual("DO"):
                    f.Clause = PgClause.OnConflictAction;
                    break;
                case "WHEN" when f.Verb == "MERGE":
                    f.Clause = PgClause.MergeWhen;
                    break;
                case "THEN" when f.Verb == "MERGE":
                    f.Clause = PgClause.MergeAction;
                    break;
                case "UPDATE" when f.Verb == "MERGE" && f.Clause == PgClause.MergeAction:
                    break;
                case "INSERT" when f.Verb == "MERGE":
                    f.Clause = PgClause.MergeAction;
                    break;
                case "DELETE" when f.Verb == "MERGE":
                    f.Clause = PgClause.MergeAction;
                    break;
                case "TABLE" when f.Verb is "ALTER" or "DROP" or "TRUNCATE" or "LOCK" or "COMMENT" or "REINDEX" or "GRANT":
                    f.DdlObject = "TABLE";
                    f.Clause = PgClause.DdlRelation;
                    break;
                case "AS" when f.Verb == "CREATE" && f.DdlObject is "VIEW" or "TABLE":
                    f.Clause = PgClause.Other;
                    break;
                default:
                    Name(f, s, i, prev);
                    break;
            }
        }

        private void StartStatement(Frame f, PgSig s, int i)
        {
            var up = s.Upper;
            f.Verb = up;
            switch (up)
            {
                case "WITH":
                    f.Clause = PgClause.With;
                    f.WithIndex = i;
                    f.Verb = string.Empty;       // the verb is the statement after the CTE list
                    break;
                case "SELECT":
                    f.Clause = PgClause.SelectList;
                    break;
                case "VALUES":
                    f.Clause = PgClause.Values;
                    break;
                case "INSERT":
                case "DELETE":
                case "MERGE":
                    f.Clause = PgClause.Other;
                    break;
                case "UPDATE":
                    f.Clause = PgClause.UpdateTarget;
                    break;
                case "TABLE":
                    f.Clause = PgClause.DdlRelation;
                    break;
                case "TRUNCATE":
                case "VACUUM":
                case "ANALYZE":
                case "REINDEX":
                case "CLUSTER":
                case "LOCK":
                case "COPY":
                case "REFRESH":
                    f.Clause = PgClause.DdlRelation;
                    break;
                case "EXPLAIN":
                    f.Clause = PgClause.StatementStart;      // the statement being explained follows
                    f.Verb = string.Empty;
                    break;
                case "CREATE":
                case "ALTER":
                case "DROP":
                case "COMMENT":
                case "GRANT":
                case "REVOKE":
                    f.Clause = PgClause.Other;
                    break;
                default:
                    f.Clause = PgClause.Other;
                    break;
            }
        }

        private void Into(Frame f)
        {
            if (f.Verb == "INSERT") f.Clause = PgClause.InsertTarget;
            else if (f.Verb == "MERGE") f.Clause = PgClause.MergeTarget;
            else if (f.Verb == "SELECT") f.Clause = PgClause.DdlName;      // SELECT ... INTO new_table
            else f.Clause = PgClause.Other;
        }

        private void On(Frame f, int i)
        {
            if (f.Verb == "INSERT")
            {
                f.Clause = PgClause.OnConflict;
                f.InConflict = true;
                f.AfterConflictKeyword = false;
                return;
            }
            if (f.Verb == "CREATE" && f.DdlObject is "INDEX" or "TRIGGER" or "POLICY" or "RULE")
            {
                f.Clause = PgClause.DdlRelation;
                return;
            }
            if (f.Verb == "COMMENT")
            {
                f.Clause = PgClause.Other;
                return;
            }
            if (f.Verb is "GRANT" or "REVOKE")
            {
                f.Clause = PgClause.DdlRelation;
                return;
            }
            f.Clause = PgClause.JoinOn;
        }

        // A name or a word that is not a clause keyword: it may finish an item (a table, an alias, a column).
        private void Name(Frame f, PgSig s, int i, PgSig? prev)
        {
            var up = s.Upper;

            if (f.Verb == "CREATE" || f.Verb == "ALTER" || f.Verb == "DROP" || f.Verb == "COMMENT" || f.Verb == "GRANT" || f.Verb == "REVOKE" || f.Verb == "TRUNCATE")
            {
                Ddl(f, s, i, prev);
                return;
            }

            // join type words keep the JOIN pending
            if (up is "INNER" or "LEFT" or "RIGHT" or "FULL" or "CROSS" or "NATURAL" or "OUTER")
                f.Clause = f.Clause is PgClause.From or PgClause.Join or PgClause.JoinOn ? PgClause.From : f.Clause;
        }

        private void NonQueryWord(Frame f, PgSig s, int i, PgSig? prev)
        {
            var up = s.Upper;
            switch (up)
            {
                case "WHERE" when f.Kind is FrameKind.Group or FrameKind.Call:
                    f.Clause = PgClause.Where;
                    break;
                case "BY" when prev != null && prev.IsWord && prev.Upper is "ORDER" or "PARTITION" or "GROUP":
                    f.Clause = PgClause.OrderBy;
                    break;
                case "AS" when f.Kind == FrameKind.Call && string.Equals(f.CallName, "cast", StringComparison.Ordinal):
                    f.Clause = PgClause.TypePosition;
                    break;
                case "ORDER" when f.Kind == FrameKind.Call:
                    break;
                case "SELECT" when f.Kind == FrameKind.Group:
                    break;
            }
        }

        // ── DDL ─────────────────────────────────────────────────────────────────────────────────

        private void Ddl(Frame f, PgSig s, int i, PgSig? prev)
        {
            var up = s.Upper;

            if (f.Verb == "TRUNCATE")
            {
                if (f.Clause == PgClause.DdlRelation && !(up is "TABLE" or "ONLY")) f.Clause = PgClause.Other;
                return;
            }

            // RETURNS int |, CREATE DOMAIN d AS int | : the type has been written
            if (f.Clause == PgClause.TypePosition && !(up is "SETOF" or "TABLE"))
            {
                f.Clause = PgClause.Other;
                return;
            }

            if (f.DdlObject.Length == 0)
            {
                switch (up)
                {
                    case "TABLE" or "INDEX" or "VIEW" or "SEQUENCE" or "FUNCTION" or "PROCEDURE" or "SCHEMA" or "TYPE" or "DOMAIN"
                        or "TRIGGER" or "EXTENSION" or "DATABASE" or "ROLE" or "USER" or "POLICY" or "RULE" or "ENUM":
                        f.DdlObject = up;
                        f.Clause = f.Verb switch
                        {
                            "ALTER" or "DROP" or "TRUNCATE" or "COMMENT" => up is "TABLE" or "VIEW" or "SEQUENCE" or "INDEX" ? PgClause.DdlRelation : PgClause.Other,
                            _ => PgClause.DdlName,
                        };
                        if (f.Verb == "CREATE" && up == "INDEX") f.Clause = PgClause.DdlName;
                        break;
                    case "MATERIALIZED":
                        break;
                }
                return;
            }

            // inside "CREATE INDEX [name] ON table": remember the table for the column list
            if (f.Verb == "CREATE" && f.DdlObject == "INDEX" && f.Clause == PgClause.DdlRelation && s.IsName && !(up is "USING" or "ONLY" or "CONCURRENTLY"))
            {
                f.ListOwner = RelationAt(i);
                f.Clause = PgClause.Other;
                return;
            }

            if (f.Verb == "ALTER" && f.DdlObject == "TABLE" && f.Clause == PgClause.DdlRelation && !(up is "IF" or "EXISTS" or "ONLY"))
            {
                f.ListOwner = RelationAt(i);
                f.Clause = PgClause.Other;
                return;
            }

            if (f.Verb == "ALTER" && f.DdlObject == "TABLE" && f.ListOwner != null)
            {
                // ADD COLUMN name |  ->  type;  DROP/ALTER/RENAME COLUMN |  ->  existing column
                if (prev != null && prev.IsWordEqual("COLUMN") && i >= 2 && _t[i - 2].IsWord && _t[i - 2].Upper is "ADD")
                    f.Clause = PgClause.DdlColumnType;
                else if (prev != null && prev.IsWord && prev.Upper is "ADD" && !(up is "COLUMN" or "CONSTRAINT" or "PRIMARY" or "UNIQUE" or "FOREIGN" or "CHECK" or "IF"))
                    f.Clause = PgClause.DdlColumnType;
                return;
            }

            if (f.Verb == "DROP" && f.Clause == PgClause.DdlRelation && !(up is "IF" or "EXISTS" or "CONCURRENTLY"))
                f.Clause = PgClause.Other;
            else if (f.Verb == "TRUNCATE" && f.Clause == PgClause.DdlRelation && !(up is "TABLE" or "ONLY"))
                f.Clause = PgClause.Other;
            else if (f.Verb == "COMMENT" && f.Clause == PgClause.DdlRelation && !(up is "TABLE" or "VIEW" or "ON"))
                f.Clause = PgClause.Other;
            else if (f.Verb == "CREATE" && f.Clause == PgClause.DdlName && !(up is "IF" or "NOT" or "EXISTS" or "CONCURRENTLY"))
            {
                if (f.DdlObject is "TABLE" or "FUNCTION" or "PROCEDURE") f.Clause = PgClause.Other;
                else if (f.DdlObject == "INDEX" || f.DdlObject == "VIEW" || f.DdlObject == "SEQUENCE") f.Clause = PgClause.Other;
            }
            else if (up == "RETURNS")
                f.Clause = PgClause.TypePosition;
            else if (up == "AS" && f.DdlObject == "DOMAIN")
                f.Clause = PgClause.TypePosition;
        }

        // The table named at token index i (and its schema, when the name is qualified).
        private PgTableRef? RelationAt(int i)
        {
            string? schema = null;
            if (i >= 2 && _t[i - 1].IsDot && _t[i - 2].IsName) schema = _t[i - 2].Name.Folded;
            return new PgTableRef(string.Empty, schema, _t[i].Name.Folded, PgTableRefKind.Table, Array.Empty<string>(), Array.Empty<PgTableRef>());
        }

        private void DdlItem(Frame f, PgSig s, int i)
        {
            var up = s.Upper;
            if (f.ItemTokens == 1)
            {
                f.ItemFirst = up;
                if (up is "CONSTRAINT" or "PRIMARY" or "UNIQUE" or "CHECK" or "FOREIGN" or "LIKE" or "EXCLUDE")
                {
                    f.Clause = PgClause.Other;
                }
                else
                {
                    f.DeclaredColumns.Add(s.Name.Folded);
                    f.Clause = PgClause.DdlColumnType;
                }
                return;
            }

            f.Clause = up switch
            {
                "REFERENCES" => PgClause.DdlRelation,
                "DEFAULT" => PgClause.Values,       // a value expression follows
                _ => PgClause.Other,
            };
        }

        // One item of a function's parameter list: [mode] [name] type. After the name the type comes next.
        private void ParamItem(Frame f, PgSig s)
        {
            var up = s.Upper;
            var isMode = up is "IN" or "OUT" or "INOUT" or "VARIADIC";
            if (f.ItemTokens == 1)
            {
                f.ItemFirst = isMode ? up : string.Empty;
                f.Clause = isMode ? PgClause.Other : PgClause.DdlColumnType;
                return;
            }
            f.Clause = f.ItemTokens == 2 && f.ItemFirst.Length > 0 ? PgClause.DdlColumnType : PgClause.Other;
        }

        // ── The result ──────────────────────────────────────────────────────────────────────────

        private PgCursorContext Build()
        {
            var top = Top;
            var last = _before > 0 ? _t[_before - 1] : null;

            var chain = DotChain();
            var clause = top.Clause;

            // `::` and the type of a CAST or a column
            if (last != null && last.IsCast) clause = PgClause.TypePosition;
            else if (chain.Count == 0 && last != null && last.IsWordEqual("AS") && top.Kind == FrameKind.Call && top.CallName == "cast") clause = PgClause.TypePosition;

            var queryFrames = _stack.Where(fr => fr.IsQuery).Reverse().ToList();
            var inner = queryFrames.Count > 0 ? queryFrames[0] : _stack[0];

            var visible = new List<PgTableRef>();
            var ctes = new List<PgTableRef>();
            PgTableRef? target = null;
            PgTableRef? joinTarget = null;
            var joinLeft = new List<PgTableRef>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var innerCount = -1;
            foreach (var q in queryFrames)
            {
                if (innerCount < 0 && q != queryFrames[0]) innerCount = visible.Count;
                var start = q.QueryStart < 0 ? 0 : q.QueryStart;
                var end = q.Kind == FrameKind.Root ? _t.Count : Math.Min(_t[q.Open].Match, _t.Count);
                var items = PgTableRefExtractor.FromItems(_t, start, end);

                for (var n = 0; n < items.Count; n++)
                {
                    // INSERT INTO t ... SELECT ...: t is written to, not read from; providers use Target for it.
                    if (n == 0 && q.Verb == "INSERT") continue;
                    if (seen.Add(items[n].Ref.Qualifier + "|" + items[n].Ref.Schema)) visible.Add(items[n].Ref);
                }

                if (q == inner)
                {
                    if (q.Verb is "INSERT" or "UPDATE" or "DELETE" or "MERGE" && items.Count > 0) target = items[0].Ref;
                    if (clause is PgClause.JoinOn or PgClause.JoinUsing or PgClause.Join)
                    {
                        if (q.Verb == "MERGE" && items.Count >= 2) { joinLeft.Add(items[0].Ref); joinTarget = items[1].Ref; }
                        else if (q.LastJoin >= 0)
                        {
                            var joined = items.FirstOrDefault(it => it.IsJoin && it.Index == q.LastJoin + 1);
                            if (joined != null)
                            {
                                joinTarget = joined.Ref;
                                joinLeft.AddRange(items.Where(it => it.Index < joined.Index).Select(it => it.Ref));
                            }
                        }
                    }
                }

                // CTEs defined by this query's WITH that are complete before the caret (or recursive and containing it)
                var withIdx = q.WithIndex;
                if (withIdx >= 0)
                {
                    foreach (var (cte, bodyStart, bodyEnd, recursive) in PgTableRefExtractor.Ctes(_t, withIdx, end))
                    {
                        var complete = bodyEnd < _before && bodyEnd < _t.Count && _t[bodyEnd].IsClose;
                        var containsCaret = bodyStart <= _before && _before <= bodyEnd;
                        if (complete || (recursive && containsCaret)) ctes.Add(cte);
                    }
                }
            }

            if (innerCount < 0) innerCount = visible.Count;

            // Where the caret is in a function call
            Frame? call = null;
            for (var k = _stack.Count - 1; k >= 0; k--)
                if (_stack[k].Kind == FrameKind.Call) { call = _stack[k]; break; }

            // The relation a column list belongs to
            PgTableRef? listOwner = null;
            var declared = new List<string>();
            for (var k = _stack.Count - 1; k >= 0; k--)
            {
                var fr = _stack[k];
                if (fr.Kind is FrameKind.ColumnList or FrameKind.InsertColumns or FrameKind.ConflictTarget)
                {
                    listOwner = fr.ListOwner ?? target ?? inner.ListOwner;
                    declared.AddRange(fr.DeclaredColumns);
                    break;
                }
            }
            if (listOwner == null && clause == PgClause.DdlColumnList) listOwner = inner.ListOwner;
            if (clause is PgClause.DdlColumnType or PgClause.DdlRelation or PgClause.DdlColumnList or PgClause.Other && inner.ListOwner != null && top.Kind == FrameKind.Root)
                listOwner ??= inner.ListOwner;

            var preceding = new List<string>();
            for (var k = Math.Max(0, _before - 6); k < _before; k++)
            {
                var w = _t[k];
                preceding.Add(w.IsWord ? w.Upper : w.IsQuoted ? "\"" + w.Name.Text + "\"" : w.Text);
            }

            var afterOperand = false;
            if (last != null)
            {
                afterOperand = last.IsClose || last.IsLiteral || last.IsQuoted
                    || (last.IsWord && (CompletesOperand.Contains(last.Upper) || !ExpectsOperand.Contains(last.Upper)));
                // A star is "all columns" at the start of a list item, and a multiplication anywhere else.
                if (last.IsStar)
                {
                    var before = _before >= 2 ? _t[_before - 2] : null;
                    afterOperand = before != null && (before.IsComma || before.IsDot || before.IsOpen || before.IsWordEqual("SELECT") || before.IsWordEqual("DISTINCT"));
                }
                if (last.IsDot || last.IsCast || last.IsOpen || last.IsComma) afterOperand = false;
            }

            var verb = inner.Verb;
            if (verb.Length == 0 && inner.Clause != PgClause.StatementStart) verb = _stack[0].Verb;

            var excluded = inner.InConflict && clause is PgClause.Set or PgClause.Where or PgClause.Returning or PgClause.OnConflictAction;
            if (excluded && target != null && !visible.Contains(target)) visible.Insert(0, target);

            return new PgCursorContext
            {
                Caret = _caret,
                Prefix = _partial,
                PrefixStart = _partialStart,
                PrefixQuoted = _partialQuoted,
                DotChain = chain,
                Clause = clause,
                Verb = verb,
                AfterOperand = afterOperand,
                PrecedingWords = preceding,
                CallName = call?.CallName,
                CallSchema = call?.CallSchema,
                CallArgument = call?.Arg ?? 0,
                VisibleTables = visible,
                VisibleCtes = ctes,
                Target = target,
                JoinTarget = joinTarget,
                JoinLeft = joinLeft,
                ListOwner = listOwner,
                DeclaredColumns = declared.Count > 0 ? declared : (inner.DeclaredColumns.Count > 0 ? inner.DeclaredColumns : _stack.SelectMany(fr => fr.DeclaredColumns).ToList()),
                ExcludedAvailable = excluded,
                DdlObject = inner.DdlObject,
                InTableBody = top.Kind == FrameKind.DdlBody,
                InWindowSpec = top.IsWindowSpec,
                InnerTableCount = innerCount,
            };
        }

        // `a.b.` before the caret: the names separated by dots that end with a dot, adjacent to the partial word.
        private List<PgName> DotChain()
        {
            var chain = new List<PgName>();
            var i = _before - 1;
            if (i < 0 || !_t[i].IsDot) return chain;
            i--;
            while (i >= 0 && _t[i].IsName)
            {
                chain.Insert(0, _t[i].Name);
                if (i >= 1 && _t[i - 1].IsDot) i -= 2;
                else break;
            }
            return chain;
        }
    }
}
