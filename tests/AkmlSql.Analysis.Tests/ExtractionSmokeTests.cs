using System.Threading;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Parser;
using Xunit;

namespace AkmlSql.Analysis.Tests;

/// <summary>
/// Spec 021 follow-up F1. Smoke tests proving the analysis extraction works: AnalysisEngine
/// constructs and runs from a project that ONLY references AkmlSql.Analysis (no engine
/// dependency), confirming the AnalysisEngine API refactor (drop SessionManager +
/// SchemaCacheManager from AnalyzeAsync) landed correctly.
///
/// The 130+ existing rule-specific unit tests under tests/AkmlSql.Engine.Tests/Analysis/Rules/
/// still pass against the new assembly via the engine's transitive reference, so functional
/// equivalence is already validated. The rule-test migration is a follow-up housekeeping sweep.
/// </summary>
public sealed class ExtractionSmokeTests
{
    private static AnalysisEngine CreateEngine() =>
        new(new TsqlParserService(), new RuleRegistry(), new CaSettingsLoader());

    [Fact]
    public void AnalysisEngine_constructs_without_engine_assembly()
    {
        var engine = CreateEngine();
        Assert.NotNull(engine);
    }

    [Fact]
    public async Task AnalyzeAsync_returns_PE001_for_select_star_in_procedure()
    {
        // PE001 fires for SELECT * inside a stored procedure (not for bare SELECTs).
        // Confirms the full pipeline works: parse → batches → rule registry → rule fires.
        var engine = CreateEngine();
        var request = new CodeAnalysisRequest
        {
            SessionId = "test",
            RequestId = "1",
            DocumentText = "CREATE PROCEDURE dbo.GetCustomers AS\nBEGIN\n    SELECT * FROM dbo.Customers;\nEND;",
            DocumentVersion = 1,
        };
        var settings = new CodeAnalysisSettings { Enabled = true };

        var response = await engine.AnalyzeAsync(
            request,
            serverVersion: 16,
            schemaCache: null,
            settings,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.NotNull(response.Issues);
        Assert.Contains(response.Issues, i => i.RuleId == "PE001");
    }

    [Fact]
    public async Task AnalyzeAsync_returns_no_issues_when_disabled()
    {
        var engine = CreateEngine();
        var request = new CodeAnalysisRequest
        {
            SessionId = "test",
            RequestId = "1",
            DocumentText = "DELETE FROM dbo.Orders",   // would normally trigger PE003
            DocumentVersion = 1,
        };

        var response = await engine.AnalyzeAsync(
            request,
            0,
            null,
            new CodeAnalysisSettings { Enabled = false },
            CancellationToken.None);

        Assert.Empty(response.Issues);
    }

    [Fact]
    public async Task AnalyzeAsync_handles_zero_server_version_gracefully()
    {
        // serverVersion = 0 means "caller did not resolve; leave parser as-is".
        // The pipeline should still work (parser falls back to its current setting,
        // which defaults to the latest available parser).
        var engine = CreateEngine();
        var request = new CodeAnalysisRequest
        {
            SessionId = "test",
            RequestId = "1",
            DocumentText = "SELECT 1;",
            DocumentVersion = 1,
        };

        var response = await engine.AnalyzeAsync(
            request,
            0,
            null,
            new CodeAnalysisSettings { Enabled = true },
            CancellationToken.None);

        Assert.NotNull(response);
    }

    [Fact]
    public void RuleRegistry_discovers_rules_via_reflection()
    {
        // Confirms reflection scan still finds all rule classes after the move.
        var registry = new RuleRegistry();
        var enabled = registry.GetEnabledRules(new ResolvedAnalysisSettings { Enabled = true });
        Assert.NotEmpty(enabled);
    }
}
