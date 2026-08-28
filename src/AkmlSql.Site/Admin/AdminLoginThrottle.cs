using System.Collections.Concurrent;

namespace AkmlSql.Site.Admin;

/// <summary>
/// In-memory brute-force throttle for /admin/login: per-IP failure counting; once an IP reaches
/// <see cref="FailureThreshold"/>, each subsequent attempt is delayed exponentially (2s, 4s, 8s,
/// 16s…) capped at <see cref="MaxDelay"/>. Resets on a successful sign-in. In-memory is
/// deliberate — an app-pool recycle clears the counters, which is acceptable for this portal.
/// </summary>
public sealed class AdminLoginThrottle
{
    /// <summary>Failures tolerated before delays kick in.</summary>
    public const int FailureThreshold = 5;

    /// <summary>Upper bound for the exponential back-off.</summary>
    public static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, int> _failures = new(StringComparer.Ordinal);

    /// <summary>Records a failed attempt for <paramref name="ip"/> and returns the running total.</summary>
    public int RecordFailure(string ip) => _failures.AddOrUpdate(ip, 1, static (_, count) => count + 1);

    /// <summary>Clears the counter for <paramref name="ip"/> (called after a successful sign-in).</summary>
    public void Reset(string ip) => _failures.TryRemove(ip, out _);

    /// <summary>Current failure count for <paramref name="ip"/> (0 when unknown).</summary>
    public int GetFailureCount(string ip) => _failures.GetValueOrDefault(ip);

    /// <summary>Delay to apply before processing the next attempt from <paramref name="ip"/>.</summary>
    public TimeSpan GetDelay(string ip) => ComputeDelay(GetFailureCount(ip));

    /// <summary>Exponential back-off: below threshold → none; at/above → 2^(n-threshold+1) seconds, capped.</summary>
    public static TimeSpan ComputeDelay(int failures)
    {
        if (failures < FailureThreshold)
        {
            return TimeSpan.Zero;
        }

        var seconds = Math.Pow(2, failures - FailureThreshold + 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxDelay.TotalSeconds));
    }
}
