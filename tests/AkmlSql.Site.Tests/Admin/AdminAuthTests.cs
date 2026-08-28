using System.Security.Claims;
using AkmlSql.Site.Admin;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AkmlSql.Site.Tests.Admin;

/// <summary>
/// Admin auth: fixed-time SHA-256 hash verification, the not-configured state for an empty
/// hash, exponential login throttling, and the /admin branch guard.
/// </summary>
public sealed class AdminAuthTests
{
    // SHA-256("password") — well-known reference digest.
    private const string PasswordHash = "5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8";

    [Fact]
    public void ComputeSha256Hex_MatchesReferenceDigest() =>
        Assert.Equal(PasswordHash, AdminAuth.ComputeSha256Hex("password"));

    [Fact]
    public void Verify_AcceptsCorrectPassword() =>
        Assert.True(AdminAuth.Verify("password", PasswordHash));

    [Theory]
    [InlineData("passwors")]   // wrong password
    [InlineData("Password")]   // case-sensitive
    [InlineData("")]
    [InlineData(null)]
    public void Verify_RejectsWrongPassword(string? password) =>
        Assert.False(AdminAuth.Verify(password, PasswordHash));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")] // 64 chars, non-hex
    [InlineData("5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d")] // 63 chars
    public void Verify_RejectsWhenHashMissingOrMalformed(string? configuredHash) =>
        Assert.False(AdminAuth.Verify("password", configuredHash));

    [Fact]
    public void IsConfigured_TrueOnlyForWellFormedHash()
    {
        Assert.False(new AdminOptions().IsConfigured);
        Assert.False(new AdminOptions { PasswordHash = "abc" }.IsConfigured);
        Assert.True(new AdminOptions { PasswordHash = PasswordHash }.IsConfigured);
    }

    [Fact]
    public void Throttle_NoDelayBelowThreshold_ThenExponentialWithCap()
    {
        Assert.Equal(TimeSpan.Zero, AdminLoginThrottle.ComputeDelay(0));
        Assert.Equal(TimeSpan.Zero, AdminLoginThrottle.ComputeDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(2), AdminLoginThrottle.ComputeDelay(5));
        Assert.Equal(TimeSpan.FromSeconds(4), AdminLoginThrottle.ComputeDelay(6));
        Assert.Equal(TimeSpan.FromSeconds(16), AdminLoginThrottle.ComputeDelay(8));
        Assert.Equal(TimeSpan.FromSeconds(30), AdminLoginThrottle.ComputeDelay(9));
        Assert.Equal(TimeSpan.FromSeconds(30), AdminLoginThrottle.ComputeDelay(25)); // capped
    }

    [Fact]
    public void Throttle_TripsAfterFiveFailures_AndResets()
    {
        var throttle = new AdminLoginThrottle();
        const string ip = "203.0.113.5";

        for (var i = 0; i < 4; i++)
        {
            throttle.RecordFailure(ip);
        }

        Assert.Equal(4, throttle.GetFailureCount(ip));
        Assert.Equal(TimeSpan.Zero, throttle.GetDelay(ip));

        throttle.RecordFailure(ip);
        Assert.Equal(5, throttle.GetFailureCount(ip));
        Assert.Equal(TimeSpan.FromSeconds(2), throttle.GetDelay(ip));

        throttle.Reset(ip);
        Assert.Equal(0, throttle.GetFailureCount(ip));
        Assert.Equal(TimeSpan.Zero, throttle.GetDelay(ip));
    }

    [Fact]
    public void AdminGuard_ChallengesUnauthenticatedRequestsToProtectedPaths()
    {
        Assert.True(AdminBranchMiddleware.RequiresChallenge(NewContext("/admin", authenticated: false)));
        Assert.True(AdminBranchMiddleware.RequiresChallenge(NewContext("/admin/logout", authenticated: false)));
    }

    [Fact]
    public void AdminGuard_AllowsAuthenticatedRequestsAndTheLoginPage()
    {
        Assert.False(AdminBranchMiddleware.RequiresChallenge(NewContext("/admin", authenticated: true)));
        Assert.False(AdminBranchMiddleware.RequiresChallenge(NewContext("/admin/login", authenticated: false)));
    }

    [Fact]
    public void AdminGuard_IgnoresPublicPathsAndLookalikeSegments()
    {
        Assert.False(AdminBranchMiddleware.RequiresChallenge(NewContext("/", authenticated: false)));
        Assert.False(AdminBranchMiddleware.RequiresChallenge(NewContext("/download", authenticated: false)));
        Assert.False(AdminBranchMiddleware.RequiresChallenge(NewContext("/administrator", authenticated: false)));
    }

    private static DefaultHttpContext NewContext(string path, bool authenticated)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (authenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "admin")],
                AdminAuth.Scheme));
        }

        return context;
    }
}
