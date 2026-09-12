#nullable enable
using System;
using Serilog.Core;
using Serilog.Events;

namespace AkmlSql.Core.Logging
{
    /// <summary>
    /// Serilog adapter over <see cref="TelemetryClient"/>. Level filtering is done by Serilog
    /// itself (<c>restrictedToMinimumLevel</c> at registration) so the sink only ever sees
    /// events the upload wants. Disposed by Serilog when the logger closes.
    /// </summary>
    internal sealed class TelemetrySink : ILogEventSink, IDisposable
    {
        private readonly TelemetryClient _client;

        public TelemetrySink(TelemetryClient client)
        {
            _client = client;
        }

        public void Emit(LogEvent logEvent)
        {
            // Field caps are enforced by TelemetryClient.Enqueue for every producer.
            _client.Enqueue(new TelemetryEvent
            {
                Utc = logEvent.Timestamp.ToUniversalTime(),
                Level = logEvent.Level.ToString(),
                Message = logEvent.RenderMessage(),
                Exception = logEvent.Exception?.ToString(),
                Source = ResolveSource(logEvent)
            });
        }

        private static string? ResolveSource(LogEvent logEvent)
        {
            if (logEvent.Properties.TryGetValue("SourceContext", out var value)
                && value is ScalarValue { Value: string source })
            {
                return source;
            }

            return null;
        }

        public void Dispose() => _client.Dispose();
    }
}
