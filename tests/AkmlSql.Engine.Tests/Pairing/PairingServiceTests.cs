using System;
using System.Threading;
using AkmlSql.Engine.Pairing;
using Xunit;

namespace AkmlSql.Engine.Tests.Pairing;

/// <summary>
/// Spec 021 (web edition) -- M3 task T066. Covers <see cref="PairingService"/>'s
/// single-use, expiry, constant-time, rate-limit, and regenerate semantics.
/// </summary>
public sealed class PairingServiceTests
{
    private static (PairingService svc, Func<DateTimeOffset> getNow, Action<TimeSpan> advance) CreateWithClock()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = () => now;
        void Advance(TimeSpan delta) => now = now.Add(delta);
        var svc = new PairingService(clock, pinTtl: TimeSpan.FromMinutes(30));
        return (svc, clock, Advance);
    }

    [Fact]
    public void Constructor_mints_an_initial_pin()
    {
        var (svc, _, _) = CreateWithClock();

        Assert.Equal(PairingService.PinLength, svc.CurrentPin.Length);
        Assert.All(svc.CurrentPin, ch => Assert.True(char.IsDigit(ch)));
    }

    [Fact]
    public void RegeneratePin_returns_new_pin_and_fires_event()
    {
        var (svc, _, _) = CreateWithClock();
        var original = svc.CurrentPin;
        string? observed = null;
        svc.PinChanged += (_, pin) => observed = pin;

        var fresh = svc.RegeneratePin();

        Assert.NotEqual(original, fresh);
        Assert.Equal(fresh, observed);
        Assert.Equal(fresh, svc.CurrentPin);
    }

    [Fact]
    public void ValidatePin_accepts_the_current_pin_exactly_once()
    {
        var (svc, _, _) = CreateWithClock();
        var pin = svc.CurrentPin;

        Assert.Equal(PinAttemptResult.Valid, svc.ValidatePin("10.0.0.1", pin));
        // PIN is single-use: a second attempt with the same PIN fails.
        Assert.Equal(PinAttemptResult.Invalid, svc.ValidatePin("10.0.0.1", pin));
        // CurrentPin reports empty after consumption (until regenerate).
        Assert.Equal(string.Empty, svc.CurrentPin);
    }

    [Fact]
    public void ValidatePin_returns_invalid_for_wrong_pin()
    {
        var (svc, _, _) = CreateWithClock();
        Assert.Equal(PinAttemptResult.Invalid, svc.ValidatePin("10.0.0.1", "000000"));
        // Wrong attempt does NOT consume the PIN.
        Assert.NotEqual(string.Empty, svc.CurrentPin);
    }

    [Fact]
    public void ValidatePin_returns_expired_after_ttl_elapses()
    {
        var (svc, _, advance) = CreateWithClock();
        var pin = svc.CurrentPin;

        advance(TimeSpan.FromHours(1));    // > 30 min TTL

        Assert.Equal(PinAttemptResult.Expired, svc.ValidatePin("10.0.0.1", pin));
    }

    [Fact]
    public void Rate_limit_kicks_in_at_sixth_attempt_from_same_source()
    {
        var (svc, _, _) = CreateWithClock();
        // 5 wrong attempts -- all rejected as Invalid but counted by the rate limiter.
        for (var i = 0; i < PairingService.RateLimitMaxAttempts; i++)
        {
            Assert.Equal(PinAttemptResult.Invalid, svc.ValidatePin("10.0.0.1", "000000"));
        }
        // 6th attempt from the SAME source: rate-limited even if the PIN is correct.
        Assert.Equal(PinAttemptResult.RateLimited, svc.ValidatePin("10.0.0.1", svc.CurrentPin));
        // Different source still works.
        Assert.Equal(PinAttemptResult.Valid, svc.ValidatePin("10.0.0.2", svc.CurrentPin));
    }

    [Fact]
    public void Rate_limit_window_slides_so_old_attempts_drop_off()
    {
        var (svc, _, advance) = CreateWithClock();

        // Fill the window.
        for (var i = 0; i < PairingService.RateLimitMaxAttempts; i++)
        {
            Assert.Equal(PinAttemptResult.Invalid, svc.ValidatePin("10.0.0.5", "000000"));
        }
        Assert.Equal(PinAttemptResult.RateLimited, svc.ValidatePin("10.0.0.5", "111111"));

        // Slide past the window -- the old attempts are pruned.
        advance(PairingService.RateLimitWindow + TimeSpan.FromSeconds(1));
        Assert.Equal(PinAttemptResult.Invalid, svc.ValidatePin("10.0.0.5", "000000"));
    }

    [Fact]
    public void Global_circuit_breaker_freezes_after_many_failures_across_distinct_sources()
    {
        var (svc, _, _) = CreateWithClock();

        // Spread failures across distinct sources so the PER-source 5/min limit never trips, but the
        // GLOBAL cap does -- this is the multi-IP brute-force the per-source limit alone misses.
        var frozen = false;
        for (var i = 0; i < PairingService.GlobalRateLimitMaxFailures + 5; i++)
        {
            var r = svc.ValidatePin($"10.0.{i / 256}.{i % 256}", "000000");
            if (r == PinAttemptResult.RateLimited) { frozen = true; break; }
        }
        Assert.True(frozen, "Global circuit-breaker should freeze pairing after the global failure cap.");

        // While frozen, even the correct PIN from a brand-new source is refused.
        Assert.Equal(PinAttemptResult.RateLimited, svc.ValidatePin("10.99.99.99", svc.CurrentPin));

        // Operator regenerates -> the freeze clears and pairing works again.
        var fresh = svc.RegeneratePin();
        Assert.Equal(PinAttemptResult.Valid, svc.ValidatePin("10.88.88.88", fresh));
    }

    [Fact]
    public void RegeneratePin_resets_the_consumed_flag()
    {
        var (svc, _, _) = CreateWithClock();
        var first = svc.CurrentPin;
        Assert.Equal(PinAttemptResult.Valid, svc.ValidatePin("a", first));
        Assert.Equal(string.Empty, svc.CurrentPin);

        var second = svc.RegeneratePin();
        Assert.NotEqual(first, second);
        Assert.Equal(second, svc.CurrentPin);
        Assert.Equal(PinAttemptResult.Valid, svc.ValidatePin("b", second));
    }
}
