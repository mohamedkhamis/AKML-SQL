using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Parser;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Analysis;

/// <summary>
/// Spec 027 (M5 offline closure) T027 / US4. Two scopes (Decision 4):
///   * Line  — the browser emits the shared <c>-- akml-disable-line RULEID</c> directive (the
///     documented form, which the WPF shell also emits); this test proves that exact string is
///     honoured by the real <c>AkmlSql.Analysis.SuppressionParser</c> (cross-surface format
///     contract, FR-019/FR-022). The original <c>-- noqa:</c> form still parses, and is covered
///     below so scripts written before the change keep working.
///   * Global — a browser-local per-rule override; this test proves the T024
///     <see cref="AnalyserService"/> bugfix actually applies it (FR-020/FR-021), where
///     previously the override was persisted but ignored.
/// </summary>
public sealed class SuppressionEditTests
{
    // ---- Line scope: the -- akml-disable-line RULEID directive is the shared format ----

    [Fact]
    public void Line_directive_in_shared_akml_form_is_parsed_by_the_engine_parser()
    {
        // The exact string the browser appends at the finding's line end.
        const string ruleId = "PE001";
        var line = $"SELECT * FROM dbo.Orders; -- akml-disable-line {ruleId}";

        var parser = new TsqlParserService();
        var tokens = parser.GetTokenStream(line);
        var map = SuppressionParser.Parse(tokens, out _);

        // SuppressionParser records the suppression on line 1 for PE001.
        Assert.True(map.IsSuppressed(1, ruleId));
        // A different rule on the same line is NOT suppressed (per-rule, not blanket).
        Assert.False(map.IsSuppressed(1, "ST001"));
    }

    [Fact]
    public void Line_directive_does_not_suppress_the_rule_on_other_lines()
    {
        const string sql = "SELECT * FROM dbo.A; -- akml-disable-line PE001\nSELECT * FROM dbo.B;";
        var parser = new TsqlParserService();
        var map = SuppressionParser.Parse(parser.GetTokenStream(sql), out _);

        Assert.True(map.IsSuppressed(1, "PE001"));   // suppressed where the directive is
        Assert.False(map.IsSuppressed(2, "PE001"));  // still fires elsewhere
    }

    [Fact]
    public void The_original_noqa_form_still_parses_so_existing_scripts_keep_working()
    {
        const string sql = "SELECT * FROM dbo.A; -- noqa: PE001\nSELECT * FROM dbo.B;";
        var parser = new TsqlParserService();
        var map = SuppressionParser.Parse(parser.GetTokenStream(sql), out _);

        Assert.True(map.IsSuppressed(1, "PE001"));
        Assert.False(map.IsSuppressed(2, "PE001"));
    }

    // ---- Global scope: the T024 bugfix makes the browser-local override apply ----

    // PE001 (avoid SELECT *) fires for SELECT * INSIDE a stored procedure, not for a bare
    // SELECT — matching the existing AnalyserServiceTests fixture.
    private const string SelectStarProc =
        "CREATE PROCEDURE dbo.GetCustomers AS\nBEGIN\n    SELECT * FROM dbo.Customers;\nEND;";

    [Fact]
    public async Task Global_override_off_suppresses_the_rule_after_the_bugfix()
    {
        // With no override, the PE001 finding appears.
        var withoutOverride = new AnalyserService(new FakeSettingsStore(new Dictionary<string, string>()));
        var baseline = await withoutOverride.AnalyseAsync(SelectStarProc);
        Assert.Contains(baseline.Issues, i => i.RuleId == "PE001");

        // With PE001 -> "off", the T024 post-pass drops it.
        var withOverride = new AnalyserService(
            new FakeSettingsStore(new Dictionary<string, string> { ["PE001"] = "off" }));
        var suppressed = await withOverride.AnalyseAsync(SelectStarProc);
        Assert.DoesNotContain(suppressed.Issues, i => i.RuleId == "PE001");
    }

    [Fact]
    public async Task No_store_wired_leaves_findings_untouched()
    {
        // The parameterless ctor (used by existing tests) applies no overrides.
        var svc = new AnalyserService();
        var result = await svc.AnalyseAsync(SelectStarProc);
        Assert.Contains(result.Issues, i => i.RuleId == "PE001");
    }

    private sealed class FakeSettingsStore : IAnalysisSettingsStore
    {
        private readonly WebAnalysisSettings _settings;
        public FakeSettingsStore(Dictionary<string, string> overrides)
            => _settings = new WebAnalysisSettings { RuleOverrides = overrides };
        public Task<WebAnalysisSettings> GetAsync() => Task.FromResult(_settings);
        public Task SetAsync(WebAnalysisSettings settings) => Task.CompletedTask;
    }
}
