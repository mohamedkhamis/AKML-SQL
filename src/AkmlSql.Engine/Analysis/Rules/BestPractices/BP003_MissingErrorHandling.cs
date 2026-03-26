using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP003 — Stored procedure contains DML but no TRY/CATCH error handling.</summary>
public sealed class Bp003MissingErrorHandling : IAnalysisRule
{
    public string RuleId => "BP003";
    public string Category => "BestPractices";
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
            Check(node, node.StatementList);
        }

        public override void Visit(AlterProcedureStatement node)
        {
            Check(node, node.StatementList);
        }

        private void Check(TSqlStatement proc, StatementList? body)
        {
            if (body == null || body.Statements.Count == 0) return;

            var hasDml      = false;
            var hasTryCatch = false;

            foreach (var stmt in body.Statements)
            {
                if (stmt is UpdateStatement or DeleteStatement or InsertStatement)
                    hasDml = true;

                if (stmt is TryCatchStatement)
                    hasTryCatch = true;
            }

            if (!hasDml || hasTryCatch) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP003",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP003", DiagnosticSeverity.Warning),
                Message      = "Stored procedure contains DML but no TRY/CATCH error handling",
                StartOffset  = proc.StartOffset,
                EndOffset    = proc.StartOffset + proc.FragmentLength,
                Line         = proc.StartLine,
                Column       = proc.StartColumn,
                FixActions   = []
            });
        }
    }
}
