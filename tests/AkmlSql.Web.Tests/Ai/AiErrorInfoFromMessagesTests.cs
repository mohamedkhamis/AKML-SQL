using AkmlSql.Core;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Spec 047 (AI-002), installed-product run F2: Fix is asked about the first server error of the tab's last run. The editor
/// read that error only for PostgreSQL, so once Fix required an error (AI-002) it could never be used on a SQL Server tab —
/// after "Query completed with errors." (Msg 208) Fix stayed disabled. Both dialects now give their error.
/// </summary>
public sealed class AiErrorInfoFromMessagesTests
{
    private static ExecuteMessageDto Row(long count) => new() { Kind = ExecuteMessageKind.RowCount, RowCount = count, Text = $"({count} rows affected)" };

    private static ExecuteMessageDto Info(string text) => new() { Kind = ExecuteMessageKind.Info, Text = text, Severity = 0 };

    private static ExecuteMessageDto SqlError(int number, string text) =>
        new() { Kind = ExecuteMessageKind.Error, Number = number, Severity = 16, State = 1, Line = 1, Text = text };

    [Fact]
    public void A_SQL_Server_error_is_given_with_its_number()
    {
        var messages = new[] { Info("Changed database context to 'Northwind'."), SqlError(208, "Invalid object name 'dbo.NoSuchTable'.") };

        var error = AiErrorInfo.FromMessages(messages, DatabaseKind.SqlServer);

        Assert.NotNull(error);
        Assert.Equal("Invalid object name 'dbo.NoSuchTable'.", error!.Message);
        Assert.Equal(208, error.Number);
        Assert.Null(error.SqlState);
    }

    [Fact]
    public void A_PostgreSQL_error_is_given_with_its_SQLSTATE_detail_and_hint()
    {
        var messages = new[]
        {
            new ExecuteMessageDto
            {
                Kind = ExecuteMessageKind.Error, Text = "relation \"nosuch\" does not exist", SqlState = "42P01",
                Detail = "a detail", Hint = "a hint",
            },
        };

        var error = AiErrorInfo.FromMessages(messages, DatabaseKind.PostgreSql);

        Assert.NotNull(error);
        Assert.Equal("relation \"nosuch\" does not exist", error!.Message);
        Assert.Equal(0, error.Number);
        Assert.Equal("42P01", error.SqlState);
        Assert.Equal("a detail", error.Detail);
        Assert.Equal("a hint", error.Hint);
    }

    [Fact]
    public void The_first_error_of_the_run_is_the_one_given()
    {
        var messages = new[] { Row(3), SqlError(2627, "Violation of PRIMARY KEY constraint."), SqlError(3621, "The statement has been terminated.") };

        Assert.Equal(2627, AiErrorInfo.FromMessages(messages, DatabaseKind.SqlServer)!.Number);
    }

    [Theory]
    [InlineData(DatabaseKind.SqlServer)]
    [InlineData(DatabaseKind.PostgreSql)]
    public void A_run_without_an_error_gives_none(DatabaseKind kind)
    {
        Assert.Null(AiErrorInfo.FromMessages(new[] { Info("hello"), Row(5) }, kind));
        Assert.Null(AiErrorInfo.FromMessages(Array.Empty<ExecuteMessageDto>(), kind));
        Assert.Null(AiErrorInfo.FromMessages(null, kind));
    }
}
