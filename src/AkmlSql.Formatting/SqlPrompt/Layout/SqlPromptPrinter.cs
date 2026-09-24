using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.SqlPrompt.Layout;

/// <summary>
/// Lays a parsed script out the way a SQL Prompt style describes. Each printer handles one
/// construct and reads its layout straight from the style's options; everything goes through
/// <see cref="SqlWriter"/>, which owns the tokens, so a printer only ever decides whitespace.
/// Constructs no printer models are emitted with default spacing (short ones) or exactly as
/// written (multi-line ones), never dropped.
/// </summary>
internal sealed partial class SqlPromptPrinter
{
    private readonly SqlWriter W;
    private readonly SqlPromptStyle S;
    private readonly IList<TSqlParserToken> T;
    private readonly Func<int, bool> _isNoFormat;

    public SqlPromptPrinter(SqlWriter writer, SqlPromptStyle style, Func<int, bool> isNoFormatToken)
    {
        W = writer;
        S = style;
        T = writer.Tokens;
        _isNoFormat = isNoFormatToken;
    }

    private int Tab => W.TabSize;
    private int Max => W.MaxLineLength;

    // ── script and batches ───────────────────────────────────────────────────

    public void Script(TSqlScript script)
    {
        var previousLast = -1;
        var afterGo = false;
        foreach (var batch in script.Batches)
        {
            // Batch separators (GO) between batches: each on its own line at column 0.
            if (previousLast >= 0)
            {
                afterGo = EmitBatchSeparators(previousLast, batch.FirstTokenIndex);
            }

            foreach (var statement in batch.Statements)
            {
                if (previousLast >= 0)
                {
                    var blank = afterGo
                        ? BlankLines(S.Whitespace.PreserveEmptyLinesAfterBatchSeparator, S.Whitespace.EmptyLinesAfterBatchSeparator, W.OriginalBlankLinesAfter(LastSignificantBefore(statement.FirstTokenIndex)))
                        : BlankLines(S.Whitespace.PreserveEmptyLinesBetweenStatements, S.Whitespace.EmptyLinesBetweenStatements, W.OriginalBlankLinesAfter(previousLast));
                    W.NewLine(0, blank);
                }
                else
                {
                    W.NewLine(0);
                }
                afterGo = false;
                Statement(statement);
                previousLast = statement.LastTokenIndex;
            }

            if (batch.Statements.Count == 0) previousLast = Math.Max(previousLast, batch.LastTokenIndex);
        }

        // Trailing GO / comments.
        if (previousLast >= 0) EmitBatchSeparators(previousLast, T.Count);
        W.NewLine(0);
        W.Through(T.Count - 1);
    }

    /// <summary>Emits the GO tokens between two batches; returns true when there was one.</summary>
    private bool EmitBatchSeparators(int afterToken, int beforeToken)
    {
        var any = false;
        for (var i = afterToken + 1; i < beforeToken && i < T.Count; i++)
        {
            if (T[i].TokenType != TSqlTokenType.Go) continue;
            W.NewLine(0);
            W.Through(i);
            // "GO 5": the count belongs to the separator line
            var j = NextSignificant(i + 1);
            if (j >= 0 && j < beforeToken && T[j].TokenType == TSqlTokenType.Integer && !W.OriginalNewLineBetween(i, j))
            {
                W.Space();
                W.Through(j);
                i = j;
            }
            any = true;
        }
        return any;
    }

    private static int BlankLines(bool preserve, int configured, int original) =>
        preserve ? Math.Max(configured, original) : configured;

    // ── statements ───────────────────────────────────────────────────────────

    /// <summary>Prints one statement at the current position (the caller has placed its first token).</summary>
    private void Statement(TSqlStatement s)
    {
        if (IntersectsNoFormat(s))
        {
            _blockColumn = null;
            W.Verbatim(s.FirstTokenIndex, s.LastTokenIndex);
            return;
        }

        // A block or ELSE IF that does not start its own line belongs to its owner's column.
        var column = _blockColumn ?? W.Column;
        _blockColumn = null;
        var semicolon = T[s.LastTokenIndex].TokenType == TSqlTokenType.Semicolon ? s.LastTokenIndex : -1;
        var bodyLast = semicolon >= 0 ? semicolon - 1 : s.LastTokenIndex;

        if (!TryCollapseStatement(s, bodyLast, column))
            StatementBody(s, column);

        W.Through(bodyLast);
        if (semicolon >= 0) Semicolon(semicolon, column);
    }

