using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Grid;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Results;

/// <summary>
/// Spec 041 (T085, research R34, FR-020, FR-021, FR-029, FR-033, FR-034) — the results pane: Results, Messages and
/// Problems tabs that are always there; an empty and a running state; every result set stacked with its own
/// header and its own Apply / Discard; a Problems badge that counts what the filters let through.
/// </summary>
public sealed class ResultsPaneComponentTests : IDisposable
{
    private readonly BunitContext _ctx = new();
    private readonly RecordingQueryExecution _exec = new();
    private DateTimeOffset _now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly WorkspaceStatus _status;

    public ResultsPaneComponentTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _status = new WorkspaceStatus(() => _now);
        _ctx.Services.AddSingleton<IQueryExecutionService>(_exec);
        _ctx.Services.AddSingleton<IWorkspaceStatus>(_status);
        _ctx.Services.AddSingleton<IDialogService>(new FakeDialogService());
        _ctx.Services.AddSingleton<INotificationService>(new FakeNotificationService());
    }

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<ResultsPaneComponent> Render(
        ExecuteQueryResult? result, bool running = false, string activeTab = ResultsPaneComponent.ResultsTab,
        CodeAnalysisResponse? findings = null, bool analysisEnabled = true, Action<string>? tabChanged = null) =>
        _ctx.Render<ResultsPaneComponent>(p => p
            .Add(x => x.Result, result)
            .Add(x => x.IsRunning, running)
            .Add(x => x.ActiveTab, activeTab)
            .Add(x => x.ActiveTabChanged, (string t) => tabChanged?.Invoke(t))
            .Add(x => x.Findings, findings)
            .Add(x => x.AnalysisEnabled, analysisEnabled));

    private static ExecuteQueryResult Sets(params ExecuteResultSet[] sets) => new()
    {
        Status = ExecuteStatus.Ok,
        ElapsedMs = 42,
        ResultSets = sets,
        MessageDetails = new[] { new ExecuteMessageDto { Kind = ExecuteMessageKind.RowCount, RowCount = 5, Text = "(5 rows affected)" } },
    };

    [Fact]
    public void The_three_tabs_are_always_there()
    {
        var cut = Render(null);

        Assert.NotNull(cut.Find("[data-testid=results-tab-results]"));
        Assert.NotNull(cut.Find("[data-testid=results-tab-messages]"));
        Assert.NotNull(cut.Find("[data-testid=results-tab-problems]"));
        Assert.NotNull(cut.Find("[data-testid=results-pane]"));
    }

    /// <summary>Review WEB-08: the old grids are not just dimmed while a query runs — they take no edit, because the new
    /// result would replace them (and their pending edits) without asking.</summary>
    [Fact]
    public void While_running_the_old_grids_take_no_edit_and_the_stamp_reaches_them()
    {
        var result = Sets(GridTestHost.EditableSet());
        result.ConnectionStamp = "stamp-A";

        var running = Render(result, running: true);
        Assert.Contains(ResultsPaneComponent.RunningReason, running.Find("[data-testid=results-frozen-reason]").TextContent);
        var grid = running.FindComponent<ResultSetGrid>();
        Assert.Equal(ResultsPaneComponent.RunningReason, grid.Instance.FrozenReason);
        Assert.Equal("stamp-A", grid.Instance.ConnectionStamp);

        running.Render(p => p.Add(x => x.IsRunning, false));
        Assert.Null(grid.Instance.FrozenReason);
        Assert.Empty(running.FindAll("[data-testid=results-frozen-reason]"));

        running.Render(p => p.Add(x => x.FrozenReason, "stale"));
        Assert.Equal("stale", grid.Instance.FrozenReason);
    }

    /// <summary>Review WEB-09: the page learns when pending edits appear and go, so it can ask the browser to warn before a
    /// reload or a closed tab — only while there is something to lose.</summary>
    [Fact]
    public void The_page_is_told_when_pending_edits_appear_and_go()
    {
        var reports = new List<bool>();
        var cut = _ctx.Render<ResultsPaneComponent>(p => p
            .Add(x => x.Result, Sets(GridTestHost.EditableSet()))
            .Add(x => x.HasPendingEditsChanged, (bool v) => reports.Add(v)));
        Assert.Empty(reports);

        var grid = cut.FindComponent<ResultSetGrid>();
        GridTestHost.Cell(grid, 0, 1).DoubleClick();
        var input = grid.Find("[data-testid=results-cell-input]");
        input.Input("changed");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        Assert.Equal(new[] { true }, reports);

        grid.Find("[data-testid=results-discard]").Click();
        Assert.Equal(new[] { true, false }, reports);
    }

    [Fact]
    public void Before_any_run_the_pane_says_how_to_get_results()
    {
        var cut = Render(null);

        Assert.Equal("Run a query to see results here (F5)", cut.Find("[data-testid=results-empty]").TextContent.Trim());
    }

    [Fact]
    public void While_running_it_shows_the_elapsed_time_says_Cancel_stops_it_and_marks_old_results_stale()
    {
        _status.Begin();
        _now = _now.AddSeconds(3);

        var cut = Render(Sets(GridTestHost.ReadOnlySet()), running: true);

        var running = cut.Find("[data-testid=results-running]");
        Assert.Contains("00:00:03", running.TextContent);
        Assert.Contains("Cancel stops the running statement.", running.TextContent);   // spec 043: the engine reads it mid-run (R3)
        Assert.Contains("akml-results-stale", cut.Find("[data-testid=results-content]").ClassName);
        Assert.Equal("true", cut.Find("[data-testid=results-pane]").GetAttribute("aria-busy"));
    }

    [Fact]
    public void A_run_without_sets_says_there_is_nothing_to_show_on_the_results_tab()
    {
        var cut = Render(new ExecuteQueryResult { Status = ExecuteStatus.Ok });

        Assert.Contains("did not return any rows", cut.Find("[data-testid=results-no-sets]").TextContent);
    }

    [Fact]
    public void The_elapsed_time_and_affected_count_are_on_the_pane()
    {
        var result = Sets(GridTestHost.ReadOnlySet());
        result.TotalRowsAffected = 5;

        var cut = Render(result);

        Assert.Equal("42 ms", cut.Find("[data-testid=results-elapsed]").TextContent.Trim());
        Assert.Equal("5 affected", cut.Find("[data-testid=results-affected]").TextContent.Trim());
    }

    [Fact]
    public void Choosing_a_tab_reports_it_and_the_panels_stay_mounted()
    {
        string? chosen = null;
        var cut = Render(Sets(GridTestHost.ReadOnlySet()), tabChanged: t => chosen = t);

        cut.Find("[data-testid=results-tab-messages]").Click();

        Assert.Equal(ResultsPaneComponent.MessagesTab, chosen);
        // The grids are kept while another tab shows: their pending edits must survive a look at Messages.
        cut.Render(p => p.Add(x => x.ActiveTab, ResultsPaneComponent.MessagesTab));
        Assert.NotNull(cut.Find("[data-testid=results-set-0]"));
        Assert.True(cut.Find("[data-testid=results-panel-results]").HasAttribute("hidden"));
        Assert.False(cut.Find("[data-testid=results-panel-messages]").HasAttribute("hidden"));
        Assert.NotNull(cut.Find("[data-testid=messages-list]"));
    }

    [Fact]
    public void Every_set_is_stacked_with_its_header()
    {
        var capped = GridTestHost.ReadOnlySet();
        capped.Truncated = true;

        var cut = Render(Sets(GridTestHost.ReadOnlySet(), capped));

        Assert.Equal("Result 1 (5 rows)", cut.Find("[data-testid=results-set-header-0]").TextContent.Trim());
        Assert.Equal("Result 2 (5 rows returned; more exist)", cut.Find("[data-testid=results-set-header-1]").TextContent.Trim());
        Assert.NotNull(cut.Find("[data-testid=results-set-1] [data-testid=results-cell-0-0]"));
        Assert.Empty(cut.FindAll("[data-testid^=results-jump-]"));
    }

    [Fact]
    public void More_than_five_sets_get_a_jump_control()
    {
        var sets = Enumerable.Range(0, 6).Select(_ => GridTestHost.ReadOnlySet()).ToArray();

        var cut = Render(Sets(sets));

        Assert.Equal(6, cut.FindAll("[data-testid^=results-jump-]").Count);
        cut.Find("[data-testid=results-jump-4]").Click();   // scrolls (interop); must not throw
    }

    [Fact]
    public void Each_editable_set_has_its_own_Apply_and_applying_one_leaves_the_others_edits_alone()
    {
        var cut = Render(Sets(GridTestHost.EditableSet(), GridTestHost.EditableSet()));

        cut.Find("[data-testid=results-set-0] [data-testid=results-cell-0-1]").DoubleClick();
        cut.Find("[data-testid=results-set-0] [data-testid=results-cell-input]").Change("first");
        cut.Find("[data-testid=results-set-1] [data-testid=results-cell-1-1]").DoubleClick();
        cut.Find("[data-testid=results-set-1] [data-testid=results-cell-input]").Change("second");
        Assert.Equal(2, cut.Instance.PendingEditCount);
        Assert.True(cut.Instance.HasPendingEdits);

        cut.Find("[data-testid=results-apply-0]").Click();

        var sent = Assert.Single(_exec.Applied);
        Assert.Equal("first", sent.Edits.Single().SetCells.Single().Value);
        cut.WaitForAssertion(() => Assert.Equal(1, cut.Instance.PendingEditCount));
        Assert.NotNull(cut.Find("[data-testid=results-discard-1]"));
        Assert.Equal("second", cut.Find("[data-testid=results-set-1] [data-testid=results-cell-1-1]").TextContent.Trim());
    }

    [Fact]
    public void A_single_set_keeps_the_grids_own_ids()
    {
        var cut = Render(Sets(GridTestHost.EditableSet()));

        cut.Find("[data-testid=results-set-0] [data-testid=results-cell-0-1]").DoubleClick();
        cut.Find("[data-testid=results-cell-input]").Change("x");

        Assert.NotNull(cut.Find("[data-testid=results-set-0] [data-testid=results-apply]"));
    }

    private static CodeAnalysisResponse Findings(params int[] severities) => new()
    {
        Issues = severities.Select((s, i) => new CodeIssueInfo { RuleId = "ST001", Message = "m", Line = i + 1, Column = 1, Severity = s }).ToArray(),
    };

    [Fact]
    public void The_problems_badge_counts_findings_and_hides_at_zero_or_when_analysis_is_off()
    {
        var none = Render(null, findings: Findings());
        Assert.Empty(none.FindAll("[data-testid=results-problems-badge]"));

        var some = Render(null, findings: Findings(3, 2));
        some.WaitForAssertion(() => Assert.Equal("2", some.Find("[data-testid=results-problems-badge]").TextContent.Trim()));

        var off = Render(null, findings: Findings(3, 2), analysisEnabled: false);
        Assert.Empty(off.FindAll("[data-testid=results-problems-badge]"));
    }

    [Fact]
    public void With_analysis_off_the_Problems_tab_says_so_and_Turn_on_reaches_the_page()
    {
        // Spec 041 (T145, FR-034): the pane hands the switch and the default filter to its Problems list.
        int enabled = 0;
        var cut = _ctx.Render<ResultsPaneComponent>(p => p
            .Add(x => x.ActiveTab, ResultsPaneComponent.ProblemsTab)
            .Add(x => x.Findings, Findings(3))
            .Add(x => x.AnalysisEnabled, false)
            .Add(x => x.OnEnableAnalysis, () => enabled++));

        cut.Find("[data-testid=problems-enable-analysis]").Click();

        Assert.Equal(1, enabled);
    }

    [Fact]
    public void The_problems_default_filter_reaches_the_list()
    {
        var cut = _ctx.Render<ResultsPaneComponent>(p => p
            .Add(x => x.ActiveTab, ResultsPaneComponent.ProblemsTab)
            .Add(x => x.Findings, Findings(3))
            .Add(x => x.ProblemsDefaultFilter, new ProblemsFilter { ShowInfo = false, ShowWarning = false, ShowError = false }));

        Assert.Empty(cut.FindAll("[data-testid=problem-item]"));
    }

    [Fact]
    public void The_problems_list_inside_the_pane_has_no_header_of_its_own()
    {
        var cut = Render(null, activeTab: ResultsPaneComponent.ProblemsTab, findings: Findings(3));

        Assert.NotNull(cut.Find("[data-testid=problems-header]"));
        Assert.Empty(cut.FindAll("[data-testid=problems-title]"));
    }

    [Fact]
    public void The_problems_list_reports_its_visible_count_once_per_change()
    {
        var counts = new List<int>();
        var cut = _ctx.Render<ProblemsListComponent>(p => p
            .Add(x => x.Findings, Findings(3, 1))
            .Add(x => x.OnVisibleCountChanged, (int n) => counts.Add(n)));

        cut.Render(p => p.Add(x => x.Findings, Findings(3, 1)));   // same count: no event
        Assert.Equal(new[] { 2 }, counts);

        cut.FindAll("[data-testid=problems-header] input")[0].Change(false);   // hide Info
        Assert.Equal(new[] { 2, 1 }, counts);
    }

    // ── Spec 043 (T017, contracts/web-ui.md §3): one pane per query tab ────────────────────────────────────────

    [Fact]
    public void IdPrefix_drives_the_pane_and_grid_element_ids()
    {
        var cut = _ctx.Render<ResultsPaneComponent>(p => p
            .Add(x => x.Result, Sets(GridTestHost.ReadOnlySet()))
            .Add(x => x.IdPrefix, "akml-results-q2"));

        Assert.NotNull(cut.Find("#akml-results-q2-tab-results"));
        Assert.NotNull(cut.Find("#akml-results-q2-panel-results"));
        Assert.NotEmpty(cut.FindAll("[id^='akml-grid-q2-0-']"));
        Assert.Empty(cut.FindAll("[id^='akml-grid-0-']"));
    }

    [Fact]
    public void Without_an_IdPrefix_the_ids_are_the_ones_a_single_pane_always_had()
    {
        var cut = Render(Sets(GridTestHost.ReadOnlySet()));

        Assert.NotNull(cut.Find("#akml-results-tab-results"));
        Assert.NotNull(cut.Find("#akml-results-panel-results"));
        Assert.NotEmpty(cut.FindAll("[id^='akml-grid-0-']"));
    }

    [Fact]
    public void Only_the_active_pane_carries_the_shared_test_ids()
    {
        var cut = _ctx.Render<ResultsPaneComponent>(p => p
            .Add(x => x.Result, Sets(GridTestHost.ReadOnlySet()))
            .Add(x => x.IdPrefix, "akml-results-q3")
            .Add(x => x.IsActive, false));

        Assert.Empty(cut.FindAll("[data-testid=results-pane]"));
        Assert.NotNull(cut.Find("[data-testid=results-pane-inactive]"));
        Assert.Empty(cut.FindAll("[data-testid^=results-tab-]"));

        cut.Render(p => p.Add(x => x.IsActive, true));

        Assert.NotNull(cut.Find("[data-testid=results-pane]"));
        Assert.NotNull(cut.Find("[data-testid=results-tab-messages]"));
    }

    [Fact]
    public void The_running_clock_reads_the_tabs_own_start_not_the_app_wide_status()
    {
        // The app-wide status shows another tab's run (or none); this pane's run started 7 s ago.
        var cut = _ctx.Render<ResultsPaneComponent>(p => p
            .Add(x => x.Result, null)
            .Add(x => x.IsRunning, true)
            .Add(x => x.RunStartedAt, _now.AddSeconds(-7)));

        Assert.Contains("00:00:07", cut.Find("[data-testid=results-running]").TextContent);
    }

    [Fact]
    public void Apply_goes_through_the_session_of_the_tab_that_read_the_rows()
    {
        var session = new SqlSessionState("tab-q2-session");
        var cut = _ctx.Render<ResultsPaneComponent>(p => p
            .Add(x => x.Result, Sets(GridTestHost.EditableSet()))
            .Add(x => x.Session, session));

        cut.Find("[data-testid=results-cell-0-1]").DoubleClick();
        cut.Find("[data-testid=results-cell-input]").Change("x");
        cut.Find("[data-testid=results-apply]").Click();

        Assert.Single(_exec.Applied);
        Assert.Same(session, Assert.Single(_exec.AppliedSessions));
    }
}
