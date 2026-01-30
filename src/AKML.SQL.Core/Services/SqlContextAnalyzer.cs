using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Analyzes SQL context for intelligent completions.
/// Implements Story 5.1: Context-Aware IntelliSense.
/// </summary>
public class SqlContextAnalyzer
{
    private readonly ILogger<SqlContextAnalyzer> _logger;
    private readonly TSql160Parser _parser;

    public SqlContextAnalyzer(ILogger<SqlContextAnalyzer> logger)
    {
        _logger = logger;
        _parser = new TSql160Parser(initialQuotedIdentifiers: true);
    }

    /// <summary>
    /// Analyzes SQL text at a specific position to determine completion context.
    /// </summary>
    public SqlContext AnalyzeContext(string sql, int offset)
    {
        var context = new SqlContext
        {
            Offset = offset,
            ContextType = SqlContextType.General
        };

        if (string.IsNullOrEmpty(sql) || offset < 0)
            return context;

        try
        {
            // Get immediate context from text before cursor
            var textBefore = sql.Substring(0, Math.Min(offset, sql.Length));
            context.FilterText = GetFilterText(textBefore);
            context.ContextType = DetermineContextFromText(textBefore, offset);

            // Check for dot notation (table.column)
            if (context.ContextType == SqlContextType.AfterDot)
            {
                context.TableOrAliasPrefix = GetTablePrefix(textBefore);
            }

            // Parse SQL to get AST information
            ParseSqlForContext(sql, offset, context);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error analyzing SQL context at offset {Offset}", offset);
        }

        return context;
    }

    /// <summary>
    /// Gets all table aliases defined in the SQL statement containing the offset.
    /// </summary>
    public Dictionary<string, TableAliasInfo> GetTableAliases(string sql, int offset)
    {
        var aliases = new Dictionary<string, TableAliasInfo>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(sql))
            return aliases;

