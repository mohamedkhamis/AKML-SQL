using System.Runtime.CompilerServices;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Re-review REV-07 (from the reviewer's acceptance probe, assertions kept): a partial answer stays marked incomplete — on the
/// turn itself, so after the panel is reopened, in the Markdown export, and after another message is sent. A complete answer
/// is never marked. An incomplete answer goes into later context with a note that it was cut off.
/// </summary>
public sealed class AiChatIncompleteTurnTests
{
    /// <summary>The first stream breaks off after a token; every later one completes and records what it was sent.</summary>
    private sealed class BreaksOnce : IAiClientFactory
    {
        private int _calls;
        public List<AiChatRequest> Requests { get; } = new();
        public Task<string> SendAsync(string id, AiChatRequest request, CancellationToken ct) => Task.FromResult("");
        public bool IsOriginAllowed(string id, string origin) => true;
        public bool IsBrowserDirectCapable(string id) => true;
        public async IAsyncEnumerable<string> StreamAsync(string id, AiChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Requests.Add(request);
            if (_calls++ == 0)
            {
                yield return "SELECT unfinished";
                await Task.Yield();
                throw new AiStreamException(AiStreamFailure.Truncated);
            }
            yield return "SELECT finished;";
        }
    }

    private static (BunitContext Context, BreaksOnce Client, ChatHistoryStore History) Host()
    {
        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        var client = new BreaksOnce();
        var history = new ChatHistoryStore(new InMemoryIndexedDbAdapter());
        context.Services.AddSingleton<IAiClientFactory>(client);
        context.Services.AddSingleton<IAiPreference>(new FakeAiPreference("anthropic"));
        context.Services.AddSingleton<IAiFeatureSettings>(new FakeAiFeatureSettings());
        context.Services.AddSingleton<IChatHistoryStore>(history);
        context.Services.AddSingleton<IDiagnosticsRingBuffer>(new FakeDiagnosticsRingBuffer());
        context.Services.AddSingleton<INotificationService>(new FakeNotificationService());
        return (context, client, history);
    }

    private static void Send(IRenderedComponent<AiChatPanel> panel, string text)
    {
        panel.Find("textarea").Input(text);
        panel.Find(".akml-chat-send").Click();
    }

    [Fact]
    public void Partial_AI_answer_must_stay_marked_incomplete_after_reopening()
    {
        var (context, _, _) = Host();
        using var _ = context;
        var panel = context.Render<AiChatPanel>();
        Send(panel, "Generate SQL");
        panel.WaitForAssertion(() => Assert.Contains("incomplete", panel.Find("[data-testid=ai-chat-error]").TextContent));
        panel.Dispose();
        var reopened = context.Render<AiChatPanel>();
        reopened.WaitForAssertion(() => Assert.Contains("SELECT unfinished", reopened.Markup));
        Assert.Contains("incomplete", reopened.Markup);
    }

    [Fact]
    public async Task The_mark_survives_the_next_message_and_the_export_and_a_complete_answer_is_not_marked()
    {
        var (context, client, history) = Host();
        using var _ = context;
        var panel = context.Render<AiChatPanel>();
        Send(panel, "Generate SQL");
        panel.WaitForAssertion(() => Assert.Single(panel.FindAll("[data-testid=ai-chat-incomplete]")));

        Send(panel, "Try something else");
        panel.WaitForAssertion(() => Assert.Contains("SELECT finished;", panel.Markup));

        Assert.Single(panel.FindAll("[data-testid=ai-chat-incomplete]"));     // the first answer only
        var saved = (await history.GetAsync())!;
        Assert.NotNull(saved.Turns[1].IncompleteReason);
        Assert.Null(saved.Turns[3].IncompleteReason);
        Assert.Contains("This answer is incomplete", saved.ToMarkdown());

        // The cut-off answer went into the next request's context with a note, not as a finished answer.
        var context2 = client.Requests[1].History;
        Assert.Contains(context2, m => m.Role == "assistant" && m.Content.Contains("SELECT unfinished") && m.Content.Contains("cut off"));
    }

    [Fact]
    public async Task Retry_replaces_the_incomplete_turn_with_a_complete_one()
    {
        var (context, _, history) = Host();
        using var _ = context;
        var panel = context.Render<AiChatPanel>();
        Send(panel, "Generate SQL");
        panel.WaitForElement("[data-testid=ai-chat-retry]").Click();

        panel.WaitForAssertion(() => Assert.Contains("SELECT finished;", panel.Markup));
        Assert.Empty(panel.FindAll("[data-testid=ai-chat-incomplete]"));
        var saved = (await history.GetAsync())!;
        Assert.Equal(2, saved.Turns.Count);
        Assert.Null(saved.Turns[1].IncompleteReason);
    }
}
