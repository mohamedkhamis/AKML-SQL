using System;
using System.Collections.Generic;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 030 (web edition) — a cheap subsequence fuzzy matcher for the ⌘P command palette. Runs
/// per-keystroke over a small action set (dozens to a few hundred rows), so it favours simplicity
/// and zero allocation on the empty-query fast path over the sophistication of a full fzf scorer.
///
/// <para>
/// Scoring rewards: matches at the start of the string, matches at word boundaries
/// (after a space / separator / a lowercase→uppercase hump), and consecutive runs. Returns the
/// matched character indices so the renderer can bold them.
/// </para>
/// </summary>
public static class FuzzyScorer
{
    /// <summary>
    /// True when every character of <paramref name="query"/> appears in <paramref name="candidate"/>
    /// in order (case-insensitive). An empty query always matches with a neutral score and no
    /// highlight, so the palette shows the full list on open.
    /// </summary>
    public static bool TryScore(string candidate, string query, out int score, out int[] matchedIndices)
    {
        score = 0;
        matchedIndices = Array.Empty<int>();
        if (string.IsNullOrEmpty(candidate)) return false;
        if (string.IsNullOrEmpty(query)) return true;   // neutral match, no highlight

        var indices = new List<int>(query.Length);
        int ci = 0;          // index into candidate
        int qi = 0;          // index into query
        int runScore = 0;
        int consecutive = 0;

        while (ci < candidate.Length && qi < query.Length)
        {
            char cc = char.ToLowerInvariant(candidate[ci]);
            char qc = char.ToLowerInvariant(query[qi]);
            if (cc == qc)
            {
                indices.Add(ci);

                int charScore = 1;
                if (ci == 0) charScore += 6;                              // very strong: start of string
                else if (IsBoundary(candidate, ci)) charScore += 4;      // word boundary
                if (consecutive > 0) charScore += 2 + consecutive;       // reward adjacency runs

                runScore += charScore;
                consecutive++;
                qi++;
            }
            else
            {
                consecutive = 0;
            }
            ci++;
        }

        if (qi < query.Length) return false;   // ran out of candidate before matching the whole query

        // Prefer shorter candidates (a query that fills more of the title ranks higher) and an
        // earlier first match.
        int firstMatch = indices.Count > 0 ? indices[0] : 0;
        score = runScore + Math.Max(0, 20 - candidate.Length / 2) - firstMatch;
        matchedIndices = indices.ToArray();
        return true;
    }

    private static bool IsBoundary(string s, int i)
    {
        if (i <= 0) return true;
        char prev = s[i - 1];
        if (prev is ' ' or '.' or '_' or '-' or '/' or '[' or ']' or ':' or '(') return true;
        // camelCase / PascalCase hump: lower-then-upper.
        if (char.IsLower(prev) && char.IsUpper(s[i])) return true;
        return false;
    }
}
