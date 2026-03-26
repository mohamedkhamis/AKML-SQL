using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE005 — ALTER DATABASE ... SET TRUSTWORTHY ON enables a security risk.</summary>
public sealed class SE005_TrustworthyDatabase : IAnalysisRule
{
    public string RuleId => "SE005";
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

        public override void Visit(AlterDatabaseSetStatement node)
        {
            // Extract the text of this statement to check for TRUSTWORTHY ON
            var startOffset = node.StartOffset;
            var length      = node.FragmentLength;

            if (startOffset < 0 || length <= 0) return;
            if (startOffset + length > ctx.DocumentText.Length) return;

            var stmtText = ctx.DocumentText.Substring(startOffset, length);

            if (stmtText.IndexOf("TRUSTWORTHY", System.StringComparison.OrdinalIgnoreCase) < 0) return;
            if (stmtText.IndexOf("ON", System.StringComparison.OrdinalIgnoreCase) < 0) return;

            // Make sure we see TRUSTWORTHY followed by ON (not TRUSTWORTHY OFF)
            // Simple heuristic: find TRUSTWORTHY position then check what follows
            var tIdx = stmtText.IndexOf("TRUSTWORTHY", System.StringComparison.OrdinalIgnoreCase);
            var remainder = stmtText.Substring(tIdx + "TRUSTWORTHY".Length).TrimStart();
            if (!remainder.StartsWith("ON", System.StringComparison.OrdinalIgnoreCase)) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "SE005",
                CategoryCode = "SE",
                Severity     = ctx.Settings.GetSeverity("SE005", DiagnosticSeverity.Warning),
                Message      = "Setting TRUSTWORTHY ON is a security risk — this allows database objects to access resources outside the database",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
