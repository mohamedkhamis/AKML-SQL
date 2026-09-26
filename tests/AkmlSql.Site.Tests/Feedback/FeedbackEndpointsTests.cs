using AkmlSql.Site.Analytics;
using AkmlSql.Site.Feedback;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace AkmlSql.Site.Tests.Feedback;

/// <summary>The inbox actions: each changes the store and returns to the tab it came from.</summary>
public sealed class FeedbackEndpointsTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _dir = new();
    private readonly FeedbackStore _store;

    public FeedbackEndpointsTests()
    {
        _store = new FeedbackStore(Path.Combine(_dir.Path, "analytics.db"));
    }

    public void Dispose()
    {
        _store.Dispose();
        _dir.Dispose();
    }

    private static IFormCollection Form(string? show = null) =>
        new FormCollection(show is null
            ? []
            : new Dictionary<string, StringValues> { ["show"] = show });

    private long AddOne() =>
        _store.Add(FeedbackCategory.Problem, "It crashed on start.", null, null, null, null, Now).Id;

    private static string Url(IResult result) => Assert.IsType<RedirectHttpResult>(result).Url;

    [Fact]
    public void MarkHandled_HandlesIt_AndReturnsToTheOpenTab()
    {
        var id = AddOne();

        var result = FeedbackEndpoints.MarkHandled(id, Form(), _store, Now);

        Assert.Equal("/admin/feedback?result=handled", Url(result));
        Assert.Equal((0L, 1L), _store.Counts());
    }

    [Fact]
    public void Reopen_OpensItAgain_AndStaysOnTheTabItCameFrom()
    {
        var id = AddOne();
        _store.SetHandled(id, handled: true, Now);

        var result = FeedbackEndpoints.Reopen(id, Form("handled"), _store);

        Assert.Equal("/admin/feedback?show=handled&result=reopened", Url(result));
        Assert.Equal((1L, 0L), _store.Counts());
    }

    [Fact]
    public void Delete_DeletesIt()
    {
        var id = AddOne();

        var result = FeedbackEndpoints.Delete(id, Form("all"), _store);

        Assert.Equal("/admin/feedback?show=all&result=deleted", Url(result));
        Assert.Empty(_store.List(handled: null));
    }

    [Fact]
    public void AnUnknownId_SaysSo()
    {
        Assert.Equal("/admin/feedback?result=missing", Url(FeedbackEndpoints.MarkHandled(404, Form(), _store, Now)));
        Assert.Equal("/admin/feedback?result=missing", Url(FeedbackEndpoints.Reopen(404, Form(), _store)));
        Assert.Equal("/admin/feedback?result=missing", Url(FeedbackEndpoints.Delete(404, Form(), _store)));
    }

    [Theory]
    [InlineData("open")]
    [InlineData("https://evil.example")]
    [InlineData("handled&result=x")]
    [InlineData("")]
    public void AnythingButAKnownTab_IsDropped(string show)
    {
        // The posted tab is user input on its way into a Location header.
        var result = FeedbackEndpoints.Delete(AddOne(), Form(show), _store);

        Assert.Equal("/admin/feedback?result=deleted", Url(result));
    }

    [Fact]
    public async Task TheTestEmail_ReportsFailureWhenEmailIsOff()
    {
        var notifier = new FeedbackNotifier(
            new NeverMailer(), Options.Create(new FeedbackOptions()), new FeedbackEmailStatus(),
            new ReportClock(TimeZoneInfo.Utc), NullLogger<FeedbackNotifier>.Instance);

        var result = await FeedbackEndpoints.SendTestAsync(Form(), notifier, CancellationToken.None);

        Assert.Equal("/admin/feedback?result=test-failed", Url(result));
    }

    private sealed class NeverMailer : IFeedbackMailer
    {
        public Task SendAsync(System.Net.Mail.MailMessage message, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("must not be called");
    }
}
