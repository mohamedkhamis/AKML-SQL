using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
///
/// <para>
/// Spec 027 (M5 offline closure) T024 / FR-021: the analyser now honours the
/// browser-local per-rule overrides written by the Settings UI and by US4's "Suppress
/// globally" action. Previously those overrides were persisted but ignored (a latent
/// no-op). When an <see cref="IAnalysisSettingsStore"/> is supplied, each analysis pass
/// reads <c>RuleOverrides</c> and post-processes the findings: a rule mapped to "off" is
/// dropped; any other value remaps the finding's severity.
/// </para>
/// </summary>
public interface IAnalyserService
{
    /// <summary>
    /// Run the analyser against <paramref name="documentText"/>. Returns the findings
    /// the IDE plugin would produce for the same input, after applying the browser-local
    /// per-rule overrides. Schema-dependent rules are silently skipped if
    /// <paramref name="schemaCache"/> is <c>null</c>.
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
    private readonly IAnalysisSettingsStore? _settingsStore;

    // Severity codes matching CodeIssueInfo.Severity / ProblemsListComponent: 0=Hint,1=Info,2=Warning,3=Error.
    private const int SeverityHint = 0;
    private const int SeverityInfo = 1;
    private const int SeverityWarning = 2;
    private const int SeverityError = 3;

    /// <summary>
    /// Production-DI constructor. <paramref name="settingsStore"/> is optional and
    /// defaults to null so existing tests that construct <c>new AnalyserService()</c>
    /// keep working (no overrides applied). The DI container injects the registered
    /// <see cref="IAnalysisSettingsStore"/> in the running app.
    /// </summary>
    public AnalyserService(IAnalysisSettingsStore? settingsStore = null)
    {
        var parser = new TsqlParserService();
        var registry = new RuleRegistry();
        var settingsLoader = new CaSettingsLoader();
        _engine = new AnalysisEngine(parser, registry, settingsLoader);
        _settingsStore = settingsStore;

        // Engine-side settings stay at defaults (analysis enabled, default severities).
        // The browser-local per-rule overrides are applied as a post-pass below rather
        // than threaded into the engine's .casettings plumbing (the web edition does not
        // read .casettings — spec 027 Decision 4).
        _settings = new CodeAnalysisSettings { Enabled = true };
    }

    public async Task<CodeAnalysisResponse> AnalyseAsync(
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

        var response = await _engine.AnalyzeAsync(request, serverVersion: 16, schemaCache, _settings, ct)
            .ConfigureAwait(false);

        return await ApplyOverridesAsync(response).ConfigureAwait(false);
    }

    /// <summary>
    /// Spec 027 T024 / FR-021: project the browser-local <c>RuleOverrides</c> onto the
    /// findings. "off" drops the finding; "info"/"warning"/"error"/"hint" remaps its
    /// severity. No-op when no store is wired or no overrides exist.
    /// </summary>
    private async Task<CodeAnalysisResponse> ApplyOverridesAsync(CodeAnalysisResponse response)
    {
        if (_settingsStore == null || response.Issues == null || response.Issues.Length == 0)
            return response;

        var settings = await _settingsStore.GetAsync().ConfigureAwait(false);
        if (settings.RuleOverrides == null || settings.RuleOverrides.Count == 0)
            return response;

        // RuleOverrides round-trips through JSON (System.Text.Json rebuilds the dictionary
        // with the default ordinal comparer regardless of the property initializer), so build
        // a case-insensitive view once — a user-entered "pe001" still matches finding "PE001".
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in settings.RuleOverrides)
            overrides[kv.Key] = kv.Value;

        var kept = new List<CodeIssueInfo>(response.Issues.Length);
        foreach (var issue in response.Issues)
        {
            if (issue.RuleId != null && overrides.TryGetValue(issue.RuleId, out var value))
            {
                if (string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
                    continue;   // suppressed globally (browser-local)

                var remapped = MapSeverity(value);
                if (remapped.HasValue)
                    issue.Severity = remapped.Value;
            }
            kept.Add(issue);
        }

        response.Issues = kept.ToArray();
        return response;
    }

    private static int? MapSeverity(string value) => value?.ToLowerInvariant() switch
    {
        "hint" => SeverityHint,
        "info" or "information" => SeverityInfo,
        "warning" or "warn" => SeverityWarning,
        "error" => SeverityError,
        _ => null,   // unknown value -> leave severity unchanged
    };
}
