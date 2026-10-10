namespace AkmlSql.Web.Shared;

/// <summary>
/// Spec 041 (research R39) — one finding shown in the editor itself (underline, gutter marker, message on
/// hover): an analyser issue (FR-087) or the line of an execution error (FR-023). Passed to
/// <c>EditorComponent.SetDiagnosticsAsync</c>; akml-editor.js clamps it to the document.
/// </summary>
/// <param name="Line">1-based document line.</param>
/// <param name="Severity">0 hint, 1 info, 2 warning, 3 error (the analyser's <c>CodeIssueInfo.Severity</c>).</param>
/// <param name="Message">Shown on hover.</param>
/// <param name="StartColumn">1-based first column; null underlines the whole line.</param>
/// <param name="EndColumn">1-based column after the last; null underlines to the end of the line.</param>
/// <param name="Source">Shown under the message (defaults to "AKML SQL").</param>
public sealed record EditorDiagnostic(
    int Line,
    int Severity,
    string Message,
    int? StartColumn = null,
    int? EndColumn = null,
    string? Source = null);

/// <summary>Spec 041 (T200, research R39, FR-087) — analyser findings as editor diagnostics.</summary>
public static class EditorDiagnostics
{
    /// <summary>
    /// Every finding with a line (the Problems tab's filters are a view of the list, not of the document): its
    /// 1-based line and column, the column after its span when the span is not empty (else to the end of the line),
    /// its severity, and "{RuleId}: {Message}".
    /// </summary>
    public static IReadOnlyList<EditorDiagnostic> FromIssues(IEnumerable<AkmlSql.Core.Ipc.Messages.CodeIssueInfo>? issues)
    {
        if (issues == null) return System.Array.Empty<EditorDiagnostic>();
        var shown = new List<EditorDiagnostic>();
        foreach (var issue in issues)
        {
            if (issue == null || issue.Line < 1) continue;
            var start = issue.Column >= 1 ? issue.Column : (int?)null;
            var length = issue.EndOffset - issue.StartOffset;
            int? end = start != null && length > 0 ? start + length : null;
            shown.Add(new EditorDiagnostic(issue.Line, issue.Severity, $"{issue.RuleId}: {issue.Message}", start, end, "Code analysis"));
        }
        return shown;
    }
}
