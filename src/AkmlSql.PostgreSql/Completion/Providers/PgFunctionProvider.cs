using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema.Models;
using AkmlSql.PostgreSql.Reference;

namespace AkmlSql.PostgreSql.Completion.Providers;

/// <summary>
/// Spec 042 (task T100, FR-061) - functions in expression positions and set-returning functions in <c>FROM</c>: the built-ins
/// from the reference (cut to the server's release) and the user-defined functions and procedures from the schema cache.
/// </summary>
internal sealed class PgFunctionProvider : IPgCompletionProvider
{
    // The everyday functions are ranked above the long tail so they survive the suggestion cap.
    private static readonly string[] TierA = { "count", "sum", "avg", "min", "max", "coalesce", "now", "lower", "upper", "length", "nullif", "substring" };

    private static readonly string[] TierB =
    {
        "trim", "round", "abs", "greatest", "least", "concat", "replace", "to_char", "date_trunc", "extract", "string_agg", "array_agg", "row_number",
        "pg_sleep", "pg_typeof",
    };

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "count", "sum", "avg", "min", "max", "coalesce", "nullif", "greatest", "least", "now", "lower", "upper", "length", "substring",
        "trim", "replace", "concat", "concat_ws", "to_char", "to_date", "to_timestamp", "date_trunc", "extract", "round", "abs", "ceil",
        "floor", "random", "string_agg", "array_agg", "json_agg", "jsonb_agg", "row_number", "rank", "dense_rank", "lag", "lead",
        "generate_series", "unnest", "jsonb_build_object", "left", "right", "position", "split_part", "regexp_replace", "format",
        "age", "date_part", "current_setting", "gen_random_uuid", "pg_size_pretty", "pg_relation_size", "pg_total_relation_size",
        "pg_database_size", "pg_get_viewdef", "pg_terminate_backend", "pg_cancel_backend", "pg_sleep", "pg_typeof",
    };

    private static int Tier(string name) =>
        TierA.Contains(name, StringComparer.OrdinalIgnoreCase) ? PgItems.CommonFunction :
        TierB.Contains(name, StringComparer.OrdinalIgnoreCase) ? PgItems.CommonFunction + 1 :
        Common.Contains(name) ? PgItems.CommonFunction + 2 : PgItems.Function;

    // Positions where an expression, and so a function call, can start.
    private static readonly HashSet<PgClause> ExpressionClauses = new()
    {
        PgClause.SelectList, PgClause.Where, PgClause.Having, PgClause.GroupBy, PgClause.OrderBy, PgClause.JoinOn, PgClause.Set,
        PgClause.Values, PgClause.Returning, PgClause.Limit, PgClause.MergeAction,
    };

    public void Collect(PgCompletionInput input, List<CompletionItem> items)
    {
        var c = input.Context;
        if (c.AfterOperand || c.AfterDot && c.DotChain.Count > 1) return;

        var last = c.PrecedingWords.Count > 0 ? c.PrecedingWords[^1] : string.Empty;

        if (c.Verb == "CALL" && last == "CALL")
        {
            UserRoutines(input, items, procedures: true, functions: false, onlySetReturning: false, c);
            return;
        }

        var inFrom = c.Clause is PgClause.From or PgClause.Join or PgClause.Using or PgClause.MergeSource;
        var inExpression = ExpressionClauses.Contains(c.Clause);
        if (!inFrom && !inExpression) return;

        // `schema.` : the functions of that schema
        if (c.AfterDot)
        {
            var schema = c.DotChain[0];
            if (schema.Matches("pg_catalog") || schema.Folded == "pg_catalog")
                BuiltIns(input, items, inFrom);
            UserRoutines(input, items, procedures: false, functions: true, onlySetReturning: inFrom, c, schema);
            return;
        }

        if (!c.PrefixQuoted) BuiltIns(input, items, inFrom);
        UserRoutines(input, items, procedures: false, functions: true, onlySetReturning: inFrom, c);
    }

    private static void BuiltIns(PgCompletionInput input, List<CompletionItem> items, bool tableFunctionsOnly)
    {
        foreach (var f in input.Reference.AllCallables)
        {
            if (f.Kind == PgEntryKind.Procedure) continue;
            if (f.Kind == PgEntryKind.Window && tableFunctionsOnly) continue;
            if (tableFunctionsOnly && !f.Signatures.Any(s => s.IsSetReturning)) continue;

            items.Add(PgItems.Make(
                f.Name,
                PgCatalogView.Written(f.Name, forceQuote: false),
                CompletionObjectType.Function,
                Describe(f),
                Tier(f.Name),
                source: "pg_catalog",
                filter: f.Name));
        }
    }

    // "(text, timestamp with time zone) -> timestamp with time zone", or "aggregate -> numeric" when it has many forms.
    private static string Describe(PgReferenceEntry f)
    {
        var kind = f.Kind switch { PgEntryKind.Aggregate => "aggregate", PgEntryKind.Window => "window", _ => "function" };
        if (f.Signatures.Count == 0) return kind;
        var first = f.Signatures[0];
        var text = f.Signatures.Count == 1 ? first.ArgumentText : "(" + f.Signatures.Count + " forms)";
        return first.Returns is { Length: > 0 } r ? $"{kind} {text} -> {r}" : $"{kind} {text}";
    }

    private static void UserRoutines(PgCompletionInput input, List<CompletionItem> items, bool procedures, bool functions, bool onlySetReturning, PgCursorContext c, PgName? schemaFilter = null)
    {
        var catalog = input.Catalog;
        foreach (var o in catalog.VisibleObjects())
        {
            var isProcedure = o.ObjectType == DbObjectType.Procedure;
            var isFunction = o.ObjectType is DbObjectType.ScalarFunction or DbObjectType.TableFunction or DbObjectType.InlineFunction;
            if (!(isProcedure && procedures) && !(isFunction && functions)) continue;
            if (onlySetReturning && o.ObjectType != DbObjectType.TableFunction) continue;
            if (schemaFilter is { } wanted && !wanted.Matches(o.SchemaName)) continue;
            if (catalog.IsSystemSchema(o.SchemaName) && !input.Options.IncludeSystemObjects && schemaFilter == null) continue;

            var bare = schemaFilter != null;
            var display = bare ? o.ObjectName : catalog.Display(o);
            var insert = bare || catalog.InSearchPath(o.SchemaName)
                ? PgCatalogView.Written(o.ObjectName, c.PrefixQuoted)
                : PgCatalogView.WrittenQualified(o.SchemaName, o.ObjectName, c.PrefixQuoted);
            var args = "(" + string.Join(", ", o.Parameters.Where(p => !p.IsOutput).Select(p => string.IsNullOrEmpty(p.ParameterName) ? p.TypeName : p.ParameterName + " " + p.TypeName)) + ")";
            items.Add(PgItems.Make(
                display,
                insert,
                isProcedure ? CompletionObjectType.Procedure : CompletionObjectType.Function,
                (isProcedure ? "procedure " : "function ") + args,
                catalog.InSearchPath(o.SchemaName) ? PgItems.TableOnSearchPath : PgItems.TableElsewhere,
                source: o.SchemaName,
                filter: o.ObjectName));
        }
    }
}

