using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Naming;

/// <summary>NM003 — Table or view name uses a Hungarian notation type prefix; use descriptive names without type prefixes.</summary>
public sealed class Nm003HungarianNotation : IAnalysisRule
{
    public string RuleId => "NM003";
    public string Category => "Naming";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Information;
    public bool RequiresSchema => false;

    private static readonly string[] HungarianPrefixes =
    [
        "tbl", "vw", "fn", "sp", "tr", "udf", "tb"
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

        public override void Visit(CreateTableStatement node)
        {
            var name = node.SchemaObjectName?.BaseIdentifier?.Value;
            CheckName(name, node);
        }

        public override void Visit(CreateViewStatement node)
        {
            var name = node.SchemaObjectName?.BaseIdentifier?.Value;
            CheckName(name, node);
        }

        private void CheckName(string? name, TSqlFragment node)
        {
            if (name == null) return;

            foreach (var prefix in HungarianPrefixes)
            {
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                // Ensure the rest of the name has at least one more character
                if (name.Length <= prefix.Length) continue;

                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "NM003",
                    CategoryCode = "NM",
                    Severity     = ctx.Settings.GetSeverity("NM003", DiagnosticSeverity.Information),
                    Message      = $"'{name}' uses Hungarian notation prefix — use descriptive names without type prefixes",
                    StartOffset  = node.StartOffset,
                    EndOffset    = node.StartOffset + node.FragmentLength,
                    Line         = node.StartLine,
                    Column       = node.StartColumn,
                    FixActions   = []
                });
                return;
            }
        }
    }
}
