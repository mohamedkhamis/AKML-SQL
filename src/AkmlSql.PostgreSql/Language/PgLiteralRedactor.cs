using System.Text;

namespace AkmlSql.PostgreSql.Language;

/// <summary>
/// The outcome of <see cref="PgLiteralRedactor.Redact"/>. When <see cref="Ok"/> is false the text could not be read to
/// the end, <see cref="Text"/> is empty and the caller must not send the original anywhere it did not already go.
/// </summary>
/// <param name="Ok">True when every literal was found and replaced.</param>
/// <param name="Text">The text with each literal replaced by a placeholder; empty when <paramref name="Ok"/> is false.</param>
/// <param name="Placeholders">
/// Placeholder as it appears in <paramref name="Text"/> (<c>'__STR_0__'</c>, <c>__NUM_1__</c>, <c>$$__STR_2__$$</c>) to the
/// literal it replaced, exactly as written. The caller uses it to put the literals back into an answer.
/// </param>
/// <param name="Error">Why the text was refused. Never contains any of the text.</param>
public sealed record PgRedaction(bool Ok, string Text, IReadOnlyDictionary<string, string> Placeholders, string? Error);

/// <summary>
/// Spec 042 (FR-081, research R20) - removes the literal values from PostgreSQL text before it goes to a cloud AI
/// provider, keeping everything else: identifiers, keywords, operators, comments, casts, parameters and layout. It is the
/// PostgreSQL counterpart of the T-SQL <c>LiteralRedactor</c>, which parses with ScriptDom and cannot read this text.
///
/// <para>
/// It works on the managed <see cref="PgLexicalScanner"/> tokens, not a parse, so it also reads scripts that do not parse
/// (the case where someone asks the assistant to fix them). Strings of every form (<c>'x'</c>, <c>E'x'</c>, <c>U&amp;'x'</c>,
/// <c>B'1'</c>, <c>X'1F'</c>, <c>N'x'</c>) become <c>'__STR_n__'</c>, numbers become <c>__NUM_n__</c>, and a dollar-quoted
/// string keeps its tag around the placeholder. A dollar-quoted body that is code - after <c>AS</c> or <c>DO</c> - is read
/// as code and redacted inside, so an explained function still shows its logic; if the body cannot be read as PostgreSQL
/// text (another language, for example) the whole body is one literal.
/// </para>
///
/// <para>
/// A number directly after <c>LIMIT</c>, <c>OFFSET</c> or <c>FETCH FIRST|NEXT</c> is a row count, part of the shape of the
/// query, and stays. Comments stay, as they do for the T-SQL redactor. Text that ends inside a string, quoted name,
/// dollar-quoted string or block comment is refused: what follows the open quote might be a literal the scanner cannot
/// delimit, so nothing from it may be sent.
/// </para>
/// </summary>
public static class PgLiteralRedactor
{
    private const int MaxBodyDepth = 8;

    public static PgRedaction Redact(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var state = new RedactionState();
        var output = new StringBuilder(text.Length);

        var error = RedactSegment(text, output, state, depth: 0);
        if (error != null)
            return new PgRedaction(false, string.Empty, new Dictionary<string, string>(), error);

        return new PgRedaction(true, output.ToString(), state.Placeholders, null);
    }

    private sealed class RedactionState
    {
        public Dictionary<string, string> Placeholders { get; } = new(StringComparer.Ordinal);
        public int Strings;
        public int Numbers;

        /// <summary>A scratch state for an attempt that may be thrown away: same numbering, no placeholders yet.</summary>
        public RedactionState Fork() => new() { Strings = Strings, Numbers = Numbers };

        /// <summary>Keeps what a successful attempt recorded.</summary>
        public void Merge(RedactionState attempt)
        {
            foreach (var pair in attempt.Placeholders) Placeholders[pair.Key] = pair.Value;
            Strings = attempt.Strings;
            Numbers = attempt.Numbers;
        }
    }

