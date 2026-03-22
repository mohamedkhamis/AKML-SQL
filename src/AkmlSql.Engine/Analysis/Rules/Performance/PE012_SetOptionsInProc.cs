using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE012 — SET ANSI_NULLS / QUOTED_IDENTIFIER etc. inside a stored procedure causes recompilation.</summary>
public sealed class PE012_SetOptionsInProc : IAnalysisRule
{
    public string RuleId => "PE012";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    private static readonly SetOptions RecompileOptions =
        SetOptions.AnsiNulls           |
        SetOptions.QuotedIdentifier    |
        SetOptions.AnsiWarnings        |
        SetOptions.AnsiPadding         |
        SetOptions.ConcatNullYieldsNull;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new Visitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    private sealed class Visitor(AnalysisContext ctx) : TSqlFragmentVisitor
    {
        public List<AnalysisDiagnostic> Diagnostics { get; } = [];

        public override void Visit(CreateProcedureStatement node) => CheckBody(node.StatementList);
        public override void Visit(AlterProcedureStatement node)  => CheckBody(node.StatementList);

        private void CheckBody(StatementList? body)
        {
            if (body == null) return;

            foreach (var stmt in body.Statements)
            {
                if (stmt is not PredicateSetStatement ps) continue;

                if ((ps.Options & RecompileOptions) == SetOptions.None) continue;

                var optionName = ps.Options.ToString();

                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "PE012",
                    CategoryCode = "PE",
                    Severity     = ctx.Settings.GetSeverity("PE012", DiagnosticSeverity.Warning),
                    Message      = $"SET {optionName} inside a stored procedure causes recompilation — place SET options before the CREATE PROCEDURE statement",
                    StartOffset  = ps.StartOffset,
                    EndOffset    = ps.StartOffset + ps.FragmentLength,
                    Line         = ps.StartLine,
                    Column       = ps.StartColumn,
                    FixActions   = []
                });
            }
        }
    }
}
