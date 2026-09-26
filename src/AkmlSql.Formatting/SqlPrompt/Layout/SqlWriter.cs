using System.Text;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.SqlPrompt.Layout;

/// <summary>
/// Emits the script's tokens, in their original order and each exactly once, deciding only the
/// whitespace in front of each one. That invariant is what keeps SQL Prompt-style layout safe:
/// whatever the printers decide, the token sequence — and so the meaning — cannot change.
/// <para>
/// Printers ask for the whitespace before the NEXT token (<see cref="NewLine"/>, <see cref="Space"/>,
/// <see cref="NoSpace"/>) and then emit through a token index. Tokens the printers skip over
/// (keywords they don't model, comments) are emitted on the way with default spacing, so a printer
/// can never lose one. Comments keep their place: an end-of-line comment stays at the end of its
/// line, an own-line comment stays on its own line.
/// </para>
/// </summary>
internal sealed class SqlWriter
{
    private readonly IList<TSqlParserToken> _tokens;
    private readonly string?[] _text;
    private readonly SqlPromptStyle _style;
    private readonly int _tabSize;
    private readonly StringBuilder _sb = new();

    private int _next;               // next token index to emit
    private int _last = -1;          // last significant token emitted
    private int _column;             // current output column (tabs expanded)
    private int _lineStart;          // column where the current line's first token sits
    private int _line;               // current output line (0-based)
    private int _flatDepth;          // > 0: new lines become spaces (measurement / collapse)
    private bool _lineCommentOpen;   // output ends inside a '--' comment: the next token needs a new line
    private bool _sawLineComment;    // a '--' comment was emitted since measurement began
    private int _firstColumn = -1;   // column of the first token emitted since measurement began
    private int _firstLine = -1;     // output line of that token

    private enum Pending { Default, NoSpace, Space, NewLine }
    private Pending _pending = Pending.NoSpace;
    private int _pendingColumn;
    private int _pendingBlankLines;
    private int _pendingSpaces = 1;

    /// <summary>Open comment-alignment groups, innermost last (lists nest: a function's arguments inside a SELECT list).</summary>
    private readonly List<List<(int SbIndex, int Column, int Line)>?> _commentGroups = [];

    private List<(int SbIndex, int Column, int Line)>? CommentGroup => _commentGroups.Count > 0 ? _commentGroups[^1] : null;

    /// <summary>Comments already emitted ahead of their place (see <see cref="HoistTrailingComments"/>).</summary>
    private readonly List<int> _hoisted = [];

    public SqlWriter(IList<TSqlParserToken> tokens, string?[] casedText, SqlPromptStyle style)
    {
        _tokens = tokens;
        _text = casedText;
        _style = style;
        _tabSize = Math.Max(1, style.Whitespace.TabSize);
    }

    public int TabSize => _tabSize;
    public int MaxLineLength => _style.Whitespace.MaxLineLength;
    public bool IsFlat => _flatDepth > 0;
    public IList<TSqlParserToken> Tokens => _tokens;

    /// <summary>Column the next token starts at, given the whitespace requested so far.</summary>
    public int Column => _pending switch
    {
        Pending.NewLine => RoundColumn(_pendingColumn),
        Pending.Space => _column + _pendingSpaces,
        Pending.Default => _sb.Length == 0 ? 0 : _column + 1,
        _ => _column,
    };

    /// <summary>Column the current (or pending) output line starts at: its indentation.</summary>
    public int LineStart => _pending == Pending.NewLine ? RoundColumn(_pendingColumn) : _lineStart;

    /// <summary>Output line the next token lands on.</summary>
    public int Line => _pending == Pending.NewLine && _sb.Length > 0 ? _line + 1 + _pendingBlankLines : _line;

    /// <summary>Index of the next token to be emitted.</summary>
    public int NextToken => _next;

    public bool HasPendingNewLine => _pending == Pending.NewLine;

    // ── whitespace requests ──────────────────────────────────────────────────

