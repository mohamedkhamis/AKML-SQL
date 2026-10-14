using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.PostgreSql.Reference;

namespace AkmlSql.PostgreSql.Formatting;

/// <summary>
/// Spec 042 (tasks T148, T150, research R2) - lays a PostgreSQL script out from a style written in SQL Prompt's model
/// (<see cref="SqlPromptStyle"/>): keyword casing, where lines break, indentation, alignment, comma and parenthesis placement.
///
/// <para>
/// <b>The token stream is the source of truth.</b> Every token of the script is printed exactly once, in order; the layout decides the
/// white space between them and the case of keywords, functions and data types, never the order, the spelling of a name, a quote,
/// a literal or a dollar-quoted body. That is what makes the result mean what the original meant (and the pipeline checks it against
/// the parse tree anyway). The parse tree gives no end positions and no position for a clause keyword (spike S2), so the structure -
/// which words start a clause, where a list ends, which AND belongs to a BETWEEN - is read off the tokens with brackets and CASE ...
/// END paired.
/// </para>
///
/// <para>
/// This part holds the shared machinery: tokens to documents, keyword and name casing, white space between tokens, comments, lists
/// and parentheses. Queries, data-changing statements and DDL are in the other parts of the class.
/// </para>
/// </summary>
internal sealed partial class PgLayout
{
    private readonly string _source;
    private readonly PgFormatTokens T;
    private readonly SqlPromptStyle S;
    private readonly bool _applyCasing;
    private readonly int Tab;
    private readonly int Max;

    private readonly int _semicolons;
    private readonly string?[] _emitted;

    /// <param name="semicolons">0 leave the terminators as they are, 1 add the missing ones, 2 remove them; 3 (as the style says) leaves them.</param>
    public PgLayout(string source, PgFormatTokens tokens, SqlPromptStyle style, bool applyCasing = true, int semicolons = 0)
    {
        _source = source;
        T = tokens;
        S = style;
        _applyCasing = applyCasing;
        _semicolons = semicolons;
        _emitted = new string?[tokens.Count];
        Tab = Math.Max(1, style.Whitespace.TabSize);
        Max = style.Whitespace.MaxLineLength;
    }

    /// <summary>
    /// The original text with only the changes of case (and, if asked, terminators) the layout would make, white space untouched: Format SQL
    /// with "Apply layout" off. Needs <see cref="Script"/> to have run, which is what decides each token's role.
    /// </summary>
    public string CaseAndTerminatorsOnly()
    {
        var edits = new List<(int Start, int End, string Text)>();
        for (var i = 0; i < T.Count; i++)
        {
            var emitted = _emitted[i];
            if (emitted is null || emitted == T[i].Text || T[i].Kind != PgFt.Word) continue;
            edits.Add((T[i].Start, T[i].End, emitted));
        }

        var statements = Statements().Where(s => s.Hi > s.Lo).ToList();
        foreach (var s in statements)
        {
            if (s.Semi >= 0 && _semicolons == 2 && IsLastStatement(s)) edits.Add((T[s.Semi].Start, T[s.Semi].End, string.Empty));
            else if (s.Semi < 0 && _semicolons == 1) edits.Add((T[s.Hi - 1].End, T[s.Hi - 1].End, ";"));
        }

        var sb = new System.Text.StringBuilder();
        var at = 0;
        foreach (var (start, end, text) in edits.OrderBy(e => e.Start))
        {
            sb.Append(_source, at, start - at).Append(text);
            at = end;
        }
        sb.Append(_source, at, _source.Length - at);
        return sb.ToString();
    }

    public PgRenderOptions RenderOptions => new(S.Whitespace.SpacesOrTabs != "spaces", Tab, Max);

    // ═══ script and statements ═══════════════════════════════════════════════════════════════════════

    private readonly record struct StatementRange(int Lo, int Hi, int Semi);

