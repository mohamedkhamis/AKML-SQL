using System;
using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE017 — Known non-SARGable function applied to a column in WHERE clause prevents index seek.</summary>
public sealed class PE017_NonSargableExpression : IAnalysisRule
{
    public string RuleId => "PE017";
    public string Category => "Performance";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    private static readonly HashSet<string> NonSargableFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ISNULL", "COALESCE", "CONVERT", "CAST", "TRY_CONVERT", "TRY_CAST",
        "YEAR", "MONTH", "DAY", "DATEPART", "DATENAME",
        "LTRIM", "RTRIM", "UPPER", "LOWER", "TRIM"
    };

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new Visitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

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

            var funcName = node.FunctionName?.Value;
            if (string.IsNullOrEmpty(funcName)) return;
            if (!NonSargableFunctions.Contains(funcName)) return;

            if (node.Parameters.Count == 0) return;
            if (node.Parameters[0] is not ColumnReferenceExpression) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "PE017",
                CategoryCode = "PE",
                Severity     = ctx.Settings.GetSeverity("PE017", DiagnosticSeverity.Warning),
                Message      = $"Function applied to column in WHERE clause prevents index seek ({funcName}(column)) — consider rewriting as a SARGable expression",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }

        public override void Visit(CastCall node)
        {
            if (!_inWhere) return;
            if (node.Parameter is not ColumnReferenceExpression) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "PE017",
                CategoryCode = "PE",
                Severity     = ctx.Settings.GetSeverity("PE017", DiagnosticSeverity.Warning),
                Message      = "Function applied to column in WHERE clause prevents index seek (CAST(column)) — consider rewriting as a SARGable expression",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }

        public override void Visit(ConvertCall node)
        {
            if (!_inWhere) return;
            if (node.Parameter is not ColumnReferenceExpression) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "PE017",
                CategoryCode = "PE",
                Severity     = ctx.Settings.GetSeverity("PE017", DiagnosticSeverity.Warning),
                Message      = "Function applied to column in WHERE clause prevents index seek (CONVERT(type, column)) — consider rewriting as a SARGable expression",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
