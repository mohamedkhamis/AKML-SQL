namespace AkmlSql.PostgreSql.Language;

/// <summary>
/// Spec 042 (FR-043): whether a statement that ran changed the catalog, so the Schema explorer reloads. The command tag says it for
/// most statements (<c>CREATE TABLE</c>, <c>DROP VIEW</c>), but the server reports <c>CREATE TABLE … AS</c>,
/// <c>CREATE MATERIALIZED VIEW … AS</c> and <c>SELECT … INTO</c> as <c>SELECT n</c>; those are read from the statement itself. A
/// statement counted here by mistake costs one catalog reload, never a wrong result.
/// </summary>
public static class PgCatalogChange
{
    /// <param name="tag">The command tag the run reported for the statement.</param>
    /// <param name="tagChangesSchema">The shared tag rule (<c>AkmlSql.Core.Text.PgCommandTags.ChangesSchema</c>).</param>
    public static bool Changes(PgStatement statement, string? tag, Func<string?, bool> tagChangesSchema)
    {
        if (tagChangesSchema(tag)) return true;
        var words = statement.LeadingWords;
        if (words.Count == 0) return false;
        return words[0] switch
        {
            "CREATE" or "ALTER" or "DROP" or "COMMENT" => true,
            "SELECT" => HasTopLevelInto(statement.Text),
            _ => false,
        };
    }

    // SELECT … INTO new_table: an INTO outside every parenthesis. Strings, quoted names and comments are single tokens, so an
    // "into" inside them is never seen as the keyword.
    private static bool HasTopLevelInto(string text)
    {
        var depth = 0;
        foreach (var token in PgLexicalScanner.Scan(text))
        {
            if (token.Kind == PgTokenKind.Punctuation && token.End - token.Start == 1)
            {
                var c = text[token.Start];
                if (c == '(') depth++;
                else if (c == ')' && depth > 0) depth--;
            }
            else if (depth == 0 && token.Kind == PgTokenKind.Identifier && token.End - token.Start == 4
                     && string.Compare(text, token.Start, "INTO", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }
        }
        return false;
    }
}