    /// <summary>Redacts <paramref name="text"/> into <paramref name="output"/>; returns why it could not, or null.</summary>
    private static string? RedactSegment(string text, StringBuilder output, RedactionState state, int depth)
    {
        var tokens = PgLexicalScanner.Scan(text);

        // The last two significant words (upper case), to tell a row count from a value and a code body from a string.
        string? previous = null;
        string? beforePrevious = null;

        var position = 0;
        foreach (var token in tokens)
        {
            if (token.Start > position) output.Append(text, position, token.Start - position);
            position = token.End;

            if (!token.Terminated && token.Kind != PgTokenKind.LineComment)
                return Unterminated(token.Kind);

            var span = text.AsSpan(token.Start, token.End - token.Start);

            switch (token.Kind)
            {
                case PgTokenKind.Whitespace:
                case PgTokenKind.LineComment:
                case PgTokenKind.BlockComment:
                    output.Append(span);
                    continue; // not significant: the words before and after still belong together

                case PgTokenKind.String:
                    AppendPlaceholder(output, state, "'", NextString(state), "'", span);
                    break;

                case PgTokenKind.Number:
                    if (IsRowCount(previous, beforePrevious)) output.Append(span);
                    else AppendPlaceholder(output, state, string.Empty, NextNumber(state), string.Empty, span);
                    break;

                case PgTokenKind.DollarString:
                    var error = AppendDollarString(text, token, output, state, depth, IsCodePosition(previous));
                    if (error != null) return error;
                    break;

                default:
                    output.Append(span);
                    break;
            }

            beforePrevious = previous;
            previous = token.Kind == PgTokenKind.Identifier ? span.ToString().ToUpperInvariant() : null;
        }

        if (position < text.Length) output.Append(text, position, text.Length - position);
        return null;
    }

    private static string? AppendDollarString(
        string text, PgLexToken token, StringBuilder output, RedactionState state, int depth, bool isCode)
    {
        var literal = text.Substring(token.Start, token.End - token.Start);
        var tagEnd = literal.IndexOf('$', 1) + 1; // "$$" -> 2, "$tag$" -> 5; the scanner only makes this token with a closing tag
        var tag = literal[..tagEnd];
        var body = literal[tagEnd..^tagEnd];

        if (isCode && depth < MaxBodyDepth)
        {
            var attempt = state.Fork();
            var inner = new StringBuilder(body.Length);
            if (RedactSegment(body, inner, attempt, depth + 1) == null)
            {
                state.Merge(attempt);
                output.Append(tag).Append(inner).Append(tag);
                return null;
            }

            // Not PostgreSQL text (another language, or a truncated body): the attempt is dropped and the whole body is one
            // literal, which is the safe reading.
        }

        var name = NextString(state);
        var placeholder = tag + name + tag;
        state.Placeholders[placeholder] = literal;
        output.Append(placeholder);
        return null;
    }

    private static void AppendPlaceholder(StringBuilder output, RedactionState state, string open, string name, string close, ReadOnlySpan<char> original)
    {
        var placeholder = open + name + close;
        state.Placeholders[placeholder] = original.ToString();
        output.Append(placeholder);
    }

    private static string NextString(RedactionState state) => $"__STR_{state.Strings++}__";

    private static string NextNumber(RedactionState state) => $"__NUM_{state.Numbers++}__";

    /// <summary>A number after LIMIT, OFFSET or FETCH FIRST|NEXT counts rows; it shapes the query and carries no data.</summary>
    private static bool IsRowCount(string? previous, string? beforePrevious) =>
        previous is "LIMIT" or "OFFSET"
        || (previous is "FIRST" or "NEXT" && beforePrevious == "FETCH");

    /// <summary>A dollar-quoted string right after AS (a function or procedure body) or DO (an anonymous block) is code.</summary>
    private static bool IsCodePosition(string? previous) => previous is "AS" or "DO";

    private static string Unterminated(PgTokenKind kind) => kind switch
    {
        PgTokenKind.String => "The text ends inside a string literal, so its literals cannot be found.",
        PgTokenKind.QuotedIdentifier => "The text ends inside a quoted name, so its literals cannot be found.",
        PgTokenKind.DollarString => "The text ends inside a dollar-quoted string, so its literals cannot be found.",
        PgTokenKind.BlockComment => "The text ends inside a block comment, so its literals cannot be found.",
        _ => "The text could not be read to the end, so its literals cannot be found.",
    };
}
