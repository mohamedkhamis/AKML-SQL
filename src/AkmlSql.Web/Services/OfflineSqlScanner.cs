using System;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) — M5 task T109 follow-up. Tiny SQL-token scanner used by
/// the offline QuickInfo / SignatureHelp paths. It is NOT a parser — just enough
/// character-walking to identify the identifier under the caret and the enclosing
/// function-call (if any). The online engine's TSql170Parser is the source of truth;
/// this scanner exists purely so the offline experience can degrade gracefully
/// instead of returning empty.
///
/// <para>
/// Trade-offs we accept:
/// <list type="bullet">
///   <item>String literals + comments are treated like normal text (cheap; the
///   worst-case wrong answer is "no match found", which is what an empty bridge
///   would have returned anyway).</item>
///   <item>Bracketed [Identifiers With Spaces] are tolerated for the basic
///   walk but not aggressively (cursor inside the brackets walks back to <c>[</c>).</item>
///   <item>Alias resolution (e.g. <c>SELECT c.Name FROM Customers c</c>) is NOT
///   attempted — the offline lookup returns the unresolved <c>c</c> prefix; the
///   caller's lookup logic falls back to a schema-agnostic search when the
///   prefix doesn't match a known schema.</item>
/// </list>
/// </para>
/// </summary>
internal static class OfflineSqlScanner
{
    /// <summary>The identifier under or immediately to the left of the caret.</summary>
    public readonly struct IdentifierAtCaret
    {
        /// <summary>Empty when nothing useful is at the caret.</summary>
        public string Identifier { get; init; }

        /// <summary>The dotted prefix (e.g. "dbo" in <c>dbo.Customers</c>, "c" in <c>c.Name</c>). Empty if absent.</summary>
        public string Prefix { get; init; }

        /// <summary>Convenience: true when <see cref="Identifier"/> is non-empty.</summary>
        public bool IsValid => !string.IsNullOrEmpty(Identifier);
    }

    /// <summary>The enclosing call-site (e.g. <c>FORMAT(value, 'd' &lt;CARET&gt; )</c>).</summary>
    public readonly struct CallSite
    {
        public string FunctionName { get; init; }
        public string Prefix { get; init; }
        /// <summary>Zero-based comma-separated parameter index at the caret.</summary>
        public int ParameterIndex { get; init; }
        public bool IsValid => !string.IsNullOrEmpty(FunctionName);
    }

    /// <summary>Find the identifier (and optional dotted prefix) at <paramref name="offset"/>.</summary>
    public static IdentifierAtCaret FindIdentifierAt(string text, int offset)
    {
        if (string.IsNullOrEmpty(text)) return default;
        if (offset < 0) offset = 0;
        if (offset > text.Length) offset = text.Length;

        // Walk left from offset while the previous char is part of an identifier.
        int left = offset;
        while (left > 0 && IsIdentChar(text[left - 1])) left--;

        // Walk right from offset while the current char is part of an identifier.
        int right = offset;
        while (right < text.Length && IsIdentChar(text[right])) right++;

        if (left == right) return default;   // caret is not on an identifier

        var current = text.Substring(left, right - left);

        // Look for a dotted prefix immediately before the identifier we found.
        // Skip any whitespace between the dot and the prefix's last char so we
        // also handle 'dbo .Customers' (rare but tolerable).
        int dotProbe = left - 1;
        while (dotProbe >= 0 && IsHorizontalWhitespace(text[dotProbe])) dotProbe--;
        if (dotProbe >= 0 && text[dotProbe] == '.')
        {
            int prefixEnd = dotProbe;
            while (prefixEnd > 0 && IsHorizontalWhitespace(text[prefixEnd - 1])) prefixEnd--;
            int prefixStart = prefixEnd;
            while (prefixStart > 0 && IsIdentChar(text[prefixStart - 1])) prefixStart--;
            if (prefixStart < prefixEnd)
            {
                return new IdentifierAtCaret
                {
                    Identifier = current,
                    Prefix = text.Substring(prefixStart, prefixEnd - prefixStart),
                };
            }
        }

        return new IdentifierAtCaret { Identifier = current, Prefix = string.Empty };
    }

    /// <summary>Find the function-call enclosing <paramref name="offset"/>, if any.</summary>
    public static CallSite FindEnclosingCall(string text, int offset)
    {
        if (string.IsNullOrEmpty(text)) return default;
        if (offset < 0) offset = 0;
        if (offset > text.Length) offset = text.Length;

        int depth = 0;
        int paramIndex = 0;
        for (int i = offset - 1; i >= 0; i--)
        {
            var c = text[i];
            if (c == ')') { depth++; continue; }
            if (c == '(')
            {
                if (depth == 0)
                {
                    // Walk back over whitespace, then the function-name identifier.
                    int end = i;
                    while (end > 0 && IsHorizontalWhitespace(text[end - 1])) end--;
                    int start = end;
                    while (start > 0 && IsIdentChar(text[start - 1])) start--;
                    if (start == end) return default;
                    var name = text.Substring(start, end - start);

                    // Optional dotted prefix.
                    int dotProbe = start - 1;
                    while (dotProbe >= 0 && IsHorizontalWhitespace(text[dotProbe])) dotProbe--;
                    var prefix = string.Empty;
                    if (dotProbe >= 0 && text[dotProbe] == '.')
                    {
                        int pEnd = dotProbe;
                        while (pEnd > 0 && IsHorizontalWhitespace(text[pEnd - 1])) pEnd--;
                        int pStart = pEnd;
                        while (pStart > 0 && IsIdentChar(text[pStart - 1])) pStart--;
                        if (pStart < pEnd) prefix = text.Substring(pStart, pEnd - pStart);
                    }

                    return new CallSite
                    {
                        FunctionName = name,
                        Prefix = prefix,
                        ParameterIndex = paramIndex,
                    };
                }
                depth--;
                continue;
            }
            if (c == ',' && depth == 0) paramIndex++;
        }
        return default;
    }

    private static bool IsIdentChar(char c) =>
        c == '_' || (c >= '0' && c <= '9')
                 || (c >= 'A' && c <= 'Z')
                 || (c >= 'a' && c <= 'z');

    private static bool IsHorizontalWhitespace(char c) => c == ' ' || c == '\t';
}
