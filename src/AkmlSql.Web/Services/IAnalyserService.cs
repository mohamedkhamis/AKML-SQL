using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;

namespace AkmlSql.Web.Services;

/// <summary>
/// Spec 021 (web edition) -- F1 follow-up enables this service. Wraps
/// <see cref="AnalysisEngine"/> from the extracted <c>AkmlSql.Analysis</c> library.
/// No IPC round-trip needed because both analyser engine and rule registry are pure
/// C# already running in the Blazor WASM process. Schema-aware rules (those with
/// <c>RequiresSchema = true</c>) need a <see cref="DatabaseCache"/> instance which
/// the web edition will source from its IndexedDB cache (M5 / T107).
/// </summary>
public interface IAnalyserService
{
    /// <summary>
    /// Run the analyser against <paramref name="documentText"/>. Returns the findings
    /// the IDE plugin would produce for the same input. Schema-dependent rules are
    /// silently skipped if <paramref name="schemaCache"/> is <c>null</c>.
    /// </summary>
    Task<CodeAnalysisResponse> AnalyseAsync(
        string documentText,
        DatabaseCache? schemaCache = null,
        CancellationToken ct = default);
}

internal sealed class AnalyserService : IAnalyserService
{
    private readonly AnalysisEngine _engine;
    private readonly CodeAnalysisSettings _settings;

    public AnalyserService()
    {
        var parser = new TsqlParserService();
        var registry = new RuleRegistry();
        var settingsLoader = new CaSettingsLoader();
        _engine = new AnalysisEngine(parser, registry, settingsLoader);

        // Default settings used until SettingsStore (T044) is wired -- analysis enabled,
        // every rule at its default severity. T044 will swap this for an IndexedDB-backed
        // store with per-rule overrides.
        _settings = new CodeAnalysisSettings { Enabled = true };
    }

    public Task<CodeAnalysisResponse> AnalyseAsync(
        string documentText,
        DatabaseCache? schemaCache = null,
        CancellationToken ct = default)
    {
        // Spec 021 FR-011 (T042): refuse documents larger than the 10 MB limit.
        DocumentSizeLimit.EnsureWithinLimit(documentText);

        var request = new CodeAnalysisRequest
        {
            SessionId = "web-session",
            RequestId = Guid.NewGuid().ToString(),
            DocumentText = documentText ?? string.Empty,
            DocumentVersion = 1,
        };
        return _engine.AnalyzeAsync(request, serverVersion: 16, schemaCache, _settings, ct);
    }
}
