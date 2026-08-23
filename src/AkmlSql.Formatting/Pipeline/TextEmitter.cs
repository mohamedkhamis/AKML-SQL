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
        bool useTabs = ws.TabStyle == "tabs";
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

        foreach (var node in nodes)
        {
            if (node.IsInNoformatRegion)
            {
                sb.Append(node.OriginalText);
                prevWasLineComment = false;
                continue;
            }

            if (prevWasLineComment && node.PrecedingBreak == BreakType.None)
            {
                // Terminate the dangling line comment so this token isn't fused into it.
                sb.Append('\n');
                AppendLineStart(sb, node, useTabs, tabSize);
            }
            else
            {
                switch (node.PrecedingBreak)
                {
                    case BreakType.EmptyLine:
                        sb.Append('\n');
                        sb.Append('\n');
                        AppendLineStart(sb, node, useTabs, tabSize);
                        break;

                    case BreakType.NewLine:
                        sb.Append('\n');
                        AppendLineStart(sb, node, useTabs, tabSize);
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
                    node.IndentLevel,
                    useTabs,
                    tabSize,
                    profile.Comments);
                sb.Append(formatted);
            }
            else
            {
                sb.Append(node.FormattedText);
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
            result = RemoveTrailingWhitespace(result);
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
    /// right-align pass (spaces mode only), else the normal IndentLevel×tabSize indent.
    /// </summary>
    private static void AppendLineStart(StringBuilder sb, LayoutNode node, bool useTabs, int tabSize)
    {
        if (!useTabs && node.AbsoluteLeadingSpaces >= 0)
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

    private static string RemoveTrailingWhitespace(string text)
    {
        var sb = new StringBuilder(text.Length);
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
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
    /// Re-indents the body lines of a <c>/* … */</c> block comment to the surrounding context
    /// indentation level. The first line (the <c>/*</c> line) and the last line (the <c>*/</c>
    /// closing delimiter) keep their leading whitespace stripped (the caller's
    /// <see cref="AppendLineStart"/> already positioned the cursor). Body lines (lines 2..n-1)
    /// have their leading whitespace replaced with the context indent string.
    ///
    /// <para>Banner / header comments — whose body lines are dominated by a repeated decoration
    /// character (<c>*</c>, <c>=</c>, <c>-</c>, <c>#</c>) — are skipped when
    /// <see cref="CommentsOptions.RecognizeCommonPatterns"/> is <see langword="true"/>.</para>
    /// </summary>
    private static string FormatBlockComment(
        string commentText,
        int indentLevel,
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

        // Build the indent string for body lines (lines 1..n-2) and the closing delimiter (line n-1).
        string indent = BuildIndentString(indentLevel, useTabs, tabSize);

        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == 0)
            {
                // First line: the /* opener. AppendLineStart already emitted the line-start indent
                // before node.FormattedText; the opener's own text starts with "/*" and may have
                // trailing content. Trim leading whitespace (the emitter provides the prefix).
                sb.Append(lines[i].TrimStart());
            }
            else
            {
                sb.Append('\n');
                // Body lines and the closing delimiter: strip their original leading whitespace
                // and re-apply the context indent.
                string trimmed = lines[i].TrimStart();
                if (trimmed.Length > 0)
                    sb.Append(indent).Append(trimmed);
                // else: a blank line inside the comment — emit as a bare newline (no trailing spaces).
            }
        }

        return sb.ToString();
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

    private static string BuildIndentString(int level, bool useTabs, int tabSize)
    {
        if (level <= 0)
            return string.Empty;
        if (useTabs)
            return new string('\t', level);
        return new string(' ', level * tabSize);
    }
}
