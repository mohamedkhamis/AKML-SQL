namespace AkmlSql.PostgreSql.Language;

/// <summary>What a token of PostgreSQL text is, as far as splitting and redaction need to know.</summary>
public enum PgTokenKind
{
    Whitespace,
    LineComment,
    BlockComment,
    Identifier,
    QuotedIdentifier,
    String,
    DollarString,
    Number,
    Parameter,
    Operator,
    Punctuation,
    Semicolon,

    /// <summary>A psql meta-command (<c>\dt</c>, <c>\g</c>) from the backslash to the end of its line.</summary>
    MetaCommand,
    Other,
}

/// <summary>A token's span (UTF-16 offsets, end exclusive); <see cref="Terminated"/> is false for a string, quoted name or comment the text ends inside.</summary>
public readonly record struct PgLexToken(PgTokenKind Kind, int Start, int End, bool Terminated = true);

/// <summary>
/// Spec 042 (research R5) - a small, tolerant, managed scanner for PostgreSQL text. It is not a parser. It knows every
/// lexical form that can hide a semicolon or a quote - standard, <c>E''</c>, <c>U&amp;''</c>, <c>B''</c>/<c>X''</c>/<c>N''</c>
/// strings, quoted identifiers, dollar quoting with tags, line comments and nested block comments - plus positional
/// parameters, casts and psql meta-commands. It never throws: unfinished text ends in an unterminated token.
///
/// <para>
/// The native scanner of the PostgreSQL parser is exact for valid text but rejects what is not (unterminated quotes,
/// backslash commands), and statements are split before the server has seen them. This scanner is what splits scripts
/// and finds literals to redact; the native one is the oracle the tests compare it with.
/// </para>
/// </summary>
public static class PgLexicalScanner
{
    private const string OperatorChars = "+-*/<>=~!@#%^&|`?";

    public static IReadOnlyList<PgLexToken> Scan(string text)
    {
        var tokens = new List<PgLexToken>();
        var n = text.Length;
        var i = 0;

        while (i < n)
        {
            var c = text[i];
            var start = i;

            if (IsWhite(c))
            {
                while (i < n && IsWhite(text[i])) i++;
                tokens.Add(new PgLexToken(PgTokenKind.Whitespace, start, i));
            }
            else if (c == '-' && i + 1 < n && text[i + 1] == '-')
            {
                while (i < n && text[i] != '\n' && text[i] != '\r') i++;
                tokens.Add(new PgLexToken(PgTokenKind.LineComment, start, i));
            }
            else if (c == '/' && i + 1 < n && text[i + 1] == '*')
            {
                i = ScanBlockComment(text, i, out var terminated);
                tokens.Add(new PgLexToken(PgTokenKind.BlockComment, start, i, terminated));
            }
            else if (c == '\'')
            {
                i = ScanQuoted(text, i, '\'', backslashEscapes: false, out var terminated);
                tokens.Add(new PgLexToken(PgTokenKind.String, start, i, terminated));
            }
            else if (c == '"')
            {
                i = ScanQuoted(text, i, '"', backslashEscapes: false, out var terminated);
                tokens.Add(new PgLexToken(PgTokenKind.QuotedIdentifier, start, i, terminated));
            }
            else if (c == '$')
            {
                i = ScanDollar(text, i, out var kind, out var terminated);
                tokens.Add(new PgLexToken(kind, start, i, terminated));
            }
            else if (c == ';')
            {
                i++;
                tokens.Add(new PgLexToken(PgTokenKind.Semicolon, start, i));
            }
            else if (c == '\\')
            {
                while (i < n && text[i] != '\n' && text[i] != '\r') i++;
                tokens.Add(new PgLexToken(PgTokenKind.MetaCommand, start, i));
            }
            else if (IsDigit(c) || (c == '.' && i + 1 < n && IsDigit(text[i + 1])))
            {
                i = ScanNumber(text, i);
                tokens.Add(new PgLexToken(PgTokenKind.Number, start, i));
            }
            else if (IsIdentifierStart(c))
            {
                i = ScanIdentifierOrPrefixedLiteral(text, i, out var kind, out var terminated);
                tokens.Add(new PgLexToken(kind, start, i, terminated));
            }
            else if (c == ':' && i + 1 < n && text[i + 1] == ':')
            {
                i += 2;
                tokens.Add(new PgLexToken(PgTokenKind.Operator, start, i));
            }
            else if (OperatorChars.IndexOf(c) >= 0)
            {
                i++;
                while (i < n && OperatorChars.IndexOf(text[i]) >= 0 && !StartsComment(text, i)) i++;
                tokens.Add(new PgLexToken(PgTokenKind.Operator, start, i));
            }
            else if ("()[],.:".IndexOf(c) >= 0)
            {
                i++;
                tokens.Add(new PgLexToken(PgTokenKind.Punctuation, start, i));
            }
            else
            {
                i++;
                tokens.Add(new PgLexToken(PgTokenKind.Other, start, i));
            }
        }

        return tokens;
    }

    private static bool IsWhite(char c) => c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';

    private static bool IsDigit(char c) => c >= '0' && c <= '9';