    /// <summary>The statements of the script: token ranges ended by a semicolon (or the end), with BEGIN ATOMIC ... END bodies kept whole.</summary>
    private List<StatementRange> Statements()
    {
        var result = new List<StatementRange>();
        var lo = 0;
        var atomic = 0;
        for (var i = 0; i < T.Count; i++)
        {
            var t = T[i];
            if (t.Kind == PgFt.Semi && atomic == 0)
            {
                if (i > lo) result.Add(new StatementRange(lo, i, i));
                else result.Add(new StatementRange(lo, lo, i));     // an empty statement: only its semicolon
                lo = i + 1;
                continue;
            }

            if (t.Kind != PgFt.Word) continue;
            if (atomic > 0)
            {
                if (t.Upper == "CASE") atomic++;
                else if (t.Upper == "END") atomic--;
            }
            else if (t.Upper == "ATOMIC" && i > 0 && T[i - 1].Is("BEGIN") && T[lo].Is("CREATE"))
            {
                atomic = 1;
            }
        }
        if (lo < T.Count) result.Add(new StatementRange(lo, T.Count, -1));
        return result;
    }

    /// <summary>The whole script as one document.</summary>
    public PgDoc Script()
    {
        var parts = new List<PgDoc>();
        var statements = Statements().Where(s => s.Hi > s.Lo).ToList();
        var previousEnd = -1;

        for (var n = 0; n < statements.Count; n++)
        {
            var statement = statements[n];
            if (n > 0)
            {
                var blank = BlankLines(S.Whitespace.PreserveEmptyLinesBetweenStatements, S.Whitespace.EmptyLinesBetweenStatements,
                    OriginalBlankLinesBefore(statements[n].Lo, previousEnd));
                parts.Add(PgDoc.HardLine(blank));
            }

            parts.Add(StatementDoc(statement));
            previousEnd = statement.Semi >= 0 ? statement.Semi : statement.Hi - 1;
        }

        if (T.Epilogue.Count > 0)
        {
            var blank = BlankLines(S.Whitespace.PreserveEmptyLinesBetweenStatements, S.Whitespace.EmptyLinesBetweenStatements,
                T.Epilogue[0].BlankLinesBefore);
            parts.Add(PgDoc.HardLine(blank));
            for (var i = 0; i < T.Epilogue.Count; i++)
            {
                if (i > 0) parts.Add(PgDoc.HardLine(Math.Min(1, T.Epilogue[i].BlankLinesBefore)));
                parts.Add(CommentDoc(T.Epilogue[i]));
            }
        }
        return PgDoc.Concat(parts);
    }

    private static int BlankLines(bool preserve, int configured, int original) => preserve ? Math.Max(configured, original) : configured;

    /// <summary>Empty lines between the end of the previous statement and the first thing (comment or token) of the next.</summary>
    private int OriginalBlankLinesBefore(int firstToken, int previousEnd)
    {
        var token = T[firstToken];
        if (token.Leading.Count > 0) return token.Leading[0].BlankLinesBefore;
        return Math.Max(0, token.NewlinesBefore - 1);
    }

    private PgDoc StatementDoc(StatementRange range)
    {
        var body = StatementBody(range.Lo, range.Hi);
        var isLast = IsLastStatement(range);
        if (range.Semi < 0)
        {
            if (_semicolons != 1) return body;
            return PgDoc.Concat(body, PgDoc.Text(";"));      // a missing terminator, added
        }
        if (_semicolons == 2 && isLast)
            return T[range.Semi].Leading.Count > 0 || T[range.Semi].TrailingLine is not null
                ? PgDoc.Concat(body, Comments(range.Semi, string.Empty))
                : body;

        var semi = T[range.Semi];
        var semicolon = Comments(range.Semi, ";");
        switch (S.Whitespace.BeforeSemicolon)
        {
            case "spaceBefore":
                return PgDoc.Concat(body, PgDoc.Text(" "), semicolon);
            case "newLineBefore":
                return PgDoc.Concat(body, PgDoc.HardLine(), semicolon);
            default:
                return PgDoc.Concat(body, semicolon);
        }
    }

    /// <summary>PostgreSQL needs the terminators between statements, so "remove" takes only the last one's.</summary>
    private bool IsLastStatement(StatementRange range)
    {
        var after = range.Semi >= 0 ? range.Semi + 1 : range.Hi;
        return after >= T.Count;
    }

    // ═══ comments and token emission ═════════════════════════════════════════════════════════════════

    private static PgDoc CommentDoc(PgFormatComment comment) =>
        comment.IsLine ? PgDoc.Text(comment.Text) : PgDoc.Verbatim(comment.Text);

