using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests;

/// <summary>
/// Web AI chat composer regression tests. The composer must react to <b>typing</b> — the
/// browser only fires <c>change</c> on blur, so a default <c>@bind</c> leaves the bound
/// field empty while the user types. That kept the Send button permanently disabled and made
/// Ctrl+Enter a silent no-op (its guard saw an empty prompt): the chat could not be used at
/// all from the keyboard. The textarea must bind on <c>oninput</c>.
/// </summary>
public sealed class AiChatPanelComposerTests : BunitContext
{
    private readonly FakeAiClientFactory _client = new();

    public AiChatPanelComposerTests()
    {
        Services.AddSingleton<IAiClientFactory>(_client);
        Services.AddSingleton<IAiPreference>(new FakeAiPreference("anthropic"));
        Services.AddSingleton<IAiFeatureSettings>(new FakeAiFeatureSettings());
        Services.AddSingleton<IChatHistoryStore>(new FakeChatHistoryStore());
        Services.AddSingleton<IDiagnosticsRingBuffer>(new FakeDiagnosticsRingBuffer());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());
    }

    private static string SendButtonMarkup(IRenderedComponent<AiChatPanel> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Send").OuterHtml;

    [Fact]
    public void Typing_enables_send_without_blur()
    {
        var cut = Render<AiChatPanel>();
        Assert.Contains("disabled", SendButtonMarkup(cut));

        cut.Find("textarea").Input("What does my query do?");

        Assert.DoesNotContain("disabled", SendButtonMarkup(cut));
    }

    [Fact]
    public void Whitespace_only_input_keeps_send_disabled()
    {
        var cut = Render<AiChatPanel>();

        cut.Find("textarea").Input("   ");

        Assert.Contains("disabled", SendButtonMarkup(cut));
    }

    [Fact]
    public void CtrlEnter_sends_the_text_typed_so_far()
    {
        var cut = Render<AiChatPanel>();
        var textarea = cut.Find("textarea");

        textarea.Input("Explain sys.databases");
        textarea.KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Explain sys.databases", Assert.Single(_client.Requests).UserPrompt);
            Assert.Contains("It lists databases.", cut.Markup);
        });
    }

    [Fact]
    public void Send_click_sends_and_renders_the_reply()
    {
        var cut = Render<AiChatPanel>();

        cut.Find("textarea").Input("Explain sys.databases");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Send").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Single(_client.Requests);
            Assert.Contains("It lists databases.", cut.Markup);
        });
    }

    // ── Spec 046 (US3, FR-011 – FR-013): the composer explains itself ─────────────────────────

    private static string SendTitle(IRenderedComponent<AiChatPanel> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Send").GetAttribute("title") ?? "";

    [Fact]
    public void Send_names_its_shortcut_when_it_can_send_and_says_why_when_it_cannot()
    {
        var cut = Render<AiChatPanel>();
        Assert.Equal("Type a question first", SendTitle(cut));

        cut.Find("textarea").Input("What does my query do?");

        Assert.Equal("Send (Ctrl+Enter)", SendTitle(cut));
    }

    [Fact]
    public void The_placeholder_names_both_send_keys_and_the_new_line_key()
    {
        var cut = Render<AiChatPanel>();

        Assert.Equal("Ask anything about your SQL…   (Enter or Ctrl+Enter sends · Shift+Enter for a new line)",
            cut.Find("textarea").GetAttribute("placeholder"));
    }

    [Fact]
    public void Export_explains_itself()
    {
        Services.AddSingleton<IChatHistoryStore>(new FakeChatHistoryStore
        {
            Saved = new ChatConversation { Id = "c1", Turns = { new ChatTurn { Role = "user", Content = "q" } } },
        });
        var cut = Render<AiChatPanel>();

        var export = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Export");
        Assert.Equal("Download the conversation as Markdown", export.GetAttribute("title"));
    }

    // ── Spec 046 (US5, FR-040 – FR-043): New chat ───────────────────────────────────────────

    [Fact]
    public void New_chat_replaces_clear_and_is_disabled_while_empty()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = Render<AiChatPanel>();

        var newChat = cut.Find("button[data-testid='chat-new']");
        Assert.True(newChat.HasAttribute("disabled"));
        Assert.Equal("The conversation is already empty", newChat.GetAttribute("title"));
        Assert.DoesNotContain(cut.FindAll("button"), b => b.TextContent.Trim() == "Clear");
    }

    [Fact]
    public void New_chat_empties_the_stored_conversation_and_shows_the_empty_state()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var history = new FakeChatHistoryStore
        {
            Saved = new ChatConversation
            {
                Id = "c1",
                Turns = { new ChatTurn { Role = "user", Content = "q" }, new ChatTurn { Role = "assistant", Content = "a" } },
            },
        };
        Services.AddSingleton<IChatHistoryStore>(history);
        var cut = Render<AiChatPanel>();
        var newChat = cut.Find("button[data-testid='chat-new']");
        Assert.Equal("Start a new conversation — clears this one", newChat.GetAttribute("title"));

        newChat.Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Null(history.Saved);
            Assert.NotNull(cut.Find("[data-testid=chat-empty]"));
            Assert.Empty(cut.FindAll(".akml-chat-turn"));
        });
    }

    [Fact]
    public void New_chat_during_an_answer_stops_it_and_starts_clean()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var hanging = new HangingAiClientFactory();
        Services.AddSingleton<IAiClientFactory>(hanging);
        var cut = Render<AiChatPanel>();

        cut.Find("textarea").Input("long question");
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Send").Click();
        // The panel throttles token renders (60 ms), so wait for the working state, not the token.
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".akml-chat-thinking")));

        cut.Find("button[data-testid='chat-new']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.True(hanging.Cancelled, "the answer in flight is stopped");
            Assert.Empty(cut.FindAll(".akml-chat-turn"));
            Assert.Empty(cut.FindAll(".akml-chat-thinking"));
            Assert.Equal(string.Empty, cut.Find("textarea").GetAttribute("value") ?? string.Empty);
        });
    }

    /// <summary>Streams one token, then waits for cancellation (an answer that would never finish).</summary>
    private sealed class HangingAiClientFactory : IAiClientFactory
    {
        public bool Cancelled { get; private set; }

        public Task<string> SendAsync(string providerId, AiChatRequest request, CancellationToken ct)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<string> StreamAsync(
            string providerId, AiChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return "partial";
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }
        }

        public bool IsOriginAllowed(string providerId, string origin) => true;
        public bool IsBrowserDirectCapable(string providerId) => true;
    }

    // ── Spec 046 (US6, FR-023): starter questions in the empty state ─────────────────────────

    [Fact]
    public void An_empty_conversation_offers_three_starters_that_send()
    {
        var cut = Render<AiChatPanel>();

        var starters = cut.FindAll("[data-testid='chat-starter']");
        Assert.Equal(new[]
        {
            "What are the largest tables in this database?",
            "Which tables have no primary key?",
            "Which stored procedures were modified most recently?",
        }, starters.Select(b => b.TextContent.Trim()));
        Assert.All(starters, b => Assert.Equal("Send this question", b.GetAttribute("title")));

        starters[1].Click();

        cut.WaitForAssertion(() => Assert.Equal("Which tables have no primary key?", Assert.Single(_client.Requests).UserPrompt));
    }

    // Spec 041 (T205, FR-088) — an empty conversation is the shared empty state, saying what to do.
    [Fact]
    public void An_empty_conversation_says_what_to_do()
    {
        var cut = Render<AiChatPanel>();

        var empty = cut.WaitForElement("[data-testid=chat-empty]");
        Assert.Contains("akml-empty", empty.ClassList);
        Assert.Contains("Type a question", empty.TextContent);
    }
}
