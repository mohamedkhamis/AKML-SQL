using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE013 — Function call on a column reference in WHERE clause prevents index seek.</summary>
public sealed class Pe013ScalarFunctionInWhere : IAnalysisRule
{
    public string RuleId => "PE013";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new Visitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    // Date part keywords that ScriptDom represents as ColumnReferenceExpression
    private static readonly HashSet<string> DatePartKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "YEAR", "QUARTER", "MONTH", "WEEK", "WEEKDAY", "DAY", "DAYOFYEAR",
            "HOUR", "MINUTE", "SECOND", "MILLISECOND", "MICROSECOND", "NANOSECOND",
            "TZoffset", "ISO_WEEK"
        };

    private sealed class Visitor(AnalysisContext ctx) : TSqlFragmentVisitor
    {
        public List<AnalysisDiagnostic> Diagnostics { get; } = [];
        private bool _inWhere;

        public override void ExplicitVisit(WhereClause node)
        {
            _inWhere = true;
            base.ExplicitVisit(node);
            _inWhere = false;
        }

        public override void Visit(FunctionCall node)
        {
            if (!_inWhere) return;

            // Only flag when the first argument is a direct column reference —
            // this is the non-SARGable pattern: FUNC(column) = value
            if (node.Parameters.Count == 0) return;
            if (node.Parameters[0] is not ColumnReferenceExpression colRef) return;

            // Skip datepart pseudo-columns (e.g., DATEADD(MONTH, -3, GETDATE()))
            // ScriptDom represents these as single-part ColumnReferenceExpression
            var parts = colRef.MultiPartIdentifier?.Identifiers;
            if (parts is { Count: 1 } && DatePartKeywords.Contains(parts[0].Value))
                return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "PE013",
                CategoryCode = "PE",
                Severity     = ctx.Settings.GetSeverity("PE013", DiagnosticSeverity.Warning),
                Message      = "Function call on column in WHERE clause prevents index seek",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
