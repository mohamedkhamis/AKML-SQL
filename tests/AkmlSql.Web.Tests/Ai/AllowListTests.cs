using System.Net.Http;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Spec 021 (web edition) -- M6 task T130. The AiClientFactory origin allow-list per
/// contracts/ai-key-wrapping.md "Provider-call contract". A fetch to a non-allow-listed
/// origin MUST be refused at the factory layer, BEFORE the request hits the network.
/// </summary>
public sealed class AllowListTests
{
    private static IAiClientFactory Build() => new AiClientFactory(
        new AiKeyVault(new InMemoryIndexedDbAdapter(), new InMemoryWebCryptoWrapper()),
        new HttpClient(),
        new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));

    [Theory]
    [InlineData("openai", "https://api.openai.com")]
    [InlineData("anthropic", "https://api.anthropic.com")]
    [InlineData("gemini", "https://generativelanguage.googleapis.com")]
    [InlineData("ollama", "http://localhost:11434")]
    [InlineData("ollama", "http://127.0.0.1:11434")]
    [InlineData("lmstudio", "http://localhost:1234")]
    [InlineData("lmstudio", "http://127.0.0.1:1234")]
    public void IsOriginAllowed_returns_true_for_documented_origins(string providerId, string origin)
    {
        var factory = Build();
        Assert.True(factory.IsOriginAllowed(providerId, origin));
    }

    [Theory]
    [InlineData("azure", "https://my-resource.openai.azure.com")]
    [InlineData("azure", "https://prod-us.openai.azure.com")]
    public void IsOriginAllowed_matches_azure_subdomain_wildcard(string providerId, string origin)
    {
        var factory = Build();
        Assert.True(factory.IsOriginAllowed(providerId, origin));
    }

    [Theory]
    [InlineData("openai", "https://api.openai.com.evil.com")]            // suffix attack
    [InlineData("openai", "https://api-openai-com.attacker.io")]
    [InlineData("anthropic", "https://api.openai.com")]                  // wrong provider
    [InlineData("gemini", "http://generativelanguage.googleapis.com")]   // http vs https
    [InlineData("azure", "https://my.azure.com")]                        // wrong domain
    [InlineData("ollama", "http://localhost:11435")]                     // wrong port
    [InlineData("ollama", "http://10.0.0.5:11434")]                      // not loopback
    [InlineData("unknown", "https://api.openai.com")]                    // unknown provider id
    public void IsOriginAllowed_rejects_non_listed_origins(string providerId, string origin)
    {
        var factory = Build();
        Assert.False(factory.IsOriginAllowed(providerId, origin));
    }

    [Fact]
    public void ExtractOrigin_strips_path_and_query()
    {
        Assert.Equal("https://api.openai.com",
            AiClientFactory.ExtractOrigin("https://api.openai.com/v1/chat/completions?x=1"));
        Assert.Equal("http://localhost:11434",
            AiClientFactory.ExtractOrigin("http://localhost:11434/v1/chat/completions"));
    }
}
