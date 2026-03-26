using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Execution;

/// <summary>EX001 — Division by a literal zero will always throw a runtime error.</summary>
public sealed class Ex001DivisionByZero : IAnalysisRule
{
    public string RuleId => "EX001";
    public string Category => "Execution";
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

        public override void Visit(BinaryExpression node)
        {
            if (node.BinaryExpressionType != BinaryExpressionType.Divide) return;
            if (node.SecondExpression is not IntegerLiteral literal) return;
            if (literal.Value != "0") return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "EX001",
                CategoryCode = "EX",
                Severity     = ctx.Settings.GetSeverity("EX001", DiagnosticSeverity.Warning),
                Message      = "Division by zero literal — this will always throw a runtime error",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
