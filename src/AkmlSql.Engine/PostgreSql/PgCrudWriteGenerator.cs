using System.Text;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>One positional parameter of a generated statement: the cell's text (null is SQL NULL) and the type it is cast to.</summary>
public sealed record PgCrudParameter(string? Value, string TypeName);

/// <summary>
/// A generated write: the statement, its parameters in <c>$1..$n</c> order, and the columns it returns from the written row
/// (<c>RETURNING</c>), in order; none for a delete or when the client asked for none.
/// </summary>
public sealed record PgCrudStatement(string Sql, IReadOnlyList<PgCrudParameter> Parameters, IReadOnlyList<string> ReturnedColumns)
{
    public bool ReturnsRow => ReturnedColumns.Count > 0;
}

/// <summary>
/// Spec 042 (task T139, FR-131, FR-132, research R11). Builds the PostgreSQL statement for one grid edit, the counterpart of the
/// SQL Server <c>CrudWriteGenerator</c>. Every value is a parameter and is cast to its column's type (<c>$1::numeric(10,2)</c>), so
/// the server reads the text the grid shows (a date as PostgreSQL prints it, an array as <c>{1,2}</c>) and a value that does not fit
/// the type is the server's own error. Identifiers are quoted only when they need it. A NULL key cell is <c>IS NULL</c>, because
/// <c>= NULL</c> matches nothing. An UPDATE or DELETE with no key is refused: there is no safe WHERE, so a whole-table write cannot
/// be produced here at all.
///
/// <para>
/// The cast type is the one piece of client text that lands in the statement, so it must pass <see cref="IsSafeTypeName"/>, a
/// grammar that allows type names as PostgreSQL prints them (<c>format_type</c>) and nothing that can end the cast or start another
/// statement.
/// </para>
/// </summary>
public static class PgCrudWriteGenerator
{
    private const int MaxTypeNameLength = 256;

    public static PgCrudStatement Build(ApplyChangesRequest request, CrudEditDto edit)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (edit == null) throw new ArgumentNullException(nameof(edit));
        if (string.IsNullOrWhiteSpace(request.BaseTable))
            throw new InvalidOperationException("Cannot build a CRUD command without a base table name.");

        var table = PgIdentifierQuoting.QualifiedName(string.IsNullOrEmpty(request.BaseSchema) ? null : request.BaseSchema, request.BaseTable);

