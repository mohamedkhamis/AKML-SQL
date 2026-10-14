using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.PostgreSql.Language;

namespace AkmlSql.PostgreSql.Formatting;

/// <summary>What the PostgreSQL formatter was asked to do besides lay the text out.</summary>
/// <param name="ApplyCasing">False keeps every keyword, function and type as written (Format SQL "Apply casing" off).</param>
/// <param name="CheckIdempotency">True formats the result again and requires the same text (FR-142).</param>
/// <param name="ApplyLayout">False keeps the white space and only does the other things asked for (case, terminators).</param>
/// <param name="Semicolons">0 leave, 1 add missing terminators, 2 remove them, 3 as the style says (leave).</param>
public sealed record PgFormatOptions(bool ApplyCasing = true, bool CheckIdempotency = true, bool ApplyLayout = true, int Semicolons = 0);

/// <summary>A note about a format: <see cref="Severity"/> 0 information, 1 warning, 2 error; <see cref="Offset"/> and <see cref="Line"/> are 0 and 1-based, 0 when unknown.</summary>
public sealed record PgFormatDiagnostic(int Severity, string Message, int Offset = 0, int Line = 0);

/// <summary>The outcome of formatting. On any failure <see cref="Text"/> is the original, untouched.</summary>
public sealed record PgFormatResult(bool Success, string Text, bool WasModified, bool ValidationPassed, IReadOnlyList<PgFormatDiagnostic> Diagnostics);

/// <summary>
/// The outcome of formatting a selection: <see cref="Text"/> replaces the characters from <see cref="OriginalStart"/> to <see cref="OriginalEnd"/>
/// of the document, which are the whole statements the selection touches.
/// </summary>
public sealed record PgSelectionResult(bool Success, string Text, int OriginalStart, int OriginalEnd, bool WasModified, IReadOnlyList<PgFormatDiagnostic> Diagnostics);

/// <summary>
/// Spec 042 (tasks T150, T151, FR-140 to FR-144) - formats a PostgreSQL script from a style written in SQL Prompt's model.
/// The steps: the script must parse (otherwise it is returned as it is, with the first error); it is laid out by <see cref="PgLayout"/>;
/// the result must parse to the same statements (the tree equal with every location ignored) and carry the same tokens and comments
/// in the same order (keyword case aside); and formatting the result again must change nothing. Any failed check returns the
/// original text with a message, never a half-formatted script.
/// </summary>
public static class PgFormatter
{
    public static PgFormatResult Format(string sql, SqlPromptStyle style, PgFormatOptions? options = null)
    {
        options ??= new PgFormatOptions();
        sql ??= string.Empty;

        if (string.IsNullOrWhiteSpace(sql)) return Unchanged(sql, success: true);

        var parsed = PgParser.Parse(sql);
        if (!parsed.Success)
        {
            var error = parsed.Error!;
            return Failed(sql, new PgFormatDiagnostic(2, $"Not formatted: {error.Message}", error.Offset, error.Line));
        }

        string formatted;
        try
        {
            formatted = Layout(sql, style, options);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Failed(sql, new PgFormatDiagnostic(2, "Not formatted: the layout could not handle this script (" + ex.GetType().Name + ")."));
        }

        if (formatted.Length == 0) return Failed(sql, new PgFormatDiagnostic(2, "Not formatted: the layout produced nothing."));

        var verdict = Verify(sql, parsed, formatted, options.Semicolons is 1 or 2);
        if (verdict is not null) return Failed(sql, new PgFormatDiagnostic(2, verdict));

        if (options.CheckIdempotency)
        {
            string again;
            try { again = Layout(formatted, style, options with { Semicolons = 0 }); }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Failed(sql, new PgFormatDiagnostic(1, "Not formatted: formatting the result again failed (" + ex.GetType().Name + ")."));
            }
            if (!string.Equals(formatted, again, StringComparison.Ordinal))
                return Failed(sql, new PgFormatDiagnostic(1, "Not formatted: this script does not format to a stable layout, so it was left as it is."));
        }

