using AkmlSql.PostgreSql.Language;

namespace AkmlSql.PostgreSql.Formatting;

/// <summary>The kinds of token the layout works with; the lexical scanner's kinds with punctuation told apart.</summary>
internal enum PgFt
{
    Word,
    Quoted,
    String,
    Number,
    Param,
    Operator,
    Comma,
    Dot,
    Colon,
    Open,       // (
    Close,      // )
    OpenSq,     // [
    CloseSq,    // ]
    Semi,
    Other,
}

/// <summary>A comment, with where it stood: on a line of its own, or after code on the same line.</summary>
internal sealed record PgFormatComment(string Text, bool IsLine, bool OnOwnLine, int BlankLinesBefore)
{
    /// <summary>Line breaks between the end of the comment and whatever follows it (the next comment or token).</summary>
    public int NewlinesAfter { get; set; }

    /// <summary>True when nothing follows the comment on its line: a line comment always, a block comment when a line break comes next.</summary>
    public bool EndsLine => IsLine || NewlinesAfter > 0;
}

/// <summary>
/// One significant token with the comments around it. A line comment that follows code on the same line is the previous token's
/// <see cref="TrailingLine"/>; every other comment leads the next token (<see cref="Leading"/>), on its own line when it began one.
/// </summary>
internal sealed class PgFormatToken
{
    public required int Index { get; init; }
    public required PgFt Kind { get; init; }
    public required string Text { get; init; }
    public required int Start { get; init; }
    public required int End { get; init; }

    /// <summary>Newlines between the previous token (and its comments) and this one, or 0.</summary>
    public int NewlinesBefore { get; set; }

    /// <summary>Upper-case invariant text of a word; empty for other kinds.</summary>
    public string Upper { get; init; } = string.Empty;

    public List<PgFormatComment> Leading { get; } = new();
    public PgFormatComment? TrailingLine { get; set; }

    public bool IsWord => Kind == PgFt.Word;
    public bool Is(string upper) => Kind == PgFt.Word && Upper == upper;
    public bool IsAny(params string[] uppers) => Kind == PgFt.Word && Array.IndexOf(uppers, Upper) >= 0;
    public bool IsPunct(PgFt kind) => Kind == kind;
    public bool IsOp(string text) => Kind == PgFt.Operator && Text == text;
    public override string ToString() => Text;
}

/// <summary>A script's significant tokens with their comments, bracket pairs and CASE ... END pairs.</summary>
internal sealed class PgFormatTokens
{
    private readonly PgFormatToken[] _tokens;
    private readonly int[] _match;

    public IReadOnlyList<PgFormatToken> All => _tokens;
    public int Count => _tokens.Length;
    public PgFormatToken this[int i] => _tokens[i];

    /// <summary>Comments after the last token, on lines of their own.</summary>
    public List<PgFormatComment> Epilogue { get; } = new();

    /// <summary>False when brackets or CASE ... END do not pair up; the formatter then leaves the text alone.</summary>
    public bool Balanced { get; private set; } = true;

    private PgFormatTokens(PgFormatToken[] tokens, int[] match)
    {
        _tokens = tokens;
        _match = match;
    }

    /// <summary>Index of the token closing the bracket (or END closing the CASE) at <paramref name="open"/>, or -1.</summary>
    public int MatchOf(int open) => open >= 0 && open < _match.Length ? _match[open] : -1;

