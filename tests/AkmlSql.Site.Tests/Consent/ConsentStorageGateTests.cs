using AkmlSql.Site.Analytics;
using AkmlSql.Site.Consent;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Site.Tests.Consent;

/// <summary>
/// Spec 038 T055 (US5): <b>the most important test in this feature.</b>
/// <para>
/// The owner chose to store full IP addresses and a persistent identifier. The single property that
/// makes that defensible is that neither is written without consent. These tests assert it at the
/// storage layer — the last point before the value reaches disk — because a guard that lives only in
/// a page or a middleware can be bypassed by the next caller somebody adds.
/// </para>
/// </summary>
public sealed class ConsentStorageGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
    private const string Ip = "203.0.113.7";

    private static string DbPath(TempDirectory dir) => Path.Combine(dir.Path, "analytics.db");

    private static object? Scalar(string dbPath, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : value;
    }

    private static VisitInfo Visit(ConsentState consent, string? visitorId) =>
        new(Now, "/download", null, "Chrome", Ip) { Consent = consent, VisitorId = visitorId };

    private static DownloadInfo Download(ConsentState consent, string? visitorId) =>
        new(Now, "AKMLSQLSetup-1.26.0910.2248.exe", null, "Chrome", Ip) { Consent = consent, VisitorId = visitorId };

    [Theory]
    [InlineData(ConsentState.Denied, "denied")]
    [InlineData(ConsentState.Unknown, "unknown")]
    public void WithoutConsent_NoAddressAndNoIdentifierReachDisk(ConsentState consent, string expectedColumn)
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));

        store.LogVisit(Visit(consent, visitorId: null));

        // SC-014: a visitor who refused leaves no identifiable trace.
        Assert.Null(Scalar(DbPath(dir), "SELECT ip FROM visits LIMIT 1;"));
        Assert.Null(Scalar(DbPath(dir), "SELECT visitor_id FROM visits LIMIT 1;"));
        Assert.Equal(expectedColumn, Scalar(DbPath(dir), "SELECT consent FROM visits LIMIT 1;"));

        // ...but they are still COUNTED, and the pre-existing privacy columns still work, so the
        // aggregate figures stay accurate for exactly the people who declined (contract C3.2/C3.3).
        Assert.NotNull(Scalar(DbPath(dir), "SELECT ip_hash FROM visits LIMIT 1;"));
        Assert.Equal("203.0.113.0", Scalar(DbPath(dir), "SELECT ip_prefix FROM visits LIMIT 1;"));
        Assert.Equal(1, store.GetSummary(30, Now).VisitsToday);
    }

    [Fact]
    public void WithConsent_TheAddressAndIdentifierArePersisted()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));
        var visitorId = Guid.NewGuid().ToString("N");

        store.LogVisit(Visit(ConsentState.Granted, visitorId));

        Assert.Equal(Ip, Scalar(DbPath(dir), "SELECT ip FROM visits LIMIT 1;"));
        Assert.Equal(visitorId, Scalar(DbPath(dir), "SELECT visitor_id FROM visits LIMIT 1;"));
        Assert.Equal("granted", Scalar(DbPath(dir), "SELECT consent FROM visits LIMIT 1;"));
    }

    [Fact]
    public void TheGateIsInTheStore_NotMerelyInTheCaller()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));

        // A caller that has BOTH a visitor id and an address, but a state of Denied. This is the
        // shape a bug would take: a page or middleware that forgot to clear the fields. The store
        // must refuse it anyway (contract C3.1).
        store.LogVisit(Visit(ConsentState.Denied, Guid.NewGuid().ToString("N")));

        Assert.Null(Scalar(DbPath(dir), "SELECT ip FROM visits LIMIT 1;"));
        Assert.Null(Scalar(DbPath(dir), "SELECT visitor_id FROM visits LIMIT 1;"));
    }

    [Fact]
    public void TheSameGateAppliesToDownloads()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));

        store.LogDownload(Download(ConsentState.Denied, Guid.NewGuid().ToString("N")));

        Assert.Null(Scalar(DbPath(dir), "SELECT ip FROM downloads LIMIT 1;"));
        Assert.Null(Scalar(DbPath(dir), "SELECT visitor_id FROM downloads LIMIT 1;"));
    }

    [Fact]
    public void ADeclinedDownload_IsStillRecordedAndStillCounted()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));

        store.LogDownload(Download(ConsentState.Denied, visitorId: null));

        // FR-044 / contract C3.4: refusing tracking must not degrade the download path or remove
        // the visitor from the totals. They are counted; they are not identified.
        Assert.Equal(1, store.GetSummary(30, Now).DownloadsTotal);
        Assert.Equal("denied", Scalar(DbPath(dir), "SELECT consent FROM downloads LIMIT 1;"));
    }

    [Fact]
    public void ReleaseVersion_IsStoredRegardlessOfConsent()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));

        store.LogDownload(Download(ConsentState.Denied, null) with { ReleaseVersion = "1.26.0910.2248" });

        // The version is a fact about the FILE, not about the person, so it is never gated.
        Assert.Equal("1.26.0910.2248", Scalar(DbPath(dir), "SELECT release_version FROM downloads LIMIT 1;"));
    }

    [Fact]
    public void DeleteVisitor_RemovesEveryRowAcrossBothTables()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));
        var mine = Guid.NewGuid().ToString("N");
        var someoneElse = Guid.NewGuid().ToString("N");

        store.LogVisit(Visit(ConsentState.Granted, mine));
        store.LogVisit(Visit(ConsentState.Granted, mine));
        store.LogDownload(Download(ConsentState.Granted, mine));
        store.LogVisit(Visit(ConsentState.Granted, someoneElse));

        var deleted = store.DeleteVisitor(mine);

        // FR-040 / contract C4.3: nothing may remain that could re-link the person.
        Assert.Equal(3, deleted);
        Assert.Equal(0L, Convert.ToInt64(Scalar(DbPath(dir), $"SELECT COUNT(*) FROM visits WHERE visitor_id = '{mine}';")));
        Assert.Equal(0L, Convert.ToInt64(Scalar(DbPath(dir), $"SELECT COUNT(*) FROM downloads WHERE visitor_id = '{mine}';")));

        // ...and nobody else is touched.
        Assert.Equal(1L, Convert.ToInt64(Scalar(DbPath(dir), $"SELECT COUNT(*) FROM visits WHERE visitor_id = '{someoneElse}';")));
    }

    [Fact]
    public void DeleteVisitor_WithNothingStored_IsAHarmlessNoOp()
    {
        using var dir = new TempDirectory();
        using var store = new AnalyticsStore(DbPath(dir));

        // Contract C4.4: withdrawal must succeed for someone who was never tracked, without
        // revealing whether an id existed.
        Assert.Equal(0, store.DeleteVisitor(Guid.NewGuid().ToString("N")));
        Assert.Equal(0, store.DeleteVisitor(""));
    }
}
