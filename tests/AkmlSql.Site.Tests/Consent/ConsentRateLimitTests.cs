using AkmlSql.Site.Consent;
using Xunit;

namespace AkmlSql.Site.Tests.Consent;

/// <summary>Spec 038 T104: the soft per-IP limit on the new public POST endpoints.</summary>
public sealed class ConsentRateLimitTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequestsWithinTheAllowance_AreAdmitted()
    {
        var limit = new ConsentRateLimit();

        for (var i = 0; i < ConsentRateLimit.Allowance; i++)
        {
            Assert.True(limit.TryAcquire("203.0.113.7", Now), $"request {i + 1} should be allowed");
        }
    }

    [Fact]
    public void RequestsBeyondTheAllowance_AreRefused()
    {
        var limit = new ConsentRateLimit();

        for (var i = 0; i < ConsentRateLimit.Allowance; i++)
        {
            limit.TryAcquire("203.0.113.7", Now);
        }

        Assert.False(limit.TryAcquire("203.0.113.7", Now));
    }

    [Fact]
    public void TheWindowRolls()
    {
        var limit = new ConsentRateLimit();

        for (var i = 0; i < ConsentRateLimit.Allowance + 5; i++)
        {
            limit.TryAcquire("203.0.113.7", Now);
        }

        Assert.False(limit.TryAcquire("203.0.113.7", Now));
        Assert.True(limit.TryAcquire("203.0.113.7", Now.AddSeconds(ConsentRateLimit.WindowSeconds + 1)));
    }

    [Fact]
    public void OneAddressCannotBlockAnother()
    {
        var limit = new ConsentRateLimit();

        for (var i = 0; i < ConsentRateLimit.Allowance + 5; i++)
        {
            limit.TryAcquire("203.0.113.7", Now);
        }

        Assert.True(limit.TryAcquire("198.51.100.4", Now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnUnattributableRequest_IsNeverThrottled(string? ip)
    {
        var limit = new ConsentRateLimit();

        // Failing OPEN is deliberate: wrongly blocking someone's consent choice — or their
        // withdrawal — is worse than letting an unattributable request through.
        for (var i = 0; i < ConsentRateLimit.Allowance * 3; i++)
        {
            Assert.True(limit.TryAcquire(ip, Now));
        }
    }

    [Fact]
    public void TheLimitIsSofterThanTheAdminLoginThrottle()
    {
        // These endpoints are not a credential surface, so the limit must not be tight enough to
        // interfere with ordinary use (a visitor toggling their choice a few times).
        Assert.True(ConsentRateLimit.Allowance > AkmlSql.Site.Admin.AdminLoginThrottle.FailureThreshold);
    }
}
