#nullable enable
using System;
using System.Collections.Generic;

namespace AkmlSql.Core.Logging
{
    /// <summary>
    /// One log event in a telemetry batch. Field caps mirror the server's limits
    /// (site <c>ClientErrorOptions</c>: message 4000, exception 8000, source 256) so a batch
    /// is never rejected for size.
    /// </summary>
    internal sealed class TelemetryEvent
    {
        public DateTimeOffset Utc { get; set; }
        public string Level { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Exception { get; set; }
        public string? Source { get; set; }

        internal const int MaxMessageLength = 4000;
        internal const int MaxExceptionLength = 8000;
        internal const int MaxSourceLength = 256;

        internal static string Truncate(string value, int max) =>
            value.Length <= max ? value : value.Substring(0, max);
    }

    /// <summary>
    /// The payload POSTed to <see cref="Constants.TelemetryUrl"/>. Anonymous by design: the
    /// install id is the random GUID every config already carries — the batch contains no user
    /// name, machine name or IP-derived value.
    /// </summary>
    internal sealed class TelemetryBatch
    {
        public string InstallId { get; set; } = string.Empty;
        public string ProductVersion { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Os { get; set; } = string.Empty;
        public DateTimeOffset SentAtUtc { get; set; }
        public List<TelemetryEvent> Events { get; set; } = new();
    }
}
