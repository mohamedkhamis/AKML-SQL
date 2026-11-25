using System.Text;
using AkmlSql.Formatting.Layout;
using AkmlSql.Formatting.Profiles;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>
/// Stage 5: Converts LayoutNode list into final formatted text string.
/// Handles indentation (tabs or spaces), line breaks, comment emission,
/// trailing whitespace removal, and final newline.
/// </summary>
public class TextEmitter
{
    public string Emit(List<LayoutNode> nodes, FormattingProfile profile)
    {
        var sb = new StringBuilder();
        var ws = profile.Whitespace;
        // "tabsWhenPossible" (SQL Prompt: tabs for indentation, spaces for alignment) indents
        // with tabs like "tabs"; only the right-align pass's absolute space columns stay spaces.
        bool useTabs = ws.TabStyle == "tabs" || ws.TabStyle == "tabsWhenPossible";
        bool allowAbsoluteSpaces = ws.TabStyle != "tabs";
        int tabSize = ws.TabSize;

        // Pre-compute whether comment formatting is active. Only the explicitly-known opt-in value
        // "normaliseIndent" activates the block-comment re-indent pass. The DEFAULT value
        // "preserve" is false here — byte-identical output guaranteed. Any other value (including
        // the modelled-but-not-yet-implemented "joinShortLines", typos, or empty string) is also
        // treated as a no-op, preventing silently wrong output.
        bool commentFormattingActive = profile.Comments.MultilineFormatting == "normaliseIndent";

        // A '--' line comment runs to end-of-line, so whatever follows on the same line is swallowed
        // into the comment (and lost). Track when the previously-emitted text ended in an
        // un-terminated line comment so the next token is forced onto a new line even if layout gave
        // it no break (e.g. a leading comment before the first statement, where the statement's first
        // token is break-suppressed). Comments between statements already get a break and are fine.
        bool prevWasLineComment = false;

        // Where a string literal or quoted identifier that spans lines was written: its line breaks, and the spaces
        // before them, are its value. The trailing-whitespace pass below leaves them exactly as written (it used to drop
        // the CR of a CRLF and the spaces at a line's end inside them, so validation refused the whole format).
        var verbatim = new List<(int Start, int End)>();

        foreach (var node in nodes)
        {
            if (node.LeadingWhitespace is { } leading)
            {
                // Text kept exactly as written (a statement the parser could not read, see
                // UnparsedText): its own whitespace, then its own text — except that nothing may
                // follow a '--' comment on its line.
                if (prevWasLineComment && !leading.Contains('\n'))
                    sb.Append('\n');
                sb.Append(leading);
                AppendToken(sb, node, node.OriginalText, verbatim);
                // A comment on the run's last line was attached to its last token like any other (review of S2): write it as
                // the formatted path does, or the user loses text the validator never sees.
                if (node.TrailingComment != null)
                {
                    sb.Append(' ');
                    sb.Append(node.TrailingComment.Text.TrimEnd());
                }
                var lastKept = node.TrailingComment?.Text ?? node.OriginalText;
                prevWasLineComment = node.TokenType == TSqlTokenType.SingleLineComment
                    || lastKept.TrimStart().StartsWith("--", System.StringComparison.Ordinal);
                continue;
            }

            if (node.IsInNoformatRegion)
            {
                AppendToken(sb, node, node.OriginalText, verbatim);
                prevWasLineComment = false;
                continue;
            }

            if (prevWasLineComment && node.PrecedingBreak == BreakType.None)
            {
                // Terminate the dangling line comment so this token isn't fused into it.
                sb.Append('\n');
                AppendLineStart(sb, node, useTabs, allowAbsoluteSpaces, tabSize);
            }
            else
            {
                switch (node.PrecedingBreak)
                {
                    case BreakType.EmptyLine:
                        // One line break ends the line above; EmptyLineCount more make the empty lines (W6: the
                        // style's count between statements, one everywhere else).
                        sb.Append('\n', 1 + Math.Max(1, node.EmptyLineCount));
                        AppendLineStart(sb, node, useTabs, allowAbsoluteSpaces, tabSize);
                        break;

                    case BreakType.NewLine:
                        sb.Append('\n');
                        AppendLineStart(sb, node, useTabs, allowAbsoluteSpaces, tabSize);
                        break;

                    case BreakType.None:
                        if (node.PrecedingSpaces > 0)
                            sb.Append(' ', node.PrecedingSpaces);
                        break;
                }
            }

            // Opt-in comment body formatting — only when MultilineFormatting == "normaliseIndent".
            // The default ("preserve") and all other values (including the modelled-but-not-implemented
            // "joinShortLines") skip this branch, preserving byte-identical output for all 709 goldens.
            if (commentFormattingActive
                && node.TokenType == TSqlTokenType.MultilineComment
                && node.FormattedText.Contains('\n'))
            {
                var formatted = FormatBlockComment(
                    node.FormattedText,
                    node.OriginalColumn,
                    CurrentColumn(sb, tabSize),
                    useTabs,
                    tabSize,
                    profile.Comments);
                sb.Append(formatted);
            }
            else
            {
                AppendToken(sb, node, node.FormattedText, verbatim);
            }

            // Emit trailing comment on same line
            if (node.TrailingComment != null)
            {
                sb.Append(' ');
                sb.Append(node.TrailingComment.Text.TrimEnd());
            }

            // Did this node's emission end in an un-terminated '--' line comment? (Block comments
            // '/* */' are self-terminating; only '--' swallows the following token.)
            var lastEmitted = node.TrailingComment?.Text ?? node.FormattedText;
            prevWasLineComment = lastEmitted.TrimStart().StartsWith("--", System.StringComparison.Ordinal);
        }

        var result = sb.ToString();

        // Trailing whitespace removal
        if (ws.TrailingWhitespace == "remove")
        {
            result = RemoveTrailingWhitespace(result, verbatim);
        }

        // Final newline handling
        if (ws.FinalNewline == "ensure")
        {
            if (result.Length > 0 && !result.EndsWith('\n'))
                result += "\n";
        }
        else if (ws.FinalNewline == "remove")
        {
            result = result.TrimEnd('\r', '\n');
        }

        return result;
    }

