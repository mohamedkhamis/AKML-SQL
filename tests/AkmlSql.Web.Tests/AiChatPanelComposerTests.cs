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

}
