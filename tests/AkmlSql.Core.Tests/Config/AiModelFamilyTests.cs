using AkmlSql.Core.Config;
using Xunit;

namespace AkmlSql.Core.Tests.Config
{
    /// <summary>
    /// Model-name family detection behind the provider/model mismatch guard. A Gemini config
    /// carrying "claude-sonnet-5" reached Google's API verbatim and died with a raw 404 in the
    /// SSMS chat panel; Detect() is what lets the factory and the Options page catch that first.
    /// </summary>
    public class AiModelFamilyTests
    {
        [Theory]
        [InlineData("claude-sonnet-5", "anthropic")]
        [InlineData("Claude-Opus-4-8", "anthropic")]
        [InlineData("gpt-4o", "openai")]
        [InlineData("chatgpt-4o-latest", "openai")]
        [InlineData("o3-mini", "openai")]
        [InlineData("o1", "openai")]
        [InlineData("gemini-flash-latest", "gemini")]
        [InlineData("models/gemini-2.0-flash", "gemini")]
        [InlineData("gemma-4-31b-it", "gemini")]
        public void Detect_recognises_first_party_families(string model, string family)
            => Assert.Equal(family, AiModelFamily.Detect(model));

        [Theory]
        [InlineData("llama3.1")]
        [InlineData("qwen2.5-coder")]
        [InlineData("orca-mini")]      // "o" prefix but not an o1/o3/o4 reasoning model
        [InlineData("my-azure-deployment")]
        [InlineData("")]
        [InlineData(null)]
        public void Detect_returns_null_for_unknown_or_local_models(string? model)
            => Assert.Null(AiModelFamily.Detect(model));

        [Theory]
        [InlineData("Anthropic", "claude-sonnet-4-6")]
        [InlineData("anthropic", "claude-sonnet-4-6")]
        [InlineData("OpenAI", "gpt-4o")]
        [InlineData("Gemini", "gemini-flash-latest")]
        public void DefaultModelFor_first_party_providers(string provider, string expected)
            => Assert.Equal(expected, AiModelFamily.DefaultModelFor(provider));

        [Theory]
        [InlineData("(None)")]
        [InlineData("Ollama")]
        [InlineData("LM Studio")]
        [InlineData("Azure OpenAI")]   // deployment names are user-defined; never auto-fill
        [InlineData("Custom")]
        [InlineData(null)]
        public void DefaultModelFor_has_no_opinion_for_local_or_custom(string? provider)
            => Assert.Null(AiModelFamily.DefaultModelFor(provider));
    }
}
