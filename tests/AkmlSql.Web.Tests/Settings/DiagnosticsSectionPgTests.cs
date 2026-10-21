using AkmlSql.Core;
using AkmlSql.Web.Services;
using Bunit;
using Xunit;

namespace AkmlSql.Web.Tests.Settings;

/// <summary>
/// Spec 042 (task T112, FR-006, contracts/web-ui.md). Settings › Diagnostics says whether the paired engine offers PostgreSQL
/// and what the active connection is, so "why is the PostgreSQL choice greyed out" has an answer on screen.
/// </summary>
public sealed class DiagnosticsSectionPgTests : IDisposable
{
    private readonly SettingsPageHost _host = new();

    public void Dispose() => _host.Dispose();

    private static string[] WithPostgres => new[] { "core.format.v1", SqlConnectionService.CapabilityPostgreSql };

    [Fact]
    public void With_no_engine_the_row_says_so_instead_of_guessing()
    {
        _host.Bridge.SetState(BridgeState.Disconnected);

        var cut = _host.Open("diagnostics");

        var row = cut.WaitForElement("[data-testid=diag-postgresql]");
        Assert.Equal("no-engine", row.GetAttribute("data-state"));
        Assert.Contains("PostgreSQL support", row.TextContent);
        Assert.Contains("engine", row.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_engine_that_does_not_advertise_PostgreSQL_says_why_it_may_not()
    {
        _host.Bridge.SetState(BridgeState.Open);
        _host.Bridge.EngineCapabilities = new[] { "core.format.v1" };

        var cut = _host.Open("diagnostics");

        var row = cut.WaitForElement("[data-testid=diag-postgresql]");
        Assert.Equal("not-advertised", row.GetAttribute("data-state"));
        Assert.Contains("not advertised", row.TextContent);
        Assert.Contains("postgreSql.enabled", row.TextContent);
    }

    [Fact]
    public void An_engine_that_advertises_PostgreSQL_is_available()
    {
        _host.Bridge.SetState(BridgeState.Open);
        _host.Bridge.EngineCapabilities = WithPostgres;
        _host.Sql.EngineSupportsPostgres = true;

        var cut = _host.Open("diagnostics");

        var row = cut.WaitForElement("[data-testid=diag-postgresql]");
        Assert.Equal("available", row.GetAttribute("data-state"));
        Assert.Contains("available", row.TextContent);
    }

    [Fact]
    public void The_active_connection_is_named_by_kind_and_version()
    {
        _host.Bridge.SetState(BridgeState.Open);
        _host.Bridge.EngineCapabilities = WithPostgres;
        _host.Sql.EngineSupportsPostgres = true;
        _host.Sql.IsConnected = true;
        _host.Sql.Kind = DatabaseKind.PostgreSql;
        _host.Sql.Server = "pg1:5432";
        _host.Sql.Database = "northwind";
        _host.Sql.ServerVersionText = "16.4";

        var cut = _host.Open("diagnostics");

        var active = cut.WaitForElement("[data-testid=diag-active-connection]");
        Assert.Contains("PostgreSQL 16.4", active.TextContent);
        Assert.Contains("pg1:5432", active.TextContent);
        Assert.Contains("northwind", active.TextContent);
    }

    [Fact]
    public void A_SQL_Server_connection_and_no_connection_are_each_named()
    {
        _host.Bridge.SetState(BridgeState.Open);
        _host.Bridge.EngineCapabilities = WithPostgres;
        _host.Sql.IsConnected = true;
        _host.Sql.Kind = DatabaseKind.SqlServer;
        _host.Sql.Server = "srv";
        _host.Sql.Database = "db";

        var connected = _host.Open("diagnostics").WaitForElement("[data-testid=diag-active-connection]");
        Assert.Contains("SQL Server", connected.TextContent);
        Assert.DoesNotContain("PostgreSQL", connected.TextContent);

        _host.Sql.IsConnected = false;
        var idle = _host.Open("diagnostics").WaitForElement("[data-testid=diag-active-connection]");
        Assert.Contains("No SQL connection", idle.TextContent);
    }
}
