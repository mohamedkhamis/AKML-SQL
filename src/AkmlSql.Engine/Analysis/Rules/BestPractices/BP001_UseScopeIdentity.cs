using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.BestPractices;

/// <summary>BP001 — @@IDENTITY can return identity values from triggers; use SCOPE_IDENTITY() instead.</summary>
public sealed class Bp001UseScopeIdentity : IAnalysisRule
{
    public string RuleId => "BP001";
    public string Category => "BestPractices";
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

        public override void Visit(GlobalVariableExpression node)
        {
            if (!node.Name.Equals("@@IDENTITY", StringComparison.OrdinalIgnoreCase)) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "BP001",
                CategoryCode = "BP",
                Severity     = ctx.Settings.GetSeverity("BP001", DiagnosticSeverity.Warning),
                Message      = "@@IDENTITY returns the last identity inserted in any scope, including triggers — use SCOPE_IDENTITY() to get the value from the current scope only",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = "Replace with SCOPE_IDENTITY()",
                        FixType          = FixType.Transform,
                        ReplacementStart = node.StartOffset,
                        ReplacementEnd   = node.StartOffset + node.FragmentLength,
                        ReplacementText  = "SCOPE_IDENTITY()"
                    }
                ]
            });
        }
    }
}
