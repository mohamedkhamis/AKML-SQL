using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP006 — Stored procedure with multiple DML statements should wrap them in a transaction.</summary>
public sealed class Bp006MissingTransaction : IAnalysisRule
{
    public string RuleId => "BP006";
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

        public override void Visit(CreateProcedureStatement node)
        {
            Check(node, node.StatementList);
        }

        public override void Visit(AlterProcedureStatement node)
        {
            Check(node, node.StatementList);
        }

        private void Check(TSqlStatement proc, StatementList? body)
        {
            if (body == null || body.Statements.Count == 0) return;

            var dmlCount = body.Statements.Count(s => s is InsertStatement or UpdateStatement or DeleteStatement);
            if (dmlCount < 2) return;

            var hasBeginTran = body.Statements.OfType<BeginTransactionStatement>().Any();
            if (hasBeginTran) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP006",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP006", DiagnosticSeverity.Information),
                Message      = "Stored procedure with multiple DML statements should wrap them in a transaction",
                StartOffset  = proc.StartOffset,
                EndOffset    = proc.StartOffset + proc.FragmentLength,
                Line         = proc.StartLine,
                Column       = proc.StartColumn,
                FixActions   = []
            });
        }
    }
}
