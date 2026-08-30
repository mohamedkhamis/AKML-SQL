using System.Collections.Concurrent;

namespace AkmlSql.Site.Admin;

/// <summary>
/// Per-IP brute-force throttle for <c>/admin/login</c>.
/// <para>
/// SEC-002: the previous version answered a throttled attempt by <c>await</c>ing up to 30 seconds
/// inside the request, so attackers could park connections simply by attempting to log in, and it
/// counted failures in an unbounded dictionary keyed on client IP (trivially grown from rotating
/// IPv6 source addresses). This version rejects immediately — the caller returns
/// <c>429 Too Many Requests</c> with a <c>Retry-After</c> header — and bounds its own state.
/// </para>
/// <para>
/// State is bounded three ways: entries idle for <see cref="WindowMinutes"/> are treated as reset
/// and pruned, pruning is triggered once the map passes <see cref="PruneThreshold"/>, and no new IP
/// is admitted past <see cref="MaxTrackedIps"/> (a flood then leaves already-locked-out IPs locked
/// out, which is the safe direction). In-memory is deliberate: an app-pool recycle clears the
/// counters, which is acceptable for this portal.
/// </para>
/// </summary>
public sealed class AdminLoginThrottle
{
    /// <summary>Failures tolerated before lockouts begin.</summary>
    public const int FailureThreshold = 5;

    /// <summary>Idle time after which an IP's failure count is forgotten.</summary>
    public const int WindowMinutes = 30;

    /// <summary>Upper bound on a single lockout.</summary>
    public static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(5);

    /// <summary>Map size that triggers an opportunistic prune of expired entries.</summary>
    public const int PruneThreshold = 4_096;

    /// <summary>Hard cap on tracked IPs; past this, new addresses are not admitted.</summary>
    public const int MaxTrackedIps = 16_384;

    private readonly ConcurrentDictionary<string, Attempt> _attempts = new(StringComparer.Ordinal);

    /// <summary>Failure count and the moment of the most recent failure for one IP.</summary>
    private sealed record Attempt(int Failures, DateTimeOffset LastFailureUtc);

    /// <summary>Records a failed attempt and returns the running failure total for that IP.</summary>
    public int RecordFailure(string ip) => RecordFailure(ip, DateTimeOffset.UtcNow);

    /// <summary>Records a failed attempt at <paramref name="now"/> (injectable for tests).</summary>
    public int RecordFailure(string ip, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ip);

        if (_attempts.Count >= PruneThreshold)
        {
            Prune(now);
        }

        // Past the hard cap an unknown IP is not admitted — but a tracked one still counts up.
        if (_attempts.Count >= MaxTrackedIps && !_attempts.ContainsKey(ip))
        {
            return 0;
        }

        var updated = _attempts.AddOrUpdate(
            ip,
            _ => new Attempt(1, now),
            (_, existing) => IsExpired(existing, now)
                ? new Attempt(1, now)
                : new Attempt(existing.Failures + 1, now));

        return updated.Failures;
    }

    /// <summary>Clears the counter for <paramref name="ip"/> (called after a successful sign-in).</summary>
    public void Reset(string ip) => _attempts.TryRemove(ip, out _);

    /// <summary>Current failure count for <paramref name="ip"/> (0 when unknown or expired).</summary>
    public int GetFailureCount(string ip) => GetFailureCount(ip, DateTimeOffset.UtcNow);

    /// <summary>Failure count for <paramref name="ip"/> as of <paramref name="now"/>.</summary>
    public int GetFailureCount(string ip, DateTimeOffset now) =>
        _attempts.TryGetValue(ip, out var attempt) && !IsExpired(attempt, now) ? attempt.Failures : 0;

    /// <summary>Number of IPs currently tracked (diagnostics and tests).</summary>
    public int TrackedCount => _attempts.Count;

    /// <summary>
    /// Time the caller must wait before this IP's next attempt is evaluated. <see cref="TimeSpan.Zero"/>
    /// means "allowed now"; anything greater is the <c>Retry-After</c> value for a 429.
    /// </summary>
    public TimeSpan GetRetryAfter(string ip) => GetRetryAfter(ip, DateTimeOffset.UtcNow);

    /// <summary>Retry-After for <paramref name="ip"/> as of <paramref name="now"/>.</summary>
    public TimeSpan GetRetryAfter(string ip, DateTimeOffset now)
    {
        if (!_attempts.TryGetValue(ip, out var attempt) || IsExpired(attempt, now))
        {
            return TimeSpan.Zero;
        }

        var lockout = ComputeLockout(attempt.Failures);
        if (lockout == TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var remaining = attempt.LastFailureUtc + lockout - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// Lockout applied after <paramref name="failures"/> failed attempts: none below the threshold,
    /// then 2^(n-threshold+1) seconds — 2s, 4s, 8s… — capped at <see cref="MaxLockout"/>.
    /// </summary>
    public static TimeSpan ComputeLockout(int failures)
    {
        if (failures < FailureThreshold)
        {
            return TimeSpan.Zero;
        }

        // Clamp the exponent before Math.Pow so a large failure count cannot overflow to infinity.
        var exponent = Math.Min(failures - FailureThreshold + 1, 20);
        var seconds = Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxLockout.TotalSeconds));
    }

    /// <summary>Drops every entry whose last failure is outside the window.</summary>
    public void Prune(DateTimeOffset now)
    {
        foreach (var pair in _attempts)
        {
            if (IsExpired(pair.Value, now))
            {
                // Remove only if unchanged since we read it — a concurrent failure must not be lost.
                ((ICollection<KeyValuePair<string, Attempt>>)_attempts).Remove(pair);
            }
        }
    }

    private static bool IsExpired(Attempt attempt, DateTimeOffset now) =>
        now - attempt.LastFailureUtc >= TimeSpan.FromMinutes(WindowMinutes);
}
