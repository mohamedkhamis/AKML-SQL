using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP002 — ISNUMERIC() has surprising behavior; use TRY_CONVERT(numeric_type, expression) IS NOT NULL instead.</summary>
public sealed class BP002_UseTryConvert : IAnalysisRule
{
    public string RuleId => "BP002";
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

        public override void Visit(FunctionCall node)
        {
            if (!node.FunctionName.Value.Equals("ISNUMERIC", System.StringComparison.OrdinalIgnoreCase))
                return;

            if (node.Parameters.Count == 0) return;

            var arg     = node.Parameters[0];
            var argText = ctx.DocumentText.Substring(arg.StartOffset, arg.FragmentLength);

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP002",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP002", DiagnosticSeverity.Warning),
                Message      = "ISNUMERIC() has surprising behavior — use TRY_CONVERT(numeric_type, expression) IS NOT NULL instead",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = "Replace with TRY_CONVERT(...) IS NOT NULL",
                        FixType          = FixType.Transform,
                        ReplacementStart = node.StartOffset,
                        ReplacementEnd   = node.StartOffset + node.FragmentLength,
                        ReplacementText  = $"TRY_CONVERT(decimal(18,2), {argText}) IS NOT NULL"
                    }
                ]
            });
        }
    }
}
