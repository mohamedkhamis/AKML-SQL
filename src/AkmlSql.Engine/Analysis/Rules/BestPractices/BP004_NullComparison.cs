using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP004 — Using = NULL or &lt;&gt; NULL always returns UNKNOWN; use IS NULL / IS NOT NULL instead.</summary>
public sealed class Bp004NullComparison : IAnalysisRule
{
    public string RuleId => "BP004";
    public string Category => "BestPractices";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Error;
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
            var isEqual = node.ComparisonType == BooleanComparisonType.Equals;
            var isNotEqual = node.ComparisonType == BooleanComparisonType.NotEqualToBrackets
                          || node.ComparisonType == BooleanComparisonType.NotEqualToExclamation;

            if (!isEqual && !isNotEqual) return;

            bool firstIsNull  = node.FirstExpression  is NullLiteral;
            bool secondIsNull = node.SecondExpression is NullLiteral;

            if (!firstIsNull && !secondIsNull) return;

            // Extract text of the non-null side from the document
            var nonNullExpr = firstIsNull ? node.SecondExpression : node.FirstExpression;
            var exprText    = ctx.DocumentText.Substring(nonNullExpr.StartOffset, nonNullExpr.FragmentLength);
            var fixText     = isEqual ? $"{exprText} IS NULL" : $"{exprText} IS NOT NULL";
            var message     = isEqual
                ? "Use IS NULL instead of = NULL; equality comparisons with NULL always return UNKNOWN"
                : "Use IS NOT NULL instead of <> NULL; inequality comparisons with NULL always return UNKNOWN";

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP004",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP004", DiagnosticSeverity.Error),
                Message      = message,
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = isEqual ? "Replace with IS NULL" : "Replace with IS NOT NULL",
                        FixType          = FixType.Transform,
                        ReplacementStart = node.StartOffset,
                        ReplacementEnd   = node.StartOffset + node.FragmentLength,
                        ReplacementText  = fixText
                    }
                ]
            });
        }
    }
}
