using AkmlSql.Core.Models.Analysis;
using AkmlSql.PostgreSql.Language;
using PgSqlParser;

namespace AkmlSql.PostgreSql.Analysis.Rules.Deprecated;

// Spec 042 (task T168, FR-150): the rules about features PostgreSQL has removed or replaced, PGDEP001 to PGDEP004.

/// <summary>PGDEP001 - object identifiers on user tables, removed in PostgreSQL 12.</summary>
public sealed class Pgdep001WithOids : PgRuleBase
{
    public Pgdep001WithOids() : base("PGDEP001", "Deprecated", "WITH OIDS and default_with_oids", DiagnosticSeverity.Error,
        "PostgreSQL 12 removed the hidden oid column of user tables: CREATE TABLE ... WITH OIDS is a syntax error (and is reported as one), "
        + "and the default_with_oids setting no longer exists. The forms that still parse, SET default_with_oids and WITH (oids = ...), are reported here.",
        "sql-createtable") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            var set = s.Stmt?.VariableSetStmt;
            if (set != null && string.Equals(set.Name, "default_with_oids", StringComparison.OrdinalIgnoreCase))
            {
                report.Statement(s, "default_with_oids was removed in PostgreSQL 12.");
                continue;
            }

            foreach (var create in s.All<CreateStmt>())
            {
                if (PgTreeFacts.Option(create.Options, "oids") is { } oids)
                    report.Node(s, oids, "Tables with OIDs were removed in PostgreSQL 12; drop the oids option.");
            }
            foreach (var command in s.All<AlterTableCmd>())
            {
                if (command.Subtype == AlterTableType.AtDropOids)
                    report.Statement(s, "SET WITHOUT OIDS does nothing since PostgreSQL 12 removed OIDs.");
            }
        }
    }
}

/// <summary>PGDEP002 - named arguments written with := instead of =&gt;.</summary>
public sealed class Pgdep002ColonEqualsArgument : PgRuleBase
{
    public Pgdep002ColonEqualsArgument() : base("PGDEP002", "Deprecated", "Named argument with :=", DiagnosticSeverity.Information,
        "func(name := value) is the older spelling of a named argument; the SQL standard form name => value has been the documented one since PostgreSQL 9.5, "
        + "and := is kept only for compatibility.",
        "sql-syntax-calling-funcs", autoFixable: true) { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            var sig = s.Tokens.Sig;
            for (var i = 1; i + 1 < sig.Count; i++)
            {
                var colon = sig[i];
                var equals = sig[i + 1];
                if (colon.Kind != PgTokenKind.Punctuation || colon.Text != ":" || equals.Kind != PgTokenKind.Operator || !equals.Text.StartsWith('=')) continue;
                if (colon.Token.End != equals.Token.Start || colon.Depth == 0 || !sig[i - 1].IsName) continue;

                report.At(colon.Token.Start, colon.Token.End + 1, "Write the named argument with =>, not :=.",
                    PgReporter.Replace("Replace := with =>", colon.Token.Start, colon.Token.Start + 2, "=>"));
            }
        }
    }
}

/// <summary>PGDEP003 - types removed in PostgreSQL 12.</summary>
public sealed class Pgdep003RemovedTypes : PgRuleBase
{
    private static readonly HashSet<string> Removed = new(StringComparer.Ordinal) { "abstime", "reltime", "tinterval" };

    public Pgdep003RemovedTypes() : base("PGDEP003", "Deprecated", "Removed time types", DiagnosticSeverity.Error,
        "abstime, reltime and tinterval were removed in PostgreSQL 12. Use timestamptz (or timestamp), interval and a pair of timestamps or a tstzrange.",
        "datatype-datetime") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var type in PgTreeFacts.TypesNamed(s))
            {
                var name = PgTreeFacts.TypeLower(type);
                if (Removed.Contains(name))
                    report.Node(s, type, $"The type {name} was removed in PostgreSQL 12.");
            }
        }
    }
}

/// <summary>PGDEP004 - inheritance used to split a table.</summary>
public sealed class Pgdep004InheritancePartitioning : PgRuleBase
{
    public Pgdep004InheritancePartitioning() : base("PGDEP004", "Deprecated", "Table inheritance", DiagnosticSeverity.Information,
        "Splitting a large table into child tables with INHERITS needs constraints, triggers or rules that you write and maintain, and does not share indexes or unique "
        + "constraints. Declarative partitioning (PARTITION BY, since PostgreSQL 10) does the routing and pruning itself.",
        "ddl-partitioning") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var create in s.All<CreateStmt>())
            {
                if (create.InhRelations.Count > 0 && create.Partbound == null)
                    report.Node(s, create.Relation, $"Table {PgTreeFacts.QualifiedName(create.Relation)} uses INHERITS; for partitioning, use declarative partitioning.");
            }
            foreach (var command in s.All<AlterTableCmd>())
            {
                if (command.Subtype == AlterTableType.AtAddInherit)
                    report.Statement(s, "ALTER TABLE ... INHERIT attaches a child table; for partitioning, use declarative partitioning (ATTACH PARTITION).");
            }
        }
    }
}
