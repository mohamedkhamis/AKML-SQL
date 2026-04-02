using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Style;

/// <summary>ST004 — Statement is not terminated with a semicolon.</summary>
public sealed class St004MissingSemicolon : IAnalysisRule
{
    public string RuleId => "ST004";
    public string Category => "Style";
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

        public override void Visit(SelectStatement node)
        {
            Check(node);
        }

        public override void Visit(InsertStatement node)
        {
            Check(node);
        }

        public override void Visit(UpdateStatement node)
        {
            Check(node);
        }

        public override void Visit(DeleteStatement node)
        {
            Check(node);
        }

        private void Check(TSqlStatement node)
        {
            var stmtText = ExtractStatementText(node);
            if (stmtText == null) return;

            // Check if statement text (trimmed) ends with semicolon
            var trimmed = stmtText.TrimEnd();
            if (trimmed.EndsWith(";")) return;

            var insertOffset = node.StartOffset + node.FragmentLength;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "ST004",
                CategoryCode = "ST",
                Severity     = ctx.Settings.GetSeverity("ST004", DiagnosticSeverity.Information),
                Message      = "Statement is not terminated with a semicolon — add ';' for clarity and future compatibility",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   =
                [
                    new AnalysisFixAction
                    {
                        Label            = "Add semicolon",
                        FixType          = FixType.Insert,
                        ReplacementStart = insertOffset,
                        ReplacementEnd   = insertOffset,
                        ReplacementText  = ";"
                    }
                ]
            });
        }

        private string? ExtractStatementText(TSqlStatement node)
        {
            var start  = node.StartOffset;
            var length = node.FragmentLength;
            if (start < 0 || length <= 0) return null;
            if (start + length > ctx.DocumentText.Length) return null;
            return ctx.DocumentText.Substring(start, length);
        }
    }
}
