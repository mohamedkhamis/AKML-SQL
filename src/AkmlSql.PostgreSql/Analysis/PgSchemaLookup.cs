using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using PgSqlParser;

namespace AkmlSql.PostgreSql.Analysis;

/// <summary>
/// Spec 042 (task T162) - resolves the names a script uses against the connection's schema cache, the way PostgreSQL does: a
/// qualified table name names its schema; an unqualified one is looked for in the search path, in order. A table whose columns
/// have not been loaded yet is reported as unknown, never as an empty table, so a rule that needs the columns stays silent
/// until it can be right.
/// </summary>
internal static class PgSchemaLookup
{
    /// <summary>The table or view a range variable names, with its columns loaded; null when the cache does not know it yet.</summary>
    public static DatabaseObject? FindRelation(DatabaseCache cache, string? schema, string name)
    {
        if (!string.IsNullOrEmpty(schema)) return Loaded(cache.FindObject(schema, name));

        var path = cache.SearchPath.Count > 0 ? cache.SearchPath : new List<string> { "public" };
        foreach (var entry in path)
        {
            var found = Loaded(cache.FindObject(entry, name));
            if (found != null) return found;
        }
        return null;
    }

    private static DatabaseObject? Loaded(DatabaseObject? table) => table is { ColumnsLoaded: true } ? table : null;

    public static DatabaseObject? FindRelation(DatabaseCache cache, RangeVar relation) =>
        FindRelation(cache, relation.Schemaname, relation.Relname);

    public static Column? FindColumn(DatabaseObject table, string name) =>
        table.Columns.FirstOrDefault(c => string.Equals(c.ColumnName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The column a reference names among the given tables, or null when it cannot be told: the name is not in the cache, or an
    /// unqualified name could belong to more than one table. A qualified reference matches the table's alias, or its name when it has none.
    /// </summary>
    public static Column? ResolveColumn(DatabaseCache cache, IReadOnlyList<RangeVar> relations, ColumnRef reference)
    {
        var parts = PgTreeFacts.Parts(reference);
        if (parts.Count is < 1 or > 2 || parts[^1] == "*") return null;

        var name = parts[^1];
        if (parts.Count == 2)
        {
            foreach (var relation in relations)
            {
                var exposed = relation.Alias?.Aliasname is { Length: > 0 } a ? a : relation.Relname;
                if (!string.Equals(exposed, parts[0], StringComparison.OrdinalIgnoreCase)) continue;
                var table = FindRelation(cache, relation);
                return table == null ? null : FindColumn(table, name);
            }
            return null;
        }

        Column? only = null;
        foreach (var relation in relations)
        {
            var table = FindRelation(cache, relation);
            if (table == null) return null;     // an unknown table might hold the name too
            var column = FindColumn(table, name);
            if (column == null) continue;
            if (only != null) return null;      // ambiguous
            only = column;
        }
        return only;
    }
}
