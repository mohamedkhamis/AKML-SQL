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