    /// <summary>The comments before token <paramref name="i"/>, then the text, then its end-of-line comment.</summary>
    private PgDoc Comments(int i, string text, bool trailing = true)
    {
        var token = T[i];
        var parts = new List<PgDoc>();
        _emitted[i] = text;

        foreach (var comment in token.Leading)
        {
            parts.Add(CommentDoc(comment));
            // A comment with nothing after it on its line keeps the next thing off that line; one with a token behind it on the same line stays in front of it.
            if (comment.EndsLine) parts.Add(PgDoc.HardLine());
            else parts.Add(PgDoc.Text(" "));
        }

        parts.Add(PgDoc.Verbatim(text));
        if (trailing && token.TrailingLine is { } line)
            parts.Add(PgDoc.Concat(PgDoc.LineSuffix(" " + line.Text), PgDoc.BreakParent));
        return parts.Count == 1 ? parts[0] : PgDoc.Concat(parts);
    }

    /// <summary>True when token <paramref name="i"/> is led by a comment that sits inline, so the comment (not the token) needs the space after <paramref name="previous"/>.</summary>
    private bool LeadsWithInlineComment(int previous, int i) =>
        T[i].Leading.Count > 0 && !T[i].Leading[0].EndsLine && T[previous].Kind is not (PgFt.Open or PgFt.OpenSq or PgFt.Dot);

    /// <summary>A token as written.</summary>
    private PgDoc Emit(int i, bool trailing = true) => Comments(i, T[i].Text, trailing);

    // ═══ casing ══════════════════════════════════════════════════════════════════════════════════════

