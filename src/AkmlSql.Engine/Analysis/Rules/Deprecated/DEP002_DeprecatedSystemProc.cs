using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Deprecated;

/// <summary>DEP002 — Use of deprecated system stored procedures that have been superseded by modern T-SQL DDL statements.</summary>
public sealed class Dep002DeprecatedSystemProc : IAnalysisRule
{
    public string RuleId => "DEP002";
    public string Category => "Deprecated";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    private static readonly HashSet<string> DeprecatedProcs = new(StringComparer.OrdinalIgnoreCase)
    {
        "sp_addlogin",
        "sp_droplogin",
        "sp_adduser",
        "sp_dropuser",
        "sp_addrolemember",
        "sp_grantlogin",
        "sp_revokelogin",
        "sp_password"
    };

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new Visitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    private sealed class Visitor(AnalysisContext ctx) : TSqlFragmentVisitor
    {
        public List<AnalysisDiagnostic> Diagnostics { get; } = [];

        public override void Visit(ExecuteStatement node)
        {
            var entity = node.ExecuteSpecification?.ExecutableEntity;
            if (entity is not ExecutableProcedureReference procRef) return;

            // ExecutableProcedureReference.ProcedureReference → ProcedureReferenceName
            // ProcedureReferenceName.ProcedureReference       → ProcedureReference
            // ProcedureReference.Name                         → SchemaObjectName
            var name = procRef.ProcedureReference?.ProcedureReference?.Name;
            if (name == null) return;

            var procName = name.BaseIdentifier?.Value;
            if (procName == null || !DeprecatedProcs.Contains(procName)) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "DEP002",
                CategoryCode = "DEP",
                Severity     = ctx.Settings.GetSeverity("DEP002", DiagnosticSeverity.Warning),
                Message      = $"Deprecated system procedure '{procName}' — use modern T-SQL DDL statements instead (e.g., CREATE LOGIN, DROP USER)",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
