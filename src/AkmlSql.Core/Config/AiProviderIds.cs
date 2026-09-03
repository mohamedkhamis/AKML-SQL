#nullable enable
using System;
using System.Collections.Generic;

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Spec 036 (US2, FR-013) — canonical AI provider ids and the single normalisation point
    /// shared by the Options page and <c>AiProviderFactory</c>. Earlier builds saved display-ish
    /// strings ("AzureOpenAI", "LMStudio") that the factory rejected as unknown (research R8);
    /// the alias table accepts those spellings on read so existing configs keep working with no
    /// migration. Save paths write canonical ids only.
    /// </summary>
    public static class AiProviderIds
    {
        public const string Anthropic = "anthropic";
        public const string OpenAI = "openai";
        public const string Azure = "azure";
        public const string Gemini = "gemini";
        public const string Kimi = "kimi";
        public const string Ollama = "ollama";
        public const string LmStudio = "lmstudio";
        public const string Custom = "custom";

        /// <summary>The canonical ids, for the "Unknown AI provider" error and validation.</summary>
        public static IReadOnlyList<string> CanonicalIds { get; } = new[]
        {
            Anthropic, OpenAI, Azure, Gemini, Kimi, Ollama, LmStudio, Custom
        };

        /// <summary>
        /// Normalises any accepted spelling (canonical id, legacy save form, or Options display
        /// name, case-insensitive) to the canonical id. Null/whitespace and the "(None)" display
        /// entry map to "" (provider = none). Unrecognised non-empty input passes through
        /// trimmed+lowercased so the factory's "Unknown AI provider" error can name what it was given.
        /// </summary>
        public static string Normalize(string? provider)
        {
            if (string.IsNullOrWhiteSpace(provider)) return string.Empty;

            return provider!.Trim().ToLowerInvariant() switch
            {
                Anthropic => Anthropic,
                OpenAI => OpenAI,
                Azure or "azureopenai" or "azure openai" => Azure,
                Gemini => Gemini,
                Kimi or "moonshot" or "kimi (moonshot)" => Kimi,
                Ollama => Ollama,
                LmStudio or "lm studio" => LmStudio,
                Custom => Custom,
                "(none)" => "",
                var other => other,
            };
        }
    }
}
