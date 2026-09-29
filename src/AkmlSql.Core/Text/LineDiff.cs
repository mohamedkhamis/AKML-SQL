using System;
using System.Collections.Generic;

namespace AkmlSql.Core.Text
{
    public enum LineDiffKind
    {
        Same,
        Added,
        Removed,
        Changed,
    }

    /// <summary>One line of a <see cref="LineDiff"/>: its kind, 1-based line numbers (null on the side it is missing from) and texts.</summary>
    public sealed class LineDiffEntry
    {
        public LineDiffEntry(LineDiffKind kind, int? leftLine, int? rightLine, string leftText, string rightText)
        {
            Kind = kind;
            LeftLine = leftLine;
            RightLine = rightLine;
            LeftText = leftText;
            RightText = rightText;
        }

        public LineDiffKind Kind { get; }
        public int? LeftLine { get; }
        public int? RightLine { get; }
        public string LeftText { get; }
        public string RightText { get; }
    }

    /// <summary>
    /// Spec 040 (T127, HIS-10) — a line diff for History's "Compare with current". Common leading and
    /// trailing lines are matched first; the rest is aligned by a longest common subsequence over
    /// lines. A run of removed lines followed by added lines is reported pair by pair as Changed,
    /// so an edited line reads as one change. CRLF, LF and CR line ends are the same.
    /// </summary>
    public static class LineDiff
    {
        /// <summary>Above this many LCS cells the middle is compared position by position instead.</summary>
        private const long MaxLcsCells = 4_000_000;

        public static IReadOnlyList<LineDiffEntry> Diff(string? left, string? right)
        {
            var a = Lines(left);
            var b = Lines(right);
            var result = new List<LineDiffEntry>(Math.Max(a.Length, b.Length));

            var prefix = 0;
            while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
            var suffix = 0;
            while (suffix < a.Length - prefix && suffix < b.Length - prefix
                   && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix]) suffix++;

            for (var i = 0; i < prefix; i++)
                result.Add(new LineDiffEntry(LineDiffKind.Same, i + 1, i + 1, a[i], b[i]));

            var raw = Middle(a, prefix, a.Length - suffix, b, prefix, b.Length - suffix);
            result.AddRange(PairChanges(raw));

            for (var k = suffix; k > 0; k--)
            {
                var i = a.Length - k;
                var j = b.Length - k;
                result.Add(new LineDiffEntry(LineDiffKind.Same, i + 1, j + 1, a[i], b[j]));
            }
            return result;
        }

        private static string[] Lines(string? text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            var normalized = text!.Replace("\r\n", "\n").Replace('\r', '\n');
            if (normalized.EndsWith("\n", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 1);
            return normalized.Split('\n');
        }

        /// <summary>Same / Removed / Added entries for a[aFrom..aTo) against b[bFrom..bTo).</summary>
        private static List<LineDiffEntry> Middle(string[] a, int aFrom, int aTo, string[] b, int bFrom, int bTo)
        {
            var n = aTo - aFrom;
            var m = bTo - bFrom;
            var entries = new List<LineDiffEntry>(n + m);
            if (n == 0 || m == 0 || (long)n * m > MaxLcsCells)
            {
                // Nothing to align, or too large to align: everything on the left went, everything on
                // the right came (paired into Changed afterwards).
                for (var i = aFrom; i < aTo; i++) entries.Add(new LineDiffEntry(LineDiffKind.Removed, i + 1, null, a[i], string.Empty));
                for (var j = bFrom; j < bTo; j++) entries.Add(new LineDiffEntry(LineDiffKind.Added, null, j + 1, string.Empty, b[j]));
                return entries;
            }

            // lcs[i, j] = LCS length of a[aFrom+i..aTo) and b[bFrom+j..bTo)
            var lcs = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
                for (var j = m - 1; j >= 0; j--)
                    lcs[i, j] = a[aFrom + i] == b[bFrom + j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a[aFrom + x] == b[bFrom + y])
                {
                    entries.Add(new LineDiffEntry(LineDiffKind.Same, aFrom + x + 1, bFrom + y + 1, a[aFrom + x], b[bFrom + y]));
                    x++; y++;
                }
                else if (lcs[x + 1, y] >= lcs[x, y + 1])
                {
                    entries.Add(new LineDiffEntry(LineDiffKind.Removed, aFrom + x + 1, null, a[aFrom + x], string.Empty));
                    x++;
                }
                else
                {
                    entries.Add(new LineDiffEntry(LineDiffKind.Added, null, bFrom + y + 1, string.Empty, b[bFrom + y]));
                    y++;
                }
            }
            for (; x < n; x++) entries.Add(new LineDiffEntry(LineDiffKind.Removed, aFrom + x + 1, null, a[aFrom + x], string.Empty));
            for (; y < m; y++) entries.Add(new LineDiffEntry(LineDiffKind.Added, null, bFrom + y + 1, string.Empty, b[bFrom + y]));
            return entries;
        }

        /// <summary>Turns each run of removed lines followed by added lines into Changed pairs.</summary>
        private static IEnumerable<LineDiffEntry> PairChanges(List<LineDiffEntry> entries)
        {
            var i = 0;
            while (i < entries.Count)
            {
                if (entries[i].Kind != LineDiffKind.Removed)
                {
                    yield return entries[i++];
                    continue;
                }

                var removedStart = i;
                while (i < entries.Count && entries[i].Kind == LineDiffKind.Removed) i++;
                var addedStart = i;
                while (i < entries.Count && entries[i].Kind == LineDiffKind.Added) i++;

                var removed = addedStart - removedStart;
                var added = i - addedStart;
                var pairs = Math.Min(removed, added);
                for (var p = 0; p < pairs; p++)
                {
                    var r = entries[removedStart + p];
                    var ad = entries[addedStart + p];
                    yield return new LineDiffEntry(LineDiffKind.Changed, r.LeftLine, ad.RightLine, r.LeftText, ad.RightText);
                }
                for (var p = pairs; p < removed; p++) yield return entries[removedStart + p];
                for (var p = pairs; p < added; p++) yield return entries[addedStart + p];
            }
        }
    }
}
