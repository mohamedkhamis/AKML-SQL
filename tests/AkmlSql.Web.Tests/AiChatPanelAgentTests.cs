using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests;

/// <summary>
/// Spec 046 (US4, FR-022/FR-030/FR-034): in the web chat every answer names the agent it was
/// requested from ("Name · Provider · model", stored with the turn), the selector sits in the
/// composer, and a switch is announced once in the conversation — a line that is never saved.
/// </summary>
public sealed class AiChatPanelAgentTests : BunitContext
{
    private readonly FakeAiClientFactory _client = new();
    private readonly FakeChatHistoryStore _history = new();
    private readonly FakeAiKeyVault _vault = new();
    private readonly FakeAiPreference _preference = new("a1");

    public AiChatPanelAgentTests()
    {
        _vault.Configs.Add(new AiProviderConfig { Id = "a1", ProviderId = "anthropic", DisplayName = "Work", Model = "claude-sonnet-4-5", HasKey = true });
        _vault.Configs.Add(new AiProviderConfig { Id = "a2", ProviderId = "gemini", DisplayName = "Gemini Flash", Model = "gemini-2.5-flash", HasKey = true });
        Services.AddSingleton<IAiClientFactory>(_client);
        Services.AddSingleton<IAiPreference>(_preference);
        Services.AddSingleton<IAiFeatureSettings>(new FakeAiFeatureSettings());
        Services.AddSingleton<IChatHistoryStore>(_history);
        Services.AddSingleton<IDiagnosticsRingBuffer>(new FakeDiagnosticsRingBuffer());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());
        Services.AddSingleton<IAiKeyVault>(_vault);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private const string WorkLabel = "Work · Anthropic · claude-sonnet-4-5";
    private const string GeminiLabel = "Gemini Flash · Gemini · gemini-2.5-flash";

    private static void Send(IRenderedComponent<AiChatPanel> cut, string text)
    {
        cut.Find("textarea").Input(text);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Send").Click();
    }

    [Fact]
    public void The_selector_is_in_the_composer()
    {
        var cut = Render<AiChatPanel>();

        Assert.NotNull(cut.Find(".akml-chat-composer-footer select[data-testid='chat-agent-select']"));
    }

    [Fact]
    public void An_answer_is_stored_and_shown_with_the_agent_it_was_requested_from()
    {
        var cut = Render<AiChatPanel>();

        Send(cut, "Explain sys.databases");

        cut.WaitForAssertion(() =>
        {
            var answer = _history.Saved!.Turns.Single(t => t.Role == "assistant");
            Assert.Equal(WorkLabel, answer.AgentLabel);
            Assert.Contains(WorkLabel, cut.Find(".akml-chat-role-assistant .akml-chat-role-label").TextContent);
        });
    }

    [Fact]
    public void A_stored_answer_without_a_label_reads_assistant()
    {
        _history.Saved = new ChatConversation
        {
            Id = "c1",
            Turns = { new ChatTurn { Role = "user", Content = "q" }, new ChatTurn { Role = "assistant", Content = "older answer" } },
        };

        var cut = Render<AiChatPanel>();

        Assert.Contains("Assistant", cut.Find(".akml-chat-role-assistant .akml-chat-role-label").TextContent);
    }

    [Fact]
    public async Task Switching_agents_is_announced_once_and_never_saved()
    {
        var cut = Render<AiChatPanel>();

        await cut.Find("select[data-testid='chat-agent-select']").ChangeAsync(new ChangeEventArgs { Value = "a2" });

        var notice = Assert.Single(cut.FindAll("[data-testid='chat-notice']"));
        Assert.Equal($"Now answering with {GeminiLabel}.", notice.TextContent.Trim());
        Assert.Null(_history.Saved);   // a notice is not a turn
    }

    [Fact]
    public void Export_names_each_answer_by_its_label()
    {
        var conversation = new ChatConversation
        {
            Turns =
            {
                new ChatTurn { Role = "user", Content = "q" },
                new ChatTurn { Role = "assistant", Content = "a", AgentLabel = GeminiLabel },
                new ChatTurn { Role = "assistant", Content = "b" },
            },
        };

        var md = conversation.ToMarkdown();

        Assert.Contains($"## {GeminiLabel}", md);
        Assert.Contains("## Assistant", md);
        Assert.Contains("## You", md);
    }
}
