using System.Threading.Channels;
using AkmlSql.Site.Telemetry;

namespace AkmlSql.Site.Analytics;

/// <summary>
/// Bounded-channel <see cref="IAnalyticsSink"/> with a single background consumer. Enqueue is a
/// non-blocking <see cref="ChannelWriter{T}.TryWrite"/> (drops when full — losing a metric beats
/// stalling a request); the consumer writes to <see cref="AnalyticsStore"/> one event at a time
/// and swallows/logs per-event failures so a database hiccup never escapes into the app.
/// </summary>
public sealed class ChannelAnalyticsSink : BackgroundService, IAnalyticsSink
{
    private const int QueueCapacity = 1024;

    private readonly Channel<PendingItem> _queue = Channel.CreateBounded<PendingItem>(new BoundedChannelOptions(QueueCapacity)
    {
        // TryWrite must report overflow; DropWrite reports success even when it discards an item.
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly AnalyticsStore _store;
    private readonly ILogger<ChannelAnalyticsSink> _logger;
    private readonly CollectionHealth _health;
    private readonly object _enqueueGate = new();
    private sealed record PendingItem(object Value, DateTimeOffset QueuedAt);

    public ChannelAnalyticsSink(AnalyticsStore store, ILogger<ChannelAnalyticsSink> logger, CollectionHealth? health = null)
    {
        _store = store;
        _logger = logger;
        _health = health ?? new CollectionHealth();
    }

    public void EnqueueVisit(VisitInfo visit) => Enqueue(visit);

    public void EnqueueDownload(DownloadInfo download) => Enqueue(download);

    public void EnqueueNotFound(NotFoundInfo notFound) => Enqueue(notFound);

    public void EnqueueClientErrors(ClientErrorBatch batch) => Enqueue(batch);

    private void Enqueue(object value)
    {
        lock (_enqueueGate)
            _health.Enqueued(_queue.Writer.TryWrite(new PendingItem(value, DateTimeOffset.UtcNow)));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                // Ensure acceptance is counted before a fast consumer completes the item.
                lock (_enqueueGate) { }
                var persisted = false;
                try
                {
                    switch (item.Value)
                    {
                        case VisitInfo visit:
                            _store.LogVisit(visit);
                            break;
                        case DownloadInfo download:
                            _store.LogDownload(download);
                            break;
                        case NotFoundInfo notFound:
                            _store.LogNotFound(notFound);
                            break;
                        case ClientErrorBatch clientErrors:
                            _store.LogClientErrors(clientErrors);
                            break;
                    }
                    persisted = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Analytics write failed; event dropped.");
                }
                finally
                {
                    _health.Completed(persisted, item.QueuedAt);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown — the queue drains best-effort and the service exits cleanly.
        }
    }
}
