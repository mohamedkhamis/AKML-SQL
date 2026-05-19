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
    public Dictionary<string, ResolvedRuleConfig> EffectiveRules { get; set; } = new();
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
