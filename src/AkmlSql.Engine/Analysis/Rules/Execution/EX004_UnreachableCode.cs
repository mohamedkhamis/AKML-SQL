using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Execution;

/// <summary>EX004 — Statements following an unconditional RETURN or THROW are unreachable.</summary>
public sealed class EX004_UnreachableCode : IAnalysisRule
{
    public string RuleId => "EX004";
    public string Category => "Execution";
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

        public override void Visit(CreateProcedureStatement node) =>
            CheckStatementList(node.StatementList);

        public override void Visit(AlterProcedureStatement node) =>
            CheckStatementList(node.StatementList);

        private void CheckStatementList(StatementList? statementList)
        {
            if (statementList == null) return;

            var foundExit = false;
            foreach (var stmt in statementList.Statements)
            {
                if (foundExit)
                {
                    // Skip label statements and GO — these may be structural
                    if (stmt is LabelStatement) continue;

                    Diagnostics.Add(new AnalysisDiagnostic
                    {
                        RuleId       = "EX004",
                        CategoryCode = "EX",
                        Severity     = ctx.Settings.GetSeverity("EX004", DiagnosticSeverity.Information),
                        Message      = "Code after RETURN/THROW is unreachable",
                        StartOffset  = stmt.StartOffset,
                        EndOffset    = stmt.StartOffset + stmt.FragmentLength,
                        Line         = stmt.StartLine,
                        Column       = stmt.StartColumn,
                        FixActions   = []
                    });
                }
                else if (stmt is ReturnStatement || stmt is ThrowStatement)
                {
                    foundExit = true;
                }
            }
        }
    }
}
