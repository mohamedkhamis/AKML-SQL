using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Deprecated;

/// <summary>DEP003 — SET FMTONLY ON is deprecated; use sys.dm_exec_describe_first_result_set or sp_describe_first_result_set instead.</summary>
public sealed class DEP003_SetFmtonly : IAnalysisRule
{
    public string RuleId => "DEP003";
    public string Category => "Deprecated";
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

        public override void Visit(PredicateSetStatement node)
        {
            if (node.Options != SetOptions.FmtOnly || !node.IsOn) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "DEP003",
                CategoryCode = "DEP",
                Severity     = ctx.Settings.GetSeverity("DEP003", DiagnosticSeverity.Warning),
                Message      = "SET FMTONLY ON is deprecated — use sys.dm_exec_describe_first_result_set or sp_describe_first_result_set instead",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
