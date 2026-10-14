using AkmlSql.Engine.Analysis;

namespace AkmlSql.PostgreSql.Analysis;

/// <summary>
/// Spec 042 (task T162, research R3) - discovers every <see cref="IPgAnalysisRule"/> in this assembly by reflection, as the T-SQL
/// <c>RuleRegistry</c> does for its own assembly, and filters them by the settings in effect. The two registries are separate by
/// construction: a PostgreSQL rule is not an <c>IAnalysisRule</c>, so it can never be handed T-SQL text, and the other way round
/// (FR-152).
/// </summary>
public sealed class PgRuleRegistry
{
    private readonly IReadOnlyList<IPgAnalysisRule> _all;

    public PgRuleRegistry()
    {
        _all = typeof(PgRuleRegistry).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IPgAnalysisRule).IsAssignableFrom(t))
            .Select(t => (IPgAnalysisRule?)Activator.CreateInstance(t))
            .Where(r => r != null)
            .Cast<IPgAnalysisRule>()
            .OrderBy(r => r.RuleId, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<IPgAnalysisRule> AllRules => _all;

    public IReadOnlyList<IPgAnalysisRule> GetEnabledRules(ResolvedAnalysisSettings settings) =>
        _all.Where(r => settings.IsEnabled(r.RuleId)).ToList();

    public IPgAnalysisRule? Find(string ruleId) =>
        _all.FirstOrDefault(r => string.Equals(r.RuleId, ruleId, StringComparison.OrdinalIgnoreCase));
}
