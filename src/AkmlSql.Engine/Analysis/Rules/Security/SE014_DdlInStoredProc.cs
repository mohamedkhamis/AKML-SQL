using System.Collections.Generic;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Security;

/// <summary>SE014 — DDL statement (CREATE/ALTER/DROP TABLE) inside a stored procedure body can cause implicit transaction issues.</summary>
public sealed class SE014_DdlInStoredProc : IAnalysisRule
{
    public string RuleId => "SE014";
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

        private bool _inProc;

        public override void ExplicitVisit(CreateProcedureStatement node)
        {
            _inProc = true;
            base.ExplicitVisit(node);
            _inProc = false;
        }

        public override void ExplicitVisit(AlterProcedureStatement node)
        {
            _inProc = true;
            base.ExplicitVisit(node);
            _inProc = false;
        }

        public override void Visit(CreateTableStatement node)
        {
            if (_inProc) ReportDdl(node, "CREATE TABLE");
        }

        public override void Visit(AlterTableStatement node)
        {
            if (_inProc) ReportDdl(node, "ALTER TABLE");
        }

        public override void Visit(DropTableStatement node)
        {
            if (_inProc) ReportDdl(node, "DROP TABLE");
        }

        private void ReportDdl(TSqlStatement node, string ddlType)
        {
            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "SE014",
                CategoryCode = "SE",
                Severity     = ctx.Settings.GetSeverity("SE014", DiagnosticSeverity.Warning),
                Message      = $"{ddlType} inside a stored procedure can cause implicit transaction issues and schema lock contention — consider moving DDL outside the procedure",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
