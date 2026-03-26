using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP015 — Single-statement IF body should use BEGIN/END for clarity and safety.</summary>
public sealed class Bp015IfWithoutBeginEnd : IAnalysisRule
{
    public string RuleId => "BP015";
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

        public override void Visit(IfStatement node)
        {
            if (node.ThenStatement is BeginEndBlockStatement) return;
            if (node.ThenStatement == null) return;

            var thenStatement = node.ThenStatement;
            var thenText      = ctx.DocumentText.Substring(thenStatement.StartOffset, thenStatement.FragmentLength);

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP015",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP015", DiagnosticSeverity.Information),
                Message      = "Single-statement IF body should use BEGIN/END for clarity and to prevent errors when adding statements",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = "Wrap with BEGIN/END",
                        FixType          = FixType.Transform,
                        ReplacementStart = thenStatement.StartOffset,
                        ReplacementEnd   = thenStatement.StartOffset + thenStatement.FragmentLength,
                        ReplacementText  = $"BEGIN\r\n    {thenText}\r\nEND"
                    }
                ]
            });
        }
    }
}
