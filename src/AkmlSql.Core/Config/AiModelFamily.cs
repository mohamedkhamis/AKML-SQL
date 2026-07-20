#nullable enable
using System;

namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Model-name family heuristics shared by the Options UI (auto-correct the model box on a
    /// provider switch) and <c>AiProviderFactory</c> (refuse first-party provider/model
    /// mismatches). A Gemini config carrying "claude-sonnet-5" previously reached Google's API
    /// verbatim and surfaced Google's raw 404 JSON in the SSMS chat panel.
    /// </summary>
    public static class AiModelFamily
    {
        /// <summary>
        /// "anthropic", "openai", or "gemini" when the name clearly belongs to that first-party
        /// family; null for anything else (local models, Azure deployment names, custom ids) —
        /// those are the user's business and must never be second-guessed.
        /// </summary>
        public static string? Detect(string? model)
        {
            if (string.IsNullOrWhiteSpace(model)) return null;
            var m = model!.Trim().ToLowerInvariant();
            if (m.StartsWith("models/", StringComparison.Ordinal)) m = m.Substring(7);

            if (m.StartsWith("claude", StringComparison.Ordinal)) return "anthropic";
            if (m.StartsWith("gpt", StringComparison.Ordinal) ||
                m.StartsWith("chatgpt", StringComparison.Ordinal) ||
                IsOpenAiReasoningSeries(m))
            {
                return "openai";
            }
            if (m.StartsWith("gemini", StringComparison.Ordinal) ||
                m.StartsWith("gemma", StringComparison.Ordinal))
            {
                return "gemini";
            }
            return null;
        }

        /// <summary>o1/o3/o4 reasoning models: the digit must end the name or be followed by
        /// '-' / '.' so local names like "orca-mini" never match.</summary>
        private static bool IsOpenAiReasoningSeries(string m)
        {
            if (m.Length < 2 || m[0] != 'o') return false;
            if (m[1] != '1' && m[1] != '3' && m[1] != '4') return false;
            return m.Length == 2 || m[2] == '-' || m[2] == '.';
        }

        /// <summary>
        /// The suggested default model for a first-party provider (accepts the provider id or the
        /// Options-page display name, case-insensitive). Null for local/custom providers whose
        /// model names are user-defined. "gemini-flash-latest" is deliberately the rolling alias:
        /// pinned Gemini names rot ("gemini-2.5-flash" is already rejected for new API keys).
        /// </summary>
        public static string? DefaultModelFor(string? provider)
        {
            if (string.IsNullOrWhiteSpace(provider)) return null;
            switch (provider!.Trim().ToLowerInvariant())
            {
                case "anthropic": return "claude-sonnet-4-6";
                case "openai": return "gpt-4o";
                case "gemini": return "gemini-flash-latest";
                default: return null;
            }
        }
    }
}
