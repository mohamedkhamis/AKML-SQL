using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE004 — LIKE pattern with a leading wildcard prevents index seeks.</summary>
public sealed class Pe004LikeWithLeadingWildcard : IAnalysisRule
{
    public string RuleId => "PE004";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new Visitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    private sealed class Visitor(AnalysisContext ctx) : TSqlFragmentVisitor
    {
        public List<AnalysisDiagnostic> Diagnostics { get; } = [];

        public override void Visit(LikePredicate node)
        {
            // Only flag when the pattern is a string literal starting with %
            if (node.SecondExpression is not StringLiteral literal) return;

            var pattern = literal.Value;
            if (pattern == null || !pattern.StartsWith("%")) return;

            // A pattern that is exactly "%" matches everything — still a leading wildcard
            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "PE004",
                CategoryCode = "PE",
                Severity     = ctx.Settings.GetSeverity("PE004", DiagnosticSeverity.Warning),
                Message      = "LIKE pattern starts with '%' — this forces a full table scan and cannot use an index seek",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
