using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Naming;

/// <summary>NM006 — Single-letter table or column alias is not descriptive; use a meaningful alias.</summary>
public sealed class Nm006SingleLetterAlias : IAnalysisRule
{
    public string RuleId => "NM006";
    public string Category => "Naming";
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

        public override void Visit(NamedTableReference node)
        {
            var alias = node.Alias?.Value;
            if (alias != null && alias.Length == 1 && char.IsLetter(alias[0]))
            {
                Diagnostics.Add(CreateDiagnostic(alias, node));
            }
        }

        public override void Visit(SelectScalarExpression node)
        {
            var alias = node.ColumnName?.Value;
            if (alias != null && alias.Length == 1 && char.IsLetter(alias[0]))
            {
                Diagnostics.Add(CreateDiagnostic(alias, node));
            }
        }

        private AnalysisDiagnostic CreateDiagnostic(string alias, TSqlFragment node)
        {
            return new AnalysisDiagnostic
            {
                RuleId = "NM006",
                CategoryCode = "NM",
                Severity = ctx.Settings.GetSeverity("NM006", DiagnosticSeverity.Information),
                Message = $"Single-letter alias '{alias}' is not descriptive — use a meaningful alias",
                StartOffset = node.StartOffset,
                EndOffset = node.StartOffset + node.FragmentLength,
                Line = node.StartLine,
                Column = node.StartColumn,
                FixActions = []
            };
        }
    }
}
