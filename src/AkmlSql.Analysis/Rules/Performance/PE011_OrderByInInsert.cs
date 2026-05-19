using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE011 — ORDER BY in INSERT INTO ... SELECT is meaningless without TOP/OFFSET.</summary>
public sealed class Pe011OrderByInInsert : IAnalysisRule
{
    public string RuleId => "PE011";
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

        public override void Visit(InsertStatement node)
        {
            var spec = node.InsertSpecification;
            if (spec?.InsertSource is not SelectInsertSource selectSource) return;
            if (selectSource.Select is not QuerySpecification qs) return;
            if (qs.OrderByClause == null) return;
            if (qs.TopRowFilter != null) return;
            if (qs.OffsetClause != null) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "PE011",
                CategoryCode = "PE",
                Severity     = ctx.Settings.GetSeverity("PE011", DiagnosticSeverity.Warning),
                Message      = "ORDER BY in INSERT INTO ... SELECT is meaningless without TOP/OFFSET — remove it to avoid confusion",
                StartOffset  = qs.OrderByClause.StartOffset,
                EndOffset    = qs.OrderByClause.StartOffset + qs.OrderByClause.FragmentLength,
                Line         = qs.OrderByClause.StartLine,
                Column       = qs.OrderByClause.StartColumn,
                FixActions   = []
            });
        }
    }
}
