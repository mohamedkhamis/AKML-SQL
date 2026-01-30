using System.Runtime.CompilerServices;
using AKML.SQL.Shared.Contracts;
using AKML.SQL.Shared.Extensions;
using Microsoft.Extensions.Logging;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Code completion service providing IntelliSense suggestions.
/// </summary>
public class CompletionService : ICompletionService
{
    private readonly ILogger<CompletionService> _logger;
    private readonly IDocumentManager _documentManager;
    private readonly IMetadataService _metadataService;
    private readonly ISqlParserService _parserService;

    // SQL Keywords for completion
    private static readonly string[] SqlKeywords = new[]
    {
        "SELECT", "FROM", "WHERE", "AND", "OR", "NOT", "IN", "LIKE", "BETWEEN",
        "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "CROSS", "ON",
        "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE",
        "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "INDEX", "PROCEDURE", "FUNCTION",
        "AS", "ALIAS", "DISTINCT", "TOP", "ORDER", "BY", "ASC", "DESC",
        "GROUP", "HAVING", "UNION", "ALL", "EXCEPT", "INTERSECT",
        "NULL", "IS", "EXISTS", "ANY", "CASE", "WHEN", "THEN", "ELSE", "END",
        "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION", "TRAN",
        "EXEC", "EXECUTE", "DECLARE", "RETURN", "IF", "WHILE", "BREAK", "CONTINUE",
        "WITH", "CTE", "OVER", "PARTITION", "ROW_NUMBER", "RANK", "DENSE_RANK",
        "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "CONSTRAINT", "DEFAULT", "CHECK",
        "IDENTITY", "CLUSTERED", "NONCLUSTERED", "UNIQUE", "CASCADE",
        "GRANT", "REVOKE", "DENY", "SCHEMA", "DATABASE", "USE", "GO"
    };

    // Common SQL functions
    private static readonly (string Name, string Description)[] SqlFunctions = new[]
    {
        ("COUNT", "Returns the number of rows"),
        ("SUM", "Returns the sum of values"),
        ("AVG", "Returns the average of values"),
        ("MIN", "Returns the minimum value"),
        ("MAX", "Returns the maximum value"),
        ("COALESCE", "Returns the first non-null value"),
        ("ISNULL", "Replaces NULL with specified value"),
        ("NULLIF", "Returns NULL if arguments are equal"),
        ("CAST", "Converts expression to specified data type"),
        ("CONVERT", "Converts expression to specified data type"),
        ("LEN", "Returns the length of a string"),
        ("SUBSTRING", "Returns part of a string"),
        ("REPLACE", "Replaces occurrences in a string"),
        ("TRIM", "Removes leading and trailing spaces"),
        ("LTRIM", "Removes leading spaces"),
        ("RTRIM", "Removes trailing spaces"),
        ("UPPER", "Converts to uppercase"),
        ("LOWER", "Converts to lowercase"),
        ("GETDATE", "Returns current date and time"),
        ("GETUTCDATE", "Returns current UTC date and time"),
        ("DATEADD", "Adds interval to a date"),
        ("DATEDIFF", "Returns difference between dates"),
        ("DATEPART", "Returns part of a date"),
        ("YEAR", "Returns year from a date"),
        ("MONTH", "Returns month from a date"),
        ("DAY", "Returns day from a date"),
        ("FORMAT", "Formats a value"),
        ("ROW_NUMBER", "Returns row number in result set"),
        ("RANK", "Returns rank of a row"),
        ("DENSE_RANK", "Returns dense rank of a row"),
        ("LEAD", "Returns value from following row"),
        ("LAG", "Returns value from preceding row"),
        ("STRING_AGG", "Concatenates string values"),
        ("JSON_VALUE", "Extracts scalar value from JSON"),
        ("JSON_QUERY", "Extracts object/array from JSON")
    };

    public CompletionService(
        ILogger<CompletionService> logger,
        IDocumentManager documentManager,
        IMetadataService metadataService,
        ISqlParserService parserService)
    {
        _logger = logger;
        _documentManager = documentManager;
        _metadataService = metadataService;
        _parserService = parserService;
    }

