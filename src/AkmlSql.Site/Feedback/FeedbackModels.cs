using System.Net.Mail;

namespace AkmlSql.Site.Feedback;

/// <summary>What a visitor can report. A fixed list: it becomes the email subject, so it is never free text.</summary>
public sealed record FeedbackCategory(string Key, string Label)
{
    public static readonly FeedbackCategory Problem = new("problem", "Something isn't working");
    public static readonly FeedbackCategory Download = new("download", "Download or installation problem");
    public static readonly FeedbackCategory Complaint = new("complaint", "Complaint");
    public static readonly FeedbackCategory Suggestion = new("suggestion", "Suggestion or idea");
    public static readonly FeedbackCategory Other = new("other", "Something else");

    public static readonly IReadOnlyList<FeedbackCategory> All = [Problem, Download, Complaint, Suggestion, Other];

    /// <summary>The category for <paramref name="key"/>, or null when it is not one of the offered ones.</summary>
    public static FeedbackCategory? Find(string? key) =>
        All.FirstOrDefault(c => string.Equals(c.Key, key?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>A submission as the form posted it, before validation.</summary>
public sealed record FeedbackSubmission(string? Category, string? Message, string? Email, string? Page);

/// <summary>One stored piece of feedback.</summary>
public sealed record FeedbackItem(
    long Id,
    DateTimeOffset ReceivedUtc,
    FeedbackCategory Category,
    string Message,
    string? Email,
    string? Page,
    string? Country,
    string? Browser,
    bool Handled,
    DateTimeOffset? HandledUtc)
{
    /// <summary>A short line for lists and email subjects: the start of the message.</summary>
    public string Preview => Message.Length <= 90 ? Message : Message[..87].TrimEnd() + "…";
}

/// <summary>
/// Validation for a public submission. Every field is untrusted input from an unauthenticated form.
/// </summary>
public static class FeedbackValidation
{
    /// <summary>Longest message accepted. Long enough for a stack trace, short enough to read.</summary>
    public const int MaxMessageLength = 4_000;

    /// <summary>Shortest message accepted: "It crashed" is useful; "a" is not.</summary>
    public const int MinMessageLength = 5;

    /// <summary>RFC 5321 limit on an address.</summary>
    public const int MaxEmailLength = 254;

    /// <summary>Longest page path kept.</summary>
    public const int MaxPageLength = 300;

    /// <summary>
    /// Validates <paramref name="submission"/>. Returns the cleaned values, or the reasons it was
    /// rejected -- phrased for the visitor, because they are shown back on the form.
    /// </summary>
    public static (FeedbackCategory Category, string Message, string? Email, string? Page)? Validate(
        FeedbackSubmission submission, out IReadOnlyList<string> errors)
    {
        var problems = new List<string>();

        var category = FeedbackCategory.Find(submission.Category);
        if (category is null)
        {
            problems.Add("Choose what the message is about.");
        }

        var message = (submission.Message ?? "").Trim();
        if (message.Length < MinMessageLength)
        {
            problems.Add("Describe the problem in a few words.");
        }
        else if (message.Length > MaxMessageLength)
        {
            problems.Add($"Keep the message under {MaxMessageLength:N0} characters.");
        }

        var email = NormalizeEmail(submission.Email, problems);

        errors = problems;
        return problems.Count > 0
            ? null
            : (category!, message, email, NormalizePage(submission.Page));
    }

    /// <summary>
    /// An optional reply address. Parsed with <see cref="MailAddress"/>, which also rejects line
    /// breaks -- the address is later used as a Reply-To header, and a newline in a header value is
    /// how header injection works.
    /// </summary>
    private static string? NormalizeEmail(string? raw, List<string> problems)
    {
        var email = raw?.Trim();
        if (string.IsNullOrEmpty(email))
        {
            return null;
        }

        if (email.Length > MaxEmailLength
            || email.IndexOfAny(['\r', '\n']) >= 0
            || !MailAddress.TryCreate(email, out var parsed)
            || !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("That email address doesn't look right. Leave it empty if you don't want a reply.");
            return null;
        }

        return parsed.Address;
    }

    /// <summary>
    /// The page the visitor was on, kept only as a local path: no scheme, no host, no query string.
    /// It is context for the owner, not a URL to follow, and a stored external URL would be an
    /// invitation to click something a stranger chose.
    /// </summary>
    internal static string? NormalizePage(string? raw)
    {
        var page = raw?.Trim();
        if (string.IsNullOrEmpty(page) || !page.StartsWith('/') || page.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        var cut = page.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            page = page[..cut];
        }

        if (page.IndexOfAny(['\\', '\r', '\n', '<', '>', '"']) >= 0)
        {
            return null;
        }

        return page.Length > MaxPageLength ? page[..MaxPageLength] : page;
    }
}

/// <summary>
/// Where new feedback is emailed. Bound from the <c>Feedback</c> configuration section; on the
/// deployed site these arrive as app-pool environment variables (<c>Feedback__NotifyEmail</c>,
/// <c>Feedback__Smtp__Host</c> …), the same way the admin password hash does.
/// Nothing here is required: without it, feedback still lands in the admin inbox.
/// </summary>
public sealed class FeedbackOptions
{
    public const string SectionName = "Feedback";

    /// <summary>Address that receives a notification for each new message. Empty = no email.</summary>
    public string NotifyEmail { get; set; } = "";

    public SmtpSettings Smtp { get; set; } = new();

    /// <summary>True when every setting email needs is present.</summary>
    public bool EmailConfigured =>
        !string.IsNullOrWhiteSpace(NotifyEmail) && !string.IsNullOrWhiteSpace(Smtp.Host);

    public sealed class SmtpSettings
    {
        /// <summary>SMTP server, e.g. <c>smtp.gmail.com</c>.</summary>
        public string Host { get; set; } = "";

        /// <summary>587 for STARTTLS, which is what most providers want.</summary>
        public int Port { get; set; } = 587;

        /// <summary>Use TLS. Only turn off for a local relay on a trusted network.</summary>
        public bool EnableSsl { get; set; } = true;

        public string UserName { get; set; } = "";

        /// <summary>For Gmail, an app password -- not the account password.</summary>
        public string Password { get; set; } = "";

        /// <summary>Sender address. Empty = <see cref="UserName"/>.</summary>
        public string From { get; set; } = "";
    }
}