    /// <summary>Start the next token on a new line at <paramref name="column"/>.</summary>
    public void NewLine(int column, int blankLines = 0)
    {
        if (IsFlat)
        {
            Space();
            return;
        }
        blankLines = Math.Max(0, blankLines);
        if (_pending == Pending.NewLine)
        {
            _pendingBlankLines = Math.Max(_pendingBlankLines, blankLines);
            _pendingColumn = Math.Max(0, column);
            return;
        }
        _pending = Pending.NewLine;
        _pendingColumn = Math.Max(0, column);
        _pendingBlankLines = blankLines;
    }

    /// <summary>Separate the next token by exactly <paramref name="count"/> spaces (a pending new line wins).</summary>
    public void Space(int count = 1)
    {
        if (_pending == Pending.NewLine) return;
        _pending = Pending.Space;
        _pendingSpaces = Math.Max(1, count);
    }

    /// <summary>Put the next token directly against the previous one, unless that would fuse them.</summary>
    public void NoSpace()
    {
        if (_pending == Pending.NewLine) return;
        _pending = Pending.NoSpace;
    }

    /// <summary>One space, or none — per a style option.</summary>
    public void SpaceIf(bool space)
    {
        if (space) Space(); else NoSpace();
    }

    /// <summary>Pad so the next token starts at <paramref name="column"/> (at least one space).</summary>
    public void PadTo(int column)
    {
        if (_pending == Pending.NewLine) { _pendingColumn = column; return; }
        if (IsFlat) { Space(); return; }
        var gap = column - _column;
        if (gap >= 1) { _pending = Pending.Space; _pendingSpaces = gap; }
        else Space();
    }

    /// <summary>Let the default spacing rule decide (one space between words, none around dots…).</summary>
    public void DefaultSpace()
    {
        if (_pending == Pending.NewLine) return;
        _pending = Pending.Default;
    }

    // ── emission ─────────────────────────────────────────────────────────────

    /// <summary>Emit every token up to and including <paramref name="index"/>.</summary>
    public void Through(int index)
    {
        while (_next <= index && _next < _tokens.Count)
        {
            var i = _next;
            var t = _tokens[i];
            switch (t.TokenType)
            {
                case TSqlTokenType.WhiteSpace:
                case TSqlTokenType.EndOfFile:
                    break;
                case TSqlTokenType.SingleLineComment:
                case TSqlTokenType.MultilineComment:
                    if (!_hoisted.Contains(i)) EmitComment(i);
                    break;
                default:
                    EmitToken(i);
                    break;
            }
            _next++;
        }
    }

    /// <summary>Emit every token before <paramref name="index"/>.</summary>
    public void Until(int index) => Through(index - 1);

    /// <summary>
    /// Emit tokens <paramref name="first"/>..<paramref name="last"/> with their original whitespace
    /// (formatting-off regions, constructs kept as written). Only the whitespace before the first
    /// token follows the pending request.
    /// </summary>
    public void Verbatim(int first, int last)
    {
        Until(first);
        var start = first;
        while (start <= last && _tokens[start].TokenType == TSqlTokenType.WhiteSpace) start++;
        while (last > start && _tokens[last].TokenType == TSqlTokenType.WhiteSpace) last--;
        if (start > last) { _next = Math.Max(_next, last + 1); return; }

        ApplyPending(_text[start] ?? _tokens[start].Text);
        if (_firstColumn == -1) { _firstColumn = _column; _firstLine = _line; }
        for (var i = start; i <= last; i++)
        {
            var t = _tokens[i];
            var isTrivia = t.TokenType is TSqlTokenType.WhiteSpace or TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment;
            var text = isTrivia ? t.Text : (_text[i] ?? t.Text);
            if (t.TokenType == TSqlTokenType.SingleLineComment || text.Contains('\n')) _sawLineComment = true;
            if (t.TokenType == TSqlTokenType.WhiteSpace && text.Contains('\n'))
            {
                // keep the author's line breaks, drop the trailing blanks before each one
                TrimTrailingSpaces();
                var nl = text.LastIndexOf('\n');
                _sb.Append('\n', text.Count(c => c == '\n'));
                _line += text.Count(c => c == '\n');
                _column = 0;
                Append(text[(nl + 1)..]);
                _lineStart = _column;
                continue;
            }
            Append(text);
            if (!isTrivia) _last = i;
        }
        _lineCommentOpen = _tokens[last].TokenType == TSqlTokenType.SingleLineComment;
        _next = last + 1;
        _pending = Pending.Default;
    }

