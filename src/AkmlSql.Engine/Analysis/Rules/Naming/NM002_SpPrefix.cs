using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Naming;

/// <summary>NM002 — Stored procedure uses reserved 'sp_' prefix which can cause performance issues and conflicts with system procedures.</summary>
public sealed class NM002_SpPrefix : IAnalysisRule
{
    public string RuleId => "NM002";
    public string Category => "Naming";
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

        public override void Visit(CreateProcedureStatement node)
        {
            var baseName = node.ProcedureReference?.Name?.BaseIdentifier;
            if (baseName == null) return;

            var procName = baseName.Value;
            if (procName == null || !procName.StartsWith("sp_", System.StringComparison.OrdinalIgnoreCase)) return;

            var suggestedName = "usp_" + procName.Substring(3);

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "NM002",
                CategoryCode = "NM",
                Severity     = ctx.Settings.GetSeverity("NM002", DiagnosticSeverity.Warning),
                Message      = $"Stored procedure '{procName}' uses reserved 'sp_' prefix — this prefix is reserved for system procedures and can cause performance issues",
                StartOffset  = baseName.StartOffset,
                EndOffset    = baseName.StartOffset + baseName.FragmentLength,
                Line         = baseName.StartLine,
                Column       = baseName.StartColumn,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = $"Rename to '{suggestedName}'",
                        FixType          = FixType.Transform,
                        ReplacementStart = baseName.StartOffset,
                        ReplacementEnd   = baseName.StartOffset + baseName.FragmentLength,
                        ReplacementText  = suggestedName
                    }
                ]
            });
        }
    }
}
