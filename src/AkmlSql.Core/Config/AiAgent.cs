#nullable enable
using System.Text.Json.Serialization;
// ReSharper disable UnusedMember.Global

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Spec 037 (data-model E1) — one saved AI configuration the user can name, pick, and test.
    /// Persisted at <c>ai.agents[]</c>; referenced by <see cref="AiSettings.ActiveAgentId"/>,
    /// by each field of <see cref="FeatureAgentAssignments"/>, and by entries of
    /// <see cref="AiSettings.FallbackOrder"/> — always by <see cref="Id"/>, never by
    /// <see cref="Name"/> (V5).
    /// </summary>
    public class AiAgent
    {
        /// <summary>32 lowercase hex (<c>Guid.NewGuid().ToString("N")</c>), assigned at creation. Immutable.</summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        /// <summary>User-facing name. 1–40 chars after trim, unique case-insensitively (V1–V3).</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>Canonical provider id from <see cref="AiProviderIds.CanonicalIds"/> (V4).</summary>
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "";

        /// <summary>Model identifier. Free text (V6).</summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        /// <summary><c>dpapi:</c>-wrapped API key, or legacy plaintext on read. Never unwrapped by migration.</summary>
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        /// <summary>Absolute URL when present (V10); required for azure/custom (V9).</summary>
        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = "";

        /// <summary>Maximum tokens in the AI response. 256 … 32768 (V11).</summary>
        [JsonPropertyName("maxTokens")]
        public int MaxTokens { get; set; } = 4096;

        /// <summary>Sampling temperature, 0.0 … 2.0 (V11).</summary>
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.2;

        /// <summary>Request timeout in seconds, 5 … 300 (V11).</summary>
        [JsonPropertyName("timeout")]
        public int Timeout { get; set; } = 30;

        /// <summary>Automatic retries on transient failures, 0 … 5 (V11).</summary>
        [JsonPropertyName("retries")]
        public int Retries { get; set; } = 2;

        /// <summary>
        /// Disabled agents are hidden from the picker and treated as absent by assignments and
        /// the fallback order (S1).
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>ISO 8601 UTC creation time. Informational; ties are broken by list order.</summary>
        [JsonPropertyName("createdUtc")]
        public string CreatedUtc { get; set; } = "";

        /// <summary>Last recorded health check. <c>null</c> ≡ never tested.</summary>
        [JsonPropertyName("health")]
        public AgentHealth? Health { get; set; }

        /// <summary>
        /// V23 working-copy state — <c>true</c> when the stored key could not be unwrapped on
        /// this machine. <b>Never serialised</b> and never set by load or migration: the Options
        /// page derives it on every bind, per agent, so switching between a decryptable and an
        /// undecryptable agent without saving keeps both states (contracts/options-agents-ui.md
        /// § Per-agent key protection).
        /// </summary>
        [JsonIgnore]
        public bool KeyDecryptFailed { get; set; }
    }

    /// <summary>
    /// Spec 037 (data-model E2) — the last recorded outcome of checking an agent, persisted at
    /// <c>ai.agents[].health</c>. Advisory only — never gates a request.
    /// </summary>
    public class AgentHealth
    {
        /// <summary>One of <see cref="AgentHealthStatus"/>; compared case-insensitively on read, written lowercase.</summary>
        [JsonPropertyName("status")]
        public string Status { get; set; } = AgentHealthStatus.Unknown;

        /// <summary>ISO 8601 UTC of the last check.</summary>
        [JsonPropertyName("checkedUtc")]
        public string? CheckedUtc { get; set; }

        /// <summary>Round trip of the last <b>successful</b> check.</summary>
        [JsonPropertyName("latencyMs")]
        public int LatencyMs { get; set; }

        /// <summary>User-facing summary, ≤ 500 chars, never contains a key (V24).</summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Spec 037 (data-model E2) — the four health statuses, as string constants rather than an
    /// enum (matching <see cref="AiProviderIds"/>): the status round-trips through JSON as text
    /// and tolerates unknown values by falling back to <see cref="Unknown"/> (V20).
    /// </summary>
    public static class AgentHealthStatus
    {
        public const string Unknown = "unknown";
        public const string Ready = "ready";
        public const string NeedsKey = "needsKey";
        public const string Failed = "failed";
    }

    /// <summary>
    /// Spec 037 (data-model E3) — which agent serves each AI feature, persisted at
    /// <c>ai.featureAgents</c>. Every field holds an agent <see cref="AiAgent.Id"/>, or
    /// <c>""</c> meaning "follow the active agent".
    /// </summary>
    public class FeatureAgentAssignments
    {
        /// <summary>Agent for the chat panel.</summary>
        [JsonPropertyName("chat")]
        public string Chat { get; set; } = "";

        /// <summary>Agent for natural-language to SQL generation.</summary>
        [JsonPropertyName("textToSql")]
        public string TextToSql { get; set; } = "";

        /// <summary>Agent for SQL explanation.</summary>
        [JsonPropertyName("explain")]
        public string Explain { get; set; } = "";

        /// <summary>Agent for error fix suggestions.</summary>
        [JsonPropertyName("fix")]
        public string Fix { get; set; } = "";

        /// <summary>Agent for query optimization suggestions.</summary>
        [JsonPropertyName("optimize")]
        public string Optimize { get; set; } = "";

        /// <summary>Agent for index suggestions.</summary>
        [JsonPropertyName("indexSuggestions")]
        public string IndexSuggestions { get; set; } = "";

        /// <summary>Agent for inline ghost-text completions.</summary>
        [JsonPropertyName("ghostText")]
        public string GhostText { get; set; } = "";
    }
}
