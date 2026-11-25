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
/// Spec 047 (AI-002, contracts/workspace-ai.md §6) — Fix needs the error the server returned: without one the button is
/// disabled and says why, and no Fix request is ever built with the "(no error)" placeholder. With an error it sends that
/// error. Before, Fix asked the AI to fix "(no error)" — a request it can only guess at.
/// </summary>
public sealed class AiPanelFixWithoutErrorTests
{
    private const string NeedsError = "Run the query first: Fix needs the error the server returned.";

    private sealed class RecordingPrompts : IAiPromptService
    {
        public List<(string Sql, string Error, int Number)> Fixes { get; } = new();
        public Task<string> ExplainAsync(string s, CancellationToken ct) => Task.FromResult("");
        public Task<string> FixAsync(string s, string e, int n, CancellationToken ct) => Task.FromResult("");
        public Task<string> OptimizeAsync(string s, CancellationToken ct) => Task.FromResult("");
        public Task<string> TextToSqlAsync(string s, CancellationToken ct) => Task.FromResult("");
        public Task<string> IndexAnalysisAsync(string s, CancellationToken ct) => Task.FromResult("");
        public IAsyncEnumerable<string> ExplainStreamAsync(string s, CancellationToken ct) => Empty();
        public IAsyncEnumerable<string> FixStreamAsync(string s, string e, int n, CancellationToken ct)
        {
            Fixes.Add((s, e, n));
            return Empty();
        }
        public IAsyncEnumerable<string> OptimizeStreamAsync(string s, CancellationToken ct) => Empty();
        public IAsyncEnumerable<string> TextToSqlStreamAsync(string s, CancellationToken ct) => Empty();
        public IAsyncEnumerable<string> IndexAnalysisStreamAsync(string s, CancellationToken ct) => Empty();
#pragma warning disable CS1998
        private static async IAsyncEnumerable<string> Empty() { yield break; }
#pragma warning restore CS1998
    }

    private sealed class Preference : IAiPreference
    {
        public Task<string> GetActiveAsync() => Task.FromResult("agent-1");
        public Task SetActiveAsync(string providerId) => Task.CompletedTask;
        public Task<string> GetActiveProviderAsync() => Task.FromResult("ollama");
    }

    private sealed class Client : IAiClientFactory
    {
        public Task<string> SendAsync(string providerId, AiChatRequest request, CancellationToken ct) => Task.FromResult("");
#pragma warning disable CS1998
        public async IAsyncEnumerable<string> StreamAsync(string providerId, AiChatRequest request, [EnumeratorCancellation] CancellationToken ct) { yield break; }
#pragma warning restore CS1998
        public bool IsOriginAllowed(string providerId, string origin) => true;
        public bool IsBrowserDirectCapable(string providerId) => true;
    }

    private sealed class Settings : IAiFeatureSettings
    {
        public Task<AiFeatureSettings> GetAsync() => Task.FromResult(new AiFeatureSettings());
        public Task SetAsync(AiFeatureSettings settings) => Task.CompletedTask;
        public Task<AiPrivacyMode> ResolveModeAsync(string featureId) => Task.FromResult(AiPrivacyMode.FullSchema);
        public Task ResetAsync() => Task.CompletedTask;
        public Task ReloadAsync() => Task.CompletedTask;
    }

    private sealed class Diagnostics : IDiagnosticsRingBuffer
    {
        public void Log(DiagnosticLevel level, string category, string message, object? data = null) { }
        public IReadOnlyList<DiagnosticEntry> Snapshot() => System.Array.Empty<DiagnosticEntry>();
        public Task FlushAsync() => Task.CompletedTask;
        public Task RestoreAsync() => Task.CompletedTask;
        public void Clear() { }
    }

    private static (BunitContext Ctx, RecordingPrompts Prompts) NewCtx()
    {
        var ctx = new BunitContext();
        var prompts = new RecordingPrompts();
        ctx.Services.AddSingleton<IAiPromptService>(prompts);
        ctx.Services.AddSingleton<IAiPreference>(new Preference());
        ctx.Services.AddSingleton<IAiClientFactory>(new Client());
        ctx.Services.AddSingleton<IAiFeatureSettings>(new Settings());
        ctx.Services.AddSingleton<IDiagnosticsRingBuffer>(new Diagnostics());
        return (ctx, prompts);
    }

    [Fact]
    public void Without_an_error_Fix_is_disabled_says_why_and_sends_nothing()
    {
        var (ctx, prompts) = NewCtx();
        using var _ = ctx;
        var cut = ctx.Render<AiPanel>(p => p
            .Add(x => x.SelectedSql, "SELEC 1")
            .Add(x => x.LastErrorProvider, () => null));

        var fix = cut.Find("[data-testid=ai-fix]");
        Assert.True(fix.HasAttribute("disabled"));
        Assert.Equal(NeedsError, fix.GetAttribute("title"));

        fix.Click();   // a disabled button sends nothing (bUnit still runs the handler, so the panel must refuse too)
        Assert.Empty(prompts.Fixes);
    }

    [Fact]
    public void Without_an_error_provider_at_all_Fix_is_disabled_too()
    {
        var (ctx, prompts) = NewCtx();
        using var _ = ctx;
        var cut = ctx.Render<AiPanel>(p => p.Add(x => x.SelectedSql, "SELEC 1"));

        Assert.True(cut.Find("[data-testid=ai-fix]").HasAttribute("disabled"));
        Assert.Empty(prompts.Fixes);
    }

    [Fact]
    public void With_the_servers_error_Fix_sends_it()
    {
        var (ctx, prompts) = NewCtx();
        using var _ = ctx;
        var cut = ctx.Render<AiPanel>(p => p
            .Add(x => x.SelectedSql, "SELEC 1")
            .Add(x => x.LastErrorProvider, () => new AiErrorInfo("Incorrect syntax near 'SELEC'.", 102)));

        var fix = cut.Find("[data-testid=ai-fix]");
        Assert.False(fix.HasAttribute("disabled"));
        fix.Click();

        cut.WaitForAssertion(() => Assert.Single(prompts.Fixes));
        Assert.Equal("Incorrect syntax near 'SELEC'.", prompts.Fixes[0].Error);
        Assert.Equal(102, prompts.Fixes[0].Number);
        Assert.DoesNotContain("(no error)", prompts.Fixes[0].Error);
    }
}
