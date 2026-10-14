using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema.Models;

namespace AkmlSql.PostgreSql.Completion.Providers;

/// <summary>
/// Spec 042 (task T100, FR-061, scenario US3-1) - schemas and the relations of the database where a relation name is expected:
/// <c>FROM</c>, <c>JOIN</c>, the target of INSERT, UPDATE, DELETE and MERGE, <c>TRUNCATE</c>, <c>DROP TABLE</c>,
/// <c>CREATE INDEX ON</c>, <c>REFERENCES</c>. Objects on the session's search path are offered unqualified and first; objects
/// of other schemas are offered schema-qualified; a name that needs it is double-quoted.
/// </summary>
internal sealed class PgSchemaObjectProvider : IPgCompletionProvider
{
    private static readonly HashSet<PgClause> ReadPositions = new() { PgClause.From, PgClause.Join, PgClause.Using, PgClause.MergeSource };

    private static readonly HashSet<PgClause> WritePositions = new() { PgClause.InsertTarget, PgClause.UpdateTarget, PgClause.DeleteTarget, PgClause.MergeTarget };

    public void Collect(PgCompletionInput input, List<CompletionItem> items)
    {
        var c = input.Context;
        if (c.AfterOperand && !c.AfterDot) return;

        var read = ReadPositions.Contains(c.Clause);
        var write = WritePositions.Contains(c.Clause);
        var ddl = c.Clause == PgClause.DdlRelation;
        if (!read && !write && !ddl) return;

        var catalog = input.Catalog;

        // `schema.` : only that schema's objects, unqualified
        if (c.AfterDot)
        {
            if (c.DotChain.Count != 1) return;
            var wanted = c.DotChain[0];
            foreach (var schema in catalog.Schemas().Concat(IncludeSystem(input)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!wanted.Matches(schema)) continue;
                foreach (var o in catalog.Objects(schema))
                    if (Allowed(o, read, write, ddl))
                        items.Add(Make(o, display: o.ObjectName, qualified: false, c, catalog, PgItems.TableOnSearchPath));
            }
            return;
        }

        var related = read && c.Clause == PgClause.Join && input.Options.JoinAssist ? RelatedTables(input) : null;

        foreach (var o in catalog.VisibleObjects())
        {
            if (!Allowed(o, read, write, ddl)) continue;
            var onPath = catalog.InSearchPath(o.SchemaName);
            var priority = onPath ? PgItems.TableOnSearchPath : PgItems.TableElsewhere;
            if (related != null && related.Contains(o.SchemaName + "." + o.ObjectName)) priority = PgItems.RelatedTable;
            items.Add(Make(o, catalog.Display(o), qualified: !onPath, c, catalog, priority));
        }

        foreach (var schema in catalog.Schemas())
        {
            items.Add(PgItems.Make(schema, PgCatalogView.Written(schema, c.PrefixQuoted), CompletionObjectType.Schema, "schema", PgItems.Schema, source: schema, filter: schema));
        }
    }

    private static IEnumerable<string> IncludeSystem(PgCompletionInput input) =>
        new[] { "pg_catalog", "information_schema" }.Where(s => input.Catalog.Objects(s).Any());

    private static bool Allowed(DatabaseObject o, bool read, bool write, bool ddl)
    {
        if (o.ObjectType is DbObjectType.Procedure or DbObjectType.ScalarFunction or DbObjectType.TableFunction or DbObjectType.InlineFunction or DbObjectType.Synonym)
            return false;
        if (read || ddl) return true;
        // INSERT / UPDATE / DELETE / MERGE can write to tables and views; a sequence or a materialized view cannot be written to
        return write && o.ObjectType is DbObjectType.Table or DbObjectType.View;
    }

