using System;
using System.Collections.Generic;

namespace AkmlSql.Engine.Completion.Providers;

/// <summary>
/// Shared helpers for FK-based completion providers (JoinProvider, JoinOnFkProvider).
/// </summary>
internal static class FkHelpers
{
    /// <summary>
    /// Splits "schema.table" into (schema, table). Defaults to ("dbo", name) for unqualified names.
    /// </summary>
    public static (string schema, string table) SplitName(string fullName)
    {
        var parts = fullName.Split('.');
        return parts.Length >= 2
            ? (parts[0], parts[1])
            : ("dbo", parts[0]);
    }

    /// <summary>
    /// Builds an FK equality predicate: "left.col = right.col" or multi-column with AND.
    /// </summary>
    /// <remarks>
    /// <paramref name="leftAlias"/> and <paramref name="rightAlias"/> must already be valid T-SQL
    /// references -- callers quote them, because only the caller knows whether a reference is a
    /// single identifier (an alias, safe to bracket) or an already-qualified <c>dbo.[Order Details]</c>
    /// that bracketing again would mangle. The COLUMNS are always single identifiers, so they are
    /// quoted here.
    /// </remarks>
    public static string BuildFkPredicate(
        string leftAlias, List<string> leftColumns,
        string rightAlias, List<string> rightColumns)
    {
        var count = Math.Min(leftColumns.Count, rightColumns.Count);
        if (count == 1)
            return $"{ColumnRef(leftAlias, leftColumns[0])} = {ColumnRef(rightAlias, rightColumns[0])}";

        var parts = new List<string>(count);
        for (int i = 0; i < count; i++)
            parts.Add($"{ColumnRef(leftAlias, leftColumns[i])} = {ColumnRef(rightAlias, rightColumns[i])}");
        return string.Join(" AND ", parts);
    }

    /// <summary>
    /// <c>reference.column</c>, bracketing the column when it needs it. <paramref name="reference"/>
    /// must already be valid (see <see cref="BuildFkPredicate"/>).
    /// </summary>
    public static string ColumnRef(string reference, string column) =>
        $"{reference}.{SqlIdentifier.QuoteIfNeeded(column)}";
}
