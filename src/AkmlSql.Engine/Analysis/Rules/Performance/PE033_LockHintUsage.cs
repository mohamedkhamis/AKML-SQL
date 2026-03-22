using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Performance;

/// <summary>PE033 — NOLOCK/READUNCOMMITTED hints can cause dirty reads and phantom reads.</summary>
public sealed class PE033_LockHintUsage : IAnalysisRule
{
    public string RuleId => "PE033";
    public string Category => "Performance";
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

        public override void ExplicitVisit(TableHint node)
        {
            if (node.HintKind is TableHintKind.NoLock or TableHintKind.ReadUncommitted)
            {
                Diagnostics.Add(new AnalysisDiagnostic
                {
                    RuleId       = "PE033",
                    CategoryCode = "PE",
                    Severity     = ctx.Settings.GetSeverity("PE033", DiagnosticSeverity.Warning),
                    Message      = "NOLOCK/READUNCOMMITTED hints can cause dirty reads and phantom reads",
                    StartOffset  = node.StartOffset,
                    EndOffset    = node.StartOffset + node.FragmentLength,
                    Line         = node.StartLine,
                    Column       = node.StartColumn,
                    FixActions   = []
                });
            }

            base.ExplicitVisit(node);
        }
    }
}
