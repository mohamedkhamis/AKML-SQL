using System.Reflection;

namespace AkmlSql.Engine.Analysis;

/// <summary>
/// Discovers all <see cref="IAnalysisRule"/> implementations in this assembly via reflection
/// and exposes a filtered list based on the active <see cref="ResolvedAnalysisSettings"/>.
/// </summary>
public class RuleRegistry
{
    private readonly IReadOnlyList<IAnalysisRule> _allRules;

    public RuleRegistry()
    {
        _allRules = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false }
                        && typeof(IAnalysisRule).IsAssignableFrom(t))
            .Select(t =>
            {
                try { return (IAnalysisRule?)Activator.CreateInstance(t); }
                catch { return null; }
            })
            .Where(r => r != null)
            .Cast<IAnalysisRule>()
            .OrderBy(r => r.RuleId)
            .ToList();
    }

    public IReadOnlyList<IAnalysisRule> AllRules => _allRules;

    public IReadOnlyList<IAnalysisRule> GetEnabledRules(ResolvedAnalysisSettings settings)
    {
        return _allRules
            .Where(r => settings.IsEnabled(r.RuleId))
            .ToList();
    }
}
