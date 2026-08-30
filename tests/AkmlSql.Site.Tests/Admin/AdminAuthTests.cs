using System.Security.Claims;
using AkmlSql.Site.Admin;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AkmlSql.Site.Tests.Admin;

/// <summary>
/// Admin auth: PBKDF2 hash generation and fixed-time verification (SEC-001), the not-configured
/// state for an empty/malformed hash, non-blocking lockout throttling (SEC-002), and the /admin
/// branch guard.
/// </summary>
public sealed class AdminAuthTests
{
    private const string Password = "correct horse battery staple";

    // Generated once here rather than inlined: the salt is random, so a fixture string would
    // only ever test one salt and would silently rot if the format version changed.
    private static readonly string ConfiguredHash = AdminAuth.HashPassword(Password);

    [Fact]
    public void HashPassword_ProducesVersionedSaltedFormat()
    {
        var parts = ConfiguredHash.Split('$');

        Assert.Equal(4, parts.Length);
        Assert.Equal(AdminAuth.HashPrefix, parts[0]);
        Assert.Equal(AdminAuth.DefaultIterations.ToString(), parts[1]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length); // salt
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length); // hash
    }

    [Fact]
    public void HashPassword_UsesAFreshSaltEachTime() =>
        Assert.NotEqual(AdminAuth.HashPassword(Password), AdminAuth.HashPassword(Password));

    [Fact]
    public void Verify_AcceptsCorrectPassword() =>
        Assert.True(AdminAuth.Verify(Password, ConfiguredHash));

    [Fact]
    public void Verify_AcceptsPasswordAgainstAnIndependentlyGeneratedHash() =>
        Assert.True(AdminAuth.Verify(Password, AdminAuth.HashPassword(Password)));

    [Theory]
    [InlineData("correct horse battery stapl")]  // wrong password
    [InlineData("Correct horse battery staple")] // case-sensitive
    [InlineData("")]
    [InlineData(null)]
    public void Verify_RejectsWrongPassword(string? password) =>
        Assert.False(AdminAuth.Verify(password, ConfiguredHash));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-hash")]
    // The old unsalted SHA-256 format is no longer accepted (SEC-001): SHA-256("password").
    [InlineData("5e884898da28047151d0e56f8dc6292773603d0d6aabbdd62a11ef721d1542d8")]
    [InlineData("AKML1$210000$notbase64$notbase64")]
    [InlineData("AKML9$210000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")] // wrong version
    [InlineData("AKML1$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]         // too few parts
    public void Verify_RejectsWhenHashMissingOrMalformed(string? configuredHash) =>
        Assert.False(AdminAuth.Verify(Password, configuredHash));

    [Fact]
    public void Verify_RejectsHashWithDowngradedIterationCount()
    {
        // A stored hash claiming fewer rounds than the floor must not be honoured, even if the
        // digest itself would match — otherwise the work factor is attacker-controlled.
        var weak = AdminAuth.HashPassword(Password, AdminAuth.MinimumIterations);
        var downgraded = weak.Replace(
            "$" + AdminAuth.MinimumIterations + "$",
            "$1000$",
            StringComparison.Ordinal);

        Assert.False(AdminAuth.Verify(Password, downgraded));
    }

    [Fact]
    public void HashPassword_RefusesAWorkFactorBelowTheFloor() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AdminAuth.HashPassword(Password, 1000));

    [Fact]
    public void IsConfigured_TrueOnlyForWellFormedHash()
    {
        Assert.False(new AdminOptions().IsConfigured);
        Assert.False(new AdminOptions { PasswordHash = "abc" }.IsConfigured);
        Assert.True(new AdminOptions { PasswordHash = ConfiguredHash }.IsConfigured);
    }

    // --- Throttle (SEC-002) --------------------------------------------------

    [Fact]
    public void Throttle_NoLockoutBelowThreshold_ThenExponentialWithCap()
    {
        Assert.Equal(TimeSpan.Zero, AdminLoginThrottle.ComputeLockout(0));
        Assert.Equal(TimeSpan.Zero, AdminLoginThrottle.ComputeLockout(4));
        Assert.Equal(TimeSpan.FromSeconds(2), AdminLoginThrottle.ComputeLockout(5));
        Assert.Equal(TimeSpan.FromSeconds(4), AdminLoginThrottle.ComputeLockout(6));
        Assert.Equal(TimeSpan.FromSeconds(16), AdminLoginThrottle.ComputeLockout(8));
        Assert.Equal(AdminLoginThrottle.MaxLockout, AdminLoginThrottle.ComputeLockout(20));
        Assert.Equal(AdminLoginThrottle.MaxLockout, AdminLoginThrottle.ComputeLockout(10_000)); // no overflow
    }

    [Fact]
    public void Throttle_LocksOutAfterFiveFailures_AndResets()
    {
        var throttle = new AdminLoginThrottle();
        var now = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        const string ip = "203.0.113.5";

        for (var i = 0; i < 4; i++)
        {
            throttle.RecordFailure(ip, now);
        }

        Assert.Equal(4, throttle.GetFailureCount(ip, now));
        Assert.Equal(TimeSpan.Zero, throttle.GetRetryAfter(ip, now));

        Assert.Equal(5, throttle.RecordFailure(ip, now));
        Assert.Equal(TimeSpan.FromSeconds(2), throttle.GetRetryAfter(ip, now));

        throttle.Reset(ip);
        Assert.Equal(0, throttle.GetFailureCount(ip, now));
        Assert.Equal(TimeSpan.Zero, throttle.GetRetryAfter(ip, now));
    }

    [Fact]
    public void Throttle_RetryAfterCountsDownAndClearsWhenTheLockoutElapses()
    {
        var throttle = new AdminLoginThrottle();
        var start = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        const string ip = "203.0.113.6";

        for (var i = 0; i < 5; i++)
        {
            throttle.RecordFailure(ip, start);
        }

        Assert.Equal(TimeSpan.FromSeconds(2), throttle.GetRetryAfter(ip, start));
        Assert.Equal(TimeSpan.FromSeconds(1), throttle.GetRetryAfter(ip, start.AddSeconds(1)));
        Assert.Equal(TimeSpan.Zero, throttle.GetRetryAfter(ip, start.AddSeconds(2)));
    }

    [Fact]
    public void Throttle_ForgetsFailuresAfterTheIdleWindow()
    {
        var throttle = new AdminLoginThrottle();
        var start = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
        var later = start.AddMinutes(AdminLoginThrottle.WindowMinutes);
        const string ip = "203.0.113.7";

        for (var i = 0; i < 5; i++)
        {
            throttle.RecordFailure(ip, start);
        }

        Assert.Equal(0, throttle.GetFailureCount(ip, later));
        Assert.Equal(TimeSpan.Zero, throttle.GetRetryAfter(ip, later));

        // The next failure starts a fresh count rather than resuming the old one.
        Assert.Equal(1, throttle.RecordFailure(ip, later));
    }

    [Fact]
    public void Throttle_PrunesExpiredEntriesSoStateStaysBounded()
    {
        var throttle = new AdminLoginThrottle();
        var start = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

        for (var i = 0; i < 500; i++)
        {
            throttle.RecordFailure($"198.51.100.{i}", start);
        }

        Assert.Equal(500, throttle.TrackedCount);

        throttle.Prune(start.AddMinutes(AdminLoginThrottle.WindowMinutes));
        Assert.Equal(0, throttle.TrackedCount);
    }

    [Fact]
    public void Throttle_StopsAdmittingNewAddressesPastTheHardCap()
    {
        var throttle = new AdminLoginThrottle();
        var now = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

        // Fill past the cap with distinct addresses, as an IPv6 flood would.
        for (var i = 0; i <= AdminLoginThrottle.MaxTrackedIps + 100; i++)
        {
            throttle.RecordFailure($"2001:db8::{i:x}", now);
        }

        Assert.True(
            throttle.TrackedCount <= AdminLoginThrottle.MaxTrackedIps,
            $"tracked {throttle.TrackedCount} addresses, cap is {AdminLoginThrottle.MaxTrackedIps}");
    }

    // --- /admin branch guard -------------------------------------------------

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
