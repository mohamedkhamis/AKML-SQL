using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.PostgreSql.Reference;

namespace AkmlSql.PostgreSql.Completion;

/// <summary>What the completion engine reads from settings and the connection (spec 042 FR-052, FR-061).</summary>
public sealed class PgCompletionOptions
{
    /// <summary>The connected server's major release: entries introduced after it are not offered (FR-052).</summary>
    public int ServerMajor { get; set; } = 18;

    public int MaxSuggestions { get; set; } = 50;

    public bool FuzzyMatch { get; set; } = true;

    public bool IncludeKeywords { get; set; } = true;

    /// <summary>Offer the objects of <c>pg_catalog</c> and <c>information_schema</c> without being asked by name.</summary>
    public bool IncludeSystemObjects { get; set; }

    public bool ShowDataTypes { get; set; } = true;

    public bool ShowNullability { get; set; } = true;

    public bool ShowKeyIndicators { get; set; } = true;

    /// <summary>Offer the FK-assisted join conditions and related tables.</summary>
    public bool JoinAssist { get; set; } = true;

    /// <summary>Snippets to offer besides the built-in pack (the user's, already filtered to PostgreSQL).</summary>
    public IReadOnlyList<PgSnippetInfo> Snippets { get; set; } = Array.Empty<PgSnippetInfo>();
}

/// <summary>A snippet as the completion list shows it.</summary>
public sealed record PgSnippetInfo(string Shortcode, string Description, string Body);

/// <summary>Everything a provider needs to answer one completion request.</summary>
internal sealed class PgCompletionInput
{
    public PgCompletionInput(PgCursorContext context, DatabaseCache? cache, PgReference reference, PgCompletionOptions options)
    {
        Context = context;
        Reference = reference;
        Options = options;
        Catalog = new PgCatalogView(cache, options);
    }

    public PgCursorContext Context { get; }

    public PgReference Reference { get; }

    public PgCompletionOptions Options { get; }

    public PgCatalogView Catalog { get; }
}

/// <summary>A column as the completion list needs to describe it.</summary>
internal sealed record PgColumnInfo(string Name, string Type, bool IsPrimaryKey, bool IsNullable, string? Description);

/// <summary>
/// The schema cache seen the way PostgreSQL looks names up: through the session's search path, with the system schemas hidden
/// unless asked for. It also resolves what a table reference stands for - a table, a CTE, a derived table, a function.
/// </summary>
internal sealed class PgCatalogView
{
    private static readonly string[] SystemSchemas = { "pg_catalog", "information_schema" };

    private readonly DatabaseCache? _cache;
    private readonly PgCompletionOptions _options;

    public PgCatalogView(DatabaseCache? cache, PgCompletionOptions options)
    {
        _cache = cache;
        _options = options;
        SearchPath = cache is { SearchPath.Count: > 0 } ? cache.SearchPath : new List<string> { "public" };
    }

    public bool HasCache => _cache != null;

    /// <summary>The schemas an unqualified name is looked up in, in order (<c>current_schemas(false)</c>).</summary>
    public IReadOnlyList<string> SearchPath { get; }

    public bool IsSystemSchema(string schema) =>
        SystemSchemas.Contains(schema, StringComparer.OrdinalIgnoreCase) || schema.StartsWith("pg_", StringComparison.OrdinalIgnoreCase)
        || (_cache?.HiddenSchemas.Contains(schema, StringComparer.OrdinalIgnoreCase) ?? false);

    public bool IsInaccessible(string schema) => _cache?.InaccessibleSchemas.Contains(schema, StringComparer.OrdinalIgnoreCase) ?? false;

    /// <summary>The schemas to offer: those with objects, minus system and inaccessible ones unless the options ask for the former.</summary>
    public IEnumerable<string> Schemas()
    {
        if (_cache == null) yield break;
        foreach (var name in _cache.GetSchemaNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (IsInaccessible(name)) continue;
            if (!_options.IncludeSystemObjects && IsSystemSchema(name)) continue;
            yield return name;
        }
    }

    public IEnumerable<DatabaseObject> Objects(string schema) =>
        _cache == null || IsInaccessible(schema) ? Enumerable.Empty<DatabaseObject>() : _cache.GetObjectsInSchema(schema);

    /// <summary>Every object the user can see by default (system schemas hidden unless the options ask).</summary>
    public IEnumerable<DatabaseObject> VisibleObjects()
    {
        foreach (var schema in Schemas())
            foreach (var o in Objects(schema))
                yield return o;
    }

    /// <summary>The foreign keys a table takes part in, as the child or as the referenced side.</summary>
    public IEnumerable<ForeignKey> ForeignKeysOf(DatabaseObject o) =>
        _cache == null ? Enumerable.Empty<ForeignKey>() : _cache.GetForeignKeysForTable(o.SchemaName, o.ObjectName);

    public bool InSearchPath(string schema) => SearchPath.Contains(schema, StringComparer.OrdinalIgnoreCase);

