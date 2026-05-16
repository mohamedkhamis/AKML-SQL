namespace AkmlSql.Engine.Completion;

public static class FuzzyMatcher
{
    /// Returns a match score (0 = no match, higher = better match)
    public static int Score(string filter, string text)
    {
        if (string.IsNullOrEmpty(filter) || string.IsNullOrEmpty(text))
        {
            return 0;
        }

        // Level 1: Exact prefix match
        if (text.StartsWith(filter, StringComparison.Ordinal))
        {
            return 1000 + (text.Length - filter.Length == 0 ? 100 : 0);
        }

        // Level 2: Case-insensitive prefix match
        if (text.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
        {
            return 800;
        }

        // Level 3: CamelCase match (e.g., "OD" → "OrderDate")
        if (MatchesCamelCase(filter, text))
        {
            return 600;
        }

        // Level 4: Substring match
        if (text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return 400;
        }

        // Level 5: Non-contiguous character match
        if (MatchesNonContiguous(filter, text))
        {
            return 200;
        }

        return 0;
    }

    private static bool MatchesCamelCase(string filter, string text)
    {
        int filterIdx = 0;
        for (int i = 0; i < text.Length && filterIdx < filter.Length; i++)
        {
            // Underscore is a boundary marker — compare the character AFTER it (#18)
            if (text[i] == '_')
            {
                if (i + 1 < text.Length)
                {
                    i++; // skip underscore, check next char
                    if (char.ToUpperInvariant(text[i]) == char.ToUpperInvariant(filter[filterIdx]))
                        filterIdx++;
                }
                continue;
            }

            if (i == 0 || char.IsUpper(text[i]))
            {
                if (char.ToUpperInvariant(text[i]) == char.ToUpperInvariant(filter[filterIdx]))
                {
                    filterIdx++;
                }
            }
        }
        return filterIdx == filter.Length;
    }

    private static bool MatchesNonContiguous(string filter, string text)
    {
        int filterIdx = 0;
        for (int i = 0; i < text.Length && filterIdx < filter.Length; i++)
        {
            if (char.ToUpperInvariant(text[i]) == char.ToUpperInvariant(filter[filterIdx]))
            {
                filterIdx++;
            }
        }
        return filterIdx == filter.Length;
    }
}