    private void EmitToken(int i)
    {
        var text = _text[i] ?? _tokens[i].Text;
        if (_lineCommentOpen && _pending != Pending.NewLine)
        {
            // Nothing may follow a '--' comment on its line.
            _pending = Pending.NewLine;
            _pendingColumn = _lineStart;
            _pendingBlankLines = 0;
        }
        ApplyPending(text);
        if (_firstColumn == -1) { _firstColumn = _column; _firstLine = _line; }
        Append(text);
        _last = i;
        _lineCommentOpen = false;
        _pending = Pending.Default;
    }

    private void ApplyPending(string nextText)
    {
        switch (_pending)
        {
            case Pending.NewLine:
                if (_sb.Length > 0)
                {
                    TrimTrailingSpaces();
                    _sb.Append('\n', 1 + _pendingBlankLines);
                    _line += 1 + _pendingBlankLines;
                    _column = 0;
                }
                AppendIndent(_pendingColumn);
                _lineStart = _column;
                break;
            case Pending.Space:
                if (_sb.Length > 0) AppendSpaces(_pendingSpaces);
                break;
            case Pending.Default:
                if (_sb.Length > 0 && NeedsDefaultSpace(nextText)) AppendSpaces(1);
                break;
            case Pending.NoSpace:
                if (_sb.Length > 0 && WouldFuse(nextText)) AppendSpaces(1);
                break;
        }
    }

    // ── comments ─────────────────────────────────────────────────────────────

    private void EmitComment(int i)
    {
        var t = _tokens[i];
        var isLine = t.TokenType == TSqlTokenType.SingleLineComment;
        var text = isLine ? t.Text.TrimEnd() : t.Text;
        if (isLine || text.Contains('\n')) _sawLineComment = true;

        if (IsFlat)
        {
            // Kept inside a collapsed run; a '--' still ends the line.
            if (_sb.Length > 0 && _column > 0) AppendSpaces(1);
            Append(text);
            if (isLine) _lineCommentOpen = true;
            return;
        }

        var trailing = _last >= 0 && _sb.Length > 0 && !NewLineBetween(_last, i);
        if (trailing)
        {
            // End-of-line comment: stays on the line of the code it follows.
            TrimTrailingSpaces();
            AppendSpaces(1);
            CommentGroup?.Add((_sb.Length, _column, _line));
            Append(text);
            if (isLine) _lineCommentOpen = true;
            return;
        }

        // Own-line comment: its own line, at the column the next token is headed for.
        var column = _pending == Pending.NewLine ? _pendingColumn : _lineStart;
        var blank = _pending == Pending.NewLine ? _pendingBlankLines : 0;
        if (_sb.Length > 0)
        {
            TrimTrailingSpaces();
            _sb.Append('\n', 1 + blank);
            _line += 1 + blank;
            _column = 0;
        }
        AppendIndent(column);
        _lineStart = _column;
        Append(isLine ? text : ReindentBlockComment(text, OriginalColumn(i), _column));

        // The code after it starts a new line at the same column, keeping at most one blank line
        // the author left between the comment and the code.
        _pending = Pending.NewLine;
        _pendingColumn = column;
        _pendingBlankLines = Math.Min(OriginalBlankLinesAfter(i), 1);
        _lineCommentOpen = false;
    }

