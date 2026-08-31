using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis;

public class ResolvedRuleConfig
{
    public bool Enabled { get; set; } = true;
    public DiagnosticSeverity Severity { get; set; }
}

public class ResolvedAnalysisSettings
{
    public bool Enabled { get; set; } = true;
    public bool RunOnType { get; set; } = true;
    public bool RunOnSave { get; set; } = true;
    public bool AutoFixOnFormat { get; set; }
    /// <summary>
    /// Per-rule state after global config.json overrides and any project .casettings have been
    /// applied. Case-insensitive: config.json documents that a hand-edited lowercase id ("pe001")
    /// is equivalent to the engine's canonical "PE001", and an ordinal dictionary here would have
    /// quietly broken that promise on lookup.
    /// </summary>
    public Dictionary<string, ResolvedRuleConfig> EffectiveRules { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> GloballySuppressedRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DiagnosticSeverity GetSeverity(string ruleId, DiagnosticSeverity defaultSeverity)
    {
        return EffectiveRules.TryGetValue(ruleId, out var cfg) ? cfg.Severity : defaultSeverity;
    }

    public bool IsEnabled(string ruleId)
    {
        return EffectiveRules.TryGetValue(ruleId, out var cfg) ? cfg.Enabled : true;
    }
}
