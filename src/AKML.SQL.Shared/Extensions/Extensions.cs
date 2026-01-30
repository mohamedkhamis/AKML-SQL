namespace AKML.SQL.Shared.Extensions;

/// <summary>
/// String extension methods.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Gets the word at the specified position in the text.
    /// </summary>
    public static string GetWordAt(this string text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset < 0 || offset > text.Length)
            return string.Empty;

        int start = offset;
        int end = offset;

        // Find start of word
        while (start > 0 && IsWordChar(text[start - 1]))
            start--;

        // Find end of word
        while (end < text.Length && IsWordChar(text[end]))
            end++;

        return text.Substring(start, end - start);
    }

    /// <summary>
    /// Gets the word before the cursor position.
    /// </summary>
    public static string GetWordBefore(this string text, int offset)
    {
        if (string.IsNullOrEmpty(text) || offset <= 0 || offset > text.Length)
            return string.Empty;

        int end = offset;
        int start = offset;

        // Move back through word characters
        while (start > 0 && IsWordChar(text[start - 1]))
            start--;

        return text.Substring(start, end - start);
    }

    /// <summary>
    /// Converts offset to line and column.
    /// </summary>
    public static (int Line, int Column) OffsetToLineColumn(this string text, int offset)
    {
        if (string.IsNullOrEmpty(text))
            return (0, 0);

        int line = 0;
        int column = 0;
        
        for (int i = 0; i < Math.Min(offset, text.Length); i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 0;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    /// <summary>
    /// Converts line and column to offset.
    /// </summary>
    public static int LineColumnToOffset(this string text, int line, int column)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        int currentLine = 0;
        int offset = 0;

        while (offset < text.Length && currentLine < line)
        {
            if (text[offset] == '\n')
                currentLine++;
            offset++;
        }

        return Math.Min(offset + column, text.Length);
    }

    /// <summary>
    /// Checks if a character is a valid SQL identifier character.
    /// </summary>
    public static bool IsWordChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$';
    }

    /// <summary>
    /// Calculates fuzzy match score between two strings.
    /// </summary>
    public static int FuzzyMatchScore(this string text, string pattern, bool caseSensitive = false)
    {
        if (string.IsNullOrEmpty(pattern))
            return 100;
        
        if (string.IsNullOrEmpty(text))
            return 0;

        string compareText = caseSensitive ? text : text.ToLowerInvariant();
        string comparePattern = caseSensitive ? pattern : pattern.ToLowerInvariant();

        // Exact match
        if (compareText == comparePattern)
            return 100;

        // Starts with
        if (compareText.StartsWith(comparePattern))
            return 90 + (comparePattern.Length * 10 / compareText.Length);

        // Contains
        if (compareText.Contains(comparePattern))
            return 70 + (comparePattern.Length * 10 / compareText.Length);

        // Fuzzy match - all characters present in order
        int patternIndex = 0;
        int score = 0;
        int consecutiveBonus = 0;

        for (int i = 0; i < compareText.Length && patternIndex < comparePattern.Length; i++)
        {
            if (compareText[i] == comparePattern[patternIndex])
            {
                score += 10 + consecutiveBonus;
                consecutiveBonus += 5; // Bonus for consecutive matches
                patternIndex++;
            }
            else
            {
                consecutiveBonus = 0;
            }
        }

        if (patternIndex < comparePattern.Length)
            return 0; // Not all characters matched

        return Math.Min(score * 100 / (comparePattern.Length * 20), 65);
    }

    /// <summary>
    /// Truncates string to specified length with ellipsis.
    /// </summary>
    public static string Truncate(this string text, int maxLength, string suffix = "...")
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength - suffix.Length) + suffix;
    }

    /// <summary>
    /// Normalizes SQL whitespace.
    /// </summary>
    public static string NormalizeSqlWhitespace(this string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return sql;

        // Replace multiple whitespace with single space
        var normalized = System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ");
        return normalized.Trim();
    }
}

/// <summary>
/// Date/Time extension methods.
/// </summary>
public static class DateTimeExtensions
{
    /// <summary>
    /// Converts DateTime to Unix timestamp (milliseconds).
    /// </summary>
    public static long ToUnixTimeMilliseconds(this DateTime dateTime)
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (long)(dateTime.ToUniversalTime() - epoch).TotalMilliseconds;
    }

    /// <summary>
    /// Converts Unix timestamp (milliseconds) to DateTime.
    /// </summary>
    public static DateTime FromUnixTimeMilliseconds(long milliseconds)
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddMilliseconds(milliseconds);
    }
}