    /// <summary>
    /// Leading whitespace for a line-start token: the opt-in absolute space count from the
    /// right-align pass (honored in every mode except pure "tabs" — right-alignment needs the
    /// space grid, so tabsWhenPossible keeps these spaces), else the normal IndentLevel×tabSize
    /// indent.
    /// </summary>
    private static void AppendLineStart(StringBuilder sb, LayoutNode node, bool useTabs, bool allowAbsoluteSpaces, int tabSize)
    {
        if (allowAbsoluteSpaces && node.AbsoluteLeadingSpaces >= 0)
            sb.Append(' ', node.AbsoluteLeadingSpaces);
        else
            AppendIndent(sb, node.IndentLevel, useTabs, tabSize);
    }

    private static void AppendIndent(StringBuilder sb, int level, bool useTabs, int tabSize)
    {
        if (level <= 0) return;

        if (useTabs)
        {
            sb.Append('\t', level);
        }
        else
        {
            sb.Append(' ', level * tabSize);
        }
    }

    /// <summary>
    /// Appends a token's text, noting where a string literal or quoted identifier that spans lines went
    /// (<paramref name="verbatim"/>, positions in <paramref name="sb"/>).
    /// </summary>
    private static void AppendToken(StringBuilder sb, LayoutNode node, string text, List<(int Start, int End)> verbatim)
    {
        int start = sb.Length;
        sb.Append(text);
        if (node.TokenType is TSqlTokenType.AsciiStringLiteral or TSqlTokenType.UnicodeStringLiteral or TSqlTokenType.QuotedIdentifier
            && text.IndexOfAny(LineBreakChars) >= 0)
            verbatim.Add((start, sb.Length));
    }

    private static readonly char[] LineBreakChars = ['\r', '\n'];