        try
        {
            using var reader = new StringReader(sql);
            var fragment = _parser.Parse(reader, out _);

            if (fragment is TSqlScript script)
            {
                var visitor = new TableAliasVisitor(offset);
                script.Accept(visitor);

                foreach (var alias in visitor.Aliases)
                {
                    aliases[alias.Alias] = alias;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error parsing SQL for aliases");
        }

        return aliases;
    }

    /// <summary>
    /// Gets the statement at the specified offset.
    /// </summary>
    public StatementInfo? GetStatementAtOffset(string sql, int offset)
    {
        if (string.IsNullOrEmpty(sql))
            return null;

        try
        {
            using var reader = new StringReader(sql);
            var fragment = _parser.Parse(reader, out _);

            if (fragment is TSqlScript script)
            {
                foreach (var batch in script.Batches)
                {
                    foreach (var statement in batch.Statements)
                    {
                        if (offset >= statement.StartOffset &&
                            offset <= statement.StartOffset + statement.FragmentLength)
                        {
                            return ExtractStatementInfo(statement);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error getting statement at offset");
        }

        return null;
    }

    private void ParseSqlForContext(string sql, int offset, SqlContext context)
    {
        using var reader = new StringReader(sql);
        var fragment = _parser.Parse(reader, out var errors);

        if (fragment is TSqlScript script)
        {
            // Find the statement containing the cursor
            foreach (var batch in script.Batches)
            {
                foreach (var statement in batch.Statements)
                {
                    if (offset >= statement.StartOffset &&
                        offset <= statement.StartOffset + statement.FragmentLength)
                    {
                        context.CurrentStatement = ExtractStatementInfo(statement);

                        // Get table references for alias resolution
                        var tableVisitor = new TableAliasVisitor(offset);
                        statement.Accept(tableVisitor);
                        context.AvailableTables.AddRange(tableVisitor.Aliases);

                        break;
                    }
                }
            }
        }
    }

    private SqlContextType DetermineContextFromText(string textBefore, int offset)
    {
        if (string.IsNullOrEmpty(textBefore))
            return SqlContextType.General;

        var trimmed = textBefore.TrimEnd();

        // Check for dot notation
        if (trimmed.EndsWith("."))
            return SqlContextType.AfterDot;

        // Check for operators that suggest column context
        if (trimmed.EndsWith("=") || trimmed.EndsWith("<") || trimmed.EndsWith(">") ||
            trimmed.EndsWith("<=") || trimmed.EndsWith(">=") || trimmed.EndsWith("<>") ||
            trimmed.EndsWith("!="))
            return SqlContextType.AfterOperator;

        // Get last significant keyword
        var upperText = textBefore.ToUpperInvariant();
        var words = upperText.Split(new[] { ' ', '\t', '\n', '\r', ',', '(', ')' },
            StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
            return SqlContextType.General;

        // Find the last SQL keyword
        var lastKeyword = FindLastKeyword(words);

        return lastKeyword switch
        {
            "SELECT" => SqlContextType.AfterSelect,
            "FROM" => SqlContextType.AfterFrom,
            "JOIN" or "INNER" or "LEFT" or "RIGHT" or "CROSS" or "OUTER" => SqlContextType.AfterJoin,
            "ON" => SqlContextType.AfterOn,
            "WHERE" => SqlContextType.AfterWhere,
            "AND" or "OR" => SqlContextType.AfterCondition,
            "ORDER" => SqlContextType.AfterOrderBy,
            "GROUP" => SqlContextType.AfterGroupBy,
            "HAVING" => SqlContextType.AfterHaving,
            "SET" => SqlContextType.AfterSet,
            "INSERT" => SqlContextType.AfterInsert,
            "INTO" => SqlContextType.AfterInto,
            "VALUES" => SqlContextType.AfterValues,
            "UPDATE" => SqlContextType.AfterUpdate,
            "DELETE" => SqlContextType.AfterDelete,
            "EXEC" or "EXECUTE" => SqlContextType.AfterExec,
            "AS" => SqlContextType.AfterAs,
            "BY" => GetByContext(words),
            _ => SqlContextType.General
        };
    }

    private SqlContextType GetByContext(string[] words)
    {
        // Look for what preceded BY (ORDER or GROUP)
        for (int i = words.Length - 2; i >= 0; i--)
        {
            if (words[i].Equals("GROUP", StringComparison.OrdinalIgnoreCase))
                return SqlContextType.AfterGroupBy;
            if (words[i].Equals("ORDER", StringComparison.OrdinalIgnoreCase))
                return SqlContextType.AfterOrderBy;
        }
        return SqlContextType.General;
    }

    private string FindLastKeyword(string[] words)
    {
        var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "FROM", "WHERE", "JOIN", "INNER", "LEFT", "RIGHT", "CROSS", "OUTER",
            "ON", "AND", "OR", "ORDER", "GROUP", "HAVING", "SET", "INSERT", "INTO",
            "VALUES", "UPDATE", "DELETE", "EXEC", "EXECUTE", "AS", "BY"
        };

        for (int i = words.Length - 1; i >= 0; i--)
        {
            if (keywords.Contains(words[i]))
                return words[i];
        }

        return string.Empty;
    }

    private bool IsAfterOrderOrGroup(string[] words)
    {
        for (int i = words.Length - 2; i >= 0; i--)
        {
            if (words[i].Equals("ORDER", StringComparison.OrdinalIgnoreCase) ||
                words[i].Equals("GROUP", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private string GetFilterText(string textBefore)
    {
        if (string.IsNullOrEmpty(textBefore))
            return string.Empty;

        int end = textBefore.Length;
        int start = end;

        while (start > 0 && IsIdentifierChar(textBefore[start - 1]))
            start--;

        return textBefore.Substring(start, end - start);
    }

    private string GetTablePrefix(string textBefore)
    {
        if (string.IsNullOrEmpty(textBefore))
            return string.Empty;

        // Find the position before the dot
        int dotPos = textBefore.LastIndexOf('.');
        if (dotPos <= 0)
            return string.Empty;

        int start = dotPos - 1;
        while (start > 0 && IsIdentifierChar(textBefore[start - 1]))
            start--;

        return textBefore.Substring(start, dotPos - start);
    }

    private bool IsIdentifierChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '_' || c == '@' || c == '#' || c == '$';
    }

    private StatementInfo ExtractStatementInfo(TSqlStatement statement)
    {
        var info = new StatementInfo
        {
            StartOffset = statement.StartOffset,
            Length = statement.FragmentLength,
            Type = GetStatementType(statement)
        };

        // Extract table references
        var tableVisitor = new TableReferenceExtractor();
        statement.Accept(tableVisitor);
        info.Tables.AddRange(tableVisitor.Tables);

        return info;
    }

    private StatementType GetStatementType(TSqlStatement statement)
    {
        return statement switch
        {
            SelectStatement => StatementType.Select,
            InsertStatement => StatementType.Insert,
            UpdateStatement => StatementType.Update,
            DeleteStatement => StatementType.Delete,
            ExecuteStatement => StatementType.Execute,
            MergeStatement => StatementType.Merge,
            _ => StatementType.Other
        };
    }

    /// <summary>
    /// Visitor to extract table references with aliases.
    /// </summary>
    private class TableAliasVisitor : TSqlFragmentVisitor
    {
        private readonly int _offset;
        public List<TableAliasInfo> Aliases { get; } = new();

        public TableAliasVisitor(int offset)
        {
            _offset = offset;
        }

        public override void Visit(NamedTableReference node)
        {
            var info = new TableAliasInfo
            {
                SchemaName = node.SchemaObject.SchemaIdentifier?.Value ?? "dbo",
                TableName = node.SchemaObject.BaseIdentifier?.Value ?? string.Empty,
                Alias = node.Alias?.Value ?? string.Empty,
                StartOffset = node.StartOffset,
                EndOffset = node.StartOffset + node.FragmentLength
            };

            // Use table name as alias if no explicit alias
            if (string.IsNullOrEmpty(info.Alias))
                info.Alias = info.TableName;

            Aliases.Add(info);
            base.Visit(node);
        }
    }

    /// <summary>
    /// Visitor to extract table references.
    /// </summary>
    private class TableReferenceExtractor : TSqlFragmentVisitor
    {
        public List<TableReferenceInfo> Tables { get; } = new();

        public override void Visit(NamedTableReference node)
        {
            Tables.Add(new TableReferenceInfo
            {
                SchemaName = node.SchemaObject.SchemaIdentifier?.Value ?? "dbo",
                TableName = node.SchemaObject.BaseIdentifier?.Value ?? string.Empty,
                Alias = node.Alias?.Value ?? string.Empty
            });
            base.Visit(node);
        }
    }
}

/// <summary>
/// Context information for SQL completion.
/// </summary>
public class SqlContext
{
    public int Offset { get; set; }
    public SqlContextType ContextType { get; set; }
    public string FilterText { get; set; } = string.Empty;
    public string? TableOrAliasPrefix { get; set; }
    public StatementInfo? CurrentStatement { get; set; }
    public List<TableAliasInfo> AvailableTables { get; set; } = new();
}

/// <summary>
/// Types of SQL context for completion.
/// </summary>
public enum SqlContextType
{
    General,
    AfterSelect,
    AfterFrom,
    AfterJoin,
    AfterOn,
    AfterWhere,
    AfterCondition,
    AfterOrderBy,
    AfterGroupBy,
    AfterHaving,
    AfterSet,
    AfterInsert,
    AfterInto,
    AfterValues,
    AfterUpdate,
    AfterDelete,
    AfterExec,
    AfterAs,
    AfterDot,
    AfterOperator
}

/// <summary>
/// Information about a table alias.
/// </summary>
public class TableAliasInfo
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }

    public string FullName => $"{SchemaName}.{TableName}";
}

/// <summary>
/// Information about a SQL statement.
/// </summary>
public class StatementInfo
{
    public int StartOffset { get; set; }
    public int Length { get; set; }
    public StatementType Type { get; set; }
    public List<TableReferenceInfo> Tables { get; set; } = new();
}

/// <summary>
/// Types of SQL statements.
/// </summary>
public enum StatementType
{
    Select,
    Insert,
    Update,
    Delete,
    Execute,
    Merge,
    Other
}

/// <summary>
/// Information about a table reference.
/// </summary>
public class TableReferenceInfo
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
}