    /// <summary>The object an unqualified or qualified name stands for, looked up the way the server would.</summary>
    public DatabaseObject? Resolve(string? schema, string name)
    {
        if (_cache == null) return null;
        if (!string.IsNullOrEmpty(schema)) return _cache.FindObject(schema!, name);
        foreach (var s in SearchPath)
        {
            var found = _cache.FindObject(s, name);
            if (found != null) return found;
        }
        return _cache.FindObject("pg_catalog", name);
    }

    /// <summary>
    /// The columns a table reference offers: the table's, a CTE's, a derived table's, a function's. A reference that names a CTE
    /// of the statement is that CTE, not a table of the same name.
    /// </summary>
    public IReadOnlyList<PgColumnInfo> ColumnsOf(PgTableRef reference, IReadOnlyList<PgTableRef> ctes, int depth = 0)
    {
        if (depth > 6) return Array.Empty<PgColumnInfo>();

        switch (reference.Kind)
        {
            case PgTableRefKind.Cte:
            case PgTableRefKind.Derived:
            case PgTableRefKind.Values:
                return Declared(reference, ctes, depth);

            case PgTableRefKind.Function:
                if (reference.Columns.Count > 0) return reference.Columns.Select(c => new PgColumnInfo(c, string.Empty, false, true, null)).ToList();
                // a function in FROM is one column named after its alias, or after the function
                return new[] { new PgColumnInfo(reference.Qualifier, string.Empty, false, true, null) };
        }

        if (reference.Schema == null && reference.Name != null)
        {
            var cte = ctes.FirstOrDefault(c => string.Equals(c.Name, reference.Name, StringComparison.Ordinal));
            if (cte != null) return Declared(reference.Columns.Count > 0 ? reference with { Columns = reference.Columns, StarSources = cte.StarSources } : cte, ctes, depth);
        }

        var table = reference.Name == null ? null : Resolve(reference.Schema, reference.Name);
        if (table == null) return Array.Empty<PgColumnInfo>();

        // a column alias list renames the leading columns
        var columns = table.Columns.OrderBy(c => c.ColumnId).Select(c => new PgColumnInfo(c.ColumnName, c.TypeName, c.IsPrimaryKey, c.IsNullable, c.Description)).ToList();
        if (reference.Columns.Count > 0)
            for (var i = 0; i < reference.Columns.Count && i < columns.Count; i++)
                columns[i] = columns[i] with { Name = reference.Columns[i] };
        return columns;
    }

    private IReadOnlyList<PgColumnInfo> Declared(PgTableRef reference, IReadOnlyList<PgTableRef> ctes, int depth)
    {
        var columns = reference.Columns.Select(c => new PgColumnInfo(c, string.Empty, false, true, null)).ToList();
        foreach (var source in reference.StarSources)
            columns.AddRange(ColumnsOf(source, ctes, depth + 1));
        return columns;
    }

    // ── Names ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>The name as it is written in a statement: double-quoted only when it has to be, or when the word typed so far began with a quote.</summary>
    public static string Written(string name, bool forceQuote) =>
        forceQuote ? "\"" + name.Replace("\"", "\"\"") + "\"" : PgIdentifierQuoting.Quote(name);

    public static string WrittenQualified(string? schema, string name, bool forceQuote) =>
        string.IsNullOrEmpty(schema) ? Written(name, forceQuote) : Written(schema!, forceQuote) + "." + Written(name, forceQuote);

    /// <summary>How an object is named in the list: bare when its schema is on the search path, schema-qualified otherwise.</summary>
    public string Display(DatabaseObject o) => InSearchPath(o.SchemaName) ? o.ObjectName : o.SchemaName + "." + o.ObjectName;
}

/// <summary>Builds <see cref="CompletionItem"/>s with the conventions of the SQL Server engine: priorities, filter text, object types.</summary>
internal static class PgItems
{
    // Lower sorts first; the numbers follow the SQL Server engine's tiers so the two lists feel alike.
    public const int JoinCondition = 5;
    public const int RelatedTable = 10;
    public const int Column = 30;
    public const int TypeName = 40;
    public const int AliasOrCte = 50;
    public const int TableOnSearchPath = 100;
    public const int TableElsewhere = 200;
    public const int Schema = 300;
    public const int Snippet = 500;
    public const int Keyword = 500;
    public const int CompoundKeyword = 510;
    public const int CommonFunction = 512;
    public const int Function = 520;

    public static CompletionItem Make(string display, string insert, CompletionObjectType type, string secondary, int priority, string source = "", string? filter = null) => new()
    {
        DisplayText = display,
        InsertText = insert,
        ObjectType = (int)type,
        SecondaryText = secondary,
        SourceObject = source,
        SortPriority = priority,
        FilterText = filter,
    };

    public static CompletionItem Keyword_(string word, int priority = Keyword) =>
        Make(word, word, CompletionObjectType.Keyword, "Keyword", priority);
}