    public static PgFormatTokens Build(string text)
    {
        var lex = PgLexicalScanner.Scan(text);
        var tokens = new List<PgFormatToken>();
        var pending = new List<PgFormatComment>();
        PgFormatToken? previous = null;
        var newlinesSincePrevious = 0;
        var blankLines = 0;
        var epilogue = new List<PgFormatComment>();
        PgFormatComment? lastComment = null;

        // The line breaks that came after a comment are known once the next comment or token arrives.
        void CloseComment()
        {
            if (lastComment is not null) lastComment.NewlinesAfter = newlinesSincePrevious;
            lastComment = null;
        }

        void AddToken(PgFt kind, int start, int end)
        {
            CloseComment();
            var raw = text.Substring(start, end - start);
            var token = new PgFormatToken
            {
                Index = tokens.Count,
                Kind = kind,
                Text = raw,
                Start = start,
                End = end,
                Upper = kind == PgFt.Word ? raw.ToUpperInvariant() : string.Empty,
                NewlinesBefore = newlinesSincePrevious,
            };
            token.Leading.AddRange(pending);
            pending.Clear();
            tokens.Add(token);
            previous = token;
            newlinesSincePrevious = 0;
            blankLines = 0;
        }

        foreach (var lexToken in lex)
        {
            switch (lexToken.Kind)
            {
                case PgTokenKind.Whitespace:
                {
                    var newlines = CountNewlines(text, lexToken.Start, lexToken.End);
                    newlinesSincePrevious += newlines;
                    if (newlines > 1) blankLines = Math.Max(blankLines, newlines - 1);
                    break;
                }

                case PgTokenKind.LineComment:
                case PgTokenKind.BlockComment:
                {
                    CloseComment();
                    var isLine = lexToken.Kind == PgTokenKind.LineComment;
                    var raw = text.Substring(lexToken.Start, lexToken.End - lexToken.Start);
                    var ownLine = newlinesSincePrevious > 0 || previous is null;
                    var comment = new PgFormatComment(raw, isLine, ownLine, blankLines);
                    if (isLine && !ownLine && previous is not null && previous.TrailingLine is null && pending.Count == 0)
                    {
                        previous.TrailingLine = comment;
                    }
                    else
                    {
                        pending.Add(comment);
                        lastComment = comment;
                    }
                    // A comment spends the gap that came before it.
                    newlinesSincePrevious = 0;
                    blankLines = 0;
                    break;
                }

                case PgTokenKind.Identifier:
                    AddToken(PgFt.Word, lexToken.Start, lexToken.End);
                    break;
                case PgTokenKind.QuotedIdentifier:
                    AddToken(PgFt.Quoted, lexToken.Start, lexToken.End);
                    break;
                case PgTokenKind.String:
                case PgTokenKind.DollarString:
                    AddToken(PgFt.String, lexToken.Start, lexToken.End);
                    break;
                case PgTokenKind.Number:
                    AddToken(PgFt.Number, lexToken.Start, lexToken.End);
                    break;
                case PgTokenKind.Parameter:
                    AddToken(PgFt.Param, lexToken.Start, lexToken.End);
                    break;
                case PgTokenKind.Semicolon:
                    AddToken(PgFt.Semi, lexToken.Start, lexToken.End);
                    break;
                case PgTokenKind.Operator:
                    foreach (var (start, end) in SplitOperator(text, lexToken.Start, lexToken.End))
                        AddToken(PgFt.Operator, start, end);
                    break;
                case PgTokenKind.Punctuation:
                    AddToken(text[lexToken.Start] switch
                    {
                        '(' => PgFt.Open,
                        ')' => PgFt.Close,
                        '[' => PgFt.OpenSq,
                        ']' => PgFt.CloseSq,
                        ',' => PgFt.Comma,
                        '.' => PgFt.Dot,
                        ':' => PgFt.Colon,
                        _ => PgFt.Other,
                    }, lexToken.Start, lexToken.End);
                    break;
                default:
                    AddToken(PgFt.Other, lexToken.Start, lexToken.End);
                    break;
            }
        }

        // Comments after the last token: a line comment on the last token's line is its trailing comment (already taken above);
        // the rest are the epilogue.
        CloseComment();
        epilogue.AddRange(pending);

        var array = tokens.ToArray();
        var (match, balanced) = Pair(array);
        var result = new PgFormatTokens(array, match) { Balanced = balanced };
        result.Epilogue.AddRange(epilogue);
        return result;
    }

    private static int CountNewlines(string text, int start, int end)
    {
        var count = 0;
        for (var i = start; i < end; i++)
            if (text[i] == '\n') count++;
        return count;
    }

    // PostgreSQL's rule: an operator of several characters does not end in + or - unless it also holds one of ~ ! @ # % ^ & | ` ?
    // The scanner takes the longest run, so `a*-1` arrives as `*-`; the server reads it as `*` and `-`.
    private static IEnumerable<(int Start, int End)> SplitOperator(string text, int start, int end)
    {
        var length = end - start;
        if (length > 1 && (text[end - 1] == '+' || text[end - 1] == '-'))
        {
            var special = false;
            for (var i = start; i < end && !special; i++)
                special = "~!@#%^&|`?".IndexOf(text[i]) >= 0;
            if (!special)
            {
                var last = end;
                while (last - 1 > start && (text[last - 1] == '+' || text[last - 1] == '-')) last--;
                yield return (start, last);
                for (var i = last; i < end; i++) yield return (i, i + 1);
                yield break;
            }
        }
        yield return (start, end);
    }

    private static (int[] Match, bool Balanced) Pair(PgFormatToken[] tokens)
    {
        var match = new int[tokens.Length];
        Array.Fill(match, -1);
        var brackets = new Stack<int>();
        var cases = new Stack<int>();
        var balanced = true;

        for (var i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            switch (t.Kind)
            {
                case PgFt.Open:
                case PgFt.OpenSq:
                    brackets.Push(i);
                    break;
                case PgFt.Close:
                case PgFt.CloseSq:
                {
                    var want = t.Kind == PgFt.Close ? PgFt.Open : PgFt.OpenSq;
                    if (brackets.Count == 0 || tokens[brackets.Peek()].Kind != want) { balanced = false; break; }
                    var open = brackets.Pop();
                    match[open] = i;
                    match[i] = open;
                    break;
                }
                case PgFt.Word when t.Upper == "CASE":
                    cases.Push(i);
                    break;
                case PgFt.Word when t.Upper == "END":
                    if (cases.Count > 0)
                    {
                        var open = cases.Pop();
                        match[open] = i;
                        match[i] = open;
                    }
                    break;
            }
        }

        if (brackets.Count > 0 || cases.Count > 0) balanced = false;
        return (match, balanced);
    }
}
