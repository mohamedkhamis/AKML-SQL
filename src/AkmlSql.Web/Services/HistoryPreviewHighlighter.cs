using System;
using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Web.Services;

/// <summary>
/// Pure helper that turns SQL preview text into a list of contiguous segments classified for syntax
/// coloring (keyword / string / comment / default) and flagged for search-term highlighting. Ported
/// from the desktop <c>SqlPreviewTokenizer</c> + RenderPreview clip logic. The concatenated segment
/// text always equals the input verbatim, so the page can render it safely (Blazor escapes the text).
/// </summary>
public static class HistoryPreviewHighlighter
{
    public const string KindKeyword = "keyword";
    public const string KindString = "string";
    public const string KindComment = "comment";
    public const string KindDefault = "default";

    public readonly record struct Segment(string Text, string Kind, bool Hit);

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT","FROM","WHERE","INSERT","UPDATE","DELETE","INTO","VALUES","SET","JOIN","INNER","LEFT",
        "RIGHT","FULL","OUTER","CROSS","ON","AS","AND","OR","NOT","NULL","IS","IN","EXISTS","BETWEEN",
        "LIKE","GROUP","BY","ORDER","HAVING","DISTINCT","TOP","UNION","ALL","CASE","WHEN","THEN","ELSE",
        "END","CREATE","ALTER","DROP","TABLE","VIEW","INDEX","PROCEDURE","PROC","FUNCTION","TRIGGER",
        "DATABASE","SCHEMA","PRIMARY","KEY","FOREIGN","REFERENCES","CONSTRAINT","DEFAULT","CHECK",
        "UNIQUE","DECLARE","BEGIN","COMMIT","ROLLBACK","TRANSACTION","TRAN","RETURN","EXEC","EXECUTE",
        "WITH","OVER","PARTITION","ASC","DESC","INT","BIGINT","VARCHAR","NVARCHAR","CHAR","NCHAR","BIT",
        "DATE","DATETIME","DATETIME2","DECIMAL","NUMERIC","FLOAT","MONEY","UNIQUEIDENTIFIER","IDENTITY",
        "OUTPUT","MERGE","USING","GO","IF","WHILE","TRY","CATCH","THROW","CAST","CONVERT","COALESCE",
        "ISNULL","COUNT","SUM","AVG","MIN","MAX","GETDATE","ROW_NUMBER","RANK","DENSE_RANK",
    };

    private readonly record struct Token(int Start, int Length, string Kind);

    /// <summary>Tokenizes SQL into contiguous spans covering every character.</summary>
    public static IReadOnlyList<(int Start, int Length, string Kind)> Tokenize(string text)
    {
        var tokens = new List<(int, int, string)>();
        if (string.IsNullOrEmpty(text)) return tokens;

        int i = 0, n = text.Length, runStart = 0;
        void EmitDefault(int from, int to) { if (to > from) tokens.Add((from, to - from, KindDefault)); }

        while (i < n)
        {
            char c = text[i];
            if (c == '-' && i + 1 < n && text[i + 1] == '-')               // line comment
            {
                EmitDefault(runStart, i);
                int s = i; i += 2;
                while (i < n && text[i] != '\n') i++;
                tokens.Add((s, i - s, KindComment)); runStart = i; continue;
            }
            if (c == '/' && i + 1 < n && text[i + 1] == '*')               // block comment
            {
                EmitDefault(runStart, i);
                int s = i; i += 2;
                while (i < n && !(text[i] == '*' && i + 1 < n && text[i + 1] == '/')) i++;
                if (i < n) i += 2;
                tokens.Add((s, i - s, KindComment)); runStart = i; continue;
            }
            if (c == '\'')                                                 // string literal
            {
                EmitDefault(runStart, i);
                int s = i; i++;
                while (i < n)
                {
                    if (text[i] == '\'')
                    {
                        if (i + 1 < n && text[i + 1] == '\'') { i += 2; continue; }
                        i++; break;
                    }
                    i++;
                }
                tokens.Add((s, i - s, KindString)); runStart = i; continue;
            }
            if (char.IsLetter(c) || c == '_' || c == '@' || c == '#')      // word / keyword
            {
                int s = i;
                while (i < n && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '@' || text[i] == '#')) i++;
                if (Keywords.Contains(text.Substring(s, i - s)))
                {
                    EmitDefault(runStart, s);
                    tokens.Add((s, i - s, KindKeyword)); runStart = i;
                }
                continue;
            }
            i++;
        }
        EmitDefault(runStart, n);
        return tokens;
    }

    /// <summary>Extracts plain highlight terms from the search box text: drops prefix:filters and the
    /// AND/OR/NOT boolean keywords; strips surrounding quotes and a trailing FTS5 <c>*</c>.</summary>
    public static IReadOnlyList<string> ExtractTerms(string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return Array.Empty<string>();
        var terms = new List<string>();
        foreach (var raw in search.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw is "AND" or "OR" or "NOT") continue;
            if (raw.IndexOf(':') > 0) continue;                            // server:/db:/name:/... filter
            var t = raw.Trim('"');
            if (t.EndsWith("*", StringComparison.Ordinal)) t = t[..^1];
            if (t.Length > 0 && !terms.Contains(t, StringComparer.OrdinalIgnoreCase)) terms.Add(t);
        }
        return terms;
    }

    /// <summary>Builds render segments: syntax-classified spans, each split where a case-insensitive
    /// search-term match begins/ends so matched sub-spans carry <see cref="Segment.Hit"/>.</summary>
    public static IReadOnlyList<Segment> BuildSegments(string sql, string? search)
    {
        sql ??= string.Empty;
        var tokens = Tokenize(sql);
        var hits = FindHitRanges(sql, ExtractTerms(search));
        var segments = new List<Segment>();

        foreach (var (start, length, kind) in tokens)
        {
            int spanEnd = start + length, cursor = start;
            while (cursor < spanEnd)
            {
                // Find the next hit range that overlaps [cursor, spanEnd).
                (int start, int end) next = (int.MaxValue, int.MaxValue);
                foreach (var h in hits)
                    if (h.end > cursor && h.start < spanEnd && h.start < next.start) next = h;

                if (next.start == int.MaxValue)
                {
                    segments.Add(new Segment(sql.Substring(cursor, spanEnd - cursor), kind, false));
                    break;
                }
                int hStart = Math.Max(next.start, cursor), hEnd = Math.Min(next.end, spanEnd);
                if (hStart > cursor) segments.Add(new Segment(sql.Substring(cursor, hStart - cursor), kind, false));
                if (hEnd > hStart) segments.Add(new Segment(sql.Substring(hStart, hEnd - hStart), kind, true));
                cursor = hEnd;
            }
        }
        return segments;
    }

    private static List<(int start, int end)> FindHitRanges(string sql, IReadOnlyList<string> terms)
    {
        var ranges = new List<(int, int)>();
        foreach (var term in terms)
        {
            if (string.IsNullOrEmpty(term)) continue;
            int pos = 0;
            while (pos < sql.Length)
            {
                int idx = sql.IndexOf(term, pos, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                ranges.Add((idx, idx + term.Length));
                pos = idx + 1;
            }
        }
        if (ranges.Count == 0) return ranges;
        ranges.Sort((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : b.Item2.CompareTo(a.Item2));
        var merged = new List<(int, int)> { ranges[0] };
        for (int i = 1; i < ranges.Count; i++)
        {
            var (ls, le) = merged[^1];
            var (cs, ce) = ranges[i];
            if (cs <= le) merged[^1] = (ls, Math.Max(le, ce));
            else merged.Add((cs, ce));
        }
        return merged;
    }
}
