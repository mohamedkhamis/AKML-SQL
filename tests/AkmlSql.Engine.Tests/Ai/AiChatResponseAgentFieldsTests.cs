using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Ai;
using AkmlSql.Engine.Handlers.Ai;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using MessagePack;
using Microsoft.Extensions.AI;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Ai;

/// <summary>
/// Spec 046 (FR-022, research R4, contracts/chat-composer-and-agents.md Part 4): the chat reply
/// names the provider and model of the agent that ACTUALLY answered — the resolved agent, a
/// fallback-chain agent, or the offline provider — in two append-only keys (7, 8), so both chat
/// panels can attribute an answer as "Name · Provider · model" even when a fallback fired. A
/// six-key peer (any engine before spec 046) leaves both null and the shell shows the name only.
/// </summary>
public class AiChatResponseAgentFieldsTests
{
    // ── Wire shape ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Keys_7_and_8_round_trip()
    {
        var reply = new AiChatResponse
        {
            Success = true,
            Response = "ok",
            AgentName = "Gemini Flash",
            AgentProvider = "gemini",
            AgentModel = "gemini-2.5-flash",
        };

        var back = MessagePackSerializer.Deserialize<AiChatResponse>(MessagePackSerializer.Serialize(reply));

        Assert.Equal("Gemini Flash", back.AgentName);
        Assert.Equal("gemini", back.AgentProvider);
        Assert.Equal("gemini-2.5-flash", back.AgentModel);
    }

    [Fact]
    public void A_seven_element_payload_from_an_older_engine_leaves_both_null()
    {
        // Keys 0..6: Success, Response, CodeActions, ErrorMessage, TokensUsed, LatencyMs, AgentName.
        var legacy = MessagePackSerializer.Serialize(new object?[] { true, "ok", null, null, 12, 34, "Kimi" });

        var back = MessagePackSerializer.Deserialize<AiChatResponse>(legacy);

        Assert.Equal("Kimi", back.AgentName);
        Assert.Null(back.AgentProvider);
        Assert.Null(back.AgentModel);
    }

    // ── Who answered ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_chat_handler_names_the_resolved_agents_provider_and_model()
    {
        var active = Agent("Active Agent", "ollama", "llama3.1");
        var chat = Agent("Chat Agent", "anthropic", "claude-sonnet-4-6", key: "k");
        var settings = Settings(active, chat);
        settings.FeatureAgents.Chat = chat.Id;
        var svcs = Services(settings, _ => new FakeChatClient("answer"));

        var reply = await new AiChatHandler(svcs).HandleAsync(
            new AiChatRequest { SessionId = "s", Message = "hi" }, NewContext(), CancellationToken.None);

        Assert.True(reply.Success, reply.ErrorMessage);
        Assert.Equal("Chat Agent", reply.AgentName);
        Assert.Equal("anthropic", reply.AgentProvider);
        Assert.Equal("claude-sonnet-4-6", reply.AgentModel);
    }

    [Fact]
    public async Task A_fallback_chain_agent_is_named_with_its_own_provider_and_model()
    {
        var selected = Agent("Selected", "ollama", "selected-model");
        var first = Agent("First", "lmstudio", "first-model");
        var settings = Settings(selected, first);
        settings.FallbackOrder.Add(first.Id);
        var svcs = Services(settings, s => s.Model == "selected-model"
            ? new ThrowingChatClient(new HttpRequestException("boom"))
            : new FakeChatClient("from first"));

        var answer = await svcs.ExecuteWithFallbackDetailedAsync(
            AiAgentResolver.Project(settings, selected), selected, Messages, new ChatOptions(), CancellationToken.None);

        Assert.True(answer.UsedFallback);
        Assert.Equal("First", answer.AgentName);
        Assert.Equal("lmstudio", answer.AgentProvider);
        Assert.Equal("first-model", answer.AgentModel);
    }

    [Fact]
    public async Task The_offline_provider_is_named_by_its_provider_and_offline_model()
    {
        var selected = Agent("Selected", "ollama", "selected-model");
        var settings = Settings(selected);
        settings.OfflineProvider = "ollama";
        settings.OfflineModel = "offline-model";
        settings.OfflineEndpoint = "http://127.0.0.1:1/";
        var svcs = Services(settings, _ => new ThrowingChatClient(new HttpRequestException("boom")));
        svcs.FallbackClientFactory = _ => new FakeChatClient("from offline");

        var answer = await svcs.ExecuteWithFallbackDetailedAsync(
            AiAgentResolver.Project(settings, selected), selected, Messages, new ChatOptions(), CancellationToken.None);

        Assert.True(answer.UsedFallback);
        Assert.Equal("Ollama", answer.AgentName);
        Assert.Equal("ollama", answer.AgentProvider);
        Assert.Equal("offline-model", answer.AgentModel);
    }

    [Fact]
    public async Task The_three_part_overload_still_answers_as_before()
    {
        var selected = Agent("Selected", "ollama", "selected-model");
        var settings = Settings(selected);
        var svcs = Services(settings, _ => new FakeChatClient("ok"));

        var (response, usedFallback, agentName) = await svcs.ExecuteWithFallbackAsync(
            AiAgentResolver.Project(settings, selected), selected, Messages, new ChatOptions(), CancellationToken.None);

        Assert.Equal("ok", response.Text);
        Assert.False(usedFallback);
        Assert.Equal("Selected", agentName);
    }

    // ── Harness ──────────────────────────────────────────────────────────────────────────────

    private static readonly List<ChatMessage> Messages = new() { new(ChatRole.User, "hi") };

    private static AiAgent Agent(string name, string provider, string model, string key = "")
        => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Provider = provider,
            Model = model,
            ApiKey = key,
            Enabled = true,
        };

    private static AiSettings Settings(AiAgent active, params AiAgent[] others)
    {
        var settings = new AiSettings
        {
            Enabled = true,
            PrivacyConsentRequired = false,   // consent GIVEN — the consent gate has its own tests
            ActiveAgentId = active.Id,
        };
        settings.Agents.Add(active);
        foreach (var agent in others) settings.Agents.Add(agent);
        return settings;
    }

    private static AiPipelineServices Services(AiSettings settings, Func<AiSettings, IChatClient> factory)
    {
        var svcs = AiPipelineServices.Build(new SchemaCacheManager(), new TsqlParserService(), () => settings);
        svcs.ClientFactory = factory;
        return svcs;
    }

    private static RpcContext NewContext() => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
        ParserService = new TsqlParserService(),
        SettingsLoader = () => new AppSettings(),
    };

    private sealed class FakeChatClient : IChatClient
    {
        private readonly string _text;
        public FakeChatClient(string text) => _text = text;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, _text)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        private readonly Exception _failure;
        public ThrowingChatClient(Exception failure) => _failure = failure;

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromException<ChatResponse>(_failure);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }
}
