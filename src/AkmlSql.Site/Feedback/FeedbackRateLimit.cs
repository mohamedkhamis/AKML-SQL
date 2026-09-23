namespace AkmlSql.Site.Feedback;

/// <summary>
/// Per-address limit on feedback submissions: a handful per ten minutes.
/// <para>
/// Much tighter than the consent limit, because each accepted submission can send an email to the
/// owner -- an open form with no limit is an open relay for filling their inbox. Five in ten
/// minutes is more than any real person reporting a problem needs.
/// </para>
/// <para>
/// In memory and per process, like the other soft limits here: bounded, pruned, and cleared by an
/// app-pool recycle.
/// </para>
/// </summary>
public sealed class FeedbackRateLimit
{
    public const int Allowance = 5;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private const int MaxTracked = 10_000;

    private readonly Dictionary<string, Queue<DateTimeOffset>> _recent = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public bool TryAcquire(string? ip) => TryAcquire(ip, DateTimeOffset.UtcNow);

    public bool TryAcquire(string? ip, DateTimeOffset now)
    {
        // No address (tests, unusual transports) is not limited -- failing open is acceptable for a
        // form whose worst case is an unwanted message in the owner's inbox.
        if (string.IsNullOrEmpty(ip))
        {
            return true;
        }

        lock (_gate)
        {
            if (!_recent.TryGetValue(ip, out var times))
            {
                if (_recent.Count >= MaxTracked)
                {
                    Prune(now);
                    if (_recent.Count >= MaxTracked)
                    {
                        // Past the cap, refuse new addresses rather than grow without bound.
                        return false;
                    }
                }

                _recent[ip] = times = new Queue<DateTimeOffset>();
            }

            while (times.Count > 0 && now - times.Peek() >= Window)
            {
                times.Dequeue();
            }

            if (times.Count >= Allowance)
            {
                return false;
            }

            times.Enqueue(now);
            return true;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var key in _recent.Where(kv => kv.Value.Count == 0 || now - kv.Value.Last() >= Window)
                                   .Select(kv => kv.Key).ToList())
        {
            _recent.Remove(key);
        }
    }
}
