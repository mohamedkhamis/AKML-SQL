using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST019 — ORDER BY column number is fragile — use column name or alias instead.</summary>
public sealed class St019OrderByColumnNumber : IAnalysisRule
{
    public string RuleId => "ST019";
    public string Category => "Style";
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

        public override void ExplicitVisit(ExpressionWithSortOrder node)
        {
            if (node.Expression is IntegerLiteral literal)
            {
                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "ST019",
                    CategoryCode = "ST",
                    Severity     = ctx.Settings.GetSeverity("ST019", DiagnosticSeverity.Warning),
                    Message      = $"ORDER BY column number ({literal.Value}) is fragile — use column name or alias instead",
                    StartOffset  = node.StartOffset,
                    EndOffset    = node.StartOffset + node.FragmentLength,
                    Line         = node.StartLine,
                    Column       = node.StartColumn,
                    FixActions   = []
                });
            }
        }
    }
}
