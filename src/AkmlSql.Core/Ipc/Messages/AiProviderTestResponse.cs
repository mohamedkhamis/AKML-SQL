using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response from an AI provider connectivity test.
    /// Sent Engine -> Shell.
    /// </summary>
    [MessagePackObject]
    public class AiProviderTestResponse
    {
        /// <summary>Whether the provider test succeeded (authentication and model access).</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>The resolved model name as reported by the provider.</summary>
        [Key(1)]
        public string? ModelName { get; set; }

        /// <summary>Provider or API version string.</summary>
        [Key(2)]
        public string? ProviderVersion { get; set; }

        /// <summary>Error message when <see cref="Success"/> is <c>false</c>.</summary>
        [Key(3)]
        public string? ErrorMessage { get; set; }

        /// <summary>Round-trip latency in milliseconds.</summary>
        [Key(4)]
        public int LatencyMs { get; set; }
    }
}
