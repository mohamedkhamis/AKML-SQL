using System.Net.Mail;
using AkmlSql.Site.Analytics;
using AkmlSql.Site.Feedback;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AkmlSql.Site.Tests.Feedback;

/// <summary>
/// The notification email: what it says, which headers a visitor can influence (only Reply-To, and
/// only with an address validation already accepted), and that a failing mail server is reported
/// rather than silent -- and never takes the site down.
/// </summary>
public sealed class FeedbackNotifierTests
{
    private static readonly DateTimeOffset Received = new(2026, 9, 22, 10, 30, 0, TimeSpan.Zero);

    private static readonly TimeZoneInfo Cairo = TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");

    private static FeedbackOptions Configured() => new()
    {
        NotifyEmail = "owner@example.com",
        Smtp = { Host = "smtp.example.com", UserName = "sender@example.com", Password = "app-password" },
    };

    private static (FeedbackNotifier Notifier, RecordingMailer Mailer, FeedbackEmailStatus Status) NewNotifier(
        FeedbackOptions? options = null, Exception? failWith = null)
    {
        var mailer = new RecordingMailer(failWith);
        var status = new FeedbackEmailStatus();
        var notifier = new FeedbackNotifier(
            mailer, Options.Create(options ?? Configured()), status, new ReportClock(Cairo),
            NullLogger<FeedbackNotifier>.Instance);
        return (notifier, mailer, status);
    }

    private static FeedbackItem Item(string? email = "visitor@example.com", string message = "The installer stops at 50%.") =>
        new(7, Received, FeedbackCategory.Download, message, email, "/download", "Egypt", "Chrome", false, null);

    [Fact]
    public void TheSubjectIsTheCategoryAndIdOnly()
    {
        var (notifier, _, _) = NewNotifier();

        using var message = notifier.Compose(Item(message: "URGENT!!! click http://evil.example"));

        Assert.Equal("[AKML SQL] Download or installation problem (#7)", message.Subject);
    }

    [Fact]
    public void TheBodyCarriesTheMessageAndItsContext_InTheOwnersTime()
    {
        var (notifier, _, _) = NewNotifier();

        using var message = notifier.Compose(Item());

        Assert.StartsWith("The installer stops at 50%.", message.Body, StringComparison.Ordinal);
        Assert.Contains("About: Download or installation problem", message.Body, StringComparison.Ordinal);
        // 10:30 UTC on 22 Sep 2026 is 13:30 in Cairo (UTC+3, summer time).
        Assert.Contains("Received: Tue 22 Sep 2026, 13:30", message.Body, StringComparison.Ordinal);
        Assert.Contains("Page: /download", message.Body, StringComparison.Ordinal);
        Assert.Contains("From: Egypt, Chrome", message.Body, StringComparison.Ordinal);
        Assert.False(message.IsBodyHtml);
    }

    [Fact]
    public void ReplyingReachesTheVisitor_WhenTheyLeftAnAddress()
    {
        var (notifier, _, _) = NewNotifier();

        using var message = notifier.Compose(Item());

        Assert.Equal("visitor@example.com", Assert.Single(message.ReplyToList).Address);
        Assert.Equal("owner@example.com", Assert.Single(message.To).Address);
        Assert.Equal("sender@example.com", message.From!.Address);
    }

