using AkmlSql.Site.Feedback;
using Xunit;

namespace AkmlSql.Site.Tests.Feedback;

/// <summary>
/// Validation of a public, unauthenticated form. Every field is attacker-controlled; the email
/// address in particular later becomes a Reply-To header, so a line break in it is an injection
/// attempt, not a typo.
/// </summary>
public sealed class FeedbackValidationTests
{
    private static (FeedbackCategory Category, string Message, string? Email, string? Page)? Validate(
        string? category = "problem", string? message = "The installer stops at 50%.",
        string? email = null, string? page = null) =>
        FeedbackValidation.Validate(new FeedbackSubmission(category, message, email, page), out _);

    private static IReadOnlyList<string> Errors(
        string? category = "problem", string? message = "The installer stops at 50%.",
        string? email = null, string? page = null)
    {
        FeedbackValidation.Validate(new FeedbackSubmission(category, message, email, page), out var errors);
        return errors;
    }

    [Fact]
    public void AMessageAndACategory_AreEnough()
    {
        var result = Validate();

        Assert.NotNull(result);
        Assert.Equal(FeedbackCategory.Problem, result.Value.Category);
        Assert.Equal("The installer stops at 50%.", result.Value.Message);
        Assert.Null(result.Value.Email);
    }

    [Fact]
    public void TheMessageIsTrimmed()
    {
        Assert.Equal("It crashed", Validate(message: "   It crashed \n ")!.Value.Message);
    }

    [Theory]
    [InlineData("DOWNLOAD")]
    [InlineData(" download ")]
    public void TheCategoryIsMatchedLoosely(string key)
    {
        Assert.Equal(FeedbackCategory.Download, Validate(category: key)!.Value.Category);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("spam")]
    [InlineData("<script>")]
    public void AnUnknownCategory_IsRejected(string? key)
    {
        // The category label becomes the email subject, so only the offered ones are accepted.
        Assert.Contains("Choose what the message is about.", Errors(category: key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("hi")]
    public void AnEmptyOrTinyMessage_IsRejected(string? message)
    {
        Assert.Contains("Describe the problem in a few words.", Errors(message: message));
    }

    [Fact]
    public void AnOverlongMessage_IsRejected()
    {
        var errors = Errors(message: new string('x', FeedbackValidation.MaxMessageLength + 1));

        Assert.Single(errors);
        Assert.Contains("4,000", errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AMessageAtTheLimit_IsAccepted()
    {
        Assert.NotNull(Validate(message: new string('x', FeedbackValidation.MaxMessageLength)));
    }

    [Fact]
    public void EveryProblemIsReportedAtOnce()
    {
        // One round trip to fix everything, not one per mistake.
        Assert.Equal(3, Errors(category: null, message: "", email: "nope").Count);
    }

    [Theory]
    [InlineData("someone@example.com", "someone@example.com")]
    [InlineData("  someone@example.com  ", "someone@example.com")]
    public void AValidEmail_IsKept(string raw, string expected)
    {
        Assert.Equal(expected, Validate(email: raw)!.Value.Email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoEmail_IsFine(string? email)
    {
        var result = Validate(email: email);

        Assert.NotNull(result);
        Assert.Null(result.Value.Email);
    }

    [Theory]
    [InlineData("not an email")]
    [InlineData("someone@")]
    [InlineData("someone@example.com\r\nBcc: victim@example.com")]
    [InlineData("someone@example.com\nBcc: victim@example.com")]
    [InlineData("Someone <someone@example.com>")]
    [InlineData("a@example.com, b@example.com")]
    public void AnythingButOnePlainAddress_IsRejected(string email)
    {
        // Display names and lists parse as MailAddress too; only a bare single address is a reply
        // address. Line breaks are the header-injection case.
        Assert.Contains(Errors(email: email), e => e.Contains("email address", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOverlongEmail_IsRejected()
    {
        var email = new string('a', 250) + "@example.com";

        Assert.NotEmpty(Errors(email: email));
    }

    [Theory]
    [InlineData("/download", "/download")]
    [InlineData("/download?ref=mail#top", "/download")]
    [InlineData("/docs/formatting", "/docs/formatting")]
    [InlineData(" /features ", "/features")]
    public void ALocalPage_IsKeptAsAPath(string raw, string expected)
    {
        Assert.Equal(expected, FeedbackValidation.NormalizePage(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("download")]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    [InlineData("/\\evil.example")]
    [InlineData("/a\"onmouseover=\"x")]
    [InlineData("/<script>")]
    public void AnythingButALocalPath_IsDropped(string? raw)
    {
        // Context for the owner, never a link a stranger chose.
        Assert.Null(FeedbackValidation.NormalizePage(raw));
    }

    [Fact]
    public void AnOverlongPage_IsCut()
    {
        var page = FeedbackValidation.NormalizePage("/" + new string('a', 1_000));

        Assert.Equal(FeedbackValidation.MaxPageLength, page!.Length);
    }

    [Fact]
    public void ABadPage_NeverRejectsTheMessage()
    {
        // The page is optional context; a visitor must not be told their message failed because of it.
        var result = Validate(page: "https://evil.example/");

        Assert.NotNull(result);
        Assert.Null(result.Value.Page);
    }

    [Fact]
    public void Preview_ShortensLongMessages()
    {
        var item = new FeedbackItem(1, DateTimeOffset.UtcNow, FeedbackCategory.Other, new string('a', 200),
            null, null, null, null, false, null);

        Assert.Equal(88, item.Preview.Length); // 87 characters + the ellipsis
        Assert.EndsWith("…", item.Preview, StringComparison.Ordinal);
    }
}
