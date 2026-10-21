using AkmlSql.Core.Models.Analysis;
using PgSqlParser;

namespace AkmlSql.PostgreSql.Analysis.Rules.Design;

// Spec 042 (task T167, FR-150, FR-153): the design rules PGDE001 to PGDE008. They look at the types and constraints that
// definitions (CREATE TABLE, ALTER TABLE ... ADD / ALTER COLUMN, composite types) give.

/// <summary>Reports a column type by name; the six type rules differ only in the name, the severity and the advice.</summary>
public abstract class ColumnTypeRule : PgRuleBase
{
    private readonly Func<TypeName, bool> _matches;

    protected ColumnTypeRule(string ruleId, string name, DiagnosticSeverity severity, string description, string manualSection, Func<TypeName, bool> matches)
        : base(ruleId, "Design", name, severity, description, manualSection) => _matches = matches;

    /// <summary>The finding for a column of the matching type; <paramref name="column"/> is its name.</summary>
    protected abstract string Message(string column);

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var column in PgTreeFacts.ColumnDefinitions(s))
            {
                if (column.TypeName != null && _matches(column.TypeName))
                    report.Node(s, column.TypeName, Message(column.Colname));
            }
        }
    }
}

/// <summary>PGDE001 - timestamp without time zone.</summary>
public sealed class Pgde001TimestampWithoutZone : ColumnTypeRule
{
    public Pgde001TimestampWithoutZone() : base("PGDE001", "timestamp without time zone", DiagnosticSeverity.Warning,
        "timestamp stores a date and time with no zone and returns it unchanged, so what it means depends on the session's TimeZone of whoever wrote it. "
        + "timestamptz stores an absolute instant (in UTC) and shows it in the reader's zone; PostgreSQL's own wiki recommends it for almost every use.",
        "datatype-datetime", t => PgTreeFacts.TypeLower(t) == "timestamp") { }

    protected override string Message(string column) => $"Column {column} is timestamp without time zone; timestamptz stores an absolute instant.";
}

/// <summary>PGDE002 - char(n).</summary>
public sealed class Pgde002FixedLengthChar : ColumnTypeRule
{
    public Pgde002FixedLengthChar() : base("PGDE002", "char(n) column", DiagnosticSeverity.Information,
        "char(n) pads every value with spaces to n characters and has no speed or storage advantage in PostgreSQL; it is usually slower because of the padding "
        + "and surprises comparisons and concatenation. text or varchar(n) is almost always what is meant.",
        "datatype-character", t => PgTreeFacts.TypeLower(t) == "bpchar") { }

    protected override string Message(string column) => $"Column {column} is char(n), which pads values with spaces; use text or varchar(n).";
}

/// <summary>PGDE003 - money.</summary>
public sealed class Pgde003MoneyType : ColumnTypeRule
{
    public Pgde003MoneyType() : base("PGDE003", "money column", DiagnosticSeverity.Warning,
        "money has a fixed fractional precision taken from the lc_monetary setting of the session, so the same value can be read or restored differently on another server. "
        + "numeric(p, s) holds an exact amount with a precision you choose.",
        "datatype-money", t => PgTreeFacts.TypeLower(t) == "money") { }

    protected override string Message(string column) => $"Column {column} is money, whose precision depends on lc_monetary; use numeric(p, s).";
}

/// <summary>PGDE007 - json.</summary>
public sealed class Pgde007JsonType : ColumnTypeRule
{
    public Pgde007JsonType() : base("PGDE007", "json column", DiagnosticSeverity.Information,
        "json stores the text as written and re-parses it on every access; it cannot be indexed or compared. jsonb stores a decomposed binary form, supports "
        + "GIN indexes and containment operators, and is what almost every application wants.",
        "datatype-json", t => PgTreeFacts.TypeLower(t) == "json") { }

    protected override string Message(string column) => $"Column {column} is json; jsonb can be indexed and queried efficiently.";
}

