using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP018 — IF statement nested more than 3 levels deep indicates excessive complexity.</summary>
public sealed class Bp018DeepNestedIf : IAnalysisRule
{
    public string RuleId => "BP018";
    public string Category => "BestPractices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Information;
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

        private int _depth;

        public override void ExplicitVisit(IfStatement node)
        {
            _depth++;

            if (_depth == 4)
            {
                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "BP018",
                    CategoryCode = "BP",
                    Severity     = ctx.Settings.GetSeverity("BP018", DiagnosticSeverity.Information),
                    Message      = "IF statement nested more than 3 levels deep — consider refactoring to reduce complexity",
                    StartOffset  = node.StartOffset,
                    EndOffset    = node.StartOffset + node.FragmentLength,
                    Line         = node.StartLine,
                    Column       = node.StartColumn,
                    FixActions   = []
                });
            }

            base.ExplicitVisit(node);

            _depth--;
        }
    }
}
