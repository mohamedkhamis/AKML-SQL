using System.Text.RegularExpressions;
using AkmlSql.Core.Models.Analysis;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AkmlSql.Engine.Analysis.Rules.Naming;

/// <summary>
/// NM005 — a name the script gives contains spaces or special characters, so it needs brackets wherever it is used.
/// <para>
/// Reported where the name is given: an object a statement creates (table, view, procedure, function, trigger, type, sequence,
/// synonym, schema, database, statistics), a column, a constraint or an index, a table made by SELECT … INTO, a CTE and its
/// columns, and a column or table alias. A reference to a name that already exists is not reported, because the script cannot
/// rename it: Northwind's [Order Details] raised a warning wherever it was queried (installed run 2026-11-23). A principal
/// ([NT AUTHORITY\SYSTEM], [DOMAIN\user]) is not an object name and is never reported.
/// </para>
/// <para>
/// The rule reads the batch it is given. Reading the whole document's tokens, it reported every quoted name once per batch.
/// </para>
/// </summary>
public sealed class Nm005SpecialCharsInNames : IAnalysisRule
{
    public string RuleId => "NM005";
    public string Category => "Naming";
    public DiagnosticSeverity DefaultSeverity => DiagnosticSeverity.Warning;
    public bool RequiresSchema => false;

    private static readonly Regex SpecialCharPattern =
        new(@"[ \-!@#$%^&*()+={}\[\]|:;<>?,./\\]", RegexOptions.Compiled);

    public IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)
    {
        var visitor = new Visitor(ctx);
        ctx.CurrentBatch.Accept(visitor);
        return visitor.Diagnostics;
    }

    private sealed class Visitor(AnalysisContext ctx) : TSqlFragmentVisitor
    {
        private readonly HashSet<int> _reported = [];

        public List<AnalysisDiagnostic> Diagnostics { get; } = [];

        // ── objects a statement creates: the name it gives, not the schema it is put in ─────────────────────────────────────
        public override void Visit(CreateTableStatement node) => Check(node.SchemaObjectName?.BaseIdentifier);
        public override void Visit(CreateViewStatement node) => Check(node.SchemaObjectName?.BaseIdentifier);
        public override void Visit(CreateOrAlterViewStatement node) => Check(node.SchemaObjectName?.BaseIdentifier);
        public override void Visit(CreateProcedureStatement node) => Check(node.ProcedureReference?.Name?.BaseIdentifier);
        public override void Visit(CreateOrAlterProcedureStatement node) => Check(node.ProcedureReference?.Name?.BaseIdentifier);
        public override void Visit(CreateFunctionStatement node) => Check(node.Name?.BaseIdentifier);
        public override void Visit(CreateOrAlterFunctionStatement node) => Check(node.Name?.BaseIdentifier);
        public override void Visit(CreateTriggerStatement node) => Check(node.Name?.BaseIdentifier);
        public override void Visit(CreateOrAlterTriggerStatement node) => Check(node.Name?.BaseIdentifier);
        public override void Visit(CreateTypeStatement node) => Check(node.Name?.BaseIdentifier);
        public override void Visit(CreateSequenceStatement node) => Check(node.Name?.BaseIdentifier);
        public override void Visit(CreateSynonymStatement node) => Check(node.Name?.BaseIdentifier);
        public override void Visit(CreateSchemaStatement node) => Check(node.Name);
        public override void Visit(CreateDatabaseStatement node) => Check(node.DatabaseName);
        public override void Visit(CreateStatisticsStatement node) => Check(node.Name);
        public override void Visit(CreateIndexStatement node) => Check(node.Name);
        public override void Visit(CreateColumnStoreIndexStatement node) => Check(node.Name);

        // A table made by SELECT … INTO.
        public override void Visit(SelectStatement node) => Check(node.Into?.BaseIdentifier);

        // ── parts of a table: columns (CREATE TABLE, ALTER TABLE ADD, table variables and types), constraints, indexes ────────
        public override void Visit(ColumnDefinition node) => Check(node.ColumnIdentifier);
        public override void Visit(ConstraintDefinition node) => Check(node.ConstraintIdentifier);
        public override void Visit(IndexDefinition node) => Check(node.Name);

        // ── names a query gives: CTEs and their columns, column and table aliases ──────────────────────────────────────────
        public override void Visit(CommonTableExpression node)
        {
            Check(node.ExpressionName);
            foreach (var column in node.Columns) Check(column);
        }

        public override void Visit(SelectScalarExpression node) => Check(node.ColumnName?.Identifier);

        public override void Visit(TableReferenceWithAlias node) => Check(node.Alias);

        public override void Visit(TableReferenceWithAliasAndColumns node)
        {
            foreach (var column in node.Columns) Check(column);
        }

        public override void Visit(MergeSpecification node) => Check(node.TableAlias);

        private void Check(Identifier? id)
        {
            if (id == null || id.QuoteType == QuoteType.NotQuoted) return;
            var name = id.Value;
            if (string.IsNullOrEmpty(name) || !SpecialCharPattern.IsMatch(name) || !_reported.Add(id.StartOffset)) return;

            Diagnostics.Add(new AnalysisDiagnostic
            {
                RuleId       = "NM005",
                CategoryCode = "NM",
                Severity     = ctx.Settings.GetSeverity("NM005", DiagnosticSeverity.Warning),
                Message      = $"Identifier '{name}' contains special characters — use standard alphanumeric names without spaces or special characters",
                StartOffset  = id.StartOffset,
                EndOffset    = id.StartOffset + id.FragmentLength,
                Line         = id.StartLine,
                Column       = id.StartColumn,
                FixActions   = []
            });
        }
    }
}
