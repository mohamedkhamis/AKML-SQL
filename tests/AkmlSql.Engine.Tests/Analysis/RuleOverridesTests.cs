using System.Linq;
using System.Threading;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;
using AkmlSql.Engine.Handlers.Analysis;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Sessions;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Analysis;

/// <summary>
/// Spec 030 T053 — the global per-rule overrides (Manage Rules dialog → config.json
/// codeAnalysis.ruleOverrides) are honored by CaSettingsLoader and therefore reflected in
/// ResolvedAnalysisSettings and the ListAnalysisRules catalog.
/// </summary>
public sealed class RuleOverridesTests
{
    private static CodeAnalysisSettings WithOverride(string ruleId, bool enabled, string severity) =>
        new() { RuleOverrides = { [ruleId] = new RuleOverride { Enabled = enabled, Severity = severity } } };

    [Fact]
    public void Disabling_override_disables_the_rule()
    {
        using var loader = new CaSettingsLoader();
        var resolved = loader.Load(null, WithOverride("PE001", enabled: false, severity: ""));
        Assert.False(resolved.IsEnabled("PE001"));
    }

    [Fact]
    public void Severity_override_changes_effective_severity()
    {
        using var loader = new CaSettingsLoader();
        var resolved = loader.Load(null, WithOverride("PE001", enabled: true, severity: "error"));
        Assert.True(resolved.IsEnabled("PE001"));
        Assert.Equal(DiagnosticSeverity.Error, resolved.GetSeverity("PE001", DiagnosticSeverity.Hint));
    }

    [Fact]
    public void Ignore_severity_disables_the_rule()
    {
        using var loader = new CaSettingsLoader();
        var resolved = loader.Load(null, WithOverride("PE001", enabled: true, severity: "ignore"));
        Assert.False(resolved.IsEnabled("PE001"));
    }

    [Fact]
    public void No_overrides_leaves_defaults_intact()
    {
        using var loader = new CaSettingsLoader();
        var resolved = loader.Load(null, new CodeAnalysisSettings());
        Assert.True(resolved.IsEnabled("PE001"));                                   // default: enabled
        Assert.Equal(DiagnosticSeverity.Warning, resolved.GetSeverity("PE001", DiagnosticSeverity.Warning));
    }

    [Fact]
    public async System.Threading.Tasks.Task ListAnalysisRules_reflects_a_disabling_override()
    {
        var registry = new RuleRegistry();
        var settings = new AppSettings();
        settings.CodeAnalysis.RuleOverrides["PE002"] = new RuleOverride { Enabled = false, Severity = "" };

        var handler = new ListAnalysisRulesHandler(registry, new CaSettingsLoader(), () => settings);
        var ctx = new RpcContext
        {
            Sessions = new SessionManager(),
            SchemaCache = new SchemaCacheManager(),
            Logger = Log.Logger,
            SettingsLoader = () => settings,
        };

        var response = await handler.HandleAsync(new ListAnalysisRulesRequest(), ctx, CancellationToken.None);

        Assert.True(response.Success);
        var pe002 = Assert.Single(response.Rules, r => r.RuleId == "PE002");
        Assert.False(pe002.Enabled);
        // A different rule with no override stays enabled.
        var pe001 = Assert.Single(response.Rules, r => r.RuleId == "PE001");
        Assert.True(pe001.Enabled);
    }

    [Fact]
    public async System.Threading.Tasks.Task ListAnalysisRules_reflects_a_severity_override()
    {
        var registry = new RuleRegistry();
        var settings = new AppSettings();
        settings.CodeAnalysis.RuleOverrides["PE002"] = new RuleOverride { Enabled = true, Severity = "error" };

        var handler = new ListAnalysisRulesHandler(registry, new CaSettingsLoader(), () => settings);
        var ctx = new RpcContext
        {
            Sessions = new SessionManager(),
            SchemaCache = new SchemaCacheManager(),
            Logger = Log.Logger,
            SettingsLoader = () => settings,
        };

        var response = await handler.HandleAsync(new ListAnalysisRulesRequest(), ctx, CancellationToken.None);

        var pe002 = Assert.Single(response.Rules, r => r.RuleId == "PE002");
        Assert.True(pe002.Enabled);
        Assert.Equal((int)DiagnosticSeverity.Error, pe002.EffectiveSeverity);
    }
}
