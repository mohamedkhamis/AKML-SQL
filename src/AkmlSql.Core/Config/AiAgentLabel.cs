#nullable enable
using System.Collections.Generic;

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Spec 046 (FR-022, FR-030, contracts/chat-composer-and-agents.md Part 3): the one way both
    /// editions name an AI agent in the chat — <c>{Name} · {Provider} · {model}</c>, e.g.
    /// <c>Gemini Flash · Gemini · gemini-2.5-flash</c>. Used by the SSMS agent picker, the answer
    /// attribution caption, the "Now answering with …" notice, the web chat's agent selector and the
    /// web answer header, so a user switching models always sees which provider and model answer.
    /// Blank parts are left out together with their separator.
    /// </summary>
    public static class AiAgentLabel
    {
        /// <summary>The separator between the label's parts (a middle dot with spaces).</summary>
        public const string Separator = " · ";

        /// <summary>
        /// Formats an agent's label. <paramref name="providerId"/> is normalised and shown through
        /// <see cref="AiProviderIds.DisplayName"/>; an unknown id passes through as given.
        /// </summary>
        public static string Format(string? name, string? providerId, string? model)
        {
            var parts = new List<string>(3);
            Add(parts, name);
            var provider = string.IsNullOrWhiteSpace(providerId)
                ? null
                : AiProviderIds.DisplayName(AiProviderIds.Normalize(providerId));
            // An agent named after its provider (and the offline fallback, which answers under the
            // provider's display name) would read "Ollama · Ollama · llama3" — say it once.
            if (provider != null && parts.Count == 1 &&
                string.Equals(parts[0], provider.Trim(), System.StringComparison.OrdinalIgnoreCase))
                provider = null;
            Add(parts, provider);
            Add(parts, model);
            return string.Join(Separator, parts);
        }

        private static void Add(List<string> parts, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add(value!.Trim());
        }
    }
}
