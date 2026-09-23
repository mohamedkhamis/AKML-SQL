using AkmlSql.Site.Feedback;
using Xunit;

namespace AkmlSql.Site.Tests.Feedback;

/// <summary>Each accepted message can send the owner an email, so the form is limited per address.</summary>
public sealed class FeedbackRateLimitTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheAllowanceIsGranted_ThenRefused()
    {
        var limit = new FeedbackRateLimit();

        for (var i = 0; i < FeedbackRateLimit.Allowance; i++)
        {
            Assert.True(limit.TryAcquire("203.0.113.1", Now.AddSeconds(i)));
        }

        Assert.False(limit.TryAcquire("203.0.113.1", Now.AddMinutes(1)));
    }

    [Fact]
    public void TheAllowanceComesBackAfterTheWindow()
    {
        var limit = new FeedbackRateLimit();
        for (var i = 0; i < FeedbackRateLimit.Allowance; i++)
        {
            limit.TryAcquire("203.0.113.1", Now);
        }

        Assert.False(limit.TryAcquire("203.0.113.1", Now + FeedbackRateLimit.Window - TimeSpan.FromSeconds(1)));
        Assert.True(limit.TryAcquire("203.0.113.1", Now + FeedbackRateLimit.Window));
    }

    [Fact]
    public void AddressesAreLimitedIndependently()
    {
        var limit = new FeedbackRateLimit();
        for (var i = 0; i < FeedbackRateLimit.Allowance; i++)
        {
            limit.TryAcquire("203.0.113.1", Now);
        }

        Assert.True(limit.TryAcquire("203.0.113.2", Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoAddress_IsNotLimited(string? ip)
    {
        var limit = new FeedbackRateLimit();

        for (var i = 0; i < FeedbackRateLimit.Allowance * 3; i++)
        {
            Assert.True(limit.TryAcquire(ip, Now));
        }
    }
}
