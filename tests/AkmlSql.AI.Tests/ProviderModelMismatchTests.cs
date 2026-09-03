using System;
using AkmlSql.Core.Config;
using AkmlSql.Engine.Ai.Providers;
using Xunit;

namespace AkmlSql.AI.Tests;

/// <summary>
/// Cross-provider model guard in <see cref="AiProviderFactory"/>. A config with
/// provider=Gemini + model=claude-sonnet-5 previously went to Google's API verbatim and the
/// user saw Google's raw 404 JSON in the SSMS chat panel. The factory must refuse the obvious
/// mismatches (first-party clouds only) with a message that names both sides and the fix.
/// Custom/local providers (Ollama, LM Studio, Azure deployments, custom endpoints) accept any
/// model name — proxies legitimately serve foreign model ids.
/// </summary>
public sealed class ProviderModelMismatchTests
{
    // The Mscc.GenerativeAI SDK guards the API-key LENGTH at client construction (Google keys
    // are exactly 39 chars), so the dummy key must be 39 chars to reach/pass the factory logic.
    private static readonly string DummyKey = "AIza" + new string('x', 35);

    private static AiSettings Settings(string provider, string model, string? endpoint = null) => new()
    {
        Provider = provider,
        Model = model,
        ApiKey = DummyKey,
        Endpoint = endpoint ?? string.Empty,
    };

    [Theory]
    [InlineData("gemini", "claude-sonnet-5")]
    [InlineData("gemini", "gpt-4o")]
    [InlineData("anthropic", "gemini-flash-latest")]
    [InlineData("anthropic", "gpt-4o")]
    [InlineData("openai", "claude-sonnet-5")]
    [InlineData("openai", "gemini-flash-latest")]
    [InlineData("kimi", "gpt-4o")]            // spec 036 FR-012: foreign model under Kimi refused
    [InlineData("kimi", "claude-sonnet-5")]
    [InlineData("openai", "kimi-latest")]     // and the other direction: Kimi model under OpenAI
    [InlineData("gemini", "moonshot-v1-8k")]
    public void First_party_provider_with_foreign_model_throws_actionable(string provider, string model)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AiProviderFactory.Create(Settings(provider, model)));

        Assert.Contains(model, ex.Message);                                   // names the offending model
        Assert.Contains(provider, ex.Message, StringComparison.OrdinalIgnoreCase); // names the configured provider
        Assert.Contains("provider", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("gemini", "gemini-flash-latest")]
    [InlineData("anthropic", "claude-sonnet-4-6")]
    [InlineData("openai", "gpt-4o")]
    [InlineData("kimi", "kimi-latest")]
    [InlineData("kimi", "moonshot-v1-8k")]
    [InlineData("kimi", "kimi-k2-0905-preview")]
    public void Matching_first_party_pairs_create_a_client(string provider, string model)
    {
        using var client = AiProviderFactory.Create(Settings(provider, model));
        Assert.NotNull(client);
    }

    // Spec 036 (US2, FR-012, T026): Kimi/Moonshot names form their own family so the guard works
    // in both directions, and genuinely unrecognised names keep returning null (local models and
    // fine-tunes are never second-guessed).
    [Theory]
    [InlineData("kimi-latest")]
    [InlineData("kimi-k2-0905-preview")]
    [InlineData("Kimi-K2-Instruct")]
    [InlineData("moonshot-v1-8k")]
    [InlineData("Moonshot-v1-128k")]
    [InlineData("models/kimi-latest")]      // the existing models/ prefix strip still applies
    public void Kimi_and_moonshot_names_detect_as_the_kimi_family(string model)
        => Assert.Equal("kimi", AiModelFamily.Detect(model));

    [Fact]
    public void Unrecognised_names_still_return_null_family()
    {
        Assert.Null(AiModelFamily.Detect("my-fine-tuned-model"));
        Assert.Null(AiModelFamily.Detect("llama3.1"));
        Assert.Null(AiModelFamily.Detect(null));
    }

    [Fact]
    public void Custom_endpoint_serves_any_model_name()
    {
        using var client = AiProviderFactory.Create(
            Settings("custom", "claude-sonnet-5", "http://localhost:9999/v1"));
        Assert.NotNull(client);
    }

    [Fact]
    public void Ollama_serves_any_model_name()
    {
        using var client = AiProviderFactory.Create(Settings("ollama", "claude-sonnet-5"));
        Assert.NotNull(client);
    }

    [Fact]
    public void Lmstudio_serves_any_model_name()
    {
        using var client = AiProviderFactory.Create(
            Settings("lmstudio", "gpt-4o", "http://localhost:1234/v1"));
        Assert.NotNull(client);
    }
}
