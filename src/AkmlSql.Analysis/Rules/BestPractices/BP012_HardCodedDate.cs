using System.Text.RegularExpressions;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP012 — Hard-coded date literal; consider using a parameter or variable for maintainability.</summary>
public sealed class Bp012HardCodedDate : IAnalysisRule
{
    public string RuleId => "BP012";
    public string Category => "BestPractices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Information;
    public bool RequiresSchema => false;

    private static readonly Regex DatePattern =
        new(@"^\d{4}-\d{2}-\d{2}|^\d{1,2}/\d{1,2}/\d{4}", RegexOptions.Compiled);

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new Visitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    private sealed class Visitor(AnalysisContext ctx) : TSqlFragmentVisitor
    {
        public List<AnalysisDiagnostic> Diagnostics { get; } = [];

        public override void Visit(StringLiteral node)
        {
            if (!DatePattern.IsMatch(node.Value)) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP012",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP012", DiagnosticSeverity.Information),
                Message      = $"Hard-coded date literal '{node.Value}' — consider using a parameter or variable for maintainability",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