        return edit.Op switch
        {
            CrudOp.Update => BuildUpdate(table, edit, request.ReturnColumns),
            CrudOp.Insert => BuildInsert(table, edit, request.ReturnColumns),
            CrudOp.Delete => BuildDelete(table, edit),
            _ => throw new InvalidOperationException($"Unknown CRUD op {edit.Op}."),
        };
    }

    private static PgCrudStatement BuildUpdate(string table, CrudEditDto edit, string[]? returnColumns)
    {
        if (edit.KeyCells.Length == 0)
            throw new InvalidOperationException("Refusing to UPDATE without a key column (would update the whole table).");
        if (edit.SetCells.Length == 0)
            throw new InvalidOperationException("UPDATE has no columns to set.");

        var sql = new StringBuilder("UPDATE ").Append(table).Append(" SET ");
        var parameters = new List<PgCrudParameter>();
        for (var i = 0; i < edit.SetCells.Length; i++)
        {
            if (i > 0) sql.Append(", ");
            sql.Append(PgIdentifierQuoting.Quote(edit.SetCells[i].BaseColumnName)).Append(" = ").Append(Placeholder(edit.SetCells[i], parameters));
        }
        AppendWhere(sql, parameters, edit.KeyCells);
        var returned = AppendReturning(sql, returnColumns);
        return new PgCrudStatement(sql.ToString(), parameters, returned);
    }

    private static PgCrudStatement BuildInsert(string table, CrudEditDto edit, string[]? returnColumns)
    {
        if (edit.SetCells.Length == 0)
            throw new InvalidOperationException("INSERT has no column values.");

        var columns = new StringBuilder();
        var values = new StringBuilder();
        var parameters = new List<PgCrudParameter>();
        for (var i = 0; i < edit.SetCells.Length; i++)
        {
            if (i > 0) { columns.Append(", "); values.Append(", "); }
            columns.Append(PgIdentifierQuoting.Quote(edit.SetCells[i].BaseColumnName));
            values.Append(Placeholder(edit.SetCells[i], parameters));
        }

        var sql = new StringBuilder("INSERT INTO ").Append(table).Append(" (").Append(columns).Append(") VALUES (").Append(values).Append(')');
        var returned = AppendReturning(sql, returnColumns);
        return new PgCrudStatement(sql.ToString(), parameters, returned);
    }

    private static PgCrudStatement BuildDelete(string table, CrudEditDto edit)
    {
        if (edit.KeyCells.Length == 0)
            throw new InvalidOperationException("Refusing to DELETE without a key column (would delete the whole table).");

        var sql = new StringBuilder("DELETE FROM ").Append(table);
        var parameters = new List<PgCrudParameter>();
        AppendWhere(sql, parameters, edit.KeyCells);
        return new PgCrudStatement(sql.ToString(), parameters, Array.Empty<string>());
    }

    /// <summary>
    /// <c> RETURNING a, b, c</c> over the columns the client asked for (the result set's own, so a column the login may not read
    /// is never asked for). Returns the names, in order; nothing is appended for an empty list.
    /// </summary>
    private static IReadOnlyList<string> AppendReturning(StringBuilder sql, string[]? returnColumns)
    {
        var names = (returnColumns ?? Array.Empty<string>()).Where(c => !string.IsNullOrEmpty(c)).ToArray();
        if (names.Length == 0) return Array.Empty<string>();
        sql.Append(" RETURNING ").Append(string.Join(", ", names.Select(PgIdentifierQuoting.Quote)));
        return names;
    }

    private static void AppendWhere(StringBuilder sql, List<PgCrudParameter> parameters, CrudCellDto[] keys)
    {
        sql.Append(" WHERE ");
        for (var i = 0; i < keys.Length; i++)
        {
            if (i > 0) sql.Append(" AND ");
            sql.Append(PgIdentifierQuoting.Quote(keys[i].BaseColumnName));
            // A NULL key must use IS NULL: "= NULL" never matches, and the write would then touch no row.
            if (keys[i].Value is null) sql.Append(" IS NULL");
            else sql.Append(" = ").Append(Placeholder(keys[i], parameters));
        }
    }

    /// <summary>Adds the cell as the next parameter and returns its <c>$n::type</c> text.</summary>
    private static string Placeholder(CrudCellDto cell, List<PgCrudParameter> parameters)
    {
        if (string.IsNullOrWhiteSpace(cell.TypeName))
            throw new InvalidOperationException($"Column '{cell.BaseColumnName}' carried no type, so its value cannot be converted.");
        if (!IsSafeTypeName(cell.TypeName))
            throw new InvalidOperationException($"Column '{cell.BaseColumnName}' carried a type name that is not accepted.");

        parameters.Add(new PgCrudParameter(cell.Value, cell.TypeName!));
        return "$" + parameters.Count + "::" + CastType(cell.TypeName!);
    }

    /// <summary>
    /// The type as a cast can name it. The driver reports a column's type in display form, without quotes (<c>public.Mood</c>,
    /// <c>Mood[]</c>), which the server would fold to lower case; a name that is not already lower case is quoted part by part,
    /// with the schema, the modifier and the array brackets left outside the quotes. A lower-case name is written as it came,
    /// so the built-in types with spaces (<c>character varying(5)</c>, <c>timestamp(3) with time zone</c>) stay as they are.
    /// </summary>
    internal static string CastType(string typeName)
    {
        var name = typeName.Trim();
        var suffixAt = name.IndexOfAny(new[] { '(', '[' });        // "(10,2)" and "[]" belong after the name
        var suffix = suffixAt < 0 ? string.Empty : name[suffixAt..];
        var bare = suffixAt < 0 ? name : name[..suffixAt];

        var dot = bare.IndexOf('.');
        if (dot < 0) return QuoteIfNeeded(bare) + suffix;
        return QuoteIfNeeded(bare[..dot]) + "." + QuoteIfNeeded(bare[(dot + 1)..]) + suffix;
    }

    private static string QuoteIfNeeded(string part)
    {
        if (part.Length == 0 || part[0] == '"') return part;
        var needsQuotes = false;
        foreach (var c in part)
        {
            if (char.IsLower(c) || char.IsDigit(c) || c is '_' or ' ') continue;
            needsQuotes = true;
            break;
        }
        return needsQuotes ? "\"" + part.Replace("\"", "\"\"") + "\"" : part;
    }

    /// <summary>
    /// True for a type name as PostgreSQL prints it - <c>numeric(10,2)</c>, <c>character varying(40)</c>, <c>timestamp(3) with time
    /// zone</c>, <c>integer[]</c>, <c>public.mood</c>, <c>"Sales"."order status"</c> - and for nothing that could end the cast or
    /// begin another statement. Outside double quotes only letters, digits, underscore, space, dot, comma and round and square
    /// brackets are allowed (so no quote, semicolon, comment marker, backslash, dollar sign or line break); inside double quotes
    /// anything goes, as in PostgreSQL, with a doubled quote standing for one; every quote must close.
    /// </summary>
    public static bool IsSafeTypeName(string? type)
    {
        if (string.IsNullOrWhiteSpace(type) || type.Length > MaxTypeNameLength) return false;

        var inQuote = false;
        var quoted = 0;      // characters in the current quoted identifier
        var hasName = false;

        for (var i = 0; i < type.Length; i++)
        {
            var c = type[i];
            if (inQuote)
            {
                if (c == '"')
                {
                    if (i + 1 < type.Length && type[i + 1] == '"') { i++; quoted++; continue; }
                    if (quoted == 0) return false;     // "" is not an identifier
                    inQuote = false;
                    hasName = true;
                    continue;
                }
                quoted++;
                continue;
            }

            if (c == '"') { inQuote = true; quoted = 0; continue; }
            if (char.IsLetter(c)) { hasName = true; continue; }
            if (char.IsDigit(c) || c is '_' or ' ' or '.' or ',' or '(' or ')' or '[' or ']') continue;
            return false;
        }

        return !inQuote && hasName;
    }
}