    private static string ApplyCase(string text, string mode) => mode switch
    {
        "uppercase" => text.ToUpperInvariant(),
        "lowercase" => text.ToLowerInvariant(),
        "lowerCamelCase" => text.ToLowerInvariant(),
        "upperCamelCase" => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..].ToLowerInvariant(),
        _ => text,
    };

    /// <summary>A keyword, cased by the reserved-keyword option.</summary>
    private PgDoc Kw(int i, bool trailing = true) =>
        Comments(i, _applyCasing ? ApplyCase(T[i].Text, S.Casing.ReservedKeywords) : T[i].Text, trailing);

    /// <summary>A built-in function name, cased by the function option.</summary>
    private PgDoc Fn(int i, bool trailing = true) =>
        Comments(i, _applyCasing ? ApplyCase(T[i].Text, S.Casing.BuiltInFunctions) : T[i].Text, trailing);

    /// <summary>A built-in data type word, cased by the data type option.</summary>
    private PgDoc Ty(int i, bool trailing = true) =>
        Comments(i, _applyCasing ? ApplyCase(T[i].Text, S.Casing.BuiltInDataTypes) : T[i].Text, trailing);

    /// <summary>Width of the words of a clause keyword (<c>GROUP BY</c>) with single spaces between them.</summary>
    private int PhraseWidth(int first, int last)
    {
        var width = 0;
        for (var i = first; i <= last; i++) width += T[i].Text.Length + (i > first ? 1 : 0);
        return width;
    }

    /// <summary>The words of a keyword phrase as a document: <c>GROUP BY</c>, <c>LEFT OUTER JOIN</c>, each word cased.</summary>
    private PgDoc Phrase(int first, int last)
    {
        var parts = new List<PgDoc>();
        for (var i = first; i <= last; i++)
        {
            if (i > first) parts.Add(PgDoc.Text(" "));
            parts.Add(Kw(i));
        }
        return PgDoc.Concat(parts);
    }

    // ═══ searching the tokens ════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The first token in [from, hi) that satisfies <paramref name="match"/> outside any bracket pair and CASE ... END; <paramref name="hi"/>
    /// when there is none.
    /// </summary>
    private int FindTop(int from, int hi, Func<PgFormatToken, bool> match)
    {
        var i = from;
        while (i < hi)
        {
            var t = T[i];
            if (match(t)) return i;
            if (IsGroupOpen(t))
            {
                var close = T.MatchOf(i);
                if (close > i)
                {
                    i = close + 1;
                    continue;
                }
            }
            i++;
        }
        return hi;
    }

    private static bool IsGroupOpen(PgFormatToken t) =>
        t.Kind is PgFt.Open or PgFt.OpenSq || (t.Kind == PgFt.Word && t.Upper == "CASE");

    private List<int> FindAllTop(int lo, int hi, Func<PgFormatToken, bool> match)
    {
        var found = new List<int>();
        var i = lo;
        while (i < hi)
        {
            i = FindTop(i, hi, match);
            if (i >= hi) break;
            found.Add(i);
            i++;
        }
        return found;
    }

    /// <summary>Ranges between top-level commas in [lo, hi).</summary>
    private List<(int Lo, int Hi, int Comma)> SplitCommas(int lo, int hi)
    {
        var parts = new List<(int, int, int)>();
        var start = lo;
        foreach (var comma in FindAllTop(lo, hi, t => t.Kind == PgFt.Comma))
        {
            parts.Add((start, comma, comma));
            start = comma + 1;
        }
        parts.Add((start, hi, -1));
        return parts;
    }

    /// <summary>The first token at or after <paramref name="i"/> that is a word equal to one of <paramref name="uppers"/>, at the top level.</summary>
    private int FindWord(int from, int hi, params string[] uppers) =>
        FindTop(from, hi, t => t.Kind == PgFt.Word && Array.IndexOf(uppers, t.Upper) >= 0);

    // ═══ white space between tokens ══════════════════════════════════════════════════════════════════

    /// <summary>True when the token can end an operand, so an operator after it is binary (<c>a - b</c>) and not a sign (<c>= -b</c>).</summary>
    private bool IsOperandEnd(int i)
    {
        var t = T[i];
        switch (t.Kind)
        {
            case PgFt.Quoted:
            case PgFt.String:
            case PgFt.Number:
            case PgFt.Param:
            case PgFt.Close:
            case PgFt.CloseSq:
                return true;
            case PgFt.Word:
                return !PgWords.Reserved.Contains(t.Upper) || PgWords.ValueWords.Contains(t.Upper);
            default:
                return false;
        }
    }

    /// <summary>An operator with no operand before it in the flow: a sign, a prefix operator, or a star (<c>SELECT *</c>, <c>count(*)</c>).</summary>
    private bool IsPrefixOperator(int i, int flowLo) => i <= flowLo || !IsOperandEnd(i - 1);

    private bool SpaceAroundOperator(PgFormatToken op)
    {
        if (PgWords.ComparisonOperators.Contains(op.Text)) return S.Operators.SpacesAroundComparison;
        if (PgWords.ArithmeticOperators.Contains(op.Text)) return S.Operators.SpacesAroundArithmetic;
        return true;
    }

    /// <summary>Whether a space goes between token <paramref name="a"/> and the token <paramref name="b"/> that follows it in a run of tokens starting at <paramref name="flowLo"/>.</summary>
    private bool NeedSpace(int a, int b, int flowLo)
    {
        var ta = T[a];
        var tb = T[b];

        if (tb.Kind is PgFt.Comma or PgFt.Semi or PgFt.Close or PgFt.CloseSq or PgFt.Dot or PgFt.Colon) return false;
        if (ta.Kind is PgFt.Open or PgFt.OpenSq or PgFt.Dot or PgFt.Colon) return false;
        if (tb.IsOp("::") || ta.IsOp("::")) return false;

        if (tb.Kind == PgFt.Open) return SpaceBeforeOpenParen(a);
        if (tb.Kind == PgFt.OpenSq) return !(IsOperandEnd(a) || ta.Is("ARRAY"));

        if (ta.Kind == PgFt.Operator)
        {
            if (IsPrefixOperator(a, flowLo)) return ta.Text == "*" || tb.Kind == PgFt.Operator;      // a sign is tight to its operand, a star is not; two operators never touch
            return tb.Kind == PgFt.Operator || SpaceAroundOperator(ta);
        }

        if (tb.Kind == PgFt.Operator)
        {
            if (IsPrefixOperator(b, flowLo)) return true;
            return SpaceAroundOperator(tb);
        }

        return true;
    }

    /// <summary>Whether a space goes before an opening parenthesis that follows token <paramref name="a"/>: no for a function call or a type with modifiers.</summary>
    private bool SpaceBeforeOpenParen(int a)
    {
        var ta = T[a];
        switch (ta.Kind)
        {
            case PgFt.Comma:
            case PgFt.Operator:
            case PgFt.Close:
            case PgFt.CloseSq:
            case PgFt.String:
            case PgFt.Number:
            case PgFt.Param:
                return true;

            case PgFt.Quoted:
                return !Adjacent(a, a + 1);

            case PgFt.Word:
                if (PgWords.SpaceBeforeParen.Contains(ta.Upper))
                    return ta.IsAny("IN", "VALUES", "EXISTS", "ANY", "ALL", "SOME") ? S.Parentheses.SpacesAround : true;
                if (IsKnownCallable(a) || IsKnownType(a))
                    return S.FunctionCalls.SpacesAroundParentheses && IsKnownCallable(a);
                // Anything else follows what was written: a name touching its parenthesis is a call.
                return !Adjacent(a, a + 1);

            default:
                return true;
        }
    }

    private bool Adjacent(int a, int b) => b < T.Count && T[a].End == T[b].Start;

    private bool IsKnownCallable(int i) => T[i].IsWord && PgReference.Instance.Callables(T[i].Text.ToLowerInvariant()).Count > 0;

    private bool IsKnownType(int i) => T[i].IsWord && PgWords.BuiltInTypes.Contains(T[i].Upper);

    // ═══ a run of tokens ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tokens [lo, hi) laid out on a line: spaces by <see cref="NeedSpace"/>, brackets and CASE expressions as units, keywords, function
    /// names and data types cased. Lines break only where a structure asks (CASE, a long argument list, a subquery), never between
    /// plain tokens, except after a token that ends in a line comment.
    /// </summary>
    private PgDoc Flow(int lo, int hi, bool hoistLastTrailing = false)
    {
        if (lo >= hi) return PgDoc.Empty;
        var parts = new List<PgDoc>();
        var previousLast = -1;
        var i = lo;

        while (i < hi)
        {
            var end = ElementEnd(i, hi);
            if (previousLast >= 0)
            {
                if (T[previousLast].TrailingLine is not null) parts.Add(PgDoc.HardLine());
                else if (NeedSpace(previousLast, i, lo) || LeadsWithInlineComment(previousLast, i)) parts.Add(PgDoc.Text(" "));
            }
            var suppressTrailing = hoistLastTrailing && end >= hi;
            parts.Add(Element(i, end, lo, suppressTrailing));
            previousLast = end - 1;
            i = end;
        }
        return parts.Count == 1 ? parts[0] : PgDoc.Concat(parts);
    }

    /// <summary>End (exclusive) of the unit that starts at <paramref name="i"/>: a bracket group, a CASE expression, or one token.</summary>
    private int ElementEnd(int i, int hi)
    {
        var t = T[i];
        if (IsGroupOpen(t))
        {
            var close = T.MatchOf(i);
            if (close > i && close < hi) return close + 1;
        }
        return i + 1;
    }

    private PgDoc Element(int i, int end, int flowLo, bool suppressTrailing)
    {
        var t = T[i];
        if (end == i + 1 && !IsGroupOpen(t)) return Single(i, flowLo, suppressTrailing);
        if (end == i + 1) return Single(i, flowLo, suppressTrailing);       // an unpaired opener: just the token

        return t.Kind switch
        {
            PgFt.Open => ParenElement(i, end - 1, flowLo, suppressTrailing),
            PgFt.OpenSq => BracketElement(i, end - 1, suppressTrailing),
            _ => CaseElement(i, end - 1, suppressTrailing),
        };
    }

    private PgDoc Single(int i, int flowLo, bool suppressTrailing)
    {
        var t = T[i];
        var trailing = !suppressTrailing;
        switch (t.Kind)
        {
            case PgFt.Word:
                return WordDoc(i, trailing);
            default:
                return Emit(i, trailing);
        }
    }

    /// <summary>A word: keyword, function name, data type or a name as written.</summary>
    private PgDoc WordDoc(int i, bool trailing)
    {
        var t = T[i];
        var next = i + 1 < T.Count ? T[i + 1] : null;
        var previous = i > 0 ? T[i - 1] : null;

        // A name after a dot is not a keyword; a word of a data type (after :: or in CAST(x AS type)) is cased as one.
        if (previous is not null && previous.Kind == PgFt.Dot) return IsCallPosition(i) && IsKnownCallable(i) ? Fn(i, trailing) : Emit(i, trailing);
        if (IsDataTypeWord(i)) return Ty(i, trailing);
        if (previous is not null && previous.IsOp("::")) return Emit(i, trailing);

        if (_keywordTokens.Contains(i)) return Kw(i, trailing);
        if (PgWords.Reserved.Contains(t.Upper) && !(IsCallPosition(i) && PgWords.FunctionKeywords.Contains(t.Upper))) return Kw(i, trailing);
        if (IsCallPosition(i) && (IsKnownCallable(i) || PgWords.FunctionKeywords.Contains(t.Upper)) && !PgWords.SpaceBeforeParen.Contains(t.Upper)) return Fn(i, trailing);
        if (IsInExpressionKeyword(i)) return Kw(i, trailing);
        return Emit(i, trailing);
    }

    /// <summary>True for a word of a built-in data type written after <c>::</c> or as the type of a CAST (<c>double precision</c> counts as two).</summary>
    private bool IsDataTypeWord(int i)
    {
        for (var k = 0; k <= 3 && i - k > 0; k++)
        {
            var start = i - k;
            if (!TypeStartsAt(start)) continue;
            if (k == 0) return true;
            foreach (var phrase in PgWords.MultiWordTypes)
                if (phrase.Length > k && MatchesPhrase(start, phrase)) return true;
        }
        return false;
    }

    private bool TypeStartsAt(int start) =>
        T[start].IsWord && (T[start - 1].IsOp("::") || (T[start - 1].Is("AS") && InsideCast(start - 1)))
        && (IsKnownType(start) || TypePhraseStartsAt(start));

    /// <summary>True when the token is inside the parentheses of a CAST(...).</summary>
    private bool InsideCast(int index)
    {
        var depth = 0;
        for (var k = index - 1; k >= 0; k--)
        {
            if (T[k].Kind == PgFt.Close) depth++;
            else if (T[k].Kind == PgFt.Open)
            {
                if (depth == 0) return k > 0 && T[k - 1].IsAny("CAST", "TREAT");
                depth--;
            }
            else if (T[k].Kind == PgFt.Semi) return false;
        }
        return false;
    }

    private bool IsCallPosition(int i) => i + 1 < T.Count && T[i + 1].Kind == PgFt.Open;

    /// <summary>Tokens a statement's own structure makes keywords (the head of a CREATE, constraint words in a table), cased as keywords.</summary>
    private readonly HashSet<int> _keywordTokens = new();

    private bool TypePhraseStartsAt(int i)
    {
        foreach (var phrase in PgWords.MultiWordTypes)
            if (MatchesPhrase(i, phrase)) return true;
        return false;
    }

    private bool MatchesPhrase(int i, string[] words)
    {
        if (i + words.Length > T.Count) return false;
        for (var k = 0; k < words.Length; k++)
            if (!T[i + k].Is(words[k])) return false;
        return true;
    }

    /// <summary>Unreserved words that are syntax where they stand: <c>BY</c> after GROUP / ORDER / PARTITION, <c>NULLS FIRST</c>, window frame words ...</summary>
    private bool IsInExpressionKeyword(int i)
    {
        var t = T[i];
        var prev = i > 0 ? T[i - 1] : null;
        var next = i + 1 < T.Count ? T[i + 1] : null;
        switch (t.Upper)
        {
            case "BY":
                return prev is not null && prev.IsAny("GROUP", "ORDER", "PARTITION");
            case "KEY":
                return prev is not null && prev.IsAny("PRIMARY", "FOREIGN");
            case "NULLS":
                return next is not null && next.IsAny("FIRST", "LAST");
            case "FIRST":
            case "LAST":
                return prev is not null && prev.Is("NULLS");
            case "BETWEEN":
            case "EXISTS":
            case "VALUES":
            case "UNBOUNDED":
            case "PRECEDING":
            case "FOLLOWING":
            case "ESCAPE":
                return true;
            case "OVER":
                return next is not null && (next.Kind == PgFt.Open || (next.IsWord && prev is not null && prev.Kind == PgFt.Close));
            case "FILTER":
                return next is not null && next.Kind == PgFt.Open;
            case "UNKNOWN":
                return prev is not null && (prev.Is("IS") || (prev.Is("NOT") && i > 1 && T[i - 2].Is("IS")));
            case "PARTITION":
                return next is not null && next.Is("BY");
            case "WITHIN":
                return next is not null && next.Is("GROUP");
            case "ROW":
                return prev is not null && prev.Is("CURRENT");
            case "CURRENT":
                return next is not null && next.Is("ROW");
            case "ROWS":
            case "RANGE":
            case "GROUPS":
                return next is not null && (next.IsAny("BETWEEN", "UNBOUNDED", "CURRENT") || next.Kind == PgFt.Number);
            case "AT":
                return next is not null && next.Is("TIME");
            case "TIME":
                return prev is not null && prev.Is("AT") && next is not null && next.Is("ZONE");
            case "ZONE":
                return prev is not null && prev.Is("TIME");
            case "RECURSIVE":
            case "MATERIALIZED":
            case "LATERAL":
            case "NOTHING":
            case "CONFLICT":
                return true;
            default:
                return false;
        }
    }

    // ═══ brackets ════════════════════════════════════════════════════════════════════════════════════

    /// <summary>Array subscripts and constructors: <c>a[1]</c>, <c>ARRAY[1, 2]</c>. Contents flow with ", " between items, nothing breaks.</summary>
    private PgDoc BracketElement(int open, int close, bool suppressTrailing)
    {
        var inner = CommaFlow(open + 1, close);
        return PgDoc.Concat(Emit(open), inner, Emit(close, !suppressTrailing));
    }

    /// <summary>Comma-separated runs on one line: the commas are followed by a space (or not, per the style).</summary>
    private PgDoc CommaFlow(int lo, int hi)
    {
        var parts = new List<PgDoc>();
        foreach (var (a, b, comma) in SplitCommas(lo, hi))
        {
            if (a > lo) parts.Add(PgDoc.Text(S.Lists.SpaceAfterComma ? " " : string.Empty));
            parts.Add(Flow(a, b, hoistLastTrailing: comma >= 0 && T[b - 1].TrailingLine is not null));
            if (comma >= 0) parts.Add(CommaDoc(comma, b > a ? b - 1 : -1));
        }
        return PgDoc.Concat(parts);
    }

    /// <summary>A comma with the comment (if any) in front of it, followed by the end-of-line comment that belongs to the line it ends.</summary>
    private PgDoc CommaDoc(int comma, int previousLast) => PgDoc.Concat(CommaText(comma), CommaSuffix(comma, previousLast));

    /// <summary>
    /// The comma itself, with the space before it the style asks for and the comments that stood in front of it. With
    /// <paramref name="inlineCommentsElsewhere"/> the comments that sat inline on the previous item's line are left out (see <see cref="CommaInlineComments"/>).
    /// </summary>
    private PgDoc CommaText(int comma, bool inlineCommentsElsewhere = false)
    {
        var spaceBefore = S.Lists.SpaceBeforeComma ? " " : string.Empty;
        var token = T[comma];
        var parts = new List<PgDoc> { PgDoc.Text(spaceBefore) };
        foreach (var c in token.Leading)
        {
            var inline = !c.IsLine && !c.OnOwnLine;       // a block comment written after the item, on its line
            if (inline && inlineCommentsElsewhere) continue;
            if (inline && parts.Count == 1) parts.Add(PgDoc.Text(" "));
            parts.Add(CommentDoc(c));
            if (!inline) parts.Add(PgDoc.HardLine());
            else parts.Add(PgDoc.Text(" "));
        }
        if (parts.Count > 1 && parts[^1] is PgText { Value: " " }) parts.RemoveAt(parts.Count - 1);
        parts.Add(PgDoc.Text(","));
        return PgDoc.Concat(parts);
    }

    /// <summary>The comments that stood inline in front of a comma (<c>a /* note */, b</c>): they stay on the line of the item they follow.</summary>
    private PgDoc CommaInlineComments(int comma)
    {
        var parts = new List<PgDoc>();
        foreach (var c in T[comma].Leading)
        {
            if (c.IsLine || c.OnOwnLine) continue;
            parts.Add(PgDoc.Text(" "));
            parts.Add(CommentDoc(c));
        }
        return PgDoc.Concat(parts);
    }

    /// <summary>
    /// The end-of-line comment that follows a comma: the comma's own, or the one of the token before it that was written after the item
    /// and is moved past the comma. It goes out at the end of the line the text before it is on.
    /// </summary>
    private PgDoc CommaSuffix(int comma, int previousLast)
    {
        var trailing = T[comma].TrailingLine ?? (previousLast >= 0 ? T[previousLast].TrailingLine : null);
        return trailing is null ? PgDoc.Empty : PgDoc.Concat(PgDoc.LineSuffix(" " + trailing.Text), PgDoc.BreakParent);
    }
}
