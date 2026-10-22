using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using AkmlSql.Web.Tests.Bridge;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Editor;

/// <summary>
/// Spec 043 follow-up (review, finding 13) — the CodeMirror type each completion kind is handed to the editor as. CodeMirror
/// uses it for the badge's class (cm-completionIcon-{type}, styled in Pages/Editor.razor), so a view and a procedure get their
/// own badges ('V', 'P') instead of the table's and the function's; akml-editor.js branches on "snippet" alone.
/// </summary>
public sealed class EditorComponentCompletionTypeTests : IDisposable
{
    /// <summary>Answers every request with one item per <see cref="CompletionObjectType"/>, labelled with the kind's name.</summary>
    private sealed class EveryKindCompletion : ICompletionService
    {
        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct, string? documentText = null) =>
            Task.FromResult(new CompletionResponse
            {
                Items = Enum.GetValues<CompletionObjectType>()
                    .Select(kind => new CompletionItem { DisplayText = kind.ToString(), InsertText = kind.ToString(), ObjectType = (int)kind })
                    .ToArray(),
            });
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

    private sealed class NoQuickInfo : IQuickInfoService
    {
        public Task<QuickInfoResponse> GetAsync(QuickInfoRequest request, CancellationToken ct) => Task.FromResult(new QuickInfoResponse());
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

    public EditorComponentCompletionTypeTests()
    {
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.JSInterop.SetupModule("./js/akml-editor.js");
        _bridge.SetState(BridgeState.Open);
        _ctx.Services.AddSingleton<ISqlConnectionService>(new SqlConnectionService(_bridge, new NoDiagnostics()));
        _ctx.Services.AddSingleton<ICompletionService>(new EveryKindCompletion());
        _ctx.Services.AddSingleton<IAiGhostTextService>(new NoGhostText());
        _ctx.Services.AddSingleton<IAiFeatureSettings>(new DefaultAiFeatures());
        _ctx.Services.AddSingleton<IEditorSettingsStore>(new DefaultEditorSettings());
        _ctx.Services.AddSingleton<IQuickInfoService>(new NoQuickInfo());
        _ctx.Services.AddSingleton<ISignatureHelpService>(new NoSignature());
        _ctx.Services.AddSingleton<IGotoDefinitionService>(new NoDefinition());
        _ctx.Services.AddSingleton<IWildcardExpansionService>(new NoWildcard());
    }

    public void Dispose() => _ctx.Dispose();

    [Theory]
    [InlineData(CompletionObjectType.Table, "class")]
    [InlineData(CompletionObjectType.View, "view")]
    [InlineData(CompletionObjectType.Function, "function")]
    [InlineData(CompletionObjectType.Procedure, "procedure")]
    [InlineData(CompletionObjectType.Variable, "variable")]
    [InlineData(CompletionObjectType.Parameter, "variable")]
    [InlineData(CompletionObjectType.Column, "property")]
    [InlineData(CompletionObjectType.Snippet, "snippet")]
    public async Task A_completion_kind_reaches_the_editor_as_its_CodeMirror_type(CompletionObjectType kind, string expected)
    {
        var cut = _ctx.Render<EditorComponent>();

        var items = await cut.InvokeAsync(() => cut.Instance.RequestCompletionsFromJs(7, "SELECT "));

        Assert.Equal(expected, Assert.Single(items, i => i.Label == kind.ToString()).Type);
    }

    [Fact]
    public async Task A_view_and_a_procedure_no_longer_share_the_table_and_the_function_badges()
    {
        var cut = _ctx.Render<EditorComponent>();

        var types = (await cut.InvokeAsync(() => cut.Instance.RequestCompletionsFromJs(7, "SELECT ")))
            .ToDictionary(i => i.Label, i => i.Type);

        Assert.NotEqual(types[nameof(CompletionObjectType.Table)], types[nameof(CompletionObjectType.View)]);
        Assert.NotEqual(types[nameof(CompletionObjectType.Function)], types[nameof(CompletionObjectType.Procedure)]);
    }
}
