using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP008 — Stored procedure should end with an explicit RETURN statement.</summary>
public sealed class BP008_MissingReturn : IAnalysisRule
{
    public string RuleId => "BP008";
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

        public override void Visit(CreateProcedureStatement node) => Check(node, node.StatementList);
        public override void Visit(AlterProcedureStatement node)  => Check(node, node.StatementList);

        private void Check(TSqlStatement proc, StatementList? body)
        {
            if (body == null || body.Statements.Count == 0) return;

            var lastStatement = body.Statements[body.Statements.Count - 1];
            if (lastStatement is ReturnStatement) return;

            var insertOffset = lastStatement.StartOffset + lastStatement.FragmentLength;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP008",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP008", DiagnosticSeverity.Information),
                Message      = "Stored procedure should end with an explicit RETURN statement",
                StartOffset  = proc.StartOffset,
                EndOffset    = proc.StartOffset + proc.FragmentLength,
                Line         = proc.StartLine,
                Column       = proc.StartColumn,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = "Insert RETURN 0;",
                        FixType          = FixType.Insert,
                        ReplacementStart = insertOffset,
                        ReplacementEnd   = insertOffset,
                        ReplacementText  = "\r\nRETURN 0;"
                    }
                ]
            });
        }
    }
}
