using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// 2026-11 master–detail redesign: the web edition stores AI AGENTS keyed by their own id, so one provider can carry several.
/// A call names the agent; the agent's provider decides the wire, the auth header and the gates. Agents saved before keep
/// their provider id as their id. Test connection sends the typed key, or the stored one while it still belongs to the provider.
/// </summary>
public sealed class AiAgentIdTests
{
    private const string AnthropicReply = "{\"content\":[{\"type\":\"text\",\"text\":\"OK\"}]}";

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();
        public RecordingHandler(HttpStatusCode status = HttpStatusCode.OK, string body = AnthropicReply) { _status = status; _body = body; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(ct));
            return new HttpResponseMessage(_status) { Content = new StringContent(_body) };
        }
    }

    private static (AiKeyVault Vault, AiPreference Preference, AiClientFactory Client, RecordingHandler Http) Build(
        HttpStatusCode status = HttpStatusCode.OK, string body = AnthropicReply)
    {
        var db = new InMemoryIndexedDbAdapter();
        var vault = new AiKeyVault(db, new InMemoryWebCryptoWrapper());
        var http = new RecordingHandler(status, body);
        var client = new AiClientFactory(vault, new HttpClient(http), new DiagnosticsRingBuffer(new InMemoryIndexedDbAdapter()));
        return (vault, new AiPreference(db), client, http);
    }

    private static AiProviderConfig Agent(string provider, string model, string? endpoint = null) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        ProviderId = provider,
        DisplayName = provider + " " + model,
        Model = model,
        Endpoint = endpoint,
        CreatedUtc = DateTime.UtcNow.ToString("O"),
    };

    private static string? Header(HttpRequestMessage request, string name) =>
        request.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    [Fact]
    public async Task A_call_by_agent_id_uses_that_agents_provider_and_key()
    {
        var (vault, _, client, http) = Build();
        var sonnet = Agent("anthropic", "claude-sonnet-4-6");
        var opus = Agent("anthropic", "claude-opus-4-8");
        await vault.SaveAgentAsync(sonnet, "sk-sonnet");
        await vault.SaveAgentAsync(opus, "sk-opus");

        var answer = await client.SendAsync(opus.Id, new AiChatRequest { UserPrompt = "q" }, CancellationToken.None);

        Assert.Equal("OK", answer);
        var request = Assert.Single(http.Requests);
        Assert.Equal("https://api.anthropic.com/v1/messages", request.RequestUri!.ToString());
        Assert.Equal("sk-opus", Header(request, "x-api-key"));
        Assert.Contains("claude-opus-4-8", Assert.Single(http.Bodies));
    }

    [Fact]
    public async Task An_agent_of_a_provider_that_is_never_browser_direct_is_refused_as_such()
    {
        var (vault, _, client, http) = Build();
        var gpt = Agent("openai", "gpt-4.1");
        await vault.SaveAgentAsync(gpt, "sk-openai");

        var ex = await Assert.ThrowsAsync<ProviderNotBrowserDirectException>(() =>
            client.SendAsync(gpt.Id, new AiChatRequest { UserPrompt = "q" }, CancellationToken.None));

        Assert.Equal("openai", ex.ProviderId);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Save_with_an_empty_key_keeps_the_stored_key_until_the_provider_changes()
    {
        var (vault, _, _, _) = Build();
        var agent = Agent("anthropic", "claude-sonnet-4-6");
        await vault.SaveAgentAsync(agent, "sk-first");

        agent.Model = "claude-opus-4-8";
        await vault.SaveAgentAsync(agent, null);
        Assert.True(await vault.HasKeyAsync(agent.Id));
        using (var kept = await vault.UnwrapForCallAsync(agent.Id)) Assert.Equal("sk-first", kept.Value);

        agent.ProviderId = "gemini";
        await vault.SaveAgentAsync(agent, null);
        Assert.False(await vault.HasKeyAsync(agent.Id));   // a key belongs to its provider
    }

    [Fact]
    public async Task Duplicate_rewraps_the_key_for_the_copy()
    {
        var (vault, _, _, _) = Build();
        var source = Agent("gemini", "gemini-flash-latest");
        await vault.SaveAgentAsync(source, "gm-key");
        var copy = Agent("gemini", "gemini-flash-latest");

        await vault.DuplicateAsync(source.Id, copy);

        using var key = await vault.UnwrapForCallAsync(copy.Id);
        Assert.Equal("gm-key", key.Value);
        Assert.Equal(2, (await vault.ListAsync()).Count);
    }

    [Fact]
    public async Task The_active_provider_is_read_from_the_active_agent()
    {
        var (vault, preference, _, _) = Build();
        var local = Agent("ollama", "llama3.1:8b", "http://localhost:11434");
        await vault.SaveAgentAsync(local, null);

        await preference.SetActiveAsync(local.Id);
        Assert.Equal("ollama", await preference.GetActiveProviderAsync());

        // An agent saved before agents had ids is keyed by its provider id.
        await vault.SetKeyAsync("anthropic", new AiProviderConfig { DisplayName = "Anthropic", Model = "claude-sonnet-4-6" }, "sk-old");
        await preference.SetActiveAsync("anthropic");
        Assert.Equal("anthropic", await preference.GetActiveProviderAsync());
        Assert.Equal("anthropic", Assert.Single(await vault.ListAsync(), c => c.ProviderId == "anthropic").Id);

        await preference.SetActiveAsync(string.Empty);
        Assert.Equal(string.Empty, await preference.GetActiveProviderAsync());
    }

    [Fact]
    public async Task Test_connection_sends_the_typed_key_and_reports_the_latency()
    {
        var (_, _, client, http) = Build();

        var result = await client.TestAsync(Agent("anthropic", "claude-sonnet-4-6"), "sk-typed", CancellationToken.None);

        Assert.True(result.Success, result.Message);
        Assert.True(result.LatencyMs > 0);
        Assert.Contains("claude-sonnet-4-6", result.Message);
        Assert.Equal("sk-typed", Header(Assert.Single(http.Requests), "x-api-key"));
    }

    [Fact]
    public async Task Test_connection_uses_the_stored_key_when_none_is_typed()
    {
        var (vault, _, client, http) = Build();
        var agent = Agent("anthropic", "claude-sonnet-4-6");
        await vault.SaveAgentAsync(agent, "sk-stored");

        var result = await client.TestAsync(agent, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("sk-stored", Header(Assert.Single(http.Requests), "x-api-key"));
    }

    [Fact]
    public async Task Test_connection_never_sends_a_key_stored_for_another_provider()
    {
        var (vault, _, client, http) = Build(body: "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}");
        var agent = Agent("anthropic", "claude-sonnet-4-6");
        await vault.SaveAgentAsync(agent, "sk-anthropic");
        agent.ProviderId = "gemini";
        agent.Model = "gemini-flash-latest";

        await client.TestAsync(agent, null, CancellationToken.None);

        var request = Assert.Single(http.Requests);
        Assert.Null(request.Headers.Authorization);
        Assert.Null(Header(request, "x-api-key"));
    }

    [Fact]
    public async Task Test_connection_explains_a_refusal_without_the_body()
    {
        const string secret = "SECRET-BODY-MARKER";
        var (_, _, client, _) = Build(HttpStatusCode.Unauthorized, "{\"error\":{\"type\":\"authentication_error\",\"message\":\"" + secret + "\"}}");

        var result = await client.TestAsync(Agent("anthropic", "claude-sonnet-4-6"), "sk-bad", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("rejected the API key", result.Message);
        Assert.DoesNotContain(secret, result.Message);
        Assert.DoesNotContain("sk-bad", result.Message);
    }

    [Fact]
    public async Task Test_connection_refuses_what_cannot_run_in_a_browser_before_sending()
    {
        var (_, _, client, http) = Build();

        var openai = await client.TestAsync(Agent("openai", "gpt-4.1"), "sk", CancellationToken.None);
        var foreign = await client.TestAsync(Agent("ollama", "llama3", "http://evil.example.com:11434"), null, CancellationToken.None);

        Assert.False(openai.Success);
        Assert.Contains("browser", openai.Message);
        Assert.False(foreign.Success);
        Assert.Contains("Endpoint", foreign.Message);
        Assert.Empty(http.Requests);
    }
}
