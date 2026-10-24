using System.Linq;
using AkmlSql.Core;
using AkmlSql.Formatting.Pipeline;
using AkmlSql.Formatting.Profiles;
using AkmlSql.Formatting.SqlPrompt;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SnippetsPage = AkmlSql.Web.Pages.Snippets;
using StylesPage = AkmlSql.Web.Pages.Styles;

namespace AkmlSql.Web.Tests.Shell;

/// <summary>
/// Spec 043 (T093, research R14, FR-036, FR-037). Every input that takes SQL text asks browser writing assistants to
/// stay out: Grammarly reads <c>data-gramm</c>, <c>data-gramm_editor</c> or <c>data-enable-grammarly</c> (it has
/// renamed the attribute over the years), LanguageTool reads <c>data-lt-active</c>. The chat composer takes natural
/// language and keeps the browser's assistance. The SQL editor itself is covered by akml-editor.attributes.test.mjs.
/// </summary>
public sealed class WritingAssistantOptOutTests : BunitContext
{
    private static readonly string[] OptOut = { "data-gramm", "data-gramm_editor", "data-enable-grammarly", "data-lt-active" };

    public WritingAssistantOptOutTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    private static void AssertOptedOut(IElement textarea)
    {
        foreach (var name in OptOut) Assert.Equal("false", textarea.GetAttribute(name));
        Assert.Equal("false", textarea.GetAttribute("spellcheck"));
        // FR-036: no auto-correction or auto-capitalisation either (mobile keyboards, Safari), as the editor's content has.
        Assert.Equal("off", textarea.GetAttribute("autocorrect"));
        Assert.Equal("off", textarea.GetAttribute("autocapitalize"));
        Assert.Equal("false", textarea.GetAttribute("writingsuggestions"));
        Assert.Equal("no", textarea.GetAttribute("translate"));
    }

    [Fact]
    public void The_snippet_body_opts_out()
    {
        var adapter = new InMemoryIndexedDbAdapter();
        Services.AddSingleton<ISnippetStore>(new SnippetStore(adapter));
        Services.AddSingleton<IDiagnosticsRingBuffer>(new DiagnosticsRingBuffer(adapter));
        Services.AddSingleton<IDialogService>(new FakeDialogService());

        var page = Render<SnippetsPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=snippet-row]")));
        page.Find("[data-testid=snippet-new]").Click();

        page.WaitForAssertion(() => AssertOptedOut(page.Find("#akml-snippet-body")));
    }

    [Fact]
    public void The_format_styles_input_opts_out()
    {
        var store = new ProfileStore(new InMemoryIndexedDbAdapter());
        store.SaveDocumentAsync(null, SqlPromptStyleDocument.CreateDefault("SQL Prompt defaults")).GetAwaiter().GetResult();
        Services.AddSingleton<IProfileStore>(store);
        Services.AddSingleton<IDialectGate>(new SqlServerGate());
        Services.AddSingleton<IFormatterService>(new FormatterService(new SqlServerGate(), new NoEngine()));
        Services.AddSingleton<IDialogService>(new FakeDialogService());

        var page = Render<StylesPage>();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=style-row]")));
        page.FindAll("[data-testid=style-row]").First().Click();
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll("[data-testid=preview-mysql]")));
        page.Find("[data-testid=preview-mysql]").Click();

        page.WaitForAssertion(() => AssertOptedOut(page.Find("[data-testid=preview-input]")));
    }

    [Fact]
    public void The_chat_composer_keeps_the_browser_assistance()
    {
        Services.AddSingleton<IAiClientFactory>(new FakeAiClientFactory());
        Services.AddSingleton<IAiPreference>(new FakeAiPreference("anthropic"));
        Services.AddSingleton<IAiFeatureSettings>(new FakeAiFeatureSettings());
        Services.AddSingleton<IChatHistoryStore>(new FakeChatHistoryStore());
        Services.AddSingleton<IDiagnosticsRingBuffer>(new FakeDiagnosticsRingBuffer());
        Services.AddSingleton<INotificationService>(new FakeNotificationService());

        var composer = Render<AiChatPanel>().Find("textarea");

        foreach (var name in OptOut) Assert.Null(composer.GetAttribute(name));
    }

    private sealed class SqlServerGate : IDialectGate
    {
        public DatabaseKind Kind => DatabaseKind.SqlServer;
        public bool IsPostgres => false;
    }

    private sealed class NoEngine : IPostgresFormatEngine
    {
        public System.Threading.Tasks.Task<FormatResult> FormatAsync(string sql, FormattingProfile profile, System.Threading.CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult(new FormatResult { Success = false });

        public System.Threading.Tasks.Task<FormatResult> PreviewAsync(string sample, FormattingProfile profile, System.Threading.CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult(new FormatResult { Success = false });
    }
}
