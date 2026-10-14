using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.History;

/// <summary>
/// Spec 042 (task T070, FR-020): a run is recorded with the kind of server it ran on, and the connection manager can be
/// asked to open on a kind.
/// </summary>
public sealed class WebHistoryKindTests
{
    private static ExecuteQueryResult Ok() => new() { Status = ExecuteStatus.Ok, ElapsedMs = 12 };

    [Fact]
    public void A_run_is_recorded_as_SQL_Server_unless_told_otherwise()
    {
        var request = WebHistoryLogic.BuildRecordRequest("SELECT 1", Ok(), "localhost", "master");

        Assert.Equal((int)DatabaseKind.SqlServer, request.Kind);
    }

    [Fact]
    public void A_PostgreSQL_run_carries_its_kind_server_and_database()
    {
        var request = WebHistoryLogic.BuildRecordRequest("SELECT 1", Ok(), "db1:5432", "northwind", "session-1", "SQLQuery1", DatabaseKind.PostgreSql);

        Assert.Equal((int)DatabaseKind.PostgreSql, request.Kind);
        Assert.Equal("db1:5432", request.Server);
        Assert.Equal("northwind", request.Database);
        Assert.Equal("web", request.Source);
    }

    [Fact]
    public void Open_remembers_the_kind_it_was_asked_for_and_a_plain_open_clears_it()
    {
        var controller = new ConnectionManagerController();
        var opened = 0;
        controller.OpenRequested += () => opened++;

        controller.Open(DatabaseKind.PostgreSql);
        Assert.Equal(DatabaseKind.PostgreSql, controller.RequestedKind);

        controller.Open();
        Assert.Null(controller.RequestedKind);
        Assert.Equal(2, opened);
    }
}
