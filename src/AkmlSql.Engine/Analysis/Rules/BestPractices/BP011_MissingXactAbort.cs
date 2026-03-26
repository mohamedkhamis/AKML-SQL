using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP011 — Procedure uses transactions but is missing SET XACT_ABORT ON.</summary>
public sealed class Bp011MissingXactAbort : IAnalysisRule
{
    public string RuleId => "BP011";
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

            var hasBeginTran = body.Statements.OfType<BeginTransactionStatement>().Any();
            if (!hasBeginTran) return;

            var hasXactAbort = body.Statements
                .OfType<PredicateSetStatement>()
                .Any(s => s.Options == SetOptions.XactAbort && s.IsOn);

            if (hasXactAbort) return;

            var firstStatement = body.Statements[0];

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP011",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP011", DiagnosticSeverity.Information),
                Message      = "Procedure uses transactions but is missing SET XACT_ABORT ON — without it, partial updates can occur on error",
                StartOffset  = proc.StartOffset,
                EndOffset    = proc.StartOffset + proc.FragmentLength,
                Line         = proc.StartLine,
                Column       = proc.StartColumn,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = "Insert SET XACT_ABORT ON",
                        FixType          = FixType.Insert,
                        ReplacementStart = firstStatement.StartOffset,
                        ReplacementEnd   = firstStatement.StartOffset,
                        ReplacementText  = "SET XACT_ABORT ON;\r\n"
                    }
                ]
            });
        }
    }
}
