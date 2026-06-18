using System.Text;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Engine.Refactoring.Operations.Heavyweight;

/// <summary>
/// Base class for heavyweight refactoring operations that use the preview/apply pattern.
/// </summary>
public abstract class HeavyweightOperationBase
{
    public abstract Task<RefactorPreviewResponse> PreviewAsync(
        RefactorPreviewRequest request,
        RefactoringContext ctx,
        CancellationToken ct);

    public abstract Task<RefactorApplyResponse> ApplyAsync(
        RefactorApplyRequest request,
        CancellationToken ct);

    // ── Shared text utilities ────────────────────────────────────────────────

    /// <summary>
    /// Spec 030 (review): returns an end offset that excludes a trailing statement terminator. If the
    /// span <c>[start, end)</c> ends with optional whitespace then a <c>;</c>, the returned end points
    /// just before the <c>;</c> so replacing <c>[start, newEnd)</c> leaves the <c>;</c> in the document
    /// (a statement-replacing refactor must not swallow the terminator).
    /// </summary>
    protected internal static int TrimTrailingTerminator(string docText, int start, int end)
    {
        int limit = Math.Min(end, docText.Length);
        int probe = limit;
        while (probe > start && char.IsWhiteSpace(docText[probe - 1])) probe--;
        if (probe > start && docText[probe - 1] == ';')
            return probe - 1; // exclude the ';'
        return limit;
    }

    protected internal static (int line, int col) OffsetToLineCol(string text, int offset)
    {
        int line = 1, col = 1;
        for (int i = 0; i < Math.Min(offset, text.Length); i++)
        {
            if (text[i] == '\n') { line++; col = 1; }
            else col++;
        }
        return (line, col);
    }

    protected internal static string ExtractContext(string text, int offset)
    {
        int start = offset;
        int newlinesBack = 0;
        while (start > 0 && newlinesBack < 2)
        {
            start--;
            if (text[start] == '\n') newlinesBack++;
        }
        int end = offset;
        int newlinesFwd = 0;
        while (end < text.Length && newlinesFwd < 2)
        {
            if (text[end] == '\n') newlinesFwd++;
            end++;
        }
        return text.Substring(start, Math.Min(end - start, 200));
    }

    /// <summary>
    /// Applies current-document changes (FilePath null/empty) to a document string, sorted
    /// descending by offset. Changes with StartOffset == EndOffset == 0 are prefix inserts.
    /// </summary>
    protected internal static string ApplyChanges(RefactorChangeInfo[] changes, string documentText)
    {
        var sb = new StringBuilder(documentText);
        var sorted = changes
            .Where(c => string.IsNullOrEmpty(c.FilePath))
            .OrderByDescending(c => c.StartOffset)
            .ToArray();

        foreach (var ch in sorted)
        {
            if (ch.StartOffset == 0 && ch.EndOffset == 0)
            {
                sb.Insert(0, ch.NewText);
            }
            else
            {
                var len = ch.EndOffset - ch.StartOffset;
                if (ch.StartOffset >= 0 && ch.StartOffset <= sb.Length)
                {
                    sb.Remove(ch.StartOffset, Math.Min(len, sb.Length - ch.StartOffset));
                    sb.Insert(ch.StartOffset, ch.NewText);
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Appends a substituted parameter value to <paramref name="sb"/> during token-aware inlining,
    /// inserting a single separating space when the value's leading operator character would FUSE
    /// with a trailing operator character already in the buffer. Without this, a binding adjacent to
    /// an operator with no whitespace (e.g. <c>5-@x</c> with <c>@x = -1</c>) emits <c>5--1</c> — a
    /// <c>--</c> line comment that silently truncates the statement (also guards <c>/*</c>, <c>+-</c>).
    /// A space is semantically inert in every scalar position, unlike wrapping parens which is invalid
    /// in some spots (e.g. EXEC arguments). Positive literals never trip this, so goldens are unchanged.
    /// </summary>
    protected internal static void AppendSubstitutedValue(StringBuilder sb, string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        if (sb.Length > 0 && IsOperatorChar(sb[sb.Length - 1]) && IsOperatorChar(value[0]))
            sb.Append(' ');
        sb.Append(value);
    }

    private static bool IsOperatorChar(char c) => "+-*/%=<>!~&|^".IndexOf(c) >= 0;

    /// <summary>
    /// Applies a pre-filtered list of changes directly to a text string (no FilePath filtering).
    /// Assumes the caller has already grouped changes by file. Applies in descending offset order.
    /// </summary>
    protected static string ApplyChangesToText(string text, IEnumerable<RefactorChangeInfo> changes)
    {
        var sb = new StringBuilder(text);
        foreach (var ch in changes.OrderByDescending(c => c.StartOffset))
        {
            if (ch.StartOffset >= 0 && ch.EndOffset <= sb.Length && ch.EndOffset >= ch.StartOffset)
                sb.Remove(ch.StartOffset, ch.EndOffset - ch.StartOffset).Insert(ch.StartOffset, ch.NewText);
        }
        return sb.ToString();
    }
}

/// <summary>
/// String constants for <see cref="RefactorChangeInfo.ChangeCategory"/>.
/// </summary>
internal static class ChangeCategory
{
    internal const string Rename      = "rename";
    internal const string Wrap        = "wrap";
    internal const string Structure   = "structure";
    internal const string Declaration = "declaration";
}
