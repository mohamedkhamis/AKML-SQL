namespace AkmlSql.PostgreSql.Language;

/// <summary>
/// The first words of a statement, upper-cased: the identifiers from its start, past white space and comments, up to the first
/// token that is not one (a parenthesis, a quoted name, a literal). The command tag, the transaction tracker and the inspectors
/// all read a statement by them. The splitter records them while it reads the script, so a statement of a run is read into
/// tokens once (<see cref="PgStatement.LeadingWords"/>); the text form here is for text that was not split.
/// </summary>
public static class PgLeadingWords
{
    /// <summary>How many words any reader needs: the longest forms are <c>CREATE OR REPLACE TRIGGER</c>-like object kinds and <c>ROLLBACK WORK TO SAVEPOINT x</c>.</summary>
    public const int Max = 8;

    public static IReadOnlyList<string> Of(string text, int max = Max)
    {
        var words = new List<string>();
        foreach (var token in PgLexicalScanner.Scan(text))
        {
            if (!Take(text, token, words, max)) break;
        }
        return words;
    }

    /// <summary>
    /// Adds <paramref name="token"/>'s word to <paramref name="words"/> when it is one of the leading words; returns false once the
    /// leading words have ended (a token that is not an identifier, or the limit), so a caller walking tokens can stop looking.
    /// </summary>
    internal static bool Take(string text, PgLexToken token, List<string> words, int max = Max)
    {
        if (token.Kind is PgTokenKind.Whitespace or PgTokenKind.LineComment or PgTokenKind.BlockComment) return true;
        if (token.Kind != PgTokenKind.Identifier || words.Count >= max) return false;
        words.Add(text.Substring(token.Start, token.End - token.Start).ToUpperInvariant());
        return words.Count < max;
    }
}