/// <summary>PGDE008 - time with time zone.</summary>
public sealed class Pgde008TimeWithZone : ColumnTypeRule
{
    public Pgde008TimeWithZone() : base("PGDE008", "timetz column", DiagnosticSeverity.Information,
        "time with time zone attaches a zone offset to a time of day, which has no meaning without a date (daylight saving cannot be applied). "
        + "The manual itself discourages it: use time, or timestamptz when the instant matters.",
        "datatype-datetime", t => PgTreeFacts.TypeLower(t) == "timetz") { }

    protected override string Message(string column) => $"Column {column} is time with time zone, which the manual discourages; use time or timestamptz.";
}

/// <summary>PGDE004 - serial pseudo-types.</summary>
public sealed class Pgde004SerialType : PgRuleBase
{
    private static readonly HashSet<string> SerialTypes = new(StringComparer.Ordinal) { "serial", "serial2", "serial4", "serial8", "smallserial", "bigserial" };

    public Pgde004SerialType() : base("PGDE004", "Design", "serial column", DiagnosticSeverity.Information,
        "serial is shorthand for a sequence plus a default, owned by the column only by convention: a copied table keeps using the original sequence, and the "
        + "privileges on the sequence must be granted separately. GENERATED ... AS IDENTITY (PostgreSQL 10 and later) is the standard form and avoids both.",
        "datatype-numeric") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var column in PgTreeFacts.ColumnDefinitions(s))
            {
                if (column.TypeName is { Names.Count: 1 } type && SerialTypes.Contains(PgTreeFacts.TypeLower(type)))
                    report.Node(s, type, $"Column {column.Colname} uses {PgTreeFacts.TypeLower(type)}; prefer GENERATED ALWAYS AS IDENTITY.");
            }
        }
    }
}

/// <summary>PGDE005 - a table with no primary key.</summary>
public sealed class Pgde005NoPrimaryKey : PgRuleBase
{
    public Pgde005NoPrimaryKey() : base("PGDE005", "Design", "Table without a primary key", DiagnosticSeverity.Warning,
        "Without a primary key, rows cannot be told apart, duplicates are accepted, logical replication cannot replicate UPDATE and DELETE, and tools cannot edit the rows by key. "
        + "Partitions (which take the key of their parent), typed tables, temporary tables and tables created LIKE another table are not reported.",
        "ddl-constraints") { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        foreach (var s in ctx.Statements)
        {
            foreach (var create in s.All<CreateStmt>())
            {
                if (create.Partbound != null || create.OfTypename != null || create.Relation.Relpersistence == "t" || create.TableElts.Any(e => e.TableLikeClause != null)) continue;
                if (HasPrimaryKey(create)) continue;
                report.Node(s, create.Relation, $"Table {PgTreeFacts.QualifiedName(create.Relation)} has no primary key.");
            }
        }
    }

    private static bool HasPrimaryKey(CreateStmt create) =>
        create.TableElts.Any(e => e.Constraint?.Contype == ConstrType.ConstrPrimary
            || e.ColumnDef?.Constraints.Any(c => c.Constraint?.Contype == ConstrType.ConstrPrimary) == true)
        || create.Constraints.Any(c => c.Constraint?.Contype == ConstrType.ConstrPrimary);
}

/// <summary>PGDE006 - a foreign key whose referencing columns no index covers.</summary>
public sealed class Pgde006UnindexedForeignKey : PgRuleBase
{
    public Pgde006UnindexedForeignKey() : base("PGDE006", "Design", "Foreign key without an index", DiagnosticSeverity.Warning,
        "PostgreSQL indexes the referenced (primary key) side of a foreign key but not the referencing side. Deleting or updating a row of the referenced table then scans the whole "
        + "referencing table, and joins on the key are slow. An index whose leading columns are the key's columns fixes both. Needs the schema to see existing indexes.",
        "ddl-constraints", requiresSchema: true) { }

