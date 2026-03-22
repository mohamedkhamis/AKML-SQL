using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE003 — GRANT permission to PUBLIC role exposes access to all users.</summary>
public sealed class SE003_GrantToPublic : IAnalysisRule
{
    public string RuleId => "SE003";
    public string Category => "Security";
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

        public override void Visit(GrantStatement node)
        {
            if (node.Principals == null) return;

            foreach (var principal in node.Principals)
            {
                var isPublic = principal.PrincipalType == PrincipalType.Public
                    || string.Equals(principal.Identifier?.Value, "public", System.StringComparison.OrdinalIgnoreCase);

                if (!isPublic) continue;

                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "SE003",
                    CategoryCode = "SE",
                    Severity     = ctx.Settings.GetSeverity("SE003", DiagnosticSeverity.Warning),
                    Message      = "GRANT to PUBLIC role exposes permissions to all users — grant to specific roles or users instead",
                    StartOffset  = node.StartOffset,
                    EndOffset    = node.StartOffset + node.FragmentLength,
                    Line         = node.StartLine,
                    Column       = node.StartColumn,
                    FixActions   = []
                });

                break; // Report once per GRANT statement
            }
        }
    }
}
