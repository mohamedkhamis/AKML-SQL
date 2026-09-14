using System.Collections.Concurrent;

namespace AkmlSql.Site.Consent;

/// <summary>
/// Spec 038 T104: a light per-IP rate limit for the two new public POST endpoints
/// (<c>/consent</c> and <c>/privacy/forget</c>).
/// <para>
/// The abuse surface is genuinely small — a visitor can only set their own cookies and delete their
/// own rows — so this is not a security control in the way <see cref="Admin.AdminLoginThrottle"/> is.
/// It exists because unauthenticated public POSTs that each do a database write should not be
/// unbounded, and because <c>/privacy/forget</c> issues a <c>DELETE</c>.
/// </para>
/// <para>
/// Bounded state, same reasoning as the admin throttle: idle entries are pruned, pruning is
/// triggered by size, and past a hard cap no new address is admitted. In-memory and per-process; an
/// app-pool recycle clears it, which is fine for a limit this soft.
/// </para>
/// </summary>
public sealed class ConsentRateLimit
{
    /// <summary>Requests allowed per address within <see cref="WindowSeconds"/>.</summary>
    public const int Allowance = 20;

    /// <summary>Rolling window, in seconds.</summary>
    public const int WindowSeconds = 60;

    /// <summary>Map size that triggers an opportunistic prune.</summary>
    public const int PruneThreshold = 4_096;

    /// <summary>Hard cap on tracked addresses.</summary>
    public const int MaxTrackedIps = 16_384;

    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);

    private sealed record Window(int Count, DateTimeOffset StartedUtc);

    /// <summary>True when this address may proceed.</summary>
    public bool TryAcquire(string? ip) => TryAcquire(ip, DateTimeOffset.UtcNow);

    /// <summary>Testable overload.</summary>
    public bool TryAcquire(string? ip, DateTimeOffset now)
    {
        // No address (a test, or an unusual transport) is never throttled: failing open here is
        // right, because the cost of wrongly blocking a consent choice is higher than the cost of
        // letting an unattributable request through.
        if (string.IsNullOrEmpty(ip))
        {
            return true;
        }

        if (_windows.Count > PruneThreshold)
        {
            Prune(now);
        }

        var allowed = true;

        _windows.AddOrUpdate(
            ip,
            _ =>
            {
                if (_windows.Count >= MaxTrackedIps)
                {
                    // At the cap, admit the request rather than tracking it. A flood then costs
                    // memory nothing and still cannot exceed what the endpoints themselves do.
                    return new Window(0, now);
                }

                return new Window(1, now);
            },
            (_, existing) =>
            {
                if (now - existing.StartedUtc >= TimeSpan.FromSeconds(WindowSeconds))
                {
                    return new Window(1, now);
                }

                if (existing.Count >= Allowance)
                {
                    allowed = false;
                    return existing;
                }

                return existing with { Count = existing.Count + 1 };
            });

        return allowed;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var pair in _windows)
        {
            if (now - pair.Value.StartedUtc >= TimeSpan.FromSeconds(WindowSeconds))
            {
                _windows.TryRemove(pair.Key, out _);
            }
        }
    }
}
