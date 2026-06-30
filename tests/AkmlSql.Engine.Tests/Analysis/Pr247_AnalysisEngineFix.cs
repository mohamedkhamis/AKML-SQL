using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>
/// PR-247 regression test: after analysis settings change (rule disabled) and
/// <see cref="AnalysisEngine.ClearBatchCache"/> is called, the SAME batch text must no longer
/// return the now-disabled rule's diagnostic (before the fix, the stale cache entry was returned).
/// </summary>
public sealed class Pr247_AnalysisEngineFix
{
    // PE002 fires on unqualified table references (e.g. "SELECT 1 FROM Orders").
    // It is enabled by default and reliably fires on the SQL below without schema.
    private const string SqlWithPe002 = "SELECT Id FROM Orders";
    private const string TargetRule    = "PE002";

    private static CodeAnalysisSettings WithRuleDisabled(string ruleId)
        => new() { RuleOverrides = { [ruleId] = new RuleOverride { Enabled = false, Severity = "" } } };

    [Fact]
    public async Task DisabledRule_IsStillReturnedFromCache_WhenCacheNotCleared()
    {
        // Arrange: warm up the cache with the rule enabled (default settings).
        var parser   = new TsqlParserService();
        var registry = new RuleRegistry();
        using var loader = new CaSettingsLoader();
        var engine = new AnalysisEngine(parser, registry, loader);

        var request = new CodeAnalysisRequest
        {
            SessionId    = "pr247-test",
            RequestId    = "r1",
            DocumentText = SqlWithPe002,
        };

        var defaultSettings = new CodeAnalysisSettings { Enabled = true };
        var firstResult = await engine.AnalyzeAsync(
            request, serverVersion: 160, schemaCache: null, defaultSettings, CancellationToken.None);

        Assert.Contains(firstResult.Issues, i => i.RuleId == TargetRule);

        // Act: disable the rule and invalidate the CaSettingsLoader cache (as the real
        // AnalysisSettingsChanged callback does) but do NOT clear the AnalysisEngine batch cache.
        // This isolates the batch cache as the staleness source the fix targets.
        loader.InvalidateCache();
        var disabledSettings = WithRuleDisabled(TargetRule);
        var cachedResult = await engine.AnalyzeAsync(
            request, serverVersion: 160, schemaCache: null, disabledSettings, CancellationToken.None);

        // Assert: even with fresh settings, the stale batch-cache entry is still returned.
        // (This demonstrates the bug that existed before the fix.)
        Assert.Contains(cachedResult.Issues, i => i.RuleId == TargetRule);
    }

    [Fact]
    public async Task DisabledRule_IsAbsent_AfterClearBatchCacheCalledOnSettingsChange()
    {
        // Arrange: warm up the cache with the rule enabled (default settings).
        var parser   = new TsqlParserService();
        var registry = new RuleRegistry();
        using var loader = new CaSettingsLoader();
        var engine = new AnalysisEngine(parser, registry, loader);

        var request = new CodeAnalysisRequest
        {
            SessionId    = "pr247-test",
            RequestId    = "r1",
            DocumentText = SqlWithPe002,
        };

        var defaultSettings = new CodeAnalysisSettings { Enabled = true };
        var firstResult = await engine.AnalyzeAsync(
            request, serverVersion: 160, schemaCache: null, defaultSettings, CancellationToken.None);

        Assert.Contains(firstResult.Issues, i => i.RuleId == TargetRule);

        // Act: simulate the full AnalysisSettingsChanged callback — invalidate BOTH the
        // CaSettingsLoader cache (fresh settings) AND the AnalysisEngine batch cache (the fix).
        loader.InvalidateCache();
        engine.ClearBatchCache();

        var disabledSettings = WithRuleDisabled(TargetRule);
        var freshResult = await engine.AnalyzeAsync(
            request, serverVersion: 160, schemaCache: null, disabledSettings, CancellationToken.None);

        // Assert: the disabled rule must NOT appear in the fresh result.
        Assert.DoesNotContain(freshResult.Issues, i => i.RuleId == TargetRule);
    }
}
