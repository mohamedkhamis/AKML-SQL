using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Engine.Analysis;

/// <summary>
/// Contract for all static code analysis rules. Implementations must be stateless —
/// all inputs arrive via <see cref="AnalysisContext"/> and outputs are returned as a
/// new enumerable. Rules must never mutate the context.
/// </summary>
public interface IAnalysisRule
{
    string RuleId { get; }
    string Category { get; }
    DiagnosticSeverity DefaultSeverity { get; }

    /// <summary>
    /// When true the rule is skipped if <see cref="AnalysisContext.SchemaCache"/> is null.
    /// </summary>
    bool RequiresSchema { get; }

    IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx);
}
