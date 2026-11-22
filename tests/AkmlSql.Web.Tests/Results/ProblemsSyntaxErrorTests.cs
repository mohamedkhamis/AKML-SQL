using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Shared;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Results;

/// <summary>
/// Spec 047 (WEB-005, contracts/analysis-display.md §4) — a script that does not parse shows its syntax errors in
/// Problems, first, and the list never says "No problems found." while any finding exists. It used to show the empty
/// state for SQL that did not parse.
/// </summary>
public sealed class ProblemsSyntaxErrorTests : IDisposable
{
    private readonly BunitContext _ctx = new();

    public ProblemsSyntaxErrorTests() => _ctx.JSInterop.Mode = JSRuntimeMode.Loose;

    public void Dispose() => _ctx.Dispose();

    [Fact]
    public void Syntax_errors_are_listed_first_and_the_empty_state_is_absent()
    {
        var findings = new CodeAnalysisResponse
        {
            Issues = new[]
            {
                new CodeIssueInfo { RuleId = "ST004", Message = "Missing semicolon", Line = 1, Column = 1, Severity = 1 },
                new CodeIssueInfo { RuleId = "SYNTAX", Message = "Incorrect syntax near '*'.", Line = 3, Column = 7, Severity = 3 },
            },
        };

        var cut = _ctx.Render<ProblemsListComponent>(p => p.Add(x => x.Findings, findings));

        var rules = cut.FindAll("[data-testid=problem-item] .akml-problems-rule").Select(r => r.TextContent.Trim()).ToList();
        Assert.Equal(new[] { "SYNTAX", "ST004" }, rules);
        Assert.DoesNotContain("No problems found", cut.Markup);
    }

    [Fact]
    public void With_no_findings_the_empty_state_shows()
    {
        var cut = _ctx.Render<ProblemsListComponent>(p => p.Add(x => x.Findings, new CodeAnalysisResponse { Issues = Array.Empty<CodeIssueInfo>() }));

        Assert.Contains("No problems found.", cut.Find("[data-testid=problems-empty]").TextContent);
    }
}
