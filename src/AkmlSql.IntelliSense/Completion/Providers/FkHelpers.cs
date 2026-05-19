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
    public static string BuildFkPredicate(
        string leftAlias, List<string> leftColumns,
        string rightAlias, List<string> rightColumns)
    {
        var count = Math.Min(leftColumns.Count, rightColumns.Count);
        if (count == 1)
            return $"{leftAlias}.{leftColumns[0]} = {rightAlias}.{rightColumns[0]}";

        var parts = new List<string>(count);
        for (int i = 0; i < count; i++)
            parts.Add($"{leftAlias}.{leftColumns[i]} = {rightAlias}.{rightColumns[i]}");
        return string.Join(" AND ", parts);
    }
}