        return new PgFormatResult(true, formatted, !string.Equals(formatted, sql, StringComparison.Ordinal), true, Array.Empty<PgFormatDiagnostic>());
    }

    /// <summary>
    /// Formats the statements a selection touches (or the one the caret is in, for an empty selection) and nothing else. The result replaces
    /// the statements' own text, from the first one's start to the last one's end, so the semicolons and what lies around them stay.
    /// </summary>
    public static PgSelectionResult FormatSelection(string sql, int selectionStart, int selectionEnd, SqlPromptStyle style, PgFormatOptions? options = null)
    {
        sql ??= string.Empty;
        var start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, sql.Length);
        var end = Math.Clamp(Math.Max(selectionStart, selectionEnd), start, sql.Length);

        var touched = PgStatementSplitter.Split(sql)
            .Where(s => !s.IsMetaCommand && (start == end ? s.Start <= start && start <= s.End : s.End > start && s.Start < end))
            .ToList();
        if (touched.Count == 0)
        {
            return new PgSelectionResult(false, string.Empty, start, end, false,
                new[] { new PgFormatDiagnostic(1, "The selection holds no statement to format.") });
        }

        var regionStart = touched[0].Start;
        var regionEnd = touched[^1].End;
        var region = sql.Substring(regionStart, regionEnd - regionStart);
        var result = Format(region, style, options);
        return new PgSelectionResult(result.Success, result.Success ? result.Text : region, regionStart, regionEnd, result.WasModified, result.Diagnostics);
    }

    /// <summary>The layout of the script as text, without the safety checks. Public for the corpus tests; callers use <see cref="Format"/>.</summary>
    internal static string Layout(string sql, SqlPromptStyle style, PgFormatOptions options)
    {
        var tokens = PgFormatTokens.Build(sql);
        if (!tokens.Balanced || tokens.Count == 0) return sql;

        var layout = new PgLayout(sql, tokens, style, options.ApplyCasing, options.Semicolons);
        var doc = layout.Script();
        if (!options.ApplyLayout) return layout.CaseAndTerminatorsOnly();
        var text = PgDocRenderer.Render(doc, layout.RenderOptions);

        // The text ends with a line break exactly when it did before, and uses the line breaks the script used.
        if (sql.EndsWith('\n')) text += "\n";
        if (sql.Contains("\r\n")) text = System.Text.RegularExpressions.Regex.Replace(text, "(?<!\r)\n", "\r\n");
        return text;
    }

    /// <summary>The raw layout, before the safety checks. For tests that look at what the layout does when a check rejects it.</summary>
    internal static string LayoutForTests(string sql, SqlPromptStyle style, bool applyCasing = true) =>
        Layout(sql, style, new PgFormatOptions(ApplyCasing: applyCasing));

    private static PgFormatResult Unchanged(string sql, bool success) =>
        new(success, sql, false, true, Array.Empty<PgFormatDiagnostic>());

    private static PgFormatResult Failed(string sql, PgFormatDiagnostic diagnostic) =>
        new(false, sql, false, false, new[] { diagnostic });

    // ═══ the safety checks ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Null when the formatted text means the same and holds the same tokens and comments; otherwise the reason it does not.</summary>
    private static string? Verify(string original, PgParseResult originalTree, string formatted, bool ignoreTerminators)
    {
        var reparsed = PgParser.Parse(formatted);
        if (!reparsed.Success)
            return "Not formatted: the formatted script did not parse, so the original was kept.";
        if (!PgParser.TreesEquivalent(originalTree.Tree!, reparsed.Tree!))
            return "Not formatted: the formatted script did not read as the same statements, so the original was kept.";
        if (!SameTokens(original, formatted, ignoreTerminators))
            return "Not formatted: a token or a comment would have changed, so the original was kept.";
        return null;
    }

    /// <summary>
    /// The tokens of both texts match one by one (a word may differ in case only) and so do the comments, in order. They are compared
    /// apart because a comment that ended a line before a comma is written after the comma.
    /// </summary>
    private static bool SameTokens(string a, string b, bool ignoreTerminators)
    {
        var (tokensA, commentsA) = Significant(a, ignoreTerminators);
        var (tokensB, commentsB) = Significant(b, ignoreTerminators);
        if (tokensA.Count != tokensB.Count || commentsA.Count != commentsB.Count) return false;
        for (var i = 0; i < tokensA.Count; i++)
        {
            var (kindA, textA) = tokensA[i];
            var (kindB, textB) = tokensB[i];
            if (kindA != kindB) return false;
            var comparison = kindA == PgFt.Word ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(textA, textB, comparison)) return false;
        }
        for (var i = 0; i < commentsA.Count; i++)
            if (!string.Equals(commentsA[i], commentsB[i], StringComparison.Ordinal)) return false;
        return true;
    }

    /// <summary>The tokens as the layout reads them (an operator run is split where PostgreSQL splits it) and the comments in order.</summary>
    private static (List<(PgFt Kind, string Text)> Tokens, List<string> Comments) Significant(string text, bool ignoreTerminators)
    {
        var tokens = new List<(PgFt, string)>();
        var comments = new List<string>();
        var read = PgFormatTokens.Build(text);
        foreach (var token in read.All)
        {
            foreach (var comment in token.Leading) comments.Add(comment.IsLine ? comment.Text.TrimEnd() : comment.Text);
            if (!(ignoreTerminators && token.Kind == PgFt.Semi)) tokens.Add((token.Kind, token.Text));
            if (token.TrailingLine is { } line) comments.Add(line.Text.TrimEnd());
        }
        foreach (var comment in read.Epilogue) comments.Add(comment.IsLine ? comment.Text.TrimEnd() : comment.Text);
        return (tokens, comments);
    }
}
