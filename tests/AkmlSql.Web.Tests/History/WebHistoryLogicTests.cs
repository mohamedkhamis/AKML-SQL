using System;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.History;

public class WebHistoryLogicTests
{
    private static readonly DateTime Now = new(2026, 6, 28, 12, 0, 0, DateTimeKind.Local);
    private static string Iso(DateTime local) => local.ToUniversalTime().ToString("o");

    [Fact]
    public void DateBucket_Today() =>
        Assert.Equal(WebHistoryLogic.BucketToday, WebHistoryLogic.DateBucket(Iso(Now.AddHours(-2)), Now));

    [Fact]
    public void DateBucket_ThisWeek() =>
        Assert.Equal(WebHistoryLogic.BucketThisWeek, WebHistoryLogic.DateBucket(Iso(Now.AddDays(-3)), Now));

    [Fact]
    public void DateBucket_TwoMonths() =>
        Assert.Equal(WebHistoryLogic.BucketTwoMonths, WebHistoryLogic.DateBucket(Iso(Now.AddDays(-30)), Now));

    [Fact]
    public void DateBucket_Older() =>
        Assert.Equal(WebHistoryLogic.BucketOlder, WebHistoryLogic.DateBucket(Iso(Now.AddDays(-90)), Now));

    [Fact]
    public void DateBucket_Unparseable_Older() =>
        Assert.Equal(WebHistoryLogic.BucketOlder, WebHistoryLogic.DateBucket("not-a-date", Now));

    [Fact]
    public void DeriveSources_DistinctSortedNonEmpty()
    {
        var entries = new[]
        {
            new HistoryEntryDto { Server = "S2", Database = "D1" },
            new HistoryEntryDto { Server = "s2", Database = "" },
            new HistoryEntryDto { Server = "S1", Database = "D2" },
            new HistoryEntryDto { Server = null,  Database = "D1" },
        };
        var (servers, databases) = WebHistoryLogic.DeriveSources(entries);
        Assert.Equal(new[] { "S1", "S2" }, servers);
        Assert.Equal(new[] { "D1", "D2" }, databases);
    }

    [Fact]
    public void DeriveRowCount_Select_SumsResultRows()
    {
        var result = new ExecuteQueryResult
        {
            TotalRowsAffected = -1,
            ResultSets = new[]
            {
                new ExecuteResultSet { Rows = new string?[3][] },
                new ExecuteResultSet { Rows = new string?[2][] },
            }
        };
        Assert.Equal(5, WebHistoryLogic.DeriveRowCount(result));
    }

    [Fact]
    public void DeriveRowCount_Dml_UsesAffected()
    {
        var result = new ExecuteQueryResult { TotalRowsAffected = 7, ResultSets = Array.Empty<ExecuteResultSet>() };
        Assert.Equal(7, WebHistoryLogic.DeriveRowCount(result));
    }

    [Theory]
    [InlineData(ExecuteStatus.Ok, true, 0)]
    [InlineData(ExecuteStatus.Error, true, 1)]
    [InlineData(ExecuteStatus.TimedOut, true, 1)]
    [InlineData(ExecuteStatus.Cancelled, true, 2)]
    [InlineData(ExecuteStatus.NoConnection, false, 1)]
    public void StatusMapping(int status, bool shouldRecord, int mapped)
    {
        Assert.Equal(shouldRecord, WebHistoryLogic.ShouldRecord(status));
        Assert.Equal(mapped, WebHistoryLogic.MapStatus(status));
    }

    [Fact]
    public void BuildRecordRequest_ComposesFields()
    {
        var result = new ExecuteQueryResult
        {
            Status = ExecuteStatus.Ok,
            ElapsedMs = 42,
            TotalRowsAffected = -1,
            ResultSets = new[] { new ExecuteResultSet { Rows = new string?[4][] } }
        };
        var req = WebHistoryLogic.BuildRecordRequest("SELECT 1", result, "localhost", "Northwind");
        Assert.Equal("SELECT 1", req.SqlText);
        Assert.Equal("localhost", req.Server);
        Assert.Equal("Northwind", req.Database);
        Assert.Equal(42, req.DurationMs);
        Assert.Equal(4, req.RowCount);
        Assert.Equal(0, req.Status);
        Assert.Equal("web", req.Source);
    }
}
