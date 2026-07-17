namespace AkmlSql.Engine.Parser;

public static class SuffixCompletionHelper
{
    private const string DummyIdentifier = "__akml_dummy__";

    /// Append dummy tokens to incomplete SQL to produce a valid AST
    public static string AppendDummyTokens(string sql)
    {
        var trimmed = sql.TrimEnd();
        if (string.IsNullOrEmpty(trimmed))
        {
            return $"SELECT {DummyIdentifier}";
        }

        if (TryRepairTail(trimmed, out var repaired))
        {
            return repaired;
        }

        // Default: append dummy identifier
        return trimmed + $" {DummyIdentifier}";
    }

    /// <summary>
    /// Spec 032 (A1/E6): repair incomplete SQL AT the caret instead of the document tail.
    /// Applies the same tail patterns as <see cref="AppendDummyTokens"/> to the text
    /// BEFORE the caret and reattaches the untouched suffix, so a document that is broken
    /// exactly at the caret (e.g. <c>… IN (SELECT | FROM T)</c>, or inside a later CTE
    /// body) becomes parseable while everything after the caret is preserved.
    /// Returns the input unchanged when no explicit pattern matches — the caller falls
    /// back to plain tail repair (the catch-all dummy append would corrupt a prefix that
    /// merely ends in an identifier).
    /// </summary>
    public static string RepairAtCursor(string sql, int cursorOffset)
    {
        if (string.IsNullOrEmpty(sql)) return sql;
        if (cursorOffset < 0) cursorOffset = 0;
        if (cursorOffset > sql.Length) cursorOffset = sql.Length;

        var prefix = sql.Substring(0, cursorOffset);
        var trimmedPrefix = prefix.TrimEnd();
        if (trimmedPrefix.Length == 0) return sql;

        if (!TryRepairTail(trimmedPrefix, out var repairedPrefix))
        {
            return sql;
        }

        return repairedPrefix + sql.Substring(cursorOffset);
    }

    /// <summary>
    /// The explicit tail-repair patterns. Returns false when only the catch-all default
    /// would apply. <paramref name="trimmed"/> must already be right-trimmed.
    /// </summary>
    private static bool TryRepairTail(string trimmed, out string repaired)
    {
        var upper = trimmed.ToUpperInvariant();

        // After SELECT — needs column list
        if (EndsWithKeyword(upper, "SELECT"))
        {
            repaired = trimmed + $" {DummyIdentifier}";
            return true;
        }

        // After FROM — needs table reference
        if (EndsWithKeyword(upper, "FROM"))
        {
            repaired = trimmed + $" {DummyIdentifier}";
            return true;
        }

        // After JOIN — needs table reference AND an ON clause for the parser to accept it.
        // Without ON, the parser rejects the JOIN and we lose earlier aliases from the AST.
        // CROSS JOIN doesn't require ON, so handle it separately.
        if (upper.EndsWith("CROSS JOIN"))
        {
            repaired = trimmed + $" {DummyIdentifier}";
            return true;
        }

        if (EndsWithKeyword(upper, "JOIN"))
        {
            repaired = trimmed + $" {DummyIdentifier} ON 1=1";
            return true;
        }

        // After WHERE/AND/OR — needs expression. Word-boundary checked (spec 032 H4):
        // "…dbo.Or" / "…Grand" are partially typed identifiers, not operators.
        if (EndsWithKeyword(upper, "WHERE") || EndsWithKeyword(upper, "AND") || EndsWithKeyword(upper, "OR"))
        {
            repaired = trimmed + $" {DummyIdentifier} = 1";
            return true;
        }

        // After JOIN-ON — needs a boolean predicate. Use the leading-space
        // form (" ON") so this doesn't match keywords ending in "ON" like
        // UNION (ends in "ION"). The single-line case sql == "ON" is also
        // accepted.
        if (upper.EndsWith(" ON") || upper == "ON")
        {
            repaired = trimmed + $" {DummyIdentifier} = 1";
            return true;
        }

        // After SET — needs column = value
        if (EndsWithKeyword(upper, "SET"))
        {
            repaired = trimmed + $" {DummyIdentifier} = 1";
            return true;
        }

        // After dot — needs identifier. In a BOOLEAN position (WHERE/AND/OR/ON/HAVING
        // owns the dotted reference) a bare column isn't a valid predicate, so complete
        // it into a comparison — `… WHERE i.` must parse as `… WHERE i.dummy = 1`
        // (spec 032, CTE-042: mid-CTE-body carets depend on this parsing clean).
        if (trimmed.EndsWith("."))
        {
            var booleanContext = System.Text.RegularExpressions.Regex.IsMatch(
                upper, @"\b(WHERE|AND|OR|ON|HAVING|WHEN)\s+[\w\[\]""@#$.]*\.$");
            repaired = booleanContext
                ? trimmed + DummyIdentifier + " = 1"
                : trimmed + DummyIdentifier;
            return true;
        }

        // After EXEC/EXECUTE — needs proc name
        if (EndsWithKeyword(upper, "EXEC") || EndsWithKeyword(upper, "EXECUTE"))
        {
            repaired = trimmed + $" {DummyIdentifier}";
            return true;
        }

        // After ORDER BY / GROUP BY — needs column
        if (upper.EndsWith("ORDER BY") || upper.EndsWith("GROUP BY"))
        {
            repaired = trimmed + $" {DummyIdentifier}";
            return true;
        }

        repaired = trimmed;
        return false;
    }

    /// <summary>
    /// True when <paramref name="upper"/> ends with <paramref name="keyword"/> at a word
    /// boundary — i.e. the preceding character (if any) cannot be part of an identifier.
    /// Prevents identifier tails ("dbo.Or", "Grand") from being misread as operators.
    /// </summary>
    private static bool EndsWithKeyword(string upper, string keyword)
    {
        if (!upper.EndsWith(keyword, StringComparison.Ordinal)) return false;
        int idx = upper.Length - keyword.Length;
        if (idx == 0) return true;
        char c = upper[idx - 1];
        return !(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '@' || c == '#' || c == '$' || c == ']' || c == '"');
    }

    public static bool IsDummyIdentifier(string name)
    {
        return name.Equals(DummyIdentifier, StringComparison.OrdinalIgnoreCase);
    }
}
