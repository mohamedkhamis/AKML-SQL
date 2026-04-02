using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Execution;

/// <summary>EX006 — Always-true or always-false conditions with two literal operands (e.g., 1=1 or 'a'='b').</summary>
public sealed class Ex006AlwaysTrueCondition : IAnalysisRule
{
    public string RuleId => "EX006";
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

        public override void Visit(BooleanComparisonExpression node)
        {
            if (node.ComparisonType != BooleanComparisonType.Equals) return;

            var leftValue  = GetLiteralValue(node.FirstExpression);
            var rightValue = GetLiteralValue(node.SecondExpression);

            if (leftValue == null || rightValue == null) return;

            // Only flag always-true (equal literals) — 1=1, 'a'='a'
            if (!leftValue.Equals(rightValue, StringComparison.OrdinalIgnoreCase)) return;

            var conditionText = $"{leftValue}={rightValue}";

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "EX006",
                CategoryCode = "EX",
                Severity     = ctx.Settings.GetSeverity("EX006", DiagnosticSeverity.Warning),
                Message      = $"Always-true condition '{conditionText}' detected — this is usually a mistake or leftover from dynamic SQL construction",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }

        private static string? GetLiteralValue(ScalarExpression expr)
        {
            return expr switch
            {
                IntegerLiteral il => il.Value,
                StringLiteral sl => sl.Value,
                _ => null
            };
        }
    }
}
