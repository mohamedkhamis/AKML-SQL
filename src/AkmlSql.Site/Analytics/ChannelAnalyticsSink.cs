using System.Threading.Channels;

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

    private readonly Channel<object> _queue = Channel.CreateBounded<object>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly AnalyticsStore _store;
    private readonly ILogger<ChannelAnalyticsSink> _logger;

    public ChannelAnalyticsSink(AnalyticsStore store, ILogger<ChannelAnalyticsSink> logger)
    {
        _store = store;
        _logger = logger;
    }

    public void EnqueueVisit(VisitInfo visit) => _queue.Writer.TryWrite(visit);

    public void EnqueueDownload(DownloadInfo download) => _queue.Writer.TryWrite(download);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var item in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    switch (item)
                    {
                        case VisitInfo visit:
                            _store.LogVisit(visit);
                            break;
                        case DownloadInfo download:
                            _store.LogDownload(download);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Analytics write failed; event dropped.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown — the queue drains best-effort and the service exits cleanly.
        }
    }
}
