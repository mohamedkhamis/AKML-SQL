using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Productivity;
using AkmlSql.PostgreSql.Language;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (task T062) - the statements of a PostgreSQL script as the editor needs them, for "execute current statement"
/// and next / previous statement. The split is the one a run uses (<see cref="PgStatementSplitter"/>), so what is
/// highlighted is exactly what would be sent; which statement a cursor belongs to follows the same rule as for SQL Server.
/// </summary>
public static class PgStatementBoundaries
{
    /// <summary>Every statement, in script order; empty for an empty script.</summary>
    public static StatementRangeDto[] GetAll(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return Array.Empty<StatementRangeDto>();

        var ranges = new List<StatementRangeDto>();
        foreach (var statement in PgStatementSplitter.Split(sql))
        {
            ranges.Add(new StatementRangeDto
            {
                StartOffset = statement.Start,
                EndOffset = statement.End,
                // The editor counts lines from 0; the splitter from 1.
                StartLine = statement.StartLine - 1,
                EndLine = PgPositions.ToLineColumn(sql, statement.End).Line - 1,
                StatementType = TypeName(statement),
            });
        }
        return ranges.ToArray();
    }

    /// <summary>The statement the cursor belongs to, or null when the script has none.</summary>
    public static StatementRangeDto? FindAtOffset(string sql, int cursorOffset)
    {
        var all = GetAll(sql);
        return all.Length == 0 ? null : StatementBoundaryDetector.Select(all, cursorOffset);
    }

    // "SelectStatement", "CreateStatement": the first word of the statement, as the SQL Server names read.
    private static string TypeName(PgStatement statement)
    {
        if (statement.IsMetaCommand) return "MetaCommand";

        var tag = PgCommandTag.For(statement.Text, PgStatementType.Other, 0).Tag;
        var first = tag.Split(' ', 2)[0];
        if (first.Length == 0) return "Statement";
        return char.ToUpperInvariant(first[0]) + first.Substring(1).ToLowerInvariant() + "Statement";
    }
}
