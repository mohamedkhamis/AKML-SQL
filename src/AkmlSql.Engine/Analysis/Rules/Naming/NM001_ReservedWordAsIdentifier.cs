using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Naming;

/// <summary>NM001 — A T-SQL reserved word is used as an identifier without bracket escaping.</summary>
public sealed class Nm001ReservedWordAsIdentifier : IAnalysisRule
{
    public string RuleId => "NM001";
    public string Category => "Naming";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "SELECT", "FROM", "WHERE", "TABLE", "INDEX", "USER", "ORDER", "GROUP",
        "BY", "KEY", "VALUE", "NAME", "DATE", "TIME", "YEAR", "MONTH", "DAY",
        "DATA", "STATUS", "TYPE", "LEVEL", "IDENTITY", "LANGUAGE", "COLUMN",
        "SCHEMA", "FUNCTION", "PROCEDURE", "VIEW", "TRIGGER", "CURSOR",
        "TRANSACTION", "DATABASE", "SERVER", "ROLE", "LOGIN", "PASSWORD",
        "ACTION", "RANGE", "ZONE", "OFFSET", "POSITION", "MATCH", "RANK",
        "ROW", "ROWS", "NEXT", "FIRST", "LAST", "PRIOR", "ABSOLUTE", "RELATIVE"
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

        public override void Visit(ColumnDefinition node)
        {
            var name = node.ColumnIdentifier?.Value;
            CheckIdentifier(name, node.ColumnIdentifier ?? (TSqlFragment)node);
        }

        public override void Visit(NamedTableReference node)
        {
            var name = node.SchemaObject?.BaseIdentifier?.Value;
            CheckIdentifier(name, node);
        }

        private void CheckIdentifier(string? name, TSqlFragment node)
        {
            if (name == null || !ReservedWords.Contains(name)) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "NM001",
                CategoryCode = "NM",
                Severity     = ctx.Settings.GetSeverity("NM001", DiagnosticSeverity.Warning),
                Message      = $"'{name}' is a T-SQL reserved word used as an identifier — rename it or use square bracket escaping",
                StartOffset  = node.StartOffset,
                EndOffset    = node.StartOffset + node.FragmentLength,
                Line         = node.StartLine,
                Column       = node.StartColumn,
                FixActions   = []
            });
        }
    }
}