    [Fact]
    public void WithoutAnAddress_ThereIsNoReplyTo_AndTheBodySaysSo()
    {
        var (notifier, _, _) = NewNotifier();

        using var message = notifier.Compose(Item(email: null));

        Assert.Empty(message.ReplyToList);
        Assert.Contains("left no email address", message.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void HeaderLikeTextInTheMessage_StaysInTheBody()
    {
        var (notifier, _, _) = NewNotifier();

        using var message = notifier.Compose(Item(message: "hello\r\nBcc: victim@example.com\r\n\r\nbody"));

        Assert.Empty(message.Bcc);
        Assert.Empty(message.CC);
        Assert.Single(message.To);
    }

    [Theory]
    [InlineData("", "sender@example.com", "sender@example.com")]
    [InlineData("noreply@example.com", "sender@example.com", "noreply@example.com")]
    [InlineData("", "", "owner@example.com")]
    public void TheSender_FallsBackSensibly(string from, string userName, string expected)
    {
        var options = Configured();
        options.Smtp.From = from;
        options.Smtp.UserName = userName;
        var (notifier, _, _) = NewNotifier(options);

        using var message = notifier.Compose(Item());

        Assert.Equal(expected, message.From!.Address);
    }

    [Fact]
    public async Task NotConfigured_NothingIsQueuedOrSent()
    {
        var (notifier, mailer, _) = NewNotifier(new FeedbackOptions());

        Assert.False(notifier.Enabled);
        notifier.Enqueue(Item());
        Assert.False(await notifier.SendTestAsync(CancellationToken.None));
        Assert.Empty(mailer.Sent);
    }

    [Fact]
    public async Task ATestEmail_IsSentAndRecorded()
    {
        var (notifier, mailer, status) = NewNotifier();

        Assert.True(await notifier.SendTestAsync(CancellationToken.None));

        Assert.Equal("[AKML SQL] Test email from the site", Assert.Single(mailer.Sent).Subject);
        Assert.NotNull(status.LastSuccessUtc);
        Assert.Null(status.LastError);
    }

    [Fact]
    public async Task AFailingServer_IsReportedWithItsOwnWords()
    {
        var (notifier, _, status) = NewNotifier(
            failWith: new SmtpException("Failure sending mail.", new InvalidOperationException("5.7.8 Username and Password not accepted")));

        Assert.False(await notifier.SendTestAsync(CancellationToken.None));

        Assert.Equal("5.7.8 Username and Password not accepted", status.LastError);
        Assert.NotNull(status.LastAttemptUtc);
        Assert.Null(status.LastSuccessUtc);
    }

    [Fact]
    public async Task AMisconfiguredAddress_FailsTheEmailNotTheSite()
    {
        // MailMessage throws on a malformed address. That must be a recorded failure, never an
        // exception out of the background loop (which would stop the host).
        var options = Configured();
        options.NotifyEmail = "not an address";
        var (notifier, _, status) = NewNotifier(options);

        Assert.False(await notifier.SendTestAsync(CancellationToken.None));
        Assert.NotNull(status.LastError);
    }

    [Fact]
    public async Task QueuedMessages_AreSentInTheBackground()
    {
        var (notifier, mailer, status) = NewNotifier();
        await notifier.StartAsync(CancellationToken.None);
        try
        {
            notifier.Enqueue(Item());

            var sent = await mailer.NextAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal("[AKML SQL] Download or installation problem (#7)", sent);
            Assert.NotNull(status.LastSuccessUtc);
        }
        finally
        {
            await notifier.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task OneFailure_DoesNotStopLaterMessages()
    {
        var mailer = new RecordingMailer(failFirst: true);
        var notifier = new FeedbackNotifier(
            mailer, Options.Create(Configured()), new FeedbackEmailStatus(), new ReportClock(Cairo),
            NullLogger<FeedbackNotifier>.Instance);
        await notifier.StartAsync(CancellationToken.None);
        try
        {
            notifier.Enqueue(Item());
            notifier.Enqueue(Item() with { Id = 8 });

            Assert.Equal("[AKML SQL] Download or installation problem (#8)",
                await mailer.NextAsync().WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            await notifier.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>Records what would have been sent. Subjects are captured before the message is disposed.</summary>
    private sealed class RecordingMailer(Exception? failWith = null, bool failFirst = false) : IFeedbackMailer
    {
        private readonly TaskCompletionSource<string> _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _failedOnce;

        public List<(string Subject, string Body)> Sent { get; } = [];

        public Task<string> NextAsync() => _next.Task;

        public Task SendAsync(MailMessage message, CancellationToken cancellationToken)
        {
            if (failWith is not null)
            {
                throw failWith;
            }

            if (failFirst && !_failedOnce)
            {
                _failedOnce = true;
                throw new SmtpException("temporary failure");
            }

            Sent.Add((message.Subject, message.Body));
            _next.TrySetResult(message.Subject);
            return Task.CompletedTask;
        }
    }
}