    private void Semicolon(int index, int statementColumn)
    {
        if (index < W.NextToken) return;
        W.Until(index);
        switch (S.Whitespace.BeforeSemicolon)
        {
            case "spaceBefore": W.Space(); break;
            case "newLineBefore": W.NewLine(statementColumn); break;
            default: W.NoSpace(); break;
        }
        W.Through(index);
    }

    /// <summary>The DML / DDL / control-flow "collapse short statements" options.</summary>
    private bool TryCollapseStatement(TSqlStatement s, int bodyLast, int column)
    {
        var threshold = s switch
        {
            SelectStatement or InsertStatement or UpdateStatement or DeleteStatement or MergeStatement
                when S.Dml.CollapseShortStatements => S.Dml.CollapseStatementsShorterThan,
            CreateTableStatement or CreateViewStatement or AlterViewStatement or CreateOrAlterViewStatement
                or CreateIndexStatement or AlterTableStatement or ProcedureStatementBody or FunctionStatementBody
                when S.Ddl.CollapseShortStatements => S.Ddl.CollapseStatementsShorterThan,
            IfStatement or WhileStatement when S.ControlFlow.CollapseShortStatements => S.ControlFlow.CollapseStatementsShorterThan,
            _ => 0,
        };
        if (threshold <= 0) return false;
        var width = W.MeasureFlat(() => { StatementBody(s, column); W.Through(bodyLast); });
        if (width >= threshold || W.Column + width > Max) return false;
        W.Flat(() => { StatementBody(s, column); W.Through(bodyLast); });
        return true;
    }

    private void StatementBody(TSqlStatement s, int column)
    {
        switch (s)
        {
            case SelectStatement x: PrintSelect(x, column); break;
            case InsertStatement x: PrintInsert(x, column); break;
            case UpdateStatement x: PrintUpdate(x, column); break;
            case DeleteStatement x: PrintDelete(x, column); break;
            case MergeStatement x: PrintMerge(x, column); break;
            case DeclareVariableStatement x: DeclareVariables(x, column); break;
            case DeclareTableVariableStatement x: DeclareTableVariable(x, column); break;
            case SetVariableStatement x: SetVariable(x, column); break;
            case IfStatement x: If(x, column); break;
            case WhileStatement x: While(x, column); break;
            case BeginEndBlockStatement x: BeginEnd(x, column); break;
            case TryCatchStatement x: TryCatch(x, column); break;
            case ReturnStatement x: Return(x); break;
            case CreateTableStatement x: CreateTable(x, column); break;
            case AlterTableAddTableElementStatement x: AlterTableAdd(x, column); break;
            case ProcedureStatementBody x: Procedure(x, column); break;
            case FunctionStatementBody x: Function(x, column); break;
            case ViewStatementBody x: View(x, column); break;
            case TriggerStatementBody x: Trigger(x, column); break;
            case CreateIndexStatement x: CreateIndex(x, column); break;
            case ExecuteStatement x: Execute(x, column); break;
            case PrintStatement x: Leading(x, x.Expression); break;
            case ThrowStatement or RaiseErrorStatement: GenericStatement(s); break;
            default: GenericStatement(s); break;
        }
    }

    /// <summary>A statement no printer models: default spacing when written on one line, as written otherwise.</summary>
    private void GenericStatement(TSqlStatement s)
    {
        var last = T[s.LastTokenIndex].TokenType == TSqlTokenType.Semicolon ? LastSignificantBefore(s.LastTokenIndex) : s.LastTokenIndex;
        if (SpansLines(s.FirstTokenIndex, last))
            W.Verbatim(s.FirstTokenIndex, last);
        else
            W.Through(last);
    }

