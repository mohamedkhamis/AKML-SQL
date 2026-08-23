using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests;

/// <summary>
/// Per-message copy affordance in the AI chat log: every non-empty turn (user and assistant)
/// carries a small copy button that puts that turn's text on the clipboard via
/// <c>navigator.clipboard.writeText</c> (the History page idiom) and shows a transient
/// "Copied" indicator. Empty turns (the placeholder an in-flight stream renders into) get none.
/// </summary>
public sealed class AiChatPanelCopyTests : TestContext
{
    private readonly FakeChatHistoryStore _history = new();

    public AiChatPanelCopyTests()
    {
        Services.AddSingleton<IAiClientFactory>(new FakeAiClientFactory());
        Services.AddSingleton<IAiPreference>(new FakeAiPreference("anthropic"));
        Services.AddSingleton<IAiFeatureSettings>(new FakeAiFeatureSettings());
        Services.AddSingleton<IChatHistoryStore>(_history);
        Services.AddSingleton<IDiagnosticsRingBuffer>(new FakeDiagnosticsRingBuffer());
        JSInterop.Mode = JSRuntimeMode.Loose;

        _history.Saved = new ChatConversation
        {
            Id = "c1",
            Title = "conversation",
            Turns =
            {
                new ChatTurn { Role = "user", Content = "How do I count rows?" },
                new ChatTurn { Role = "assistant", Content = "Use COUNT(*)." },
                new ChatTurn { Role = "assistant", Content = "" },   // in-flight placeholder
            },
        };
    }

    [Fact]
    public void Every_nonEmpty_turn_gets_a_copy_button()
    {
        var cut = RenderComponent<AiChatPanel>();

        Assert.Equal(2, cut.FindAll("button[aria-label='Copy message']").Count);
    }

    [Fact]
    public void Clicking_copy_puts_that_turns_text_on_the_clipboard()
    {
        var cut = RenderComponent<AiChatPanel>();

        cut.FindAll("button[aria-label='Copy message']")[1].Click();

        var call = Assert.Single(JSInterop.Invocations["navigator.clipboard.writeText"]);
        Assert.Equal("Use COUNT(*).", call.Arguments[0]);
    }

    [Fact]
    public void Copy_shows_a_transient_copied_indicator_on_that_turn_only()
    {
        var cut = RenderComponent<AiChatPanel>();

        cut.FindAll("button[aria-label='Copy message']")[0].Click();

        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll("button[aria-label='Copy message']");
            Assert.Contains("Copied", buttons[0].TextContent);
            Assert.DoesNotContain("Copied", buttons[1].TextContent);
        });
    }
}