    private static CompletionItem Make(DatabaseObject o, string display, bool qualified, PgCursorContext c, PgCatalogView catalog, int priority)
    {
        var insert = qualified
            ? PgCatalogView.WrittenQualified(o.SchemaName, o.ObjectName, c.PrefixQuoted)
            : PgCatalogView.Written(o.ObjectName, c.PrefixQuoted);

        var kind = o.ObjectType switch
        {
            DbObjectType.View => "view",
            DbObjectType.MaterializedView => "materialized view",
            DbObjectType.Sequence => "sequence",
            _ => "table",
        };
        var secondary = string.IsNullOrWhiteSpace(o.Description) ? kind : kind + " - " + o.Description;

        return PgItems.Make(
            display,
            insert,
            o.ObjectType is DbObjectType.View or DbObjectType.MaterializedView ? CompletionObjectType.View : CompletionObjectType.Table,
            secondary,
            priority,
            source: o.SchemaName,
            filter: o.ObjectName);
    }

    // Tables that have a foreign key to or from a table already in the query: the likely next JOIN.
    private static HashSet<string>? RelatedTables(PgCompletionInput input)
    {
        var c = input.Context;
        var cache = input.Catalog;
        if (!cache.HasCache || c.VisibleTables.Count == 0) return null;

        var related = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in c.VisibleTables)
        {
            if (t.Name == null) continue;
            var resolved = cache.Resolve(t.Schema, t.Name);
            if (resolved == null) continue;
            foreach (var fk in input.Catalog.ForeignKeysOf(resolved))
            {
                related.Add(fk.ParentSchema + "." + fk.ParentTable);
                related.Add(fk.ReferencedSchema + "." + fk.ReferencedTable);
            }
        }
        return related;
    }
}

/// <summary>
/// Spec 042 (task T100) - the names of things the statement defines: CTEs where a relation is expected, and the aliases (or
/// table names) that can qualify a column where an expression is expected.
/// </summary>
internal sealed class PgAliasCteProvider : IPgCompletionProvider
{
    private static readonly HashSet<PgClause> RelationPositions = new() { PgClause.From, PgClause.Join, PgClause.Using, PgClause.MergeSource };

    private static readonly HashSet<PgClause> ColumnPositions = new()
    {
        PgClause.SelectList, PgClause.Where, PgClause.Having, PgClause.GroupBy, PgClause.OrderBy, PgClause.JoinOn, PgClause.Set,
        PgClause.Returning, PgClause.MergeAction,
    };

    public void Collect(PgCompletionInput input, List<CompletionItem> items)
    {
        var c = input.Context;
        if (c.AfterDot || c.AfterOperand) return;

        if (RelationPositions.Contains(c.Clause))
        {
            foreach (var cte in c.VisibleCtes)
            {
                var name = cte.Name ?? string.Empty;
                items.Add(PgItems.Make(name, PgCatalogView.Written(name, c.PrefixQuoted), CompletionObjectType.Table, "CTE", PgItems.AliasOrCte, source: "cte", filter: name));
            }
            return;
        }

        if (!ColumnPositions.Contains(c.Clause)) return;

        var qualifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in c.VisibleTables)
        {
            var q = t.Qualifier;
            if (q.Length == 0 || !qualifiers.Add(q)) continue;
            items.Add(PgItems.Make(q, PgCatalogView.Written(q, c.PrefixQuoted), CompletionObjectType.Alias, AliasKind(t), PgItems.AliasOrCte, source: t.Name ?? string.Empty, filter: q));
        }
        if (c.ExcludedAvailable)
            items.Add(PgItems.Make("excluded", "excluded", CompletionObjectType.Alias, "the row proposed for insertion", PgItems.AliasOrCte, filter: "excluded"));
    }

    private static string AliasKind(PgTableRef t) => t.Kind switch
    {
        PgTableRefKind.Derived => "subquery",
        PgTableRefKind.Function => "function",
        PgTableRefKind.Values => "values",
        PgTableRefKind.Cte => "CTE",
        _ => t.Alias.Length > 0 ? "alias of " + (t.Schema != null ? t.Schema + "." : string.Empty) + t.Name : "table",
    };
}
