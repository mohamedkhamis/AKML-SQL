using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Shared;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Spec 041 (T200, research R39, FR-087) — every analyser finding becomes an underline in the editor: its line, its
/// columns, its severity, and "{RuleId}: {Message}" on hover.
/// </summary>
public sealed class EditorDiagnosticsTests
{
    private static CodeIssueInfo Issue(string rule, int severity, int line, int column, int start, int end, string message = "msg") =>
        new() { RuleId = rule, Severity = severity, Line = line, Column = column, StartOffset = start, EndOffset = end, Message = message };

    [Fact]
    public void A_finding_keeps_its_line_columns_and_severity_and_names_its_rule()
    {
        var shown = EditorDiagnostics.FromIssues(new[] { Issue("PE003", 2, 7, 5, 120, 131, "DELETE without WHERE") });

        var d = Assert.Single(shown);
        Assert.Equal(new EditorDiagnostic(7, 2, "PE003: DELETE without WHERE", 5, 16, "Code analysis"), d);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Every_severity_passes_through(int severity) =>
        Assert.Equal(severity, EditorDiagnostics.FromIssues(new[] { Issue("ST001", severity, 1, 1, 0, 3) })[0].Severity);

    [Fact]
    public void An_empty_span_underlines_the_whole_line()
    {
        var d = EditorDiagnostics.FromIssues(new[] { Issue("BP001", 1, 3, 4, 50, 50) })[0];

        Assert.Equal(4, d.StartColumn);
        Assert.Null(d.EndColumn);
    }

    [Fact]
    public void All_findings_are_shown_whatever_the_problems_filter_and_none_is_nothing()
    {
        var issues = Enumerable.Range(1, 5).Select(i => Issue("ST00" + i, i % 4, i, 1, 0, 1)).ToArray();

        Assert.Equal(5, EditorDiagnostics.FromIssues(issues).Count);
        Assert.Empty(EditorDiagnostics.FromIssues(null));
        Assert.Empty(EditorDiagnostics.FromIssues(Array.Empty<CodeIssueInfo>()));
    }

    [Fact]
    public void A_finding_without_a_line_is_not_shown()
    {
        Assert.Empty(EditorDiagnostics.FromIssues(new[] { Issue("DE001", 2, 0, 0, 0, 0) }));
    }
}
