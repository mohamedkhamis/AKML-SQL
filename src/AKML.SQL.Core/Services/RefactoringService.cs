using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Service for SQL refactoring operations.
/// Implements Story 7.1: SQL Refactoring Tools.
/// </summary>
public class RefactoringService : IRefactoringService
{
    private readonly ILogger<RefactoringService> _logger;
    private readonly TSql160Parser _parser;

    public RefactoringService(ILogger<RefactoringService> logger)
    {
        _logger = logger;
        _parser = new TSql160Parser(initialQuotedIdentifiers: true);
    }

    /// <summary>
    /// Renames a table alias throughout the SQL statement.
    /// </summary>
    public RefactoringResult RenameAlias(string sql, string oldAlias, string newAlias, int? offset = null)
    {
        if (string.IsNullOrEmpty(sql))
            return RefactoringResult.Failure("SQL text cannot be empty");

        if (string.IsNullOrEmpty(oldAlias))
            return RefactoringResult.Failure("Old alias cannot be empty");

        if (string.IsNullOrEmpty(newAlias))
            return RefactoringResult.Failure("New alias cannot be empty");

        if (oldAlias.Equals(newAlias, StringComparison.OrdinalIgnoreCase))
            return RefactoringResult.Failure("New alias must be different from old alias");

        try
        {
            using var reader = new StringReader(sql);
            var fragment = _parser.Parse(reader, out var errors);

            if (errors?.Count > 0)
                return RefactoringResult.Failure($"SQL has parse errors: {errors[0].Message}");

            // Find all occurrences of the alias
            var visitor = new AliasRenameVisitor(oldAlias, newAlias, offset);
            fragment.Accept(visitor);

            if (visitor.Replacements.Count == 0)
                return RefactoringResult.Failure($"Alias '{oldAlias}' not found");

            // Apply replacements in reverse order
            var result = ApplyReplacements(sql, visitor.Replacements);

            _logger.LogInformation("Renamed alias '{OldAlias}' to '{NewAlias}', {Count} occurrences",
                oldAlias, newAlias, visitor.Replacements.Count);

            return RefactoringResult.Success(result, visitor.Replacements.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error renaming alias");
            return RefactoringResult.Failure($"Refactoring error: {ex.Message}");
        }
    }

    /// <summary>
    /// Extracts a subquery to a Common Table Expression (CTE).
    /// </summary>
    public RefactoringResult ExtractToCte(string sql, int startOffset, int endOffset, string cteName)
    {
        if (string.IsNullOrEmpty(sql))
            return RefactoringResult.Failure("SQL text cannot be empty");

        if (string.IsNullOrEmpty(cteName))
            return RefactoringResult.Failure("CTE name cannot be empty");

        if (startOffset < 0 || endOffset <= startOffset || endOffset > sql.Length)
            return RefactoringResult.Failure("Invalid offset range");

        try
        {
            // Extract the subquery text
            var subquery = sql.Substring(startOffset, endOffset - startOffset).Trim();

            // Remove parentheses if present
            if (subquery.StartsWith("(") && subquery.EndsWith(")"))
                subquery = subquery.Substring(1, subquery.Length - 2).Trim();

            // Validate the subquery is a SELECT
            using var reader = new StringReader(subquery);
            var fragment = _parser.Parse(reader, out var errors);

            if (errors?.Count > 0)
                return RefactoringResult.Failure($"Selected text is not valid SQL: {errors[0].Message}");

            // Check if it's a SELECT statement
            if (fragment is TSqlScript script && script.Batches.Count > 0)
            {
                var statement = script.Batches[0].Statements.FirstOrDefault();
                if (statement is not SelectStatement)
                    return RefactoringResult.Failure("Selected text must be a SELECT statement");
            }

            // Build the CTE
            var cte = $"WITH {cteName} AS (\n    {subquery.Replace("\n", "\n    ")}\n)";

            // Find where to insert the CTE and replace the subquery
            var beforeSelection = sql.Substring(0, startOffset);
            var afterSelection = sql.Substring(endOffset);

            // Check if there's already a WITH clause
            var upperSql = sql.ToUpperInvariant();
            var withIndex = FindTopLevelWith(sql);

            string result;
            if (withIndex >= 0 && withIndex < startOffset)
            {
                // Add to existing WITH clause
                var insertPoint = FindCteInsertPoint(sql, withIndex);
                result = sql.Substring(0, insertPoint) +
                         $",\n{cteName} AS (\n    {subquery.Replace("\n", "\n    ")}\n)" +
                         sql.Substring(insertPoint, startOffset - insertPoint) +
                         cteName +
                         afterSelection;
            }
            else
            {
                // Find the start of the main query to insert CTE before it
                var mainQueryStart = FindMainQueryStart(sql);
                result = sql.Substring(0, mainQueryStart) +
                         cte + "\n" +
                         sql.Substring(mainQueryStart, startOffset - mainQueryStart) +
                         cteName +
                         afterSelection;
            }

            _logger.LogInformation("Extracted subquery to CTE '{CteName}'", cteName);
            return RefactoringResult.Success(result, 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting to CTE");
            return RefactoringResult.Failure($"Refactoring error: {ex.Message}");
        }
    }

    /// <summary>
    /// Qualifies all column references with table aliases.
    /// </summary>
    public RefactoringResult QualifyColumnNames(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return RefactoringResult.Failure("SQL text cannot be empty");

        try
        {
            using var reader = new StringReader(sql);
            var fragment = _parser.Parse(reader, out var errors);

            if (errors?.Count > 0)
                return RefactoringResult.Failure($"SQL has parse errors: {errors[0].Message}");

            var visitor = new ColumnQualifierVisitor();
            fragment.Accept(visitor);

            if (visitor.UnqualifiedColumns.Count == 0)
                return RefactoringResult.Success(sql, 0, "All columns are already qualified");

            // For columns that can be uniquely resolved, add qualifications
            var replacements = new List<TextReplacement>();
            foreach (var column in visitor.UnqualifiedColumns.OrderByDescending(c => c.Offset))
            {
                if (!string.IsNullOrEmpty(column.ResolvedTableAlias))
                {
                    replacements.Add(new TextReplacement
                    {
                        StartOffset = column.Offset,
                        Length = column.ColumnName.Length,
                        NewText = $"{column.ResolvedTableAlias}.{column.ColumnName}"
                    });
                }
            }

            var result = ApplyReplacements(sql, replacements);

            _logger.LogInformation("Qualified {Count} column references", replacements.Count);
            return RefactoringResult.Success(result, replacements.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error qualifying columns");
            return RefactoringResult.Failure($"Refactoring error: {ex.Message}");
        }
    }

    /// <summary>
    /// Expands SELECT * to explicit column list.
    /// </summary>
    public RefactoringResult ExpandSelectStar(string sql, IEnumerable<string> columnNames, int starOffset)
    {
        if (string.IsNullOrEmpty(sql))
            return RefactoringResult.Failure("SQL text cannot be empty");

        var columns = columnNames?.ToList();
        if (columns == null || columns.Count == 0)
            return RefactoringResult.Failure("Column names must be provided");

        if (starOffset < 0 || starOffset >= sql.Length)
            return RefactoringResult.Failure("Invalid star offset");

        try
        {
            // Find the extent of the star (could be t.* or just *)
            var startOffset = starOffset;
            var endOffset = starOffset + 1;

            // Check for table-qualified star (t.*)
            if (startOffset > 0 && sql[startOffset - 1] == '.')
            {
                startOffset--;
                while (startOffset > 0 && IsIdentifierChar(sql[startOffset - 1]))
                    startOffset--;
            }

            var expandedColumns = string.Join(",\n    ", columns);
            var result = sql.Substring(0, startOffset) + expandedColumns + sql.Substring(endOffset);

            _logger.LogInformation("Expanded SELECT * to {Count} columns", columns.Count);
            return RefactoringResult.Success(result, columns.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error expanding SELECT *");
            return RefactoringResult.Failure($"Refactoring error: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts a correlated subquery to a JOIN.
    /// </summary>
    public RefactoringResult ConvertSubqueryToJoin(string sql, int subqueryOffset)
    {
        if (string.IsNullOrEmpty(sql))
            return RefactoringResult.Failure("SQL text cannot be empty");

        try
        {
            using var reader = new StringReader(sql);
            var fragment = _parser.Parse(reader, out var errors);

            if (errors?.Count > 0)
                return RefactoringResult.Failure($"SQL has parse errors: {errors[0].Message}");

            var visitor = new SubqueryToJoinVisitor(subqueryOffset);
            fragment.Accept(visitor);

            if (!visitor.CanConvert)
                return RefactoringResult.Failure(visitor.FailureReason ?? "Subquery cannot be converted to JOIN");

            // Build the JOIN clause
            var joinClause = $"LEFT JOIN (\n    {visitor.SubqueryText}\n) AS {visitor.SuggestedAlias} ON {visitor.JoinCondition}";

            // Apply the transformation
            var result = sql.Substring(0, visitor.SubqueryStart) +
                         visitor.ReplacementExpression +
                         sql.Substring(visitor.SubqueryEnd);

            // Insert the JOIN
            var joinInsertPoint = FindJoinInsertPoint(result, visitor.FromClauseEnd);
            result = result.Substring(0, joinInsertPoint) + "\n" + joinClause + result.Substring(joinInsertPoint);

            _logger.LogInformation("Converted subquery to JOIN");
            return RefactoringResult.Success(result, 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error converting subquery to JOIN");
            return RefactoringResult.Failure($"Refactoring error: {ex.Message}");
        }
    }

    /// <summary>
    /// Adds table alias to a table reference.
    /// </summary>
    public RefactoringResult AddTableAlias(string sql, int tableOffset, string alias)
    {
        if (string.IsNullOrEmpty(sql))
            return RefactoringResult.Failure("SQL text cannot be empty");

        if (string.IsNullOrEmpty(alias))
            return RefactoringResult.Failure("Alias cannot be empty");

        try
        {
            using var reader = new StringReader(sql);
            var fragment = _parser.Parse(reader, out var errors);

            if (errors?.Count > 0)
                return RefactoringResult.Failure($"SQL has parse errors: {errors[0].Message}");

            var visitor = new TableReferenceFinderVisitor(tableOffset);
            fragment.Accept(visitor);

            if (visitor.FoundTable == null)
                return RefactoringResult.Failure("No table reference found at specified position");

            if (!string.IsNullOrEmpty(visitor.FoundTable.ExistingAlias))
                return RefactoringResult.Failure($"Table already has alias: {visitor.FoundTable.ExistingAlias}");

            // Insert alias after table name
            var insertPoint = visitor.FoundTable.EndOffset;
            var result = sql.Substring(0, insertPoint) + " " + alias + sql.Substring(insertPoint);

            _logger.LogInformation("Added alias '{Alias}' to table '{Table}'", alias, visitor.FoundTable.TableName);
            return RefactoringResult.Success(result, 1);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding table alias");
            return RefactoringResult.Failure($"Refactoring error: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets suggestions for refactoring at a specific position.
    /// </summary>
    public List<RefactoringSuggestion> GetSuggestions(string sql, int offset)
    {
        var suggestions = new List<RefactoringSuggestion>();

        if (string.IsNullOrEmpty(sql) || offset < 0)
            return suggestions;

        try
        {
            using var reader = new StringReader(sql);
            var fragment = _parser.Parse(reader, out var errors);

            if (errors?.Count > 0)
                return suggestions;

            var visitor = new RefactoringSuggestionVisitor(offset);
            fragment.Accept(visitor);

            suggestions.AddRange(visitor.Suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting refactoring suggestions");
        }

        return suggestions;
    }

    #region Helper Methods

    private string ApplyReplacements(string sql, List<TextReplacement> replacements)
    {
        var sb = new StringBuilder(sql);
        foreach (var replacement in replacements.OrderByDescending(r => r.StartOffset))
        {
            sb.Remove(replacement.StartOffset, replacement.Length);
            sb.Insert(replacement.StartOffset, replacement.NewText);
        }
        return sb.ToString();
    }

    private int FindTopLevelWith(string sql)
    {
        var upperSql = sql.ToUpperInvariant();
        var index = 0;

        while (index < sql.Length)
        {
            var withIndex = upperSql.IndexOf("WITH", index, StringComparison.Ordinal);
            if (withIndex < 0)
                return -1;

            // Check if it's at the start or after whitespace
            if (withIndex == 0 || char.IsWhiteSpace(sql[withIndex - 1]))
            {
                // Check if it's followed by whitespace (not "WITHOUT" etc.)
                if (withIndex + 4 < sql.Length && char.IsWhiteSpace(sql[withIndex + 4]))
                    return withIndex;
            }

            index = withIndex + 1;
        }

        return -1;
    }

    private int FindCteInsertPoint(string sql, int withIndex)
    {
        // Find the end of the last CTE before the main SELECT
        var depth = 0;
        var inCte = false;

        for (int i = withIndex + 4; i < sql.Length; i++)
        {
            if (sql[i] == '(')
            {
                depth++;
                inCte = true;
            }
            else if (sql[i] == ')')
            {
                depth--;
                if (depth == 0 && inCte)
                {
                    // Check if there's a comma after (another CTE follows)
                    var j = i + 1;
                    while (j < sql.Length && char.IsWhiteSpace(sql[j]))
                        j++;

                    if (j < sql.Length && sql[j] == ',')
                    {
                        inCte = false;
                        continue;
                    }

                    // This is the end of the CTE list
                    return i + 1;
                }
            }
        }

        return withIndex + 4;
    }

    private int FindMainQueryStart(string sql)
    {
        var upperSql = sql.ToUpperInvariant();
        var keywords = new[] { "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE" };

        foreach (var keyword in keywords)
        {
            var index = upperSql.IndexOf(keyword, StringComparison.Ordinal);
            if (index >= 0)
                return index;
        }

        return 0;
    }

    private int FindJoinInsertPoint(string sql, int fromClauseEnd)
    {
        // Find the end of FROM clause (before WHERE, GROUP BY, etc.)
        var upperSql = sql.ToUpperInvariant();
        var endKeywords = new[] { "WHERE", "GROUP", "HAVING", "ORDER", "UNION", "EXCEPT", "INTERSECT" };

        var insertPoint = sql.Length;
        foreach (var keyword in endKeywords)
        {
            var index = upperSql.IndexOf(keyword, fromClauseEnd, StringComparison.Ordinal);
            if (index > fromClauseEnd && index < insertPoint)
                insertPoint = index;
        }

        return insertPoint;
    }

    private bool IsIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#';
    }

    #endregion

    #region Visitor Classes

    private class AliasRenameVisitor : TSqlFragmentVisitor
    {
        private readonly string _oldAlias;
        private readonly string _newAlias;
        private readonly int? _scopeOffset;
        public List<TextReplacement> Replacements { get; } = new();

        public AliasRenameVisitor(string oldAlias, string newAlias, int? scopeOffset)
        {
            _oldAlias = oldAlias;
            _newAlias = newAlias;
            _scopeOffset = scopeOffset;
        }

        public override void Visit(NamedTableReference node)
        {
            if (node.Alias?.Value?.Equals(_oldAlias, StringComparison.OrdinalIgnoreCase) == true)
            {
                var identifier = node.Alias;
                Replacements.Add(new TextReplacement
                {
                    StartOffset = identifier.StartOffset,
                    Length = identifier.FragmentLength,
                    NewText = _newAlias
                });
            }
            base.Visit(node);
        }

        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.MultiPartIdentifier?.Identifiers?.Count >= 2)
            {
                var tableIdentifier = node.MultiPartIdentifier.Identifiers[node.MultiPartIdentifier.Identifiers.Count - 2];
                if (tableIdentifier.Value.Equals(_oldAlias, StringComparison.OrdinalIgnoreCase))
                {
                    Replacements.Add(new TextReplacement
                    {
                        StartOffset = tableIdentifier.StartOffset,
                        Length = tableIdentifier.FragmentLength,
                        NewText = _newAlias
                    });
                }
            }
            base.Visit(node);
        }
    }

    private class ColumnQualifierVisitor : TSqlFragmentVisitor
    {
        private readonly List<TableInfo> _tables = new();
        private readonly List<(ColumnReferenceExpression Node, int Offset)> _columnNodes = new();
        public List<UnqualifiedColumn> UnqualifiedColumns { get; } = new();

        public override void Visit(NamedTableReference node)
        {
            _tables.Add(new TableInfo
            {
                TableName = node.SchemaObject.BaseIdentifier?.Value ?? "",
                Alias = node.Alias?.Value ?? node.SchemaObject.BaseIdentifier?.Value ?? ""
            });
            base.Visit(node);
        }

        public override void Visit(ColumnReferenceExpression node)
        {
            if (node.ColumnType == ColumnType.Regular &&
                node.MultiPartIdentifier?.Identifiers?.Count == 1)
            {
                _columnNodes.Add((node, node.StartOffset));
            }
            base.Visit(node);
        }

        public override void ExplicitVisit(TSqlScript node)
        {
            // First pass to collect tables
            base.ExplicitVisit(node);

            // Now process columns with table info available
            foreach (var (colNode, offset) in _columnNodes)
            {
                var columnName = colNode.MultiPartIdentifier.Identifiers[0].Value;
                var resolvedAlias = _tables.Count == 1 ? _tables[0].Alias : null;

                UnqualifiedColumns.Add(new UnqualifiedColumn
                {
                    ColumnName = columnName,
                    Offset = offset,
                    ResolvedTableAlias = resolvedAlias
                });
            }
        }

        private class TableInfo
        {
            public string TableName { get; set; } = "";
            public string Alias { get; set; } = "";
        }
    }

    private class SubqueryToJoinVisitor : TSqlFragmentVisitor
    {
        private readonly int _targetOffset;
        public bool CanConvert { get; private set; }
        public string? FailureReason { get; private set; }
        public string SubqueryText { get; private set; } = "";
        public string SuggestedAlias { get; private set; } = "subq";
        public string JoinCondition { get; private set; } = "";
        public string ReplacementExpression { get; private set; } = "";
        public int SubqueryStart { get; private set; }
        public int SubqueryEnd { get; private set; }
        public int FromClauseEnd { get; private set; }

        public SubqueryToJoinVisitor(int targetOffset)
        {
            _targetOffset = targetOffset;
            FailureReason = "No suitable subquery found at position";
        }

        public override void Visit(ScalarSubquery node)
        {
            if (node.StartOffset <= _targetOffset && _targetOffset <= node.StartOffset + node.FragmentLength)
            {
                CanConvert = true;
                SubqueryStart = node.StartOffset;
                SubqueryEnd = node.StartOffset + node.FragmentLength;
            }
            base.Visit(node);
        }
    }

    private class TableReferenceFinderVisitor : TSqlFragmentVisitor
    {
        private readonly int _targetOffset;
        public TableReferenceResult? FoundTable { get; private set; }

        public TableReferenceFinderVisitor(int targetOffset)
        {
            _targetOffset = targetOffset;
        }

        public override void Visit(NamedTableReference node)
        {
            if (node.StartOffset <= _targetOffset && _targetOffset <= node.StartOffset + node.FragmentLength)
            {
                FoundTable = new TableReferenceResult
                {
                    TableName = node.SchemaObject.BaseIdentifier?.Value ?? "",
                    ExistingAlias = node.Alias?.Value,
                    StartOffset = node.StartOffset,
                    EndOffset = node.Alias != null
                        ? node.Alias.StartOffset + node.Alias.FragmentLength
                        : node.SchemaObject.StartOffset + node.SchemaObject.FragmentLength
                };
            }
            base.Visit(node);
        }

        public class TableReferenceResult
        {
            public string TableName { get; set; } = "";
            public string? ExistingAlias { get; set; }
            public int StartOffset { get; set; }
            public int EndOffset { get; set; }
        }
    }

    private class RefactoringSuggestionVisitor : TSqlFragmentVisitor
    {
        private readonly int _offset;
        public List<RefactoringSuggestion> Suggestions { get; } = new();

        public RefactoringSuggestionVisitor(int offset)
        {
            _offset = offset;
        }

        public override void Visit(SelectStarExpression node)
        {
            if (IsNearOffset(node))
            {
                Suggestions.Add(new RefactoringSuggestion
                {
                    Type = RefactoringType.ExpandSelectStar,
                    Title = "Expand SELECT *",
                    Description = "Replace * with explicit column list",
                    StartOffset = node.StartOffset,
                    EndOffset = node.StartOffset + node.FragmentLength
                });
            }
            base.Visit(node);
        }

        public override void Visit(NamedTableReference node)
        {
            if (IsNearOffset(node))
            {
                if (node.Alias == null)
                {
                    Suggestions.Add(new RefactoringSuggestion
                    {
                        Type = RefactoringType.AddAlias,
                        Title = "Add table alias",
                        Description = $"Add alias to {node.SchemaObject.BaseIdentifier?.Value}",
                        StartOffset = node.StartOffset,
                        EndOffset = node.StartOffset + node.FragmentLength
                    });
                }
                else
                {
                    Suggestions.Add(new RefactoringSuggestion
                    {
                        Type = RefactoringType.RenameAlias,
                        Title = "Rename alias",
                        Description = $"Rename alias '{node.Alias.Value}'",
                        StartOffset = node.Alias.StartOffset,
                        EndOffset = node.Alias.StartOffset + node.Alias.FragmentLength
                    });
                }
            }
            base.Visit(node);
        }

        public override void Visit(ScalarSubquery node)
        {
            if (IsNearOffset(node))
            {
                Suggestions.Add(new RefactoringSuggestion
                {
                    Type = RefactoringType.ExtractToCte,
                    Title = "Extract to CTE",
                    Description = "Extract subquery to Common Table Expression",
                    StartOffset = node.StartOffset,
                    EndOffset = node.StartOffset + node.FragmentLength
                });
            }
            base.Visit(node);
        }

        private bool IsNearOffset(TSqlFragment node)
        {
            return node.StartOffset <= _offset && _offset <= node.StartOffset + node.FragmentLength;
        }
    }

    #endregion
}

/// <summary>
/// Interface for refactoring service.
/// </summary>
public interface IRefactoringService
{
    RefactoringResult RenameAlias(string sql, string oldAlias, string newAlias, int? offset = null);
    RefactoringResult ExtractToCte(string sql, int startOffset, int endOffset, string cteName);
    RefactoringResult QualifyColumnNames(string sql);
    RefactoringResult ExpandSelectStar(string sql, IEnumerable<string> columnNames, int starOffset);
    RefactoringResult ConvertSubqueryToJoin(string sql, int subqueryOffset);
    RefactoringResult AddTableAlias(string sql, int tableOffset, string alias);
    List<RefactoringSuggestion> GetSuggestions(string sql, int offset);
}

/// <summary>
/// Result of a refactoring operation.
/// </summary>
public class RefactoringResult
{
    public bool IsSuccess { get; private set; }
    public string RefactoredSql { get; private set; } = string.Empty;
    public string? ErrorMessage { get; private set; }
    public string? Message { get; private set; }
    public int ChangesApplied { get; private set; }

    public static RefactoringResult Success(string sql, int changes, string? message = null)
    {
        return new RefactoringResult
        {
            IsSuccess = true,
            RefactoredSql = sql,
            ChangesApplied = changes,
            Message = message
        };
    }

    public static RefactoringResult Failure(string error)
    {
        return new RefactoringResult
        {
            IsSuccess = false,
            ErrorMessage = error
        };
    }
}

/// <summary>
/// Suggestion for a refactoring operation.
/// </summary>
public class RefactoringSuggestion
{
    public RefactoringType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
}

/// <summary>
/// Types of refactoring operations.
/// </summary>
public enum RefactoringType
{
    RenameAlias,
    ExtractToCte,
    ExpandSelectStar,
    QualifyColumns,
    ConvertSubqueryToJoin,
    AddAlias
}

/// <summary>
/// Text replacement for refactoring.
/// </summary>
internal class TextReplacement
{
    public int StartOffset { get; set; }
    public int Length { get; set; }
    public string NewText { get; set; } = string.Empty;
}

/// <summary>
/// Information about an unqualified column.
/// </summary>
internal class UnqualifiedColumn
{
    public string ColumnName { get; set; } = string.Empty;
    public int Offset { get; set; }
    public string? ResolvedTableAlias { get; set; }
}
