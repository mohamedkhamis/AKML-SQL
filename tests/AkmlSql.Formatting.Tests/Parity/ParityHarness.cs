using System.Text;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Tests.Parity;

/// <summary>
/// Spec 031 Task 13 — shared formatting-parity primitives extracted from
/// <see cref="FormatParityTests"/> so <see cref="RedgateParityTests"/> can drive the exact same
/// pipeline invocation and comparison rules without duplicating them. This is a behavior-preserving
/// extraction: <see cref="Format"/> and <see cref="Normalise"/> reproduce, line for line, what used
/// to be inlined in <c>FormatParityTests.Corpus_Matches_Golden</c>.
/// </summary>
internal static class ParityHarness
{
    /// <summary>
    /// Formats <paramref name="inputSql"/> against <paramref name="profile"/> via
    /// <see cref="FormatterPipeline"/>, mirroring the parity-driver invocation used by both
    /// <see cref="FormatParityTests"/> and <see cref="RedgateParityTests"/>.
    ///
    /// <para>Stage 7 (idempotency) is disabled so a single (input, profile) pass produces
    /// deterministic output even for inputs that would not otherwise re-parse identically. Stage 6
    /// (semantic validation) still runs as configured on <paramref name="profile"/> — if it rejects,
    /// the pipeline returns the original input unchanged, which is expected pipeline behaviour and
    /// will be visible in a diff against the golden.</para>
    /// </summary>
    public static string Format(string inputSql, FormattingProfile profile)
    {
        profile.Metadata.EnableIdempotencyCheck = false;
        var result = new FormatterPipeline().Format(inputSql, profile);
        return result.FormattedText;
    }

    /// <summary>
    /// SC-007 / Q1 clarification normalisation:
    /// 1) Strip trailing whitespace from every line
    /// 2) Normalise <c>\r\n</c> + <c>\r</c> to <c>\n</c>
    /// 3) Drop UTF-8 BOM if present
    ///
    /// Must be idempotent — <c>Normalise(Normalise(x)) == Normalise(x)</c> — because golden files
    /// are themselves read through this same normalisation before comparison.
    /// </summary>
    public static string Normalise(string text)
    {
        if (text == null) return string.Empty;

        // 3) UTF-8 BOM (U+FEFF) — strip if leading
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        // 2) line endings
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // 1) trailing whitespace per line — preserve the original trailing-newline structure by
        // joining trimmed segments with \n (so N segments produce N-1 separators, matching split).
        var lines = text.Split('\n');
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            sb.Append(lines[i].TrimEnd());
            if (i < lines.Length - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Returns a "line N, col N" pointer to the first character at which
    /// <paramref name="expected"/> and <paramref name="actual"/> diverge, with a short excerpt of
    /// each side for triage output. Both strings are assumed already <see cref="Normalise"/>d.
    /// Returns a fixed sentinel when the two strings are equal.
    /// </summary>
    public static string FirstDiff(string expected, string actual)
    {
        if (string.Equals(expected, actual, StringComparison.Ordinal))
            return "(no difference)";

        var line = 1;
        var col = 1;
        var i = 0;
        var max = Math.Min(expected.Length, actual.Length);
        while (i < max && expected[i] == actual[i])
        {
            if (expected[i] == '\n') { line++; col = 1; }
            else { col++; }
            i++;
        }

        var expectedExcerpt = Excerpt(expected, i);
        var actualExcerpt = Excerpt(actual, i);
        var lengthNote = expected.Length != actual.Length
            ? $" (expected.Length={expected.Length}, actual.Length={actual.Length})"
            : string.Empty;

        return $"First diff at line {line}, col {col} (char offset {i}){lengthNote}" +
               $"{Environment.NewLine}  expected: ...{expectedExcerpt}..." +
               $"{Environment.NewLine}  actual:   ...{actualExcerpt}...";
    }

    private static string Excerpt(string s, int pos)
    {
        const int radius = 20;
        var start = Math.Max(0, pos - radius);
        var end = Math.Min(s.Length, pos + radius);
        return s.Substring(start, end - start).Replace("\n", "\\n");
    }
}
