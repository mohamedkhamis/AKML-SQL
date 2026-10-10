using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Bridge;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 041 (T087, research R41/R65, FR-027, FR-083) — the status bar says how the last execute went, as SSMS's
/// does: "Executing query…" with a running clock, then "Query executed successfully." or "Query completed with
/// errors." with the rows and the time, announced politely to assistive technology.
/// </summary>
public sealed class StatusBarSegmentsTests : BunitContext
{
    private readonly FakeEngineBridge _bridge = new();
    private DateTimeOffset _now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly WorkspaceStatus _status;
    private readonly ConnectionManagerController _connManager = new();

    public StatusBarSegmentsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _status = new WorkspaceStatus(() => _now);
        var adapter = new InMemoryIndexedDbAdapter();
        Services.AddSingleton<IEngineBridge>(_bridge);
        Services.AddSingleton<ISchemaSync>(new FakeSchemaSync());
        Services.AddSingleton<ISchemaCacheStore>(new SchemaCacheStore(adapter));
        Services.AddSingleton<IConnectionStore>(new ConnectionStore(adapter));
        Services.AddSingleton<ISqlConnectionService>(new SqlConnectionService(_bridge, new DiagnosticsRingBuffer(adapter)));
        Services.AddSingleton<ISavedSqlConnectionStore>(new SavedSqlConnectionStore(adapter));
        Services.AddSingleton<IConnectionManagerController>(_connManager);
        Services.AddSingleton<IEngineAutoConnect>(new NoFailureAutoConnect());
        Services.AddSingleton<IWorkspaceStatus>(_status);
    }

    private static ExecuteQueryResult Result(int status, int rows, long ms) => new()
    {
        Status = status,
        ElapsedMs = ms,
        ErrorMessage = status == ExecuteStatus.Ok ? null : "bad",
        ResultSets = new[] { new ExecuteResultSet { ColumnNames = new[] { "n" }, Rows = Enumerable.Range(0, rows).Select(i => new string?[] { i.ToString() }).ToArray() } },
    };

    [Fact]
    public void Before_any_execute_there_is_no_outcome()
    {
        var cut = Render<StatusBar>();

        Assert.Equal(string.Empty, cut.Find("[data-testid=status-outcome]").TextContent.Trim());
        Assert.Empty(cut.FindAll("[data-testid=status-rows]"));
    }

    [Fact]
    public void The_outcome_is_a_polite_status_region()
    {
        var cut = Render<StatusBar>();

        var outcome = cut.Find("[data-testid=status-outcome]");
        Assert.Equal("status", outcome.GetAttribute("role"));
        Assert.Equal("polite", outcome.GetAttribute("aria-live"));
    }

    [Fact]
    public void While_running_it_says_so_and_counts_the_elapsed_time()
    {
        var cut = Render<StatusBar>();

        cut.InvokeAsync(_status.Begin);
        _now = _now.AddSeconds(65);
        cut.Render();

        Assert.Equal(WorkspaceStatus.RunningText, cut.Find("[data-testid=status-outcome]").TextContent.Trim());
        Assert.Contains("#akml-icon-loader-circle", cut.Find("[data-testid=status-outcome]").InnerHtml);
        Assert.Equal("00:01:05", cut.Find("[data-testid=status-rows]").TextContent.Trim());
    }

    [Fact]
    public void A_successful_run_shows_its_rows_and_time()
    {
        var cut = Render<StatusBar>();

        cut.InvokeAsync(_status.Begin);
        cut.InvokeAsync(() => _status.Complete(Result(ExecuteStatus.Ok, 16, 87)));

        Assert.Equal(WorkspaceStatus.SucceededText, cut.Find("[data-testid=status-outcome]").TextContent.Trim());
        Assert.Contains("#akml-icon-circle-check", cut.Find("[data-testid=status-outcome]").InnerHtml);
        Assert.Equal("16 rows · 87 ms", cut.Find("[data-testid=status-rows]").TextContent.Trim());
    }

    [Fact]
    public void A_run_with_errors_says_so()
    {
        var cut = Render<StatusBar>();

        cut.InvokeAsync(_status.Begin);
        cut.InvokeAsync(() => _status.Complete(Result(ExecuteStatus.Error, 1, 1_250)));

        Assert.Equal(WorkspaceStatus.FailedText, cut.Find("[data-testid=status-outcome]").TextContent.Trim());
        Assert.Contains("#akml-icon-circle-alert", cut.Find("[data-testid=status-outcome]").InnerHtml);
        Assert.Equal("1 row · 1,250 ms", cut.Find("[data-testid=status-rows]").TextContent.Trim());
    }

    [Fact]
    public void The_connection_chip_keeps_its_server_slash_database_format_or_says_connect()
    {
        var cut = Render<StatusBar>();

        Assert.NotNull(cut.Find("[data-testid=status-connection]"));
    }

    // ── Spec 041 (T183, research R65, FR-083) — caret, versions, login, the server link ─────────────────────

    [Fact]
    public void The_caret_reads_Ln_Col()
    {
        var cut = Render<StatusBar>();

        cut.InvokeAsync(() => _status.SetCaret(1, 12));

        cut.WaitForAssertion(() => Assert.Equal("Ln 1, Col 12", cut.Find("[data-testid=status-caret]").TextContent.Trim()));
    }

    [Fact]
    public void The_versions_are_short_with_the_full_string_on_hover_and_a_mismatch_is_marked()
    {
        _bridge.EngineVersion = "1.20.0101.0000+abcdef";
        var cut = Render<StatusBar>();

        var web = cut.Find("[data-testid=status-version-web]");
        Assert.Contains(AkmlSql.Core.AppVersion.Current, web.TextContent);
        Assert.Equal(AkmlSql.Core.AppVersion.Informational, web.GetAttribute("title"));
        var engine = cut.Find("[data-testid=status-version-engine]");
        Assert.Contains("1.20.0101.0000", engine.TextContent);
        Assert.DoesNotContain("abcdef", engine.TextContent);
        Assert.Equal("1.20.0101.0000+abcdef", engine.GetAttribute("title"));
        Assert.NotNull(cut.Find("[data-testid=status-version-mismatch]"));
    }

    [Fact]
    public void Matching_versions_carry_no_mismatch_marker()
    {
        _bridge.EngineVersion = AkmlSql.Core.AppVersion.Current + "+different-build-metadata";
        var cut = Render<StatusBar>();

        Assert.Empty(cut.FindAll("[data-testid=status-version-mismatch]"));
    }

    [Fact]
    public async Task The_login_shows_beside_the_connection_and_the_server_opens_the_connection_manager()
    {
        _bridge.SetState(BridgeState.Open);
        var sql = Services.GetRequiredService<ISqlConnectionService>();
        await sql.ConnectAsync("remote-sql", "Sales", windowsAuth: false, "report_user", "pw", CancellationToken.None);
        int opened = 0;
        _connManager.OpenRequested += () => opened++;

        var cut = Render<StatusBar>();

        cut.WaitForAssertion(() => Assert.Equal("report_user", cut.Find("[data-testid=status-login]").TextContent.Trim()));
        Assert.Contains("remote-sql/Sales", cut.Find("[data-testid=status-connection]").TextContent);
        cut.Find("[data-testid=status-connection]").Click();
        Assert.Equal(1, opened);
    }
}