    public async Task<List<CompletionItem>> GetCompletionsAsync(
        string documentId,
        string? text,
        int line,
        int column,
        string? triggerCharacter,
        string? connectionString,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        var items = new List<CompletionItem>();

        // Get current word being typed for filtering
        var filterText = GetFilterText(text, line, column);
        _logger.LogDebug("Getting completions for filter: '{Filter}'", filterText);

        // Determine context (after FROM, after WHERE, etc.)
        var context = DetermineContext(text, line, column);

        // Add keywords
        if (context != CompletionContext.AfterDot)
        {
            AddKeywordCompletions(items, filterText);
        }

        // Add functions
        if (context == CompletionContext.General || context == CompletionContext.AfterSelect)
        {
            AddFunctionCompletions(items, filterText);
        }

        // Add schema objects if connected
        if (!string.IsNullOrEmpty(connectionString))
        {
            await AddSchemaCompletionsAsync(items, context, filterText, connectionString, cancellationToken);
        }

        // Sort by relevance
        var sorted = items
            .OrderByDescending(i => i.RelevanceScore)
            .ThenBy(i => i.SortText ?? i.Label)
            .Take(maxItems)
            .ToList();

        return sorted;
    }

    public async IAsyncEnumerable<CompletionItem> StreamCompletionsAsync(
        string documentId,
        string? text,
        int line,
        int column,
        string? triggerCharacter,
        string? connectionString,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var filterText = GetFilterText(text, line, column);
        var context = DetermineContext(text, line, column);

        // Yield keywords first (fastest)
        foreach (var keyword in SqlKeywords)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            var score = keyword.FuzzyMatchScore(filterText, caseSensitive: false);
            if (score > 0)
            {
                yield return new CompletionItem
                {
                    Label = keyword,
                    Kind = CompletionItemKind.Keyword,
                    InsertText = keyword,
                    FilterText = keyword,
                    SortText = $"0_{keyword}",
                    RelevanceScore = score
                };
            }
        }

