using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Results;

/// <summary>
/// Spec 041 (T145, research R50, R51, FR-034, FR-055) — the Problems list when code analysis is off (it says so
/// and offers to turn it on, with no filters and no list), and the default severity filters from Settings ›
/// Code analysis, applied once when the list opens.
/// </summary>
public sealed class ProblemsAnalysisOffTests : IDisposable
{
    private readonly BunitContext _ctx = new();

    public ProblemsAnalysisOffTests() => _ctx.JSInterop.Mode = JSRuntimeMode.Loose;

    public void Dispose() => _ctx.Dispose();

    private static CodeAnalysisResponse Findings() => new()
    {
        Issues = new[]
        {
            new CodeIssueInfo { RuleId = "PE001", Message = "Avoid SELECT *", Line = 3, Column = 5, Severity = 2 },
            new CodeIssueInfo { RuleId = "ST001", Message = "Style", Line = 1, Column = 1, Severity = 1 },
            new CodeIssueInfo { RuleId = "SE001", Message = "Injection", Line = 7, Column = 1, Severity = 3 },
        },
    };

    [Fact]
    public void With_analysis_off_the_list_says_so_and_offers_to_turn_it_on()
    {
        int enabled = 0;
        var cut = _ctx.Render<ProblemsListComponent>(p => p
            .Add(x => x.Findings, Findings())
            .Add(x => x.AnalysisEnabled, false)
            .Add(x => x.OnEnableAnalysis, () => enabled++));

        Assert.Contains("Code analysis is off.", cut.Find("[data-testid=problems-analysis-off]").TextContent);
        Assert.Empty(cut.FindAll("[data-testid=problem-item]"));
        Assert.Empty(cut.FindAll("input[type=checkbox]"));

        var turnOn = cut.Find("[data-testid=problems-enable-analysis]");
        Assert.Equal("Turn on", turnOn.TextContent.Trim());
        turnOn.Click();
        Assert.Equal(1, enabled);
    }

    [Fact]
    public void With_analysis_off_no_findings_are_counted()
    {
        var counts = new List<int>();
        _ctx.Render<ProblemsListComponent>(p => p
            .Add(x => x.Findings, Findings())
            .Add(x => x.AnalysisEnabled, false)
            .Add(x => x.OnVisibleCountChanged, (int n) => counts.Add(n)));

        Assert.Equal(0, counts.LastOrDefault());
    }

    [Fact]
    public void The_default_filter_applies_when_the_list_opens()
    {
        var cut = _ctx.Render<ProblemsListComponent>(p => p
            .Add(x => x.Findings, Findings())
            .Add(x => x.DefaultFilter, new ProblemsFilter { ShowInfo = false, ShowWarning = true, ShowError = true }));

        var rules = cut.FindAll("[data-testid=problem-item] .akml-problems-rule").Select(e => e.TextContent).ToArray();
        Assert.Equal(new[] { "PE001", "SE001" }, rules);
    }

    [Fact]
    public void The_default_filter_does_not_undo_what_the_user_toggled()
    {
        var cut = _ctx.Render<ProblemsListComponent>(p => p
            .Add(x => x.Findings, Findings())
            .Add(x => x.DefaultFilter, new ProblemsFilter { ShowInfo = false }));

        // The user shows Info again; a later re-render (new findings) keeps their choice.
        cut.FindAll("input[type=checkbox]")[0].Change(true);
        cut.Render(p => p.Add(x => x.Findings, Findings()).Add(x => x.DefaultFilter, new ProblemsFilter { ShowInfo = false }));

        Assert.Equal(3, cut.FindAll("[data-testid=problem-item]").Count);
    }
}
