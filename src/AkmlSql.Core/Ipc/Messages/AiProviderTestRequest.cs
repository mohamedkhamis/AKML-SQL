using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to test connectivity and authentication with an AI provider.
    /// Sent Shell -> Engine.
    /// </summary>
    [MessagePackObject]
    public class AiProviderTestRequest
    {
        /// <summary>Provider name (e.g. "OpenAI", "AzureOpenAI", "Anthropic", "Ollama").</summary>
        [Key(0)]
        public string Provider { get; set; } = string.Empty;

        /// <summary>API key for the provider.</summary>
        [Key(1)]
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>Optional custom endpoint URL (required for Azure OpenAI and Ollama).</summary>
        [Key(2)]
        public string? Endpoint { get; set; }

        /// <summary>Model identifier to test (e.g. "gpt-4o", "claude-sonnet-4-20250514").</summary>
        [Key(3)]
        public string Model { get; set; } = string.Empty;
    }
}
