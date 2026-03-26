using System;
using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Design;

/// <summary>DE005 — FLOAT or REAL used for a column with a financial-sounding name; use DECIMAL or NUMERIC to avoid rounding errors.</summary>
public sealed class DE005_FloatForFinancialData : IAnalysisRule
{
    public string RuleId => "DE005";
    public string Category => "Design";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    private static readonly string[] FinancialKeywords =
    [
        "price", "amount", "cost", "total", "salary", "wage",
        "balance", "revenue", "profit"
    ];

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new Visitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    private sealed class Visitor(AnalysisContext ctx) : TSqlFragmentVisitor
    {
        public List<AnalysisDiagnostic> Diagnostics { get; } = [];

        public override void Visit(ColumnDefinition node)
        {
            if (node.DataType is not SqlDataTypeReference sqlType) return;

            if (sqlType.SqlDataTypeOption != SqlDataTypeOption.Float &&
                sqlType.SqlDataTypeOption != SqlDataTypeOption.Real)
                return;

            var colName = node.ColumnIdentifier?.Value ?? string.Empty;
            if (!HasFinancialKeyword(colName)) return;

            var typeName = sqlType.SqlDataTypeOption == SqlDataTypeOption.Float ? "FLOAT" : "REAL";

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "DE005",
                CategoryCode = "DE",
                Severity     = ctx.Settings.GetSeverity("DE005", DiagnosticSeverity.Warning),
                Message      = $"Column '{colName}' uses {typeName} which can cause rounding errors for financial data — use DECIMAL or NUMERIC instead",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }

        private static bool HasFinancialKeyword(string name)
        {
            foreach (var keyword in FinancialKeywords)
            {
                if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}
