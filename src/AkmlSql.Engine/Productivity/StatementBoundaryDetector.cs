#nullable enable
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using Serilog;

namespace AkmlSql.Engine.Productivity;

/// <summary>
/// Parses SQL text and detects statement boundaries using the ScriptDom AST.
/// Provides both single-statement lookup (by cursor offset) and full-document enumeration.
/// </summary>
public class StatementBoundaryDetector
{
    private readonly TsqlParserService _parserService;

    public StatementBoundaryDetector(TsqlParserService parserService)
    {
        _parserService = parserService ?? throw new ArgumentNullException(nameof(parserService));
    }

    /// <summary>
    /// Returns the statement range containing the given cursor offset, or <c>null</c>
    /// if the cursor is not inside any statement.
    /// </summary>
    public StatementRangeDto? FindStatementAtOffset(string sql, int cursorOffset)
    {
        var allStatements = GetAllStatements(sql);
        if (allStatements == null || allStatements.Length == 0)
            return null;

        foreach (var stmt in allStatements)
        {
            if (cursorOffset >= stmt.StartOffset && cursorOffset <= stmt.EndOffset)
                return stmt;
        }

        // If cursor is between statements, find the nearest one before the cursor
        StatementRangeDto? nearest = null;
        int minDistance = int.MaxValue;
        foreach (var stmt in allStatements)
        {
            int distance = Math.Abs(cursorOffset - stmt.StartOffset);
            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = stmt;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Returns all statement ranges in the SQL text, ordered by start offset.
    /// </summary>
    public StatementRangeDto[]? GetAllStatements(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            return null;

        try
        {
            var script = _parserService.Parse(sql, out var errors);
            if (script == null || script.Batches.Count == 0)
            {
                Log.Debug("StatementBoundaryDetector: parse returned no batches ({ErrorCount} errors)",
                    errors?.Count ?? 0);
                return null;
            }

            var ranges = new List<StatementRangeDto>();

            foreach (var batch in script.Batches)
            {
                foreach (var statement in batch.Statements)
                {
                    CollectStatementRange(statement, sql, ranges);
                }
            }

            // Sort by StartOffset for consistent ordering
            ranges.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
            return ranges.ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "StatementBoundaryDetector: failed to parse SQL");
            return null;
        }
    }

    /// <summary>
    /// Extracts the range for a single top-level statement (does not recurse into
    /// BEGIN...END blocks or IF...ELSE — those are treated as a single statement).
    /// </summary>
    private static void CollectStatementRange(TSqlStatement statement, string sql, List<StatementRangeDto> ranges)
    {
        var startOffset = statement.StartOffset;
        var endOffset = statement.StartOffset + statement.FragmentLength;

        // Clamp to SQL length
        if (endOffset > sql.Length)
            endOffset = sql.Length;

        var startLine = statement.StartLine - 1; // ScriptDom is 1-based, we use 0-based
        var endLine = ComputeEndLine(sql, startOffset, endOffset, startLine);

        ranges.Add(new StatementRangeDto
        {
            StartOffset = startOffset,
            EndOffset = endOffset,
            StartLine = startLine,
            EndLine = endLine,
            StatementType = GetStatementTypeName(statement)
        });
    }

    /// <summary>
    /// Computes the 0-based end line by counting newlines from startOffset to endOffset.
    /// </summary>
    private static int ComputeEndLine(string sql, int startOffset, int endOffset, int startLine)
    {
        int line = startLine;
        for (int i = startOffset; i < endOffset && i < sql.Length; i++)
        {
            if (sql[i] == '\n')
                line++;
        }
        return line;
    }

    /// <summary>
    /// Returns a human-friendly type name for the statement AST node.
    /// </summary>
    private static string GetStatementTypeName(TSqlStatement statement)
    {
        // Use the concrete type name directly; strip the "Statement" suffix for brevity
        var typeName = statement.GetType().Name;
        return typeName;
    }
}
