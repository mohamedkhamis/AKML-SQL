using AkmlSql.Core.Config;
using AkmlSql.Engine.Ai.Providers;
using Xunit;

namespace AkmlSql.AI.Tests;

/// <summary>
/// Spec 036 (US2, FR-006/FR-007, T027) — Kimi (Moonshot) is a first-party, family-guarded
/// provider in <see cref="AiProviderFactory"/>: one OpenAI-compatible case with the default
/// endpoint applied, NOT a fall-through to the unguarded <c>custom</c> branch.
/// </summary>
public sealed class KimiProviderFactoryTests
{
    private static AiSettings Settings(string? key = "sk-test-kimi-key", string? model = "kimi-latest",
        string? endpoint = null, string provider = "kimi") => new()
    {
        Provider = provider,
        Model = model ?? string.Empty,
        ApiKey = key ?? string.Empty,
        Endpoint = endpoint ?? string.Empty,
    };

    [Fact]
    public void Default_endpoint_matches_the_contract_value()
    {
        // The normative value lives in contracts/kimi-provider.md; pinned here so a drift fails.
        Assert.Equal("https://api.moonshot.ai/v1", AiProviderFactory.DefaultKimiEndpoint);
    }

    [Fact]
    public void Kimi_with_defaults_creates_a_client()
    {
        // Endpoint deliberately empty — the factory must apply the default rather than take the
        // standard-OpenAI branch (an empty endpoint there would skip the default entirely).
        using var client = AiProviderFactory.Create(Settings());

        Assert.NotNull(client);
    }

    [Fact]
    public void Kimi_with_an_explicit_endpoint_creates_a_client()
    {
        // The mainland-China service is reached by overriding the endpoint (contract regional note).
        using var client = AiProviderFactory.Create(Settings(endpoint: "https://api.moonshot.cn/v1"));

        Assert.NotNull(client);
    }

    [Theory]
    [InlineData("kimi")]
    [InlineData("Kimi (Moonshot)")]
    [InlineData("moonshot")]
    public void Unnormalised_provider_spellings_resolve_to_kimi(string provider)
    {
        using var client = AiProviderFactory.Create(Settings(provider: provider));

        Assert.NotNull(client);
    }

    [Fact]
    public void Kimi_without_an_api_key_throws_naming_kimi()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => AiProviderFactory.Create(Settings(key: null)));

        Assert.Contains("Kimi", ex.Message);
        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public void Kimi_without_a_model_throws_naming_kimi()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => AiProviderFactory.Create(Settings(model: null)));

        Assert.Contains("Kimi", ex.Message);
        Assert.Contains("model", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Kimi_does_not_fall_through_to_the_unguarded_custom_case()
    {
        // The custom branch deliberately skips RequireModelFamily; Kimi must not inherit that.
        var ex = Assert.Throws<InvalidOperationException>(
            () => AiProviderFactory.Create(Settings(model: "gpt-4o")));

        Assert.Contains("Kimi", ex.Message);
        Assert.Contains("OpenAI", ex.Message);
    }

    [Fact]
    public void Unknown_provider_error_lists_the_canonical_ids()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => AiProviderFactory.Create(new AiSettings { Provider = "some-proxy", Model = "m" }));

        Assert.Contains("Unknown AI provider", ex.Message);
        Assert.Contains("kimi", ex.Message);
        Assert.Contains("azure", ex.Message);
        Assert.Contains("lmstudio", ex.Message);
    }
}
