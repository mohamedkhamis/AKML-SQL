namespace AkmlSql.Site.Analytics;

/// <summary>Process-lifetime delivery counters; contains no visitor or event content.</summary>
public sealed class CollectionHealth
{
    private readonly object _gate = new();
    private CollectionSnapshot _current = new(DateTimeOffset.UtcNow, 0, 0, 0, 0, 0, null, null);

    public CollectionSnapshot Snapshot { get { lock (_gate) return _current; } }

    internal void Enqueued(bool accepted)
    {
        lock (_gate) _current = accepted
            ? _current with { Accepted = _current.Accepted + 1, Pending = _current.Pending + 1 }
            : _current with { Dropped = _current.Dropped + 1 };
    }

    internal void Completed(bool persisted, DateTimeOffset queuedAt)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_gate) _current = _current with
        {
            Persisted = _current.Persisted + (persisted ? 1 : 0),
            Failed = _current.Failed + (persisted ? 0 : 1),
            Pending = _current.Pending - 1,
            LastPersistedUtc = persisted ? now : _current.LastPersistedUtc,
            LastProcessingDelay = now - queuedAt,
        };
    }
}

public sealed record CollectionSnapshot(DateTimeOffset StartedUtc, long Accepted, long Persisted,
    long Dropped, long Failed, long Pending, DateTimeOffset? LastPersistedUtc, TimeSpan? LastProcessingDelay);

public sealed record MaintenanceSnapshot(DateTimeOffset? LastAttemptUtc = null,
    DateTimeOffset? LastSuccessUtc = null, bool Running = false, bool Failed = false,
    long Pruned = 0, long DeIdentified = 0, long ReferrersCorrected = 0);
