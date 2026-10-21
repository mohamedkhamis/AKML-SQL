using AkmlSql.PostgreSql.Language;

namespace AkmlSql.PostgreSql.Completion;

/// <summary>One token that matters to the analysis (white space and comments are left out) with its text and nesting.</summary>
internal sealed class PgSig
{
    public PgSig(PgLexToken token, string text)
    {
        Token = token;
        Text = text;
        Upper = token.Kind == PgTokenKind.Identifier ? text.ToUpperInvariant() : string.Empty;
    }

    public PgLexToken Token { get; }

    /// <summary>The source text of the token.</summary>
    public string Text { get; }

    /// <summary>Upper-case text of a bare word (a key word or an unquoted name); empty for anything else.</summary>
    public string Upper { get; }

    public PgTokenKind Kind => Token.Kind;

    public bool IsWord => Token.Kind == PgTokenKind.Identifier;

    public bool IsName => Token.Kind is PgTokenKind.Identifier or PgTokenKind.QuotedIdentifier;

    public bool IsQuoted => Token.Kind == PgTokenKind.QuotedIdentifier;

    /// <summary>Parenthesis depth: the number of "(" open before this token (for "(" and ")" the depth outside them).</summary>
    public int Depth { get; set; }

    /// <summary>For "(": the index of the matching ")" or the token count when it is not closed; for ")": the index of its "(".</summary>
    public int Match { get; set; } = -1;

    public bool IsOpen => Token.Kind == PgTokenKind.Punctuation && Text == "(";

    public bool IsClose => Token.Kind == PgTokenKind.Punctuation && Text == ")";

    public bool IsComma => Token.Kind == PgTokenKind.Punctuation && Text == ",";

    public bool IsDot => Token.Kind == PgTokenKind.Punctuation && Text == ".";

    public bool IsStar => Token.Kind == PgTokenKind.Operator && Text == "*";

    public bool IsCast => Token.Kind == PgTokenKind.Operator && Text == "::";

    /// <summary>A number, a string or a positional parameter: something that completes an operand without being a name.</summary>
    public bool IsLiteral => Token.Kind is PgTokenKind.Number or PgTokenKind.String or PgTokenKind.DollarString or PgTokenKind.Parameter;

    public bool Is(string punctuation) => Token.Kind is PgTokenKind.Punctuation or PgTokenKind.Operator or PgTokenKind.Semicolon && Text == punctuation;

    public bool IsWordEqual(string upper) => IsWord && Upper == upper;

    /// <summary>The name this token spells, without the double quotes of a quoted identifier.</summary>
    public PgName Name
    {
        get
        {
            if (!IsQuoted) return new PgName(Text, false);
            var inner = Text.Length >= 2 && Token.Terminated ? Text.Substring(1, Text.Length - 2) : Text.Substring(1);
            return new PgName(inner.Replace("\"\"", "\""), true);
        }
    }
}

/// <summary>The significant tokens of one statement, with the parentheses matched.</summary>
internal sealed class PgStatementTokens
{
    public PgStatementTokens(IReadOnlyList<PgSig> sig) => Sig = sig;

    public IReadOnlyList<PgSig> Sig { get; }

    public int Count => Sig.Count;

    public PgSig this[int i] => Sig[i];

    /// <summary>Builds the view over the tokens whose start lies in [from, to); the token starting at <paramref name="skipStart"/> (the word being typed) is left out.</summary>
    public static PgStatementTokens Build(string text, IReadOnlyList<PgLexToken> tokens, int from, int to, int skipStart = -1)
    {
        var sig = new List<PgSig>();
        foreach (var t in tokens)
        {
            if (t.Start < from || t.Start >= to || t.Start == skipStart) continue;
            if (t.Kind is PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment or PgTokenKind.MetaCommand) continue;
            sig.Add(new PgSig(t, text.Substring(t.Start, t.End - t.Start)));
        }

        var stack = new Stack<int>();
        for (var i = 0; i < sig.Count; i++)
        {
            var s = sig[i];
            if (s.Kind == PgTokenKind.Punctuation && s.Text == "(")
            {
                s.Depth = stack.Count;
                stack.Push(i);
            }
            else if (s.Kind == PgTokenKind.Punctuation && s.Text == ")")
            {
                if (stack.Count > 0)
                {
                    var open = stack.Pop();
                    sig[open].Match = i;
                    s.Match = open;
                }
                s.Depth = stack.Count;
            }
            else
            {
                s.Depth = stack.Count;
            }
        }
        foreach (var open in stack) sig[open].Match = sig.Count;   // not closed: it runs to the end
        return new PgStatementTokens(sig);
    }

    /// <summary>True when the "(" at <paramref name="open"/> starts a subquery: the next token is SELECT, WITH, VALUES or TABLE.</summary>
    public bool StartsSubquery(int open)
    {
        if (open + 1 >= Sig.Count) return false;
        var next = Sig[open + 1];
        return next.IsWord && (next.Upper is "SELECT" or "WITH" or "VALUES" or "TABLE" or "MERGE");
    }
}
