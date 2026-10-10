using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests;

/// <summary>
/// Per-message copy affordance in the AI chat log: every non-empty turn (user and assistant)
/// carries a small copy button that puts that turn's text on the clipboard via
/// <c>akml-ui.js copyText</c> (spec 041 T201: it also works on the http LAN deployment, where
/// <c>navigator.clipboard</c> does not exist) and shows a transient "Copied" indicator; a copy that fails says
/// so in a notification. Empty turns (the placeholder an in-flight stream renders into) get none.
/// </summary>
public sealed class AiChatPanelCopyTests : BunitContext
{
    private readonly FakeChatHistoryStore _history = new();
    private readonly FakeNotificationService _notes = new();

    private BunitJSModuleInterop CopyAnswers(bool copied)
    {
        var ui = JSInterop.SetupModule("./js/akml-ui.js");
        ui.Mode = JSRuntimeMode.Loose;
        ui.Setup<bool>("copyText", _ => true).SetResult(copied);
        return ui;
    }

    public AiChatPanelCopyTests()
    {
        Services.AddSingleton<IAiClientFactory>(new FakeAiClientFactory());
        Services.AddSingleton<IAiPreference>(new FakeAiPreference("anthropic"));
        Services.AddSingleton<IAiFeatureSettings>(new FakeAiFeatureSettings());
        Services.AddSingleton<IChatHistoryStore>(_history);
        Services.AddSingleton<IDiagnosticsRingBuffer>(new FakeDiagnosticsRingBuffer());
        Services.AddSingleton<INotificationService>(_notes);
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
        var cut = Render<AiChatPanel>();

        Assert.Equal(2, cut.FindAll("button[aria-label='Copy message']").Count);
    }

    [Fact]
    public void Clicking_copy_puts_that_turns_text_on_the_clipboard()
    {
        var ui = CopyAnswers(true);
        var cut = Render<AiChatPanel>();

        cut.FindAll("button[aria-label='Copy message']")[1].Click();

        cut.WaitForAssertion(() => Assert.Equal("Use COUNT(*).", Assert.Single(ui.Invocations["copyText"]).Arguments[0]));
        Assert.DoesNotContain(JSInterop.Invocations, i => i.Identifier.StartsWith("navigator.clipboard", StringComparison.Ordinal));
        Assert.Empty(_notes.Sent);
    }

    [Fact]
    public void A_copy_that_fails_says_so_and_shows_no_copied_indicator()
    {
        CopyAnswers(false);
        var cut = Render<AiChatPanel>();

        cut.FindAll("button[aria-label='Copy message']")[0].Click();

        cut.WaitForAssertion(() => Assert.Contains(_notes.Sent, n => n.Kind == NotificationKind.Error && n.Text.Contains("Copy failed")));
        Assert.All(cut.FindAll("button[aria-label='Copy message']"), b => Assert.DoesNotContain("Copied", b.TextContent));
    }

    [Fact]
    public void Copy_shows_a_transient_copied_indicator_on_that_turn_only()
    {
        CopyAnswers(true);
        var cut = Render<AiChatPanel>();

        cut.FindAll("button[aria-label='Copy message']")[0].Click();

        cut.WaitForAssertion(() =>
        {
            var buttons = cut.FindAll("button[aria-label='Copy message']");
            Assert.Contains("Copied", buttons[0].TextContent);
            Assert.DoesNotContain("Copied", buttons[1].TextContent);
        });
    }
}
