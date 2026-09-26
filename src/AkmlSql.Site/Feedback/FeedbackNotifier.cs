using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Channels;
using AkmlSql.Site.Analytics;
using Microsoft.Extensions.Options;

namespace AkmlSql.Site.Feedback;

/// <summary>Sends one email. Abstracted so the notifier can be tested without a mail server.</summary>
public interface IFeedbackMailer
{
    Task SendAsync(MailMessage message, CancellationToken cancellationToken);
}

/// <summary>SMTP delivery using the configured server.</summary>
public sealed class SmtpFeedbackMailer(IOptions<FeedbackOptions> options) : IFeedbackMailer
{
    public async Task SendAsync(MailMessage message, CancellationToken cancellationToken)
    {
        var smtp = options.Value.Smtp;
        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            // A mail server that hangs must not hold a worker for the default 100 seconds.
            Timeout = 20_000,
        };

        if (!string.IsNullOrEmpty(smtp.UserName))
        {
            client.Credentials = new NetworkCredential(smtp.UserName, smtp.Password);
        }

        await client.SendMailAsync(message, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// What happened the last time email was attempted, for the inbox. Without it, a wrong password
/// or a blocked port fails silently: messages still arrive in the inbox, the owner assumes email
/// works, and stops checking the inbox.
/// </summary>
public sealed class FeedbackEmailStatus
{
    private readonly object _gate = new();

    public DateTimeOffset? LastAttemptUtc { get; private set; }

    public DateTimeOffset? LastSuccessUtc { get; private set; }

    /// <summary>The last failure's message, or null when the last attempt succeeded.</summary>
    public string? LastError { get; private set; }

    public void RecordSuccess(DateTimeOffset now)
    {
        lock (_gate)
        {
            LastAttemptUtc = now;
            LastSuccessUtc = now;
            LastError = null;
        }
    }

    public void RecordFailure(DateTimeOffset now, string error)
    {
        lock (_gate)
        {
            LastAttemptUtc = now;
            LastError = error;
        }
    }
}

/// <summary>
/// Composes and sends the notification email for a message, and runs queued sends in the
/// background.
/// <para>
/// Background, because the visitor must never wait on a mail server: the form redirects as soon
/// as the message is safely in the database, and the email follows. A failure is recorded on
/// <see cref="FeedbackEmailStatus"/> and logged; the message itself is already stored either way.
/// </para>
/// </summary>
public sealed class FeedbackNotifier : BackgroundService
{
    private readonly Channel<FeedbackItem> _queue =
        Channel.CreateBounded<FeedbackItem>(new BoundedChannelOptions(256)
        {
            // Under a flood, drop the NEWEST notifications rather than block the form: the messages
            // themselves are all stored, and the inbox is the source of truth.
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    private readonly IFeedbackMailer _mailer;
    private readonly IOptions<FeedbackOptions> _options;
    private readonly FeedbackEmailStatus _status;
    private readonly ReportClock _clock;
    private readonly ILogger<FeedbackNotifier> _logger;

    public FeedbackNotifier(
        IFeedbackMailer mailer,
        IOptions<FeedbackOptions> options,
        FeedbackEmailStatus status,
        ReportClock clock,
        ILogger<FeedbackNotifier> logger)
    {
        _mailer = mailer;
        _options = options;
        _status = status;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>True when email notification is configured at all.</summary>
    public bool Enabled => _options.Value.EmailConfigured;

    /// <summary>Queues the notification for <paramref name="item"/>. Never blocks, never throws.</summary>
    public void Enqueue(FeedbackItem item)
    {
        if (Enabled)
        {
            _queue.Writer.TryWrite(item);
        }
    }

    /// <summary>Sends a test message now, for the inbox's "Send a test email" button.</summary>
    public async Task<bool> SendTestAsync(CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return false;
        }

        return await TrySendAsync(
            () => NewMessage(
                "[AKML SQL] Test email from the site",
                "This is a test of the feedback email settings. If you can read this, new messages sent\n" +
                "from the site will arrive here too.\n"),
            cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            await TrySendAsync(() => Compose(item), stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The notification for one message. Plain text; the subject is built from the fixed category
    /// label and the id only, never from anything the visitor typed, so nothing they write can end
    /// up in a header.
    /// </summary>
    public MailMessage Compose(FeedbackItem item)
    {
        var local = TimeZoneInfo.ConvertTime(item.ReceivedUtc, _clock.Zone);
        var body = new StringBuilder()
            .Append(item.Message).Append("\n\n")
            .Append("----\n")
            .Append("About: ").Append(item.Category.Label).Append('\n')
            .Append("Received: ").Append(local.ToString("ddd d MMM yyyy, HH:mm", CultureInfo.InvariantCulture)).Append('\n');

        if (item.Page is not null)
        {
            body.Append("Page: ").Append(item.Page).Append('\n');
        }

        if (item.Country is not null || item.Browser is not null)
        {
            body.Append("From: ").Append(string.Join(", ", new[] { item.Country, item.Browser }.Where(v => v is not null))).Append('\n');
        }

        body.Append(item.Email is null
            ? "Reply: the sender left no email address.\n"
            : $"Reply: answer this email to reach {item.Email}.\n");
        body.Append("Inbox: /admin/feedback\n");

        var message = NewMessage($"[AKML SQL] {item.Category.Label} (#{item.Id})", body.ToString());

        // Reply-To only from an address that MailAddress already accepted at submission; it
        // rejects line breaks, which is what header injection would need.
        if (item.Email is not null && MailAddress.TryCreate(item.Email, out var replyTo))
        {
            message.ReplyToList.Add(replyTo);
        }

        return message;
    }

    private MailMessage NewMessage(string subject, string body)
    {
        var options = _options.Value;
        // A relay without authentication has no user name; sending "from" the notify address is
        // what such a relay expects, and an empty sender would throw.
        var from = new[] { options.Smtp.From, options.Smtp.UserName, options.NotifyEmail }
            .First(v => !string.IsNullOrWhiteSpace(v));

        return new MailMessage(from, options.NotifyEmail, subject, body)
        {
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };
    }

    /// <summary>
    /// Builds and sends one message. Building is inside the try on purpose: a misconfigured
    /// address makes <see cref="MailMessage"/> throw, and an exception escaping
    /// <see cref="ExecuteAsync"/> would stop the whole site, not just the email.
    /// </summary>
    private async Task<bool> TrySendAsync(Func<MailMessage> build, CancellationToken cancellationToken)
    {
        try
        {
            using var message = build();
            await _mailer.SendAsync(message, cancellationToken).ConfigureAwait(false);
            _status.RecordSuccess(DateTimeOffset.UtcNow);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // The server's own words are the useful part ("5.7.8 Username and Password not
            // accepted"); the message content is never logged.
            var reason = e.InnerException?.Message ?? e.Message;
            _status.RecordFailure(DateTimeOffset.UtcNow, reason);
            _logger.LogWarning("Feedback email failed: {Reason}", reason);
            return false;
        }
    }
}
