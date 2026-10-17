using AkmlSql.Core.Models.Analysis;
using AkmlSql.Engine.Analysis;

namespace AkmlSql.PostgreSql.Analysis;

/// <summary>
/// Spec 042 (task T162, FR-150) - one PostgreSQL analysis rule. Implementations are stateless: every input arrives in the
/// <see cref="PgAnalysisContext"/>, and the findings are the returned diagnostics, so a rule can be exercised on its own. Unlike
/// the T-SQL rules, which keep their display text in a sidecar catalogue, a PostgreSQL rule carries its own name, description and
/// manual reference (FR-150: every rule documents the PostgreSQL behaviour it rests on).
/// </summary>
public interface IPgAnalysisRule
{
    /// <summary><c>PG</c> + category code + three digits (<c>PGPE001</c>), distinct from the T-SQL ids by construction.</summary>
    string RuleId { get; }

    /// <summary>The category prefix, <c>PGPE</c> for <c>PGPE001</c>; also the <c>CategoryCode</c> of the diagnostics.</summary>
    string CategoryCode { get; }

    /// <summary>Category name as the rules window groups it: Performance, BestPractices, Security, Style, Design, Deprecated, Execution, Naming.</summary>
    string Category { get; }

    /// <summary>Short name shown in the rules window.</summary>
    string Name { get; }

    /// <summary>The PostgreSQL behaviour the rule rests on, in a sentence or two.</summary>
    string Description { get; }

    /// <summary>The section of the PostgreSQL 18 manual that documents that behaviour.</summary>
    string ReferenceUrl { get; }

    /// <summary>True when a finding carries a deterministic quick fix.</summary>
    bool AutoFixable { get; }

    DiagnosticSeverity DefaultSeverity { get; }

    /// <summary>When true the rule is skipped if the context has no schema cache.</summary>
    bool RequiresSchema { get; }

    IEnumerable<AnalysisDiagnostic> Analyze(PgAnalysisContext ctx);
}