    private string ReindentBlockComment(string text, int originalColumn, int newColumn)
    {
        if (!_style.Whitespace.AlignMultilineComments || !text.Contains('\n')) return text;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var body = lines.Skip(1).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        // Banner comments (rows of * = - #) are drawn by hand: leave them alone.
        var decorated = body.Count(l => l.All(c => c is '*' or '=' or '-' or '#' or '/' or ' '));
        if (body.Count > 0 && decorated * 2 > body.Count) return text;

        var delta = newColumn - originalColumn;
        var sb = new StringBuilder(lines[0]);
        for (var k = 1; k < lines.Length; k++)
        {
            sb.Append('\n');
            var line = lines[k];
            var trimmed = line.TrimStart(' ', '\t');
            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith('*'))
            {
                // " * text" and " */": the star sits one column right of the "/*".
                sb.Append(IndentString(newColumn + 1)).Append(trimmed);
                continue;
            }
            var lead = ExpandedWidth(line[..(line.Length - trimmed.Length)]);
            sb.Append(IndentString(Math.Max(0, lead + delta))).Append(trimmed);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Emits, now, the end-of-line comments that follow token <paramref name="token"/> on its
    /// source line — before the token itself. Used when a comma moves to the start of the next
    /// line: "a, -- note" must become "a -- note" / ", b", not ", -- note" / "b".
    /// </summary>
    public void HoistTrailingComments(int token)
    {
        if (IsFlat || _sb.Length == 0) return;
        for (var k = token + 1; k < _tokens.Count; k++)
        {
            var t = _tokens[k];
            if (t.TokenType == TSqlTokenType.WhiteSpace)
            {
                if (t.Text.Contains('\n')) return;
                continue;
            }
            if (t.TokenType is not (TSqlTokenType.SingleLineComment or TSqlTokenType.MultilineComment) || t.Text.Contains('\n')) return;
            TrimTrailingSpaces();
            AppendSpaces(1);
            CommentGroup?.Add((_sb.Length, _column, _line));
            Append(t.TokenType == TSqlTokenType.SingleLineComment ? t.Text.TrimEnd() : t.Text);
            if (t.TokenType == TSqlTokenType.SingleLineComment)
            {
                _lineCommentOpen = true;
                _sawLineComment = true;
            }
            _hoisted.Add(k);
        }
    }

    /// <summary>Start collecting end-of-line comments so <see cref="EndCommentGroup"/> can align them.</summary>
    public void BeginCommentGroup()
    {
        _commentGroups.Add(IsFlat ? null : []);
    }

    /// <summary>Align the end-of-line comments collected since <see cref="BeginCommentGroup"/> to one column.</summary>
    public void EndCommentGroup(bool align)
    {
        if (_commentGroups.Count == 0) return;
        var group = _commentGroups[^1];
        _commentGroups.RemoveAt(_commentGroups.Count - 1);
        if (!align || group is null || group.Count < 2) return;
        var target = group.Max(c => c.Column);
        foreach (var c in group.OrderByDescending(c => c.SbIndex))
        {
            if (c.Column < target) _sb.Insert(c.SbIndex - 1, new string(' ', target - c.Column));
        }
        var onCurrentLine = group.Where(c => c.Line == _line).ToList();
        if (onCurrentLine.Count == 1) _column += target - onCurrentLine[0].Column;
    }

    // ── trials and measurement ───────────────────────────────────────────────

    private readonly record struct State(
        int SbLength, int Next, int Last, int Column, int LineStart, int Line, bool LineCommentOpen,
        Pending Pending, int PendingColumn, int PendingBlankLines, int PendingSpaces, bool SawLineComment, int FirstColumn, int FirstLine,
        int CommentGroupCount, int HoistedCount);

    private State Mark() => new(_sb.Length, _next, _last, _column, _lineStart, _line, _lineCommentOpen,
        _pending, _pendingColumn, _pendingBlankLines, _pendingSpaces, _sawLineComment, _firstColumn, _firstLine,
        CommentGroup?.Count ?? -1, _hoisted.Count);

    private void Restore(State s)
    {
        _sb.Length = s.SbLength;
        _next = s.Next;
        _last = s.Last;
        _column = s.Column;
        _lineStart = s.LineStart;
        _line = s.Line;
        _lineCommentOpen = s.LineCommentOpen;
        _pending = s.Pending;
        _pendingColumn = s.PendingColumn;
        _pendingBlankLines = s.PendingBlankLines;
        _pendingSpaces = s.PendingSpaces;
        _sawLineComment = s.SawLineComment;
        _firstColumn = s.FirstColumn;
        _firstLine = s.FirstLine;
        if (_hoisted.Count > s.HoistedCount) _hoisted.RemoveRange(s.HoistedCount, _hoisted.Count - s.HoistedCount);
        if (CommentGroup is { } group && s.CommentGroupCount >= 0 && group.Count > s.CommentGroupCount)
            group.RemoveRange(s.CommentGroupCount, group.Count - s.CommentGroupCount);
    }

    /// <summary>
    /// Runs <paramref name="print"/> and keeps the result when it stayed on the current line and
    /// ended within <paramref name="maxColumn"/> (default: the wrap width); otherwise undoes it.
    /// </summary>
    public bool TryOneLine(Action print, int? maxColumn = null)
    {
        var mark = Mark();
        _sawLineComment = false;
        _firstColumn = -1;
        _firstLine = -1;
        print();
        var kept = (_firstLine == -1 || _line == _firstLine) && !_sawLineComment && !_lineCommentOpen && _column <= (maxColumn ?? MaxLineLength);
        if (kept)
        {
            _sawLineComment = mark.SawLineComment;
            if (mark.FirstColumn != -1)
            {
                _firstColumn = mark.FirstColumn;
                _firstLine = mark.FirstLine;
            }
            return true;
        }
        Restore(mark);
        return false;
    }

    /// <summary>
    /// Runs <paramref name="print"/>, then undoes it; returns true when it produced more than one line.
    /// </summary>
    public bool WouldBreak(Action print)
    {
        var mark = Mark();
        _sawLineComment = false;
        _firstColumn = -1;
        _firstLine = -1;
        print();
        var broke = (_firstLine != -1 && _line != _firstLine) || _sawLineComment || _lineCommentOpen;
        Restore(mark);
        return broke;
    }

    /// <summary>
    /// Width <paramref name="print"/> takes on one line when it starts at token
    /// <paramref name="fromToken"/>, or <see cref="Unbounded"/> when it cannot be written on one
    /// line (a '--' comment inside). Nothing is kept.
    /// </summary>
    public int MeasureFlat(Action print, int fromToken)
    {
        var mark = Mark();
        _next = fromToken;
        _lineCommentOpen = false;
        return MeasureFrom(mark, print);
    }

    /// <summary>
    /// Width <paramref name="print"/> takes on one line from the next token, or
    /// <see cref="Unbounded"/> when it cannot be written on one line. Nothing is kept.
    /// </summary>
    public int MeasureFlat(Action print) => MeasureFrom(Mark(), print);

    private int MeasureFrom(State mark, Action print)
    {
        _sawLineComment = false;
        _pending = Pending.NoSpace;
        _firstColumn = -1;
        _firstLine = -1;
        var lineBefore = _line;
        _flatDepth++;
        try
        {
            print();
        }
        finally
        {
            _flatDepth--;
        }
        var width = _sawLineComment || _line != lineBefore ? Unbounded : _firstColumn < 0 ? 0 : _column - _firstColumn;
        Restore(mark);
        return width;
    }

    /// <summary>Prints what the printers would otherwise break on one line (collapse).</summary>
    public void Flat(Action print)
    {
        _flatDepth++;
        try { print(); }
        finally { _flatDepth--; }
    }

    public const int Unbounded = int.MaxValue / 4;

    // ── output ───────────────────────────────────────────────────────────────

    public string ToText()
    {
        TrimTrailingSpaces();
        return _sb.ToString();
    }

    private void Append(string text)
    {
        _sb.Append(text);
        var nl = text.LastIndexOf('\n');
        if (nl < 0)
        {
            _column = AdvanceColumn(_column, text);
            return;
        }
        _line += text.Count(c => c == '\n');
        _column = ExpandedWidth(text[(nl + 1)..]);
    }

    private void AppendSpaces(int n)
    {
        _sb.Append(' ', n);
        _column += n;
    }

    private void AppendIndent(int column)
    {
        column = RoundColumn(column);
        _sb.Append(IndentString(column));
        _column = column;
    }

    private string IndentString(int column) => _style.Whitespace.SpacesOrTabs switch
    {
        "tabs" => new string('\t', (column + _tabSize - 1) / _tabSize),
        "tabsIfPossible" => new string('\t', column / _tabSize) + new string(' ', column % _tabSize),
        _ => new string(' ', column),
    };

    /// <summary>Tab-only indentation cannot stop between tab stops, so the column moves to the next one.</summary>
    private int RoundColumn(int column) =>
        _style.Whitespace.SpacesOrTabs == "tabs" ? (column + _tabSize - 1) / _tabSize * _tabSize : column;

    private int ExpandedWidth(string text) => AdvanceColumn(0, text);

    private int AdvanceColumn(int column, string text)
    {
        foreach (var c in text) column = c == '\t' ? (column / _tabSize + 1) * _tabSize : column + 1;
        return column;
    }

    private void TrimTrailingSpaces()
    {
        var end = _sb.Length;
        while (end > 0 && _sb[end - 1] is ' ' or '\t') end--;
        if (end == _sb.Length) return;
        _sb.Length = end;
        var lineStartIndex = end;
        while (lineStartIndex > 0 && _sb[lineStartIndex - 1] != '\n') lineStartIndex--;
        _column = ExpandedWidth(_sb.ToString(lineStartIndex, end - lineStartIndex));
    }

    private bool NewLineBetween(int from, int to)
    {
        for (var k = from + 1; k < to; k++)
            if (_tokens[k].TokenType == TSqlTokenType.WhiteSpace && _tokens[k].Text.Contains('\n')) return true;
        return false;
    }

    /// <summary>Blank lines the author left right after token <paramref name="token"/>.</summary>
    public int OriginalBlankLinesAfter(int token)
    {
        var newlines = 0;
        for (var k = token + 1; k < _tokens.Count && _tokens[k].TokenType == TSqlTokenType.WhiteSpace; k++)
            newlines += _tokens[k].Text.Count(c => c == '\n');
        return Math.Max(0, newlines - 1);
    }

    /// <summary>True when the author wrote a line break between the two tokens.</summary>
    public bool OriginalNewLineBetween(int from, int to) => NewLineBetween(from, to);

    private int OriginalColumn(int tokenIndex)
    {
        var col = 0;
        var parts = new List<string>();
        for (var k = tokenIndex - 1; k >= 0; k--)
        {
            var text = _tokens[k].Text;
            var nl = text.LastIndexOf('\n');
            if (nl >= 0)
            {
                parts.Add(text[(nl + 1)..]);
                break;
            }
            parts.Add(text);
        }
        parts.Reverse();
        foreach (var p in parts) col = AdvanceColumn(col, p);
        return col;
    }

    // ── spacing rules ────────────────────────────────────────────────────────

    private bool NeedsDefaultSpace(string nextText)
    {
        if (_last < 0) return false;
        var prev = _tokens[_last].TokenType;
        var next = _tokens[_next].TokenType;
        if (next is TSqlTokenType.Dot or TSqlTokenType.RightParenthesis or TSqlTokenType.Semicolon or TSqlTokenType.DoubleColon)
            return WouldFuse(nextText);
        if (prev is TSqlTokenType.Dot or TSqlTokenType.LeftParenthesis or TSqlTokenType.DoubleColon)
            return WouldFuse(nextText);
        if (next == TSqlTokenType.Comma) return _style.Lists.SpaceBeforeComma;
        if (prev == TSqlTokenType.Comma) return _style.Lists.SpaceAfterComma || WouldFuse(nextText);
        if (next == TSqlTokenType.LeftParenthesis && prev is TSqlTokenType.Identifier or TSqlTokenType.QuotedIdentifier)
            return _style.FunctionCalls.SpacesAroundParentheses;
        return true;
    }

    /// <summary>True when two tokens written with no space between them would lex as something else.</summary>
    private bool WouldFuse(string nextText)
    {
        if (_sb.Length == 0 || nextText.Length == 0) return false;
        var a = _sb[^1];
        var b = nextText[0];
        if (IsWordChar(a) && IsWordChar(b)) return true;
        if (a == '-' && b == '-') return true;
        if (a == '/' && b == '*') return true;
        if (a == '*' && b == '/') return true;
        if (a == '.' && char.IsDigit(b)) return true;
        if (char.IsDigit(a) && b == '.') return true;
        if (a == ':' && b == ':') return true;
        return false;
    }

    private static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '@' or '#' or '$' or '[' or ']' or '"' or '\'';
}
