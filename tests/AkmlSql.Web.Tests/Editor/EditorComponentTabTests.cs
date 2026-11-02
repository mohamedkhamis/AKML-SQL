using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Bridge;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Spec 043 (T042, research R6) — the one editor view shows one query tab at a time: switching hands the JS module the
/// tab id, seed text and language; calls that name another tab act on its kept state; a text report is filed under the
/// tab CodeMirror showed; and every language request is stamped with the shown tab's session when it starts.
/// </summary>
public sealed class EditorComponentTabTests : IDisposable
{
    private sealed class RecordingCompletion : ICompletionService
    {
        public List<string> SessionIds { get; } = new();

        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct, string? documentText = null)
        {
            SessionIds.Add(request.SessionId);
            return Task.FromResult(new CompletionResponse { Items = Array.Empty<CompletionItem>() });
        }
    }

    private sealed class NoGhostText : IAiGhostTextService
    {
        public Task<string?> CompleteAsync(string precedingText, CancellationToken ct) => Task.FromResult<string?>(null);
        public int SessionRequestCount => 0;
    }

    private sealed class DefaultAiFeatures : IAiFeatureSettings
    {
        public Task<AiFeatureSettings> GetAsync() => Task.FromResult(new AiFeatureSettings());
        public Task SetAsync(AiFeatureSettings settings) => Task.CompletedTask;
        public Task<AiPrivacyMode> ResolveModeAsync(string featureId) => Task.FromResult(default(AiPrivacyMode));
        public Task ResetAsync() => Task.CompletedTask;
        public Task ReloadAsync() => Task.CompletedTask;
    }

    private sealed class DefaultEditorSettings : IEditorSettingsStore
    {
        public Task<EditorSettings> GetAsync() => Task.FromResult(new EditorSettings());
        public Task SetAsync(EditorSettings settings) => Task.CompletedTask;
        public Task ResetAsync() => Task.CompletedTask;
        public Task ReloadAsync() => Task.CompletedTask;
        public event Action? Changed { add { } remove { } }
    }

    private sealed class RecordingQuickInfo : IQuickInfoService
    {
        public List<string> SessionIds { get; } = new();

        public Task<QuickInfoResponse> GetAsync(QuickInfoRequest request, CancellationToken ct)
        {
            SessionIds.Add(request.SessionId);
            return Task.FromResult(new QuickInfoResponse());
        }
    }

    private sealed class NoSignature : ISignatureHelpService
    {
        public Task<SignatureResponse> GetAsync(SignatureRequest request, CancellationToken ct) => Task.FromResult(new SignatureResponse());
    }

    private sealed class NoDefinition : IGotoDefinitionService
    {
        public Task<GetObjectDefinitionResponse?> GetAsync(GetObjectDefinitionRequest request, CancellationToken ct) =>
            Task.FromResult<GetObjectDefinitionResponse?>(null);
    }

    private sealed class NoWildcard : IWildcardExpansionService
    {
        public Task<WildcardExpansionResponse?> GetAsync(WildcardExpansionRequest request, CancellationToken ct) =>
            Task.FromResult<WildcardExpansionResponse?>(null);
    }

    private sealed class NoDiagnostics : IDiagnosticsRingBuffer
    {
        public void Log(DiagnosticLevel level, string source, string message, object? data = null) { }
        public IReadOnlyList<DiagnosticEntry> Snapshot() => Array.Empty<DiagnosticEntry>();
        public void Clear() { }
        public Task FlushAsync() => Task.CompletedTask;
        public Task RestoreAsync() => Task.CompletedTask;
    }

    private readonly BunitContext _ctx = new();
    private readonly FakeEngineBridge _bridge = new();
    private readonly SqlConnectionService _sql;
    private readonly RecordingCompletion _completion = new();
    private readonly RecordingQuickInfo _quickInfo = new();
    private readonly BunitJSModuleInterop _module;

    public EditorComponentTabTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _module = _ctx.JSInterop.SetupModule("./js/akml-editor.js");
        _bridge.SetState(BridgeState.Open);
        _sql = new SqlConnectionService(_bridge, new NoDiagnostics());
        _ctx.Services.AddSingleton<ISqlConnectionService>(_sql);
        _ctx.Services.AddSingleton<ICompletionService>(_completion);
        _ctx.Services.AddSingleton<IAiGhostTextService>(new NoGhostText());
        _ctx.Services.AddSingleton<IAiFeatureSettings>(new DefaultAiFeatures());
        _ctx.Services.AddSingleton<IEditorSettingsStore>(new DefaultEditorSettings());
        _ctx.Services.AddSingleton<IQuickInfoService>(_quickInfo);
        _ctx.Services.AddSingleton<ISignatureHelpService>(new NoSignature());
        _ctx.Services.AddSingleton<IGotoDefinitionService>(new NoDefinition());
        _ctx.Services.AddSingleton<IWildcardExpansionService>(new NoWildcard());
    }

    public void Dispose() => _ctx.Dispose();

    private IRenderedComponent<EditorComponent> RenderEditor(Action<EditorTabText>? onTabText = null) =>
        _ctx.Render<EditorComponent>(p =>
        {
            if (onTabText != null) p.Add(x => x.OnTabTextChanged, onTabText);
        });

    [Fact]
    public async Task The_DOM_names_a_tab_only_after_its_editor_state_has_been_applied()
    {
        var cut = RenderEditor();
        cut.WaitForAssertion(() => _module.VerifyInvoke("create"));
        Assert.Null(cut.Find("[data-testid='sql-editor']").GetAttribute("data-tab-id"));
        var apply = _module.SetupVoid("switchTab", _ => true);
        var switching = cut.InvokeAsync(() => cut.Instance.SwitchTabAsync("q2", "SELECT 2;", DatabaseKind.SqlServer));
        cut.WaitForAssertion(() => _module.VerifyInvoke("switchTab"));
        Assert.Null(cut.Find("[data-testid='sql-editor']").GetAttribute("data-tab-id"));
        apply.SetVoidResult();
        await switching;
        cut.WaitForAssertion(() => Assert.Equal("q2", cut.Find("[data-testid='sql-editor']").GetAttribute("data-tab-id")));
    }

    [Fact]
    public async Task Switching_hands_the_module_the_tab_its_seed_and_its_language_once()
    {
        var cut = RenderEditor();
        cut.WaitForAssertion(() => _module.VerifyInvoke("create"));

        await cut.InvokeAsync(() => cut.Instance.SwitchTabAsync("q2", "SELECT 2;", DatabaseKind.PostgreSql));
        await cut.InvokeAsync(() => cut.Instance.SwitchTabAsync("q2", "ignored", DatabaseKind.PostgreSql));

        var call = Assert.Single(_module.Invocations, i => i.Identifier == "switchTab");
        Assert.Equal("q2", call.Arguments[1]);
        Assert.Equal("SELECT 2;", call.Arguments[2]);
        Assert.Equal("postgresql", call.Arguments[3]);
        Assert.Equal("q2", cut.Instance.CurrentTabId);
    }

    [Fact]
    public async Task Calls_that_name_another_tab_pass_its_id_and_the_shown_tab_keeps_the_plain_call()
    {
        var cut = RenderEditor();
        cut.WaitForAssertion(() => _module.VerifyInvoke("create"));
        await cut.InvokeAsync(() => cut.Instance.SwitchTabAsync("q3", "SELECT 3;", DatabaseKind.SqlServer));   // q3 is kept from now on
        await cut.InvokeAsync(() => cut.Instance.SwitchTabAsync("q1", "", DatabaseKind.SqlServer));

        await cut.InvokeAsync(() => cut.Instance.SetTextAsync("SELECT 3;", "q3"));
        await cut.InvokeAsync(() => cut.Instance.SetTextAsync("SELECT 1;", "q1"));
        await cut.InvokeAsync(() => cut.Instance.ClearDiagnosticsAsync("q3"));
        await cut.InvokeAsync(() => cut.Instance.ReplaceTextIfUnchangedAsync("SELECT 3;", "SELECT  3;", "q3"));

        var setTexts = _module.Invocations.Where(i => i.Identifier == "setText").ToList();
        Assert.Equal(2, setTexts.Count);
        Assert.Equal(new object?[] { "SELECT 3;", "q3" }, setTexts[0].Arguments.Skip(1));
        Assert.Equal(new object?[] { "SELECT 1;" }, setTexts[1].Arguments.Skip(1));
        Assert.Equal("q3", Assert.Single(_module.Invocations, i => i.Identifier == "clearDiagnostics").Arguments[1]);
        Assert.Equal("q3", Assert.Single(_module.Invocations, i => i.Identifier == "replaceTextIfUnchanged").Arguments[3]);
    }

    /// <summary>
    /// Spec 043 (plan › Performance Goals, a swap under 200 ms for 10 MB): a tab the editor already holds comes back from its kept
    /// state, so its text is not sent across the interop boundary again; and a tab never shown is sent nothing — an empty state
    /// made for it would be picked over its text on its first show.
    /// </summary>
    [Fact]
    public async Task A_kept_tab_is_shown_again_without_its_text_and_a_tab_never_shown_is_sent_nothing()
    {
        var cut = RenderEditor();
        cut.WaitForAssertion(() => _module.VerifyInvoke("create"));
        await cut.InvokeAsync(() => cut.Instance.SwitchTabAsync("big", "SELECT 'large';", DatabaseKind.SqlServer));
        await cut.InvokeAsync(() => cut.Instance.SwitchTabAsync("small", "", DatabaseKind.SqlServer));
        await cut.InvokeAsync(() => cut.Instance.SwitchTabAsync("big", "SELECT 'large';", DatabaseKind.SqlServer));

        var switches = _module.Invocations.Where(i => i.Identifier == "switchTab").ToList();
        Assert.Equal(3, switches.Count);
        Assert.Equal("SELECT 'large';", switches[0].Arguments[2]);   // the first show seeds it
        Assert.Null(switches[2].Arguments[2]);                       // shown again from its kept state

        await cut.InvokeAsync(() => cut.Instance.SetDiagnosticsAsync(new[] { new EditorDiagnostic(1, 3, "x") }, "restored"));
        await cut.InvokeAsync(() => cut.Instance.ClearDiagnosticsAsync("restored"));
        await cut.InvokeAsync(() => cut.Instance.SetTextAsync("SELECT 7;", "restored"));
        Assert.False(await cut.InvokeAsync(() => cut.Instance.ReplaceTextIfUnchangedAsync("a", "b", "restored")));
        Assert.Equal(string.Empty, await cut.InvokeAsync(() => cut.Instance.GetTextAsync("restored")));
        Assert.DoesNotContain(_module.Invocations, i => i.Arguments.Contains("restored"));

        await cut.InvokeAsync(() => cut.Instance.CloseTabAsync("big"));
        await cut.InvokeAsync(() => cut.Instance.SwitchTabAsync("big", "SELECT 'reopened';", DatabaseKind.SqlServer));
        Assert.Equal("SELECT 'reopened';", _module.Invocations.Last(i => i.Identifier == "switchTab").Arguments[2]);   // closed: seeded again
    }

    [Fact]
    public async Task A_text_report_is_filed_under_the_tab_CodeMirror_showed()
    {
        var reports = new List<EditorTabText>();
        var cut = RenderEditor(reports.Add);
        cut.WaitForAssertion(() => _module.VerifyInvoke("create"));
        await cut.InvokeAsync(() => cut.Instance.SwitchTabAsync("q2", "", DatabaseKind.SqlServer));

        await cut.InvokeAsync(() => cut.Instance.OnTextChangedFromJs("typed in q1", "q1"));
        await cut.InvokeAsync(() => cut.Instance.OnTextChangedFromJs("no tab named", null));

        Assert.Equal(new[] { new EditorTabText("q1", "typed in q1"), new EditorTabText("q2", "no tab named") }, reports);
    }

    [Fact]
    public async Task Language_requests_carry_the_session_of_the_tab_shown_when_they_start()
    {
        var a = _sql.CreateSession();
        var b = _sql.CreateSession();
        Assert.True((await _sql.ConnectAsync(new ConnectionTarget(DatabaseKind.SqlServer, "localhost", "tempdb", WindowsAuth: true), a, CancellationToken.None)).Ok);
        var cut = RenderEditor();
        cut.WaitForAssertion(() => _module.VerifyInvoke("create"));

        _sql.Activate(a);
        await cut.InvokeAsync(() => cut.Instance.RequestCompletionsFromJs(6, "SELECT "));
        await cut.InvokeAsync(() => cut.Instance.RequestQuickInfoFromJs(1, "SELECT 1"));
        _sql.Activate(b);
        await cut.InvokeAsync(() => cut.Instance.RequestCompletionsFromJs(6, "SELECT "));

        Assert.Equal(new[] { a.SessionId, string.Empty }, _completion.SessionIds);
        Assert.Equal(a.SessionId, Assert.Single(_quickInfo.SessionIds));
        var documents = _bridge.Notifications.Where(n => n.Type == MessageTypes.DocumentChanged).Select(n => (DocumentChange)n.Payload).ToList();
        Assert.Equal(2, documents.Count);
        Assert.All(documents, d => Assert.Equal(a.SessionId, d.SessionId));   // b is not connected: nothing is sent for it
    }

    [Fact]
    public async Task A_switch_while_the_editor_is_being_created_is_shown_once_it_exists()
    {
        var create = _module.SetupVoid("create", _ => true);   // CodeMirror's creation, held open
        var cut = RenderEditor();
        cut.WaitForAssertion(() => _module.VerifyInvoke("create"));

        await cut.InvokeAsync(() => cut.Instance.SwitchTabAsync("q9", "SELECT 9;", DatabaseKind.SqlServer));
        Assert.DoesNotContain(_module.Invocations, i => i.Identifier == "switchTab");   // no editor to switch yet

        await cut.InvokeAsync(() => create.SetVoidResult());

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(_module.Invocations, i => i.Identifier == "switchTab");
            Assert.Equal("q9", call.Arguments[1]);
            Assert.Equal("SELECT 9;", call.Arguments[2]);
        });
        Assert.Equal("q9", cut.Instance.CurrentTabId);
    }
}
