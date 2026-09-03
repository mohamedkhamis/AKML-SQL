using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests
{
    /// <summary>
    /// Spec 036 (US2, FR-013, T025) — one normalisation point for AI provider ids, shared by the
    /// Options page and the provider factory. Every row of the alias table in
    /// <c>contracts/kimi-provider.md</c> is pinned here, including the legacy <c>AzureOpenAI</c>
    /// and <c>LMStudio</c> spellings that earlier builds wrote to config.json (research R8).
    /// Unrecognised non-empty input passes through trimmed+lowercased so the factory's
    /// "Unknown AI provider" error can name what it was given.
    /// </summary>
    public class ProviderIdNormalizationTests
    {
        [Theory]
        [InlineData("anthropic", "anthropic")]
        [InlineData("Anthropic", "anthropic")]
        [InlineData("openai", "openai")]
        [InlineData("OpenAI", "openai")]
        [InlineData("azure", "azure")]
        [InlineData("azureopenai", "azure")]
        [InlineData("AzureOpenAI", "azure")]      // legacy spelling written by old builds
        [InlineData("Azure OpenAI", "azure")]     // Options display name
        [InlineData("gemini", "gemini")]
        [InlineData("Gemini", "gemini")]
        [InlineData("kimi", "kimi")]
        [InlineData("KIMI", "kimi")]
        [InlineData("moonshot", "kimi")]
        [InlineData("Moonshot", "kimi")]
        [InlineData("Kimi (Moonshot)", "kimi")]   // Options display name
        [InlineData("ollama", "ollama")]
        [InlineData("Ollama", "ollama")]
        [InlineData("lmstudio", "lmstudio")]
        [InlineData("LMStudio", "lmstudio")]      // legacy spelling written by old builds
        [InlineData("LM Studio", "lmstudio")]     // Options display name
        [InlineData("lm studio", "lmstudio")]
        [InlineData("custom", "custom")]
        [InlineData("Custom", "custom")]
        [InlineData("  kimi  ", "kimi")]          // surrounding whitespace is ignored
        public void Normalize_MapsAliasesToCanonicalIds(string input, string expected)
            => Assert.Equal(expected, AiProviderIds.Normalize(input));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("(None)")]   // the Options page's none entry
        public void Normalize_NullOrWhitespace_IsNone(string? input)
            => Assert.Equal(string.Empty, AiProviderIds.Normalize(input));

        [Fact]
        public void Normalize_UnrecognisedInput_PassesThroughLowercased()
        {
            // The factory switch then rejects it with the "Unknown AI provider" message that
            // names the offending value and lists the canonical ids.
            Assert.Equal("some-proxy", AiProviderIds.Normalize("Some-Proxy"));
        }

        [Fact]
        public void CanonicalIds_ListsAllEightProviders()
        {
            Assert.Equal(
                new[] { "anthropic", "openai", "azure", "gemini", "kimi", "ollama", "lmstudio", "custom" },
                AiProviderIds.CanonicalIds);
        }
    }
}