        // Yield functions
        foreach (var (name, desc) in SqlFunctions)
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            var score = name.FuzzyMatchScore(filterText, caseSensitive: false);
            if (score > 0)
            {
                yield return new CompletionItem
                {
                    Label = name,
                    Kind = CompletionItemKind.Function,
                    Detail = desc,
                    InsertText = $"{name}($1)",
                    InsertTextFormat = CompletionItemInsertTextFormat.Snippet,
                    FilterText = name,
                    SortText = $"1_{name}",
                    RelevanceScore = score
                };
            }
        }

        // Yield schema objects (requires database connection)
        if (!string.IsNullOrEmpty(connectionString))
        {
            var metadata = await _metadataService.GetMetadataAsync(
                new MetadataRequest
                {
                    ConnectionString = connectionString,
                    Scope = MetadataScope.Tables
                },
                cancellationToken);

            foreach (var table in metadata.Tables)
            {
                if (cancellationToken.IsCancellationRequested) yield break;

                var fullName = $"{table.SchemaName}.{table.TableName}";
                var score = table.TableName.FuzzyMatchScore(filterText, caseSensitive: false);
                if (score > 0 || string.IsNullOrEmpty(filterText))
                {
                    yield return new CompletionItem
                    {
                        Label = table.TableName,
                        Kind = table.IsView ? CompletionItemKind.View : CompletionItemKind.Table,
                        Detail = fullName,
                        InsertText = table.SchemaName == "dbo" ? table.TableName : fullName,
                        FilterText = table.TableName,
                        SortText = $"2_{table.TableName}",
                        RelevanceScore = Math.Max(score, string.IsNullOrEmpty(filterText) ? 50 : 0)
                    };
                }
            }
        }
    }

    private void AddKeywordCompletions(List<CompletionItem> items, string filterText)
    {
        foreach (var keyword in SqlKeywords)
        {
            var score = keyword.FuzzyMatchScore(filterText, caseSensitive: false);
            if (score > 0 || string.IsNullOrEmpty(filterText))
            {
                items.Add(new CompletionItem
                {
                    Label = keyword,
                    Kind = CompletionItemKind.Keyword,
                    InsertText = keyword,
                    FilterText = keyword,
                    SortText = $"0_{keyword}",
                    RelevanceScore = score > 0 ? score : 30
                });
            }
        }
    }

    private void AddFunctionCompletions(List<CompletionItem> items, string filterText)
    {
        foreach (var (name, desc) in SqlFunctions)
        {
            var score = name.FuzzyMatchScore(filterText, caseSensitive: false);
            if (score > 0 || string.IsNullOrEmpty(filterText))
            {
                items.Add(new CompletionItem
                {
                    Label = name,
                    Kind = CompletionItemKind.Function,
                    Detail = desc,
                    InsertText = $"{name}($1)",
                    InsertTextFormat = CompletionItemInsertTextFormat.Snippet,
                    FilterText = name,
                    SortText = $"1_{name}",
                    RelevanceScore = score > 0 ? score : 25
                });
            }
        }
    }

    private async Task AddSchemaCompletionsAsync(
        List<CompletionItem> items,
        CompletionContext context,
        string filterText,
        string connectionString,
        CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await _metadataService.GetMetadataAsync(
                new MetadataRequest
                {
                    ConnectionString = connectionString,
                    Scope = context == CompletionContext.AfterDot ? MetadataScope.Columns : MetadataScope.Tables
                },
                cancellationToken);

            foreach (var table in metadata.Tables)
            {
                var score = table.TableName.FuzzyMatchScore(filterText, caseSensitive: false);
                if (score > 0 || string.IsNullOrEmpty(filterText))
                {
                    items.Add(new CompletionItem
                    {
                        Label = table.TableName,
                        Kind = table.IsView ? CompletionItemKind.View : CompletionItemKind.Table,
                        Detail = $"{table.SchemaName}.{table.TableName}",
                        Documentation = table.IsView ? "View" : $"Table (~{table.RowCount:N0} rows)",
                        InsertText = table.SchemaName == "dbo" ? table.TableName : $"{table.SchemaName}.{table.TableName}",
                        FilterText = table.TableName,
                        SortText = $"2_{table.TableName}",
                        RelevanceScore = score > 0 ? score : 40
                    });

                    // Add columns if in column context
                    if (context == CompletionContext.AfterDot || context == CompletionContext.AfterSelect)
                    {
                        foreach (var col in table.Columns)
                        {
                            var colScore = col.ColumnName.FuzzyMatchScore(filterText, caseSensitive: false);
                            if (colScore > 0 || string.IsNullOrEmpty(filterText))
                            {
                                items.Add(new CompletionItem
                                {
                                    Label = col.ColumnName,
                                    Kind = CompletionItemKind.Column,
                                    Detail = $"{col.DataType}{(col.IsNullable ? " NULL" : " NOT NULL")}",
                                    Documentation = $"Column in {table.TableName}",
                                    InsertText = col.ColumnName,
                                    FilterText = col.ColumnName,
                                    SortText = $"3_{col.ColumnName}",
                                    RelevanceScore = colScore > 0 ? colScore : 35
                                });
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting schema completions");
        }
    }

    private static string GetFilterText(string? text, int line, int column)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var offset = text.LineColumnToOffset(line, column);
        return text.GetWordBefore(offset);
    }

    private static CompletionContext DetermineContext(string? text, int line, int column)
    {
        if (string.IsNullOrEmpty(text))
            return CompletionContext.General;

        var offset = text.LineColumnToOffset(line, column);
        
        // Get text before cursor
        var textBefore = text.Substring(0, Math.Min(offset, text.Length)).ToUpperInvariant();

        // Check for dot (table.column context)
        if (offset > 0 && text[offset - 1] == '.')
            return CompletionContext.AfterDot;

        // Check recent keywords
        var words = textBefore.Split(new[] { ' ', '\t', '\n', '\r', ',', '(', ')' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0)
        {
            var lastKeyword = words.AsEnumerable().Reverse().FirstOrDefault(w => SqlKeywords.Contains(w));
            
            return lastKeyword switch
            {
                "SELECT" => CompletionContext.AfterSelect,
                "FROM" or "JOIN" => CompletionContext.AfterFrom,
                "WHERE" or "AND" or "OR" => CompletionContext.AfterWhere,
                "ORDER" => CompletionContext.AfterOrderBy,
                "GROUP" => CompletionContext.AfterGroupBy,
                _ => CompletionContext.General
            };
        }

        return CompletionContext.General;
    }

    private enum CompletionContext
    {
        General,
        AfterSelect,
        AfterFrom,
        AfterWhere,
        AfterOrderBy,
        AfterGroupBy,
        AfterDot
    }
}
