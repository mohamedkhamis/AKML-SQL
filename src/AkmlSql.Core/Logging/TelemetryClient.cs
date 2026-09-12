#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AkmlSql.Core.Logging
{
    /// <summary>
    /// Batches log events and POSTs them to <see cref="Constants.TelemetryUrl"/> from a single
    /// background worker. Fire-and-forget by contract: enqueue never blocks the logging thread
    /// (a full queue drops the event — losing an error report beats stalling the IDE), a failed
    /// send drops the batch and backs off (doubling the flush interval, capped at 15 minutes,
    /// so an offline machine never spins), and no exception ever escapes.
    /// </summary>
    internal sealed class TelemetryClient : IDisposable
    {
        internal const int MaxQueuedEvents = 256;
        internal const int MaxBatchEvents = 50;

        internal static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(15);
        internal static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);
        internal static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(10);
        internal static readonly TimeSpan ShutdownSendTimeout = TimeSpan.FromSeconds(3);

        private readonly Queue<TelemetryEvent> _queue = new();
        private readonly HttpClient _http;
        private readonly string _installId;
        private readonly string _host;
        private readonly TimeSpan _flushInterval;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Task _worker;
        private TimeSpan _backoff = TimeSpan.Zero;

        public TelemetryClient(string installId)
            : this(installId, new HttpClient(), null)
        {
        }

        /// <summary>Handler/interval-injectable constructor for tests.</summary>
        internal TelemetryClient(string installId, HttpMessageHandler handler, TimeSpan? flushInterval = null)
            : this(installId, new HttpClient(handler), flushInterval)
        {
        }

        private TelemetryClient(string installId, HttpClient http, TimeSpan? flushInterval)
        {
            _installId = installId;
            _http = http;
            _http.Timeout = SendTimeout;
            _host = ResolveHostName();
            _flushInterval = flushInterval ?? FlushInterval;
            _worker = Task.Run(RunAsync);
        }

        /// <summary>Queues one event; returns immediately and drops when the queue is full.</summary>
        public void Enqueue(TelemetryEvent telemetryEvent)
        {
            try
            {
                // Wire-size caps enforced at the single choke point so every producer (the
                // Serilog sink, tests, future callers) meets the server's limits.
                telemetryEvent.Message = TelemetryEvent.Truncate(
                    telemetryEvent.Message ?? string.Empty, TelemetryEvent.MaxMessageLength);
                if (telemetryEvent.Exception != null)
                {
                    telemetryEvent.Exception = TelemetryEvent.Truncate(
                        telemetryEvent.Exception, TelemetryEvent.MaxExceptionLength);
                }

                if (telemetryEvent.Source != null)
                {
                    telemetryEvent.Source = TelemetryEvent.Truncate(
                        telemetryEvent.Source, TelemetryEvent.MaxSourceLength);
                }

                lock (_queue)
                {
                    if (_queue.Count >= MaxQueuedEvents || _shutdown.IsCancellationRequested)
                    {
                        return;
                    }

                    _queue.Enqueue(telemetryEvent);
                    Monitor.Pulse(_queue);
                }
            }
            catch
            {
                // Telemetry must never throw into the logging pipeline.
            }
        }

        private async Task RunAsync()
        {
            var token = _shutdown.Token;
            while (!token.IsCancellationRequested)
            {
                var batch = CollectBatch(token);
                if (batch.Count == 0)
                {
                    continue; // woke to a cancelled/empty queue; loop re-checks the token
                }

                if (await SendAsync(batch, SendTimeout, CancellationToken.None))
                {
                    _backoff = TimeSpan.Zero;
                }
                else
                {
                    // Failed sends drop the batch and back off exponentially (flush interval →
                    // 15-minute cap), so an offline machine never hammers the endpoint.
                    _backoff = _backoff == TimeSpan.Zero
                        ? _flushInterval
                        : TimeSpan.FromTicks(Math.Min(_backoff.Ticks * 2, MaxBackoff.Ticks));

                    try
                    {
                        await Task.Delay(_backoff, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }

            // Final best-effort flush on shutdown, with a short timeout so a dead endpoint
            // cannot hold the host process open.
            var remaining = DrainRemaining();
            if (remaining.Count > 0)
            {
                await SendAsync(remaining, ShutdownSendTimeout, CancellationToken.None);
            }
        }

        /// <summary>
        /// Waits for the first event, then drains up to <see cref="MaxBatchEvents"/> more that
        /// have already arrived — sparse errors go out within one flush interval, bursts travel
        /// together.
        /// </summary>
        private List<TelemetryEvent> CollectBatch(CancellationToken token)
        {
            var batch = new List<TelemetryEvent>();

            lock (_queue)
            {
                while (_queue.Count == 0 && !token.IsCancellationRequested)
                {
                    try
                    {
                        if (!Monitor.Wait(_queue, _flushInterval))
                        {
                            return batch; // interval elapsed with nothing queued
                        }
                    }
                    catch (ThreadInterruptedException)
                    {
                        return batch;
                    }
                }

                while (_queue.Count > 0 && batch.Count < MaxBatchEvents)
                {
                    batch.Add(_queue.Dequeue());
                }
            }

            return batch;
        }

        private List<TelemetryEvent> DrainRemaining()
        {
            lock (_queue)
            {
                var remaining = new List<TelemetryEvent>(_queue);
                _queue.Clear();
                return remaining;
            }
        }

        private async Task<bool> SendAsync(List<TelemetryEvent> events, TimeSpan timeout, CancellationToken token)
        {
            try
            {
                var batch = new TelemetryBatch
                {
                    InstallId = _installId,
                    ProductVersion = Constants.RuntimeVersion,
                    Host = _host,
                    Os = TelemetryEvent.Truncate(Environment.OSVersion.VersionString, 128),
                    SentAtUtc = DateTimeOffset.UtcNow,
                    Events = events
                };

                var json = System.Text.Json.JsonSerializer.Serialize(
                    batch, TelemetryJsonContext.Default.TelemetryBatch);
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                // Shared ingestion key: the site 404s unauthenticated batches once the matching
                // server variable is set (Constants docs cover the embedded-key threat model).
                if (!string.IsNullOrEmpty(Constants.TelemetryIngestKey))
                {
                    content.Headers.TryAddWithoutValidation(
                        Constants.TelemetryIngestKeyHeader, Constants.TelemetryIngestKey);
                }

                using var timeoutCts = new CancellationTokenSource(timeout);
                using var response = await _http.PostAsync(Constants.TelemetryUrl, content, timeoutCts.Token)
                    .ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Process name ("Ssms", "devenv", "AkmlSql.Engine", …) — the least identifying host label.</summary>
        private static string ResolveHostName()
        {
            try
            {
                return TelemetryEvent.Truncate(Process.GetCurrentProcess().ProcessName, 64);
            }
            catch
            {
                return "unknown";
            }
        }

        public void Dispose()
        {
            try
            {
                _shutdown.Cancel();
                lock (_queue)
                {
                    Monitor.PulseAll(_queue);
                }

                // Give the worker one shutdown-send window to flush what is queued, then let it go.
                _worker.Wait(ShutdownSendTimeout + TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Best effort — never block or fault the caller's shutdown path.
            }
            finally
            {
                _shutdown.Dispose();
                _http.Dispose();
            }
        }
    }
}
