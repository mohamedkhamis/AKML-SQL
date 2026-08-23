using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Web.Services;
using AkmlSql.Web.Shared;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AkmlSql.Web.Tests.Ai;

/// <summary>
/// Spec 028 (M6) task T039 (US7; the deferred T134). bUnit render tests for AiPanel:
/// the no-provider prompt, the five actions, the OpenAI/Azure not-available notice, and that
/// no API key is ever present in the rendered DOM (FR-035).
/// </summary>
public sealed class AiPanelTests
{
    private static BunitContext NewCtx(string provider, bool capable)
    {
        var ctx = new BunitContext();
        ctx.Services.AddSingleton<IAiPromptService>(new StubPrompts());
        ctx.Services.AddSingleton<IAiPreference>(new StubPreference(provider));
        ctx.Services.AddSingleton<IAiClientFactory>(new StubClient(capable));
        ctx.Services.AddSingleton<IAiFeatureSettings>(new StubSettings());
        ctx.Services.AddSingleton<IDiagnosticsRingBuffer>(new StubDiagnostics());
        return ctx;
    }

    [Fact]
    public void NoProvider_ShowsAddPrompt()
    {
        using var ctx = NewCtx(provider: "", capable: true);
        var cut = ctx.Render<AiPanel>();
        Assert.Contains("No AI provider configured", cut.Markup);
        Assert.DoesNotContain("Explain</button>", cut.Markup);
    }

    [Fact]
    public void CapableProvider_ShowsFiveActions()
    {
        using var ctx = NewCtx(provider: "ollama", capable: true);
        var cut = ctx.Render<AiPanel>();
        Assert.Contains("Explain", cut.Markup);
        Assert.Contains("Fix", cut.Markup);
        Assert.Contains("Optimize", cut.Markup);
        Assert.Contains("NL → SQL", cut.Markup);
        Assert.Contains("Index Analysis", cut.Markup);
    }

    [Fact]
    public void CorsBlockedProvider_ShowsNoticeNotActions()
    {
        using var ctx = NewCtx(provider: "openai", capable: false);
        var cut = ctx.Render<AiPanel>();
        Assert.Contains("can't be used directly from the browser", cut.Markup);
        Assert.DoesNotContain("Index Analysis", cut.Markup);
    }

    [Fact]
    public void RenderedDom_NeverContainsAnApiKey()
    {
        using var ctx = NewCtx(provider: "ollama", capable: true);
        var cut = ctx.Render<AiPanel>();
        Assert.DoesNotContain("sk-", cut.Markup);   // AiPanel handles no key material at all
    }

    // ── Stubs ──────────────────────────────────────────────────────────────────────────

    private sealed class StubPrompts : IAiPromptService
    {
        public Task<string> ExplainAsync(string s, CancellationToken ct) => Task.FromResult("");
        public Task<string> FixAsync(string s, string e, int n, CancellationToken ct) => Task.FromResult("");
        public Task<string> OptimizeAsync(string s, CancellationToken ct) => Task.FromResult("");
        public Task<string> TextToSqlAsync(string s, CancellationToken ct) => Task.FromResult("");
        public Task<string> IndexAnalysisAsync(string s, CancellationToken ct) => Task.FromResult("");
        public IAsyncEnumerable<string> ExplainStreamAsync(string s, CancellationToken ct) => Empty();
        public IAsyncEnumerable<string> FixStreamAsync(string s, string e, int n, CancellationToken ct) => Empty();
        public IAsyncEnumerable<string> OptimizeStreamAsync(string s, CancellationToken ct) => Empty();
        public IAsyncEnumerable<string> TextToSqlStreamAsync(string s, CancellationToken ct) => Empty();
        public IAsyncEnumerable<string> IndexAnalysisStreamAsync(string s, CancellationToken ct) => Empty();
#pragma warning disable CS1998
        private static async IAsyncEnumerable<string> Empty() { yield break; }
#pragma warning restore CS1998
    }

    private sealed class StubPreference : IAiPreference
    {
        private readonly string _id;
        public StubPreference(string id) => _id = id;
        public Task<string> GetActiveAsync() => Task.FromResult(_id);
        public Task SetActiveAsync(string providerId) => Task.CompletedTask;
    }

    private sealed class StubClient : IAiClientFactory
    {
        private readonly bool _capable;
        public StubClient(bool capable) => _capable = capable;
        public Task<string> SendAsync(string providerId, AiChatRequest request, CancellationToken ct) => Task.FromResult("");
#pragma warning disable CS1998
        public async IAsyncEnumerable<string> StreamAsync(string providerId, AiChatRequest request, [EnumeratorCancellation] CancellationToken ct) { yield break; }
#pragma warning restore CS1998
        public bool IsOriginAllowed(string providerId, string origin) => true;
        public bool IsBrowserDirectCapable(string providerId) => _capable;
    }

    private sealed class StubSettings : IAiFeatureSettings
    {
        public Task<AiFeatureSettings> GetAsync() => Task.FromResult(new AiFeatureSettings());
        public Task SetAsync(AiFeatureSettings settings) => Task.CompletedTask;
        public Task<AiPrivacyMode> ResolveModeAsync(string featureId) => Task.FromResult(AiPrivacyMode.FullSchema);
    }

    private sealed class StubDiagnostics : IDiagnosticsRingBuffer
    {
        public void Log(DiagnosticLevel level, string category, string message, object? data = null) { }
        public IReadOnlyList<DiagnosticEntry> Snapshot() => System.Array.Empty<DiagnosticEntry>();
        public Task FlushAsync() => Task.CompletedTask;
        public Task RestoreAsync() => Task.CompletedTask;
        public void Clear() { }
    }
}
