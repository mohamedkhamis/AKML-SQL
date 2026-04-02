using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE003 — DELETE or UPDATE without a WHERE clause affects all rows.</summary>
public sealed class Pe003MissingWhereOnDeleteUpdate : IAnalysisRule
{
    public string RuleId => "PE003";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Error;
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

        public override void Visit(DeleteStatement node)
        {
            var spec = node.DeleteSpecification;
            if (spec?.WhereClause == null)
                Emit(node, "DELETE statement has no WHERE clause — all rows will be deleted");
        }

        public override void Visit(UpdateStatement node)
        {
            var spec = node.UpdateSpecification;
            if (spec?.WhereClause == null)
                Emit(node, "UPDATE statement has no WHERE clause — all rows will be updated");
        }

        private void Emit(TSqlStatement node, string msg)
        {
            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId = "PE003",
                CategoryCode = "PE",
                Severity = ctx.Settings.GetSeverity("PE003", DiagnosticSeverity.Error),
                Message = msg,
                StartOffset = node.StartOffset,
                EndOffset = node.StartOffset + node.FragmentLength,
                Line = node.StartLine,
                Column = node.StartColumn,
                FixActions = []
            });
        }
    }
}