    protected override void Run(PgAnalysisContext ctx, PgReporter report)
    {
        var cache = ctx.SchemaCache;
        if (cache == null) return;

        var scriptTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scriptIndexes = new List<(string Table, List<string> Columns)>();
        foreach (var s in ctx.Statements)
        {
            foreach (var create in s.All<CreateStmt>())
            {
                scriptTables.Add(create.Relation.Relname);
                foreach (var (columns, _) in KeyConstraints(create)) scriptIndexes.Add((create.Relation.Relname, columns));
            }
            foreach (var index in s.All<IndexStmt>())
            {
                var columns = index.IndexParams.Select(p => p.IndexElem?.Name ?? string.Empty).ToList();
                if (columns.Count > 0 && columns.All(c => c.Length > 0)) scriptIndexes.Add((index.Relation.Relname, columns));
            }
        }

        foreach (var s in ctx.Statements)
        {
            foreach (var (relation, constraint, columns) in ForeignKeys(s))
            {
                var covered = scriptIndexes.Any(i => string.Equals(i.Table, relation.Relname, StringComparison.OrdinalIgnoreCase) && Covers(i.Columns, columns));

                var table = PgSchemaLookup.FindRelation(cache, relation);
                if (table != null)
                    covered |= table.Indexes.Any(i => Covers(i.Columns.Select(c => c.ColumnName).ToList(), columns));
                else if (!scriptTables.Contains(relation.Relname))
                    continue;       // an existing table the cache has not loaded: its indexes are unknown, so say nothing

                if (!covered)
                    report.Node(s, constraint, $"Foreign key ({string.Join(", ", columns)}) on {PgTreeFacts.QualifiedName(relation)} has no index; deleting or updating the referenced rows scans the table.");
            }
        }
    }

    // An index serves the key when its leading columns are exactly the key's columns, in any order.
    private static bool Covers(IReadOnlyList<string> index, IReadOnlyList<string> key) =>
        index.Count >= key.Count
        && index.Take(key.Count).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).SequenceEqual(key.OrderBy(c => c, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    // The primary key and unique constraints of a CREATE TABLE, each as the list of its columns.
    private static IEnumerable<(List<string> Columns, Constraint Constraint)> KeyConstraints(CreateStmt create)
    {
        foreach (var element in create.TableElts)
        {
            if (element.Constraint is { } table && table.Contype is ConstrType.ConstrPrimary or ConstrType.ConstrUnique)
                yield return (PgTreeFacts.Names(table.Keys), table);
            if (element.ColumnDef is { } column)
            {
                foreach (var c in column.Constraints)
                {
                    if (c.Constraint is { } inline && inline.Contype is ConstrType.ConstrPrimary or ConstrType.ConstrUnique)
                        yield return (new List<string> { column.Colname }, inline);
                }
            }
        }
    }

    // Every foreign key the statement defines: the table it is on, the constraint node, and the referencing columns.
    private static IEnumerable<(RangeVar Relation, Constraint Constraint, List<string> Columns)> ForeignKeys(PgScriptStatement s)
    {
        foreach (var create in s.All<CreateStmt>())
        {
            foreach (var element in create.TableElts)
            {
                if (element.Constraint is { Contype: ConstrType.ConstrForeign } table)
                    yield return (create.Relation, table, PgTreeFacts.Names(table.FkAttrs));
                if (element.ColumnDef is { } column)
                {
                    foreach (var c in column.Constraints)
                    {
                        if (c.Constraint is { Contype: ConstrType.ConstrForeign } inline)
                            yield return (create.Relation, inline, new List<string> { column.Colname });
                    }
                }
            }
        }

        foreach (var alter in s.All<AlterTableStmt>())
        {
            foreach (var command in alter.Cmds.Select(c => c.AlterTableCmd))
            {
                if (command?.Def?.Constraint is { Contype: ConstrType.ConstrForeign } added && command.Subtype == AlterTableType.AtAddConstraint)
                    yield return (alter.Relation, added, PgTreeFacts.Names(added.FkAttrs));
                if (command?.Def?.ColumnDef is { } column)
                {
                    foreach (var c in column.Constraints)
                    {
                        if (c.Constraint is { Contype: ConstrType.ConstrForeign } inline)
                            yield return (alter.Relation, inline, new List<string> { column.Colname });
                    }
                }
            }
        }
    }
}