    /// <summary>
    /// Every line's trailing spaces and tabs removed, and every line break written as "\n" — except a line break inside a
    /// <paramref name="verbatim"/> range (a string literal or quoted identifier written there, in order), which keeps its
    /// own CR and the spaces before it: they are the value.
    /// </summary>
    private static string RemoveTrailingWhitespace(string text, List<(int Start, int End)> verbatim)
    {
        var sb = new StringBuilder(text.Length);
        int start = 0;
        int range = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                // A line break inside a literal: the line goes as it is, up to and including the break.
                while (range < verbatim.Count && verbatim[range].End <= i) range++;
                if (range < verbatim.Count && verbatim[range].Start < i)
                {
                    sb.Append(text, start, i + 1 - start);
                    start = i + 1;
                    continue;
                }

                // Find the end of content (excluding trailing spaces/tabs before this newline)
                int end = i;
                if (end > start && text[end - 1] == '\r')
                    end--;

                // Trim trailing whitespace from this line
                while (end > start && (text[end - 1] == ' ' || text[end - 1] == '\t'))
                    end--;

                sb.Append(text, start, end - start);
                sb.Append('\n');
                start = i + 1;
            }
        }

        // Handle last line (no trailing newline)
        if (start < text.Length)
        {
            int end = text.Length;
            while (end > start && (text[end - 1] == ' ' || text[end - 1] == '\t'))
                end--;
            sb.Append(text, start, end - start);
        }

        return sb.ToString();
    }

    // ── Comment formatting helpers (opt-in: only called when MultilineFormatting != "preserve") ──

    /// <summary>
    /// Moves a <c>/* … */</c> block comment as one block: its other lines shift by exactly as many columns as its
    /// <c>/*</c> moved (<paramref name="newColumn"/> − <paramref name="originalColumn"/>), so the comment's own layout is
    /// kept. Spec 047 (FMT-004, FR-014): this used to replace every line's indentation with the code's, which flattened
    /// the comment's inner indentation (Khamis Style). A comment that did not move, or whose original column is unknown,
    /// is written exactly as it was.
    ///
    /// <para>Banner / header comments — whose body lines are dominated by a repeated decoration
    /// character (<c>*</c>, <c>=</c>, <c>-</c>, <c>#</c>) — are skipped when
    /// <see cref="CommentsOptions.RecognizeCommonPatterns"/> is <see langword="true"/>.</para>
    /// </summary>
    private static string FormatBlockComment(
        string commentText,
        int originalColumn,
        int newColumn,
        bool useTabs,
        int tabSize,
        CommentsOptions options)
    {
        // Split on \n, preserving \r if present (strip \r, re-join with \n only).
        var lines = commentText.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        if (lines.Length <= 1)
        {
            // Single-line block comment (no internal newlines after split) — nothing to re-indent.
            return commentText;
        }

        // Banner / header detection when RecognizeCommonPatterns is active:
        // if more than half of the body lines (lines 1..n-2) have a trimmed first non-space
        // character that is a decoration char, treat the whole comment as a banner and leave it as-is.
        if (options.RecognizeCommonPatterns && IsBannerComment(lines))
            return commentText;

        int shift = newColumn - originalColumn;
        if (originalColumn < 0 || shift == 0)
            return commentText;

        var sb = new StringBuilder(lines[0]);
        for (int i = 1; i < lines.Length; i++)
        {
            sb.Append('\n');
            string trimmed = lines[i].TrimStart(' ', '\t');
            if (trimmed.Length == 0)
                continue;   // a blank line inside the comment: a bare newline, no trailing spaces
            int width = VisualWidth(lines[i][..(lines[i].Length - trimmed.Length)], tabSize) + shift;
            sb.Append(Indentation(Math.Max(0, width), useTabs, tabSize)).Append(trimmed);
        }

        return sb.ToString();
    }

    /// <summary>The width of <paramref name="text"/> in columns, a tab moving to the next multiple of <paramref name="tabSize"/>.</summary>
    internal static int VisualWidth(string text, int tabSize)
    {
        int size = Math.Max(1, tabSize);
        int width = 0;
        foreach (char c in text)
            width = c == '\t' ? (width / size + 1) * size : width + 1;
        return width;
    }

    /// <summary>The column the next character of <paramref name="sb"/> lands in.</summary>
    private static int CurrentColumn(StringBuilder sb, int tabSize)
    {
        int start = sb.Length;
        while (start > 0 && sb[start - 1] != '\n')
            start--;
        return VisualWidth(sb.ToString(start, sb.Length - start), tabSize);
    }

    /// <summary>Leading whitespace <paramref name="width"/> columns wide: tabs then spaces in a tab style, else spaces.</summary>
    private static string Indentation(int width, bool useTabs, int tabSize)
    {
        if (width <= 0) return string.Empty;
        if (!useTabs || tabSize <= 0) return new string(' ', width);
        return new string('\t', width / tabSize) + new string(' ', width % tabSize);
    }

    /// <summary>
    /// Returns <see langword="true"/> if the body lines of a split block comment look like a
    /// banner or header comment — i.e. more than half the body lines (excluding the first /*
    /// and last */ lines, which are just delimiters) start with a repeated decoration character.
    /// </summary>
    private static bool IsBannerComment(string[] lines)
    {
        // Only inspect body lines (not the first /* line or the last */ closing delimiter).
        // We need at least 3 lines to have any body lines.
        if (lines.Length < 3)
            return false;

        int bodyCount = 0;
        int decoratedCount = 0;

        for (int i = 1; i < lines.Length - 1; i++)
        {
            string trimmed = lines[i].TrimStart();
            if (trimmed.Length == 0)
                continue;

            bodyCount++;
            char first = trimmed[0];
            if (first is '*' or '=' or '-' or '#')
                decoratedCount++;
        }

        if (bodyCount == 0)
            return false;

        // Banner if > 50% of non-blank body lines start with a decoration char.
        return decoratedCount * 2 > bodyCount;
    }
}
