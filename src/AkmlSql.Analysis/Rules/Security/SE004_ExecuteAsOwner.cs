using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE004 — EXECUTE AS OWNER on a stored procedure elevates the execution context.</summary>
public sealed class Se004ExecuteAsOwner : IAnalysisRule
{
    public string RuleId => "SE004";
    public string Category => "Security";
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

        public override void Visit(CreateProcedureStatement node)
        {
            Check(node, node.Options);
        }

        public override void Visit(AlterProcedureStatement node)
        {
            Check(node, node.Options);
        }

        private void Check(TSqlStatement node, IList<ProcedureOption>? options)
        {
            if (options == null) return;

            foreach (var option in options)
            {
                if (option is not ExecuteAsProcedureOption execAs) continue;
                if (execAs.ExecuteAs?.ExecuteAsOption != ExecuteAsOption.Owner) continue;

                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "SE004",
                    CategoryCode = "SE",
                    Severity     = ctx.Settings.GetSeverity("SE004", DiagnosticSeverity.Warning),
                    Message      = "EXECUTE AS OWNER elevates execution context — ensure this is intentional and consider using a specific login instead",
                    StartOffset  = node.StartOffset,
                    EndOffset    = node.StartOffset + node.FragmentLength,
                    Line         = node.StartLine,
                    Column       = node.StartColumn,
                    FixActions   = []
                });

                break;
            }
        }
    }
}