/// <summary>
/// Spec 042 (task T100) - data types after <c>::</c>, in <c>CAST (x AS</c>, and in column and parameter definitions: the names
/// and the aliases from the reference (<c>integer</c>, <c>int</c>, <c>int4</c>; <c>timestamp with time zone</c>,
/// <c>timestamptz</c>).
/// </summary>
internal sealed class PgTypeProvider : IPgCompletionProvider
{
    private static readonly HashSet<string> Everyday = new(StringComparer.OrdinalIgnoreCase)
    {
        "integer", "bigint", "smallint", "text", "character varying", "boolean", "date", "timestamp", "timestamp with time zone",
        "numeric", "uuid", "jsonb", "json", "bytea", "serial", "bigserial", "double precision", "real", "interval", "time",
        "varchar", "int", "timestamptz",
    };

    public void Collect(PgCompletionInput input, List<CompletionItem> items)
    {
        var c = input.Context;
        if (c.AfterDot || c.PrefixQuoted) return;
        if (c.Clause is not (PgClause.TypePosition or PgClause.DdlColumnType)) return;

        var last = c.PrecedingWords.Count > 0 ? c.PrecedingWords[^1] : string.Empty;
        var pseudoToo = last == "RETURNS";

        foreach (var t in input.Reference.AllTypes)
        {
            if (t.Category == "pseudo" && !pseudoToo) continue;

            Add(items, t.Name, t, primary: true);
            foreach (var alias in t.Aliases)
                if (!string.Equals(alias, t.Name, StringComparison.OrdinalIgnoreCase))
                    Add(items, alias, t, primary: false);
        }
    }

    private static void Add(List<CompletionItem> items, string name, PgReferenceEntry type, bool primary)
    {
        var priority = Everyday.Contains(name) ? PgItems.TypeName : PgItems.TypeName + (primary ? 1 : 2);
        items.Add(PgItems.Make(name, name, CompletionObjectType.Keyword, primary ? type.Summary : "alias of " + type.Name, priority, source: "type", filter: name));
    }
}
