using AkmlSql.Site.Analytics;
using AkmlSql.Site.Telemetry;
using Xunit;

namespace AkmlSql.Site.Tests.Telemetry;

/// <summary>
/// Client-error storage against a temp-file SQLite database: batch insert, the summary read
/// model (window boundaries, per-level counts, distinct installs, recent ordering and the level
/// filter), and retention pruning of the client_errors table.
/// </summary>
public sealed class ClientErrorStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static AnalyticsStore NewStore(TempDirectory dir) => new(Path.Combine(dir.Path, "analytics.db"));

    private static ClientErrorInfo Error(
        int daysAgo,
        string level = "Error",
        string message = "boom",
        string? exception = null,
        string? installId = "install-a") =>
        new(
            Now.AddDays(-daysAgo),
            Now.AddDays(-daysAgo),
            level,
            message,
            exception,
            "parser",
            "1.4.0",
            "ssms",
            installId);

    [Fact]
    public void LogClientErrors_RoundTrips_IntoSummary()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogClientErrors(new ClientErrorBatch(
        [
            Error(0, "Error", "first", installId: "install-a"),
            Error(0, "Warning", "second", installId: "install-b"),
            Error(0, "Error", "third", exception: "System.Exception: boom", installId: "install-a"),
        ]));

        var summary = store.GetClientErrorsSummary(30, null, 200, Now);

        Assert.Equal(30, summary.Days);
        Assert.Equal(3, summary.TotalWindow);
        Assert.Equal(2, summary.DistinctInstallsWindow);
        Assert.Equal(new CountRow("Error", 2), summary.ByLevel[0]);
        Assert.Equal(new CountRow("Warning", 1), summary.ByLevel[1]);

        // Recent is newest first (id DESC).
        Assert.Equal(3, summary.Recent.Count);
        Assert.Equal("third", summary.Recent[0].Message);
        Assert.Equal("first", summary.Recent[2].Message);

        var row = summary.Recent[0];
        Assert.True(row.Id > 0);
        Assert.Equal(Now, row.Utc);
        Assert.Equal("Error", row.Level);
        Assert.Equal("1.4.0", row.ProductVersion);
        Assert.Equal("ssms", row.Host);
        Assert.Equal("install-a", row.InstallId);
        Assert.Equal("System.Exception: boom", row.Exception);
    }

    [Fact]
    public void DistinctInstalls_ExcludesNullAndEmpty()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogClientErrors(new ClientErrorBatch(
        [
            Error(0, installId: "install-a"),
            Error(0, installId: "install-a"),
            Error(0, installId: null),
            Error(0, installId: ""),
        ]));

        var summary = store.GetClientErrorsSummary(30, null, 200, Now);

        Assert.Equal(4, summary.TotalWindow);
        Assert.Equal(1, summary.DistinctInstallsWindow);
    }

    [Fact]
    public void Summary_WindowIsTodayInclusive()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogClientErrors(new ClientErrorBatch(
        [
            Error(0, message: "today"),
            Error(1, message: "yesterday"),
            Error(6, message: "day six"),
            Error(7, message: "day seven"),
            Error(40, message: "ancient"),
        ]));

        // The 7-day window is today plus the previous 6 days — the same convention as GetSummary.
        var week = store.GetClientErrorsSummary(7, null, 200, Now);
        Assert.Equal(3, week.TotalWindow);
        Assert.Equal(3, week.Recent.Count);
        Assert.DoesNotContain(week.Recent, r => r.Message == "day seven");

        Assert.Equal(1, store.GetClientErrorsSummary(1, null, 200, Now).TotalWindow);
        Assert.Equal(5, store.GetClientErrorsSummary(60, null, 200, Now).TotalWindow);
    }

    [Fact]
    public void Summary_LevelFilterAppliesToRecentOnly()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogClientErrors(new ClientErrorBatch(
        [
            Error(0, "Error", "an error"),
            Error(0, "Warning", "first warning"),
            Error(0, "Warning", "second warning"),
        ]));

        var summary = store.GetClientErrorsSummary(30, "Warning", 200, Now);

        Assert.Equal(3, summary.TotalWindow);          // every level still counted
        Assert.Equal(2, summary.ByLevel.Count);        // breakdown unaffected
        Assert.Equal(2, summary.Recent.Count);
        Assert.All(summary.Recent, r => Assert.Equal("Warning", r.Level));
    }

    [Fact]
    public void Summary_ClampsTheRecentLimit()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogClientErrors(new ClientErrorBatch([Error(0, message: "one"), Error(0, message: "two"), Error(0, message: "three")]));

        var one = store.GetClientErrorsSummary(30, null, 1, Now);
        var row = Assert.Single(one.Recent);
        Assert.Equal("three", row.Message); // the newest survives

        // 0 clamps UP to 1 rather than returning nothing; absurd values clamp down to 500.
        Assert.Single(store.GetClientErrorsSummary(30, null, 0, Now).Recent);
        Assert.Equal(3, store.GetClientErrorsSummary(30, null, 100_000, Now).Recent.Count);
    }

    [Fact]
    public void Prune_RemovesOldClientErrorRows()
    {
        using var dir = new TempDirectory();
        using var store = NewStore(dir);

        store.LogClientErrors(new ClientErrorBatch([Error(500, message: "old"), Error(0, message: "fresh")]));
        Assert.Equal(2, store.GetClientErrorsSummary(600, null, 200, Now).TotalWindow);

        Assert.Equal(1, store.Prune(400, Now));

        var summary = store.GetClientErrorsSummary(600, null, 200, Now);
        Assert.Equal(1, summary.TotalWindow);
        Assert.Equal("fresh", Assert.Single(summary.Recent).Message);
    }
}
