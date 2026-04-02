using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE018 — Table variables lack statistics and may perform poorly with large datasets.</summary>
public sealed class Pe018TableVariableLargeDataset : IAnalysisRule
{
    public string RuleId => "PE018";
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

        public override void Visit(DeclareTableVariableStatement node)
        {
            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "PE018",
                CategoryCode = "PE",
                Severity     = ctx.Settings.GetSeverity("PE018", DiagnosticSeverity.Warning),
                Message      = "Table variables lack statistics and may perform poorly with large datasets — consider using a temp table (#table) for large result sets",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
