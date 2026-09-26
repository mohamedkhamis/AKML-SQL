using AkmlSql.Site.Analytics;
using AkmlSql.Site.Feedback;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Site.Tests.Feedback;

/// <summary>The inbox's storage: lifecycle, ordering, counts, and what is deliberately NOT stored.</summary>
public sealed class FeedbackStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private string DbPath => Path.Combine(_dir.Path, "analytics.db");

    private FeedbackItem Add(FeedbackStore store, string message = "The installer stops at 50%.",
        FeedbackCategory? category = null, string? email = null, int minutesLater = 0) =>
        store.Add(category ?? FeedbackCategory.Problem, message, email, "/download", "Egypt", "Chrome",
            Now.AddMinutes(minutesLater));

    [Fact]
    public void AnAddedMessage_IsOpenAndReadsBackIntact()
    {
        using var store = new FeedbackStore(DbPath);

        var added = Add(store, email: "someone@example.com");
        var read = Assert.Single(store.List(handled: false));

        Assert.True(added.Id > 0);
        Assert.Equal(added.Id, read.Id);
        Assert.Equal(Now, read.ReceivedUtc);
        Assert.Equal(FeedbackCategory.Problem, read.Category);
        Assert.Equal("The installer stops at 50%.", read.Message);
        Assert.Equal("someone@example.com", read.Email);
        Assert.Equal("/download", read.Page);
        Assert.Equal("Egypt", read.Country);
        Assert.Equal("Chrome", read.Browser);
        Assert.False(read.Handled);
        Assert.Null(read.HandledUtc);
        Assert.Equal(1, store.CountOpen());
    }

    [Fact]
    public void MessagesAreListedNewestFirst()
    {
        using var store = new FeedbackStore(DbPath);
        Add(store, "first message");
        Add(store, "second message", minutesLater: 5);

        Assert.Equal(["second message", "first message"], store.List(handled: null).Select(i => i.Message));
    }

    [Fact]
    public void Handling_MovesAMessageBetweenTheTabs_AndBackAgain()
    {
        using var store = new FeedbackStore(DbPath);
        var open = Add(store, "one");
        Add(store, "two");

        Assert.True(store.SetHandled(open.Id, handled: true, Now.AddHours(1)));

        Assert.Equal((1L, 1L), store.Counts());
        Assert.Equal(1, store.CountOpen());
        var handled = Assert.Single(store.List(handled: true));
        Assert.Equal(Now.AddHours(1), handled.HandledUtc);

        Assert.True(store.SetHandled(open.Id, handled: false, Now.AddHours(2)));

        Assert.Equal((2L, 0L), store.Counts());
        Assert.All(store.List(handled: null), i => Assert.Null(i.HandledUtc));
    }

    [Fact]
    public void Delete_RemovesTheMessageForGood()
    {
        using var store = new FeedbackStore(DbPath);
        var item = Add(store, email: "someone@example.com");

        Assert.True(store.Delete(item.Id));

        Assert.Empty(store.List(handled: null));
        Assert.Equal((0L, 0L), store.Counts());
    }

    [Fact]
    public void ActionsOnAnUnknownId_ReportFailureInsteadOfThrowing()
    {
        using var store = new FeedbackStore(DbPath);

        Assert.False(store.SetHandled(999, handled: true, Now));
        Assert.False(store.Delete(999));
    }

    [Fact]
    public void AnEmptyInbox_CountsZero()
    {
        using var store = new FeedbackStore(DbPath);

        Assert.Equal((0L, 0L), store.Counts());
        Assert.Equal(0, store.CountOpen());
    }

    [Fact]
    public void TheListIsCapped()
    {
        using var store = new FeedbackStore(DbPath);
        for (var i = 0; i < 5; i++)
        {
            Add(store, $"message {i}", minutesLater: i);
        }

        Assert.Equal(2, store.List(handled: null, limit: 2).Count);
    }

    [Fact]
    public void ARetiredCategory_StillDisplays()
    {
        using var store = new FeedbackStore(DbPath);
        Add(store);
        using (var connection = new SqliteConnection($"Data Source={DbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE feedback SET category = 'no-longer-offered';";
            command.ExecuteNonQuery();
        }

        Assert.Equal(FeedbackCategory.Other, Assert.Single(store.List(handled: null)).Category);
    }

    [Fact]
    public void NoAddressOrVisitorIdIsEverStored()
    {
        // The privacy notice says the IP address is not stored with feedback and nothing links a
        // message to the sender's visits. This is the schema that makes that true.
        using var store = new FeedbackStore(DbPath);
        using var connection = new SqliteConnection($"Data Source={DbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM pragma_table_info('feedback');";
        var columns = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }
        }

        Assert.NotEmpty(columns);
        Assert.DoesNotContain(columns, c => c.Contains("ip", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, c => c.Contains("visitor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ItSharesTheAnalyticsDatabaseWithoutDisturbingIt()
    {
        // Same file, same process, as on the deployed site.
        using var analytics = new AnalyticsStore(DbPath);
        using var store = new FeedbackStore(DbPath);

        analytics.LogVisit(new VisitInfo(Now, "/", null, "Chrome", "203.0.113.1"));
        Add(store);

        Assert.Equal(1, store.CountOpen());
        Assert.Equal(1, analytics.GetSummary(30, Now).Headline.Visits);
    }

    [Fact]
    public void MessagesSurviveARestart()
    {
        using (var store = new FeedbackStore(DbPath))
        {
            Add(store);
        }

        using var reopened = new FeedbackStore(DbPath);

        Assert.Single(reopened.List(handled: false));
    }
}