    /// <summary>"KEYWORD expression" statements (PRINT, RETURN…): the keyword, then the expression laid out.</summary>
    private void Leading(TSqlStatement s, ScalarExpression? e)
    {
        if (e is null) { GenericStatement(s); return; }
        W.Until(e.FirstTokenIndex);
        W.Space();
        Scalar(e);
    }

    private void Return(ReturnStatement r) => Leading(r, r.Expression);

    /// <summary>Statements of a block, each on its own line at <paramref name="column"/>.</summary>
    private void Statements(IList<TSqlStatement> statements, int column)
    {
        int? previous = null;
        foreach (var st in statements)
        {
            var blank = previous is null
                ? 0
                : BlankLines(S.Whitespace.PreserveEmptyLinesBetweenStatements, S.Whitespace.EmptyLinesBetweenStatements, W.OriginalBlankLinesAfter(previous.Value));
            W.NewLine(column, blank);
            Statement(st);
            previous = st.LastTokenIndex;
        }
    }

    // ── token helpers ────────────────────────────────────────────────────────

    private int NextSignificant(int from)
    {
        for (var i = from; i < T.Count; i++)
            if (T[i].TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment))
                return i;
        return -1;
    }

    private int LastSignificantBefore(int index)
    {
        for (var i = index - 1; i >= 0; i--)
            if (T[i].TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment))
                return i;
        return -1;
    }

    /// <summary>First token of <paramref name="type"/> in [from, to], or -1.</summary>
    private int Find(int from, int to, TSqlTokenType type)
    {
        for (var i = Math.Max(0, from); i <= to && i < T.Count; i++)
            if (T[i].TokenType == type) return i;
        return -1;
    }

    /// <summary>Last token of <paramref name="type"/> in [from, to], or -1.</summary>
    private int FindLast(int from, int to, TSqlTokenType type)
    {
        for (var i = Math.Min(to, T.Count - 1); i >= from && i >= 0; i--)
            if (T[i].TokenType == type) return i;
        return -1;
    }

    /// <summary>First token in [from, to] whose text is <paramref name="word"/> (contextual keywords lex as identifiers).</summary>
    private int FindWord(int from, int to, string word)
    {
        for (var i = Math.Max(0, from); i <= to && i < T.Count; i++)
            if (T[i].TokenType is not (TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment)
                && string.Equals(T[i].Text, word, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    /// <summary>The ")" matching the "(" at <paramref name="open"/>.</summary>
    private int MatchingParen(int open)
    {
        var depth = 0;
        for (var i = open; i < T.Count; i++)
        {
            if (T[i].TokenType == TSqlTokenType.LeftParenthesis) depth++;
            else if (T[i].TokenType == TSqlTokenType.RightParenthesis && --depth == 0) return i;
        }
        return -1;
    }

    /// <summary>Printed width of the significant tokens in [from, to] joined by single spaces (keyword phrases).</summary>
    private int PhraseWidth(int from, int to)
    {
        var width = 0;
        var first = true;
        for (var i = from; i <= to; i++)
        {
            var t = T[i];
            if (t.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment) continue;
            width += t.Text.Length + (first ? 0 : 1);
            first = false;
        }
        return width;
    }

    private bool SpansLines(int from, int to)
    {
        for (var i = from; i <= to && i < T.Count; i++)
        {
            var t = T[i];
            if (t.TokenType == TSqlTokenType.WhiteSpace && t.Text.Contains('\n')) return true;
            if (t.TokenType == TSqlTokenType.SingleLineComment && i < to) return true;
        }
        return false;
    }

    private bool IntersectsNoFormat(TSqlFragment f)
    {
        for (var i = f.FirstTokenIndex; i <= f.LastTokenIndex; i++)
            if (_isNoFormat(i)) return true;
        return false;
    }

    private bool IsWord(int index, string word) =>
        index >= 0 && index < T.Count && string.Equals(T[index].Text, word, StringComparison.OrdinalIgnoreCase);

    /// <summary>Rounds a column up to the next tab stop when "align items to tab stops" is on.</summary>
    private int TabStop(int column) =>
        S.Lists.AlignItemsToTabStops ? (column + Tab - 1) / Tab * Tab : column;
}