    // PostgreSQL: letters (including those with diacritics and non-Latin letters), underscore, and any high-bit byte.
    private static bool IsIdentifierStart(char c) => c == '_' || char.IsLetter(c) || c >= 0x80;

    private static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || IsDigit(c) || c == '$';

    private static bool StartsComment(string text, int i) =>
        i + 1 < text.Length && ((text[i] == '-' && text[i + 1] == '-') || (text[i] == '/' && text[i + 1] == '*'));

    private static int ScanBlockComment(string text, int start, out bool terminated)
    {
        var depth = 1;
        var i = start + 2;
        while (i < text.Length)
        {
            if (text[i] == '/' && i + 1 < text.Length && text[i + 1] == '*') { depth++; i += 2; }
            else if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')
            {
                depth--;
                i += 2;
                if (depth == 0) { terminated = true; return i; }
            }
            else i++;
        }
        terminated = false;
        return text.Length;
    }

    /// <summary>Scans from the opening quote at <paramref name="open"/>; a doubled quote is an escaped quote.</summary>
    private static int ScanQuoted(string text, int open, char quote, bool backslashEscapes, out bool terminated)
    {
        var i = open + 1;
        while (i < text.Length)
        {
            var c = text[i];
            if (backslashEscapes && c == '\\') { i += 2; continue; }
            if (c == quote)
            {
                if (i + 1 < text.Length && text[i + 1] == quote) { i += 2; continue; }
                terminated = true;
                return i + 1;
            }
            i++;
        }
        terminated = false;
        return text.Length;
    }

    private static int ScanDollar(string text, int start, out PgTokenKind kind, out bool terminated)
    {
        terminated = true;
        var n = text.Length;
        if (start + 1 < n && IsDigit(text[start + 1]))
        {
            var i = start + 1;
            while (i < n && IsDigit(text[i])) i++;
            kind = PgTokenKind.Parameter;
            return i;
        }

        // $$ ... $$ or $tag$ ... $tag$
        string? tag = null;
        var bodyStart = start + 1;
        if (start + 1 < n && text[start + 1] == '$')
        {
            tag = "$$";
            bodyStart = start + 2;
        }
        else if (start + 1 < n && (text[start + 1] == '_' || char.IsLetter(text[start + 1]) || text[start + 1] >= 0x80))
        {
            var j = start + 1;
            while (j < n && (text[j] == '_' || char.IsLetterOrDigit(text[j]) || text[j] >= 0x80)) j++;
            if (j < n && text[j] == '$')
            {
                tag = text.Substring(start, j - start + 1);
                bodyStart = j + 1;
            }
        }

        if (tag == null)
        {
            kind = PgTokenKind.Other;
            return start + 1;
        }

        kind = PgTokenKind.DollarString;
        var close = text.IndexOf(tag, bodyStart, StringComparison.Ordinal);
        if (close < 0)
        {
            terminated = false;
            return n;
        }
        return close + tag.Length;
    }

    private static int ScanNumber(string text, int start)
    {
        var n = text.Length;
        var i = start;
        if (text[i] == '0' && i + 1 < n && "xXoObB".IndexOf(text[i + 1]) >= 0)
        {
            i += 2;
            while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
            return i;
        }

        while (i < n && (IsDigit(text[i]) || text[i] == '_')) i++;
        if (i < n && text[i] == '.' && !(i + 1 < n && text[i + 1] == '.'))
        {
            i++;
            while (i < n && (IsDigit(text[i]) || text[i] == '_')) i++;
        }
        if (i < n && (text[i] == 'e' || text[i] == 'E'))
        {
            var j = i + 1;
            if (j < n && (text[j] == '+' || text[j] == '-')) j++;
            if (j < n && IsDigit(text[j]))
            {
                while (j < n && IsDigit(text[j])) j++;
                i = j;
            }
        }
        return i;
    }

    private static int ScanIdentifierOrPrefixedLiteral(string text, int start, out PgTokenKind kind, out bool terminated)
    {
        terminated = true;
        var n = text.Length;
        var i = start;
        while (i < n && IsIdentifierPart(text[i])) i++;

        // E'..' (backslash escapes), B'..', X'..', N'..': a single prefix letter glued to a quote.
        if (i - start == 1 && i < n && text[i] == '\'' && "EeBbXxNn".IndexOf(text[start]) >= 0)
        {
            var escapes = text[start] == 'E' || text[start] == 'e';
            kind = PgTokenKind.String;
            return ScanQuoted(text, i, '\'', escapes, out terminated);
        }

        // U&'..' and U&"..": the prefix is U, an ampersand, then the quote.
        if (i - start == 1 && (text[start] == 'U' || text[start] == 'u') && i + 1 < n && text[i] == '&'
            && (text[i + 1] == '\'' || text[i + 1] == '"'))
        {
            var quote = text[i + 1];
            kind = quote == '\'' ? PgTokenKind.String : PgTokenKind.QuotedIdentifier;
            return ScanQuoted(text, i + 1, quote, backslashEscapes: false, out terminated);
        }

        kind = PgTokenKind.Identifier;
        return i;
    }
}
