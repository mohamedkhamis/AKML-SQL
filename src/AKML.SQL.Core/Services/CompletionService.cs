using System.Diagnostics;
using System.Runtime.CompilerServices;
using AKML.SQL.Core.DataStructures;
using AKML.SQL.Shared.Contracts;
using AKML.SQL.Shared.Extensions;
using Microsoft.Extensions.Logging;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Code completion service providing IntelliSense suggestions.
/// Implements Story 5.1: Context-Aware IntelliSense.
/// Enhanced with AST-based context analysis and alias resolution.
/// </summary>
public class CompletionService : ICompletionService
{
    private readonly ILogger<CompletionService> _logger;
    private readonly IDocumentManager _documentManager;
    private readonly IMetadataService _metadataService;
    private readonly ISqlParserService _parserService;
    private readonly SqlContextAnalyzer _contextAnalyzer;
    private readonly MetadataCache _metadataCache;

    // Pre-built Trie for keywords (static, lazy-loaded to ensure SqlKeywords is initialized first)
    private static Trie<KeywordInfo>? _keywordsTrie;
    private static Trie<FunctionInfo>? _functionsTrie;
    private static readonly object _trieLock = new object();

    private static Trie<KeywordInfo> KeywordsTrie
    {
        get
        {
            if (_keywordsTrie == null)
            {
                lock (_trieLock)
                {
                    _keywordsTrie ??= BuildKeywordsTrie();
                }
            }
            return _keywordsTrie;
        }
    }

    private static Trie<FunctionInfo> FunctionsTrie
    {
        get
        {
            if (_functionsTrie == null)
            {
                lock (_trieLock)
                {
                    _functionsTrie ??= BuildFunctionsTrie();
                }
            }
            return _functionsTrie;
        }
    }

    // SQL Keywords with categories for context-aware filtering
    private static readonly KeywordInfo[] SqlKeywords = new[]
    {
        // DML Keywords
        new KeywordInfo("SELECT", KeywordCategory.DML, "Retrieves data from database"),
        new KeywordInfo("FROM", KeywordCategory.DML, "Specifies table source"),
        new KeywordInfo("WHERE", KeywordCategory.DML, "Filters results"),
        new KeywordInfo("AND", KeywordCategory.Logical, "Logical AND operator"),
        new KeywordInfo("OR", KeywordCategory.Logical, "Logical OR operator"),
        new KeywordInfo("NOT", KeywordCategory.Logical, "Logical NOT operator"),
        new KeywordInfo("IN", KeywordCategory.Comparison, "Matches any value in a list"),
        new KeywordInfo("LIKE", KeywordCategory.Comparison, "Pattern matching"),
        new KeywordInfo("BETWEEN", KeywordCategory.Comparison, "Range comparison"),
        new KeywordInfo("JOIN", KeywordCategory.Join, "Joins tables"),
        new KeywordInfo("INNER", KeywordCategory.Join, "Inner join modifier"),
        new KeywordInfo("LEFT", KeywordCategory.Join, "Left outer join modifier"),
        new KeywordInfo("RIGHT", KeywordCategory.Join, "Right outer join modifier"),
        new KeywordInfo("OUTER", KeywordCategory.Join, "Outer join modifier"),
        new KeywordInfo("CROSS", KeywordCategory.Join, "Cross join modifier"),
        new KeywordInfo("ON", KeywordCategory.Join, "Join condition"),
        new KeywordInfo("INSERT", KeywordCategory.DML, "Inserts new rows"),
        new KeywordInfo("INTO", KeywordCategory.DML, "Target table for INSERT"),
        new KeywordInfo("VALUES", KeywordCategory.DML, "Values to insert"),
        new KeywordInfo("UPDATE", KeywordCategory.DML, "Updates existing rows"),
        new KeywordInfo("SET", KeywordCategory.DML, "Sets column values"),
        new KeywordInfo("DELETE", KeywordCategory.DML, "Deletes rows"),

        // DDL Keywords
        new KeywordInfo("CREATE", KeywordCategory.DDL, "Creates database object"),
        new KeywordInfo("ALTER", KeywordCategory.DDL, "Modifies database object"),
        new KeywordInfo("DROP", KeywordCategory.DDL, "Removes database object"),
        new KeywordInfo("TABLE", KeywordCategory.DDL, "Table object"),
        new KeywordInfo("VIEW", KeywordCategory.DDL, "View object"),
        new KeywordInfo("INDEX", KeywordCategory.DDL, "Index object"),
        new KeywordInfo("PROCEDURE", KeywordCategory.DDL, "Stored procedure"),
        new KeywordInfo("FUNCTION", KeywordCategory.DDL, "User-defined function"),

        // Clauses
        new KeywordInfo("AS", KeywordCategory.Clause, "Alias assignment"),
        new KeywordInfo("DISTINCT", KeywordCategory.Clause, "Removes duplicates"),
        new KeywordInfo("TOP", KeywordCategory.Clause, "Limits result count"),
        new KeywordInfo("ORDER", KeywordCategory.Clause, "Sorts results"),
        new KeywordInfo("BY", KeywordCategory.Clause, "Used with ORDER/GROUP"),
        new KeywordInfo("ASC", KeywordCategory.Clause, "Ascending order"),
        new KeywordInfo("DESC", KeywordCategory.Clause, "Descending order"),
        new KeywordInfo("GROUP", KeywordCategory.Clause, "Groups results"),
        new KeywordInfo("HAVING", KeywordCategory.Clause, "Filters grouped results"),
        new KeywordInfo("UNION", KeywordCategory.Clause, "Combines result sets"),
        new KeywordInfo("ALL", KeywordCategory.Clause, "Includes duplicates in UNION"),
        new KeywordInfo("EXCEPT", KeywordCategory.Clause, "Set difference"),
        new KeywordInfo("INTERSECT", KeywordCategory.Clause, "Set intersection"),

        // Expressions
        new KeywordInfo("NULL", KeywordCategory.Expression, "Null value"),
        new KeywordInfo("IS", KeywordCategory.Expression, "Comparison with NULL"),
        new KeywordInfo("EXISTS", KeywordCategory.Expression, "Subquery exists check"),
        new KeywordInfo("ANY", KeywordCategory.Expression, "Any comparison"),
        new KeywordInfo("CASE", KeywordCategory.Expression, "Conditional expression"),
        new KeywordInfo("WHEN", KeywordCategory.Expression, "Case condition"),
        new KeywordInfo("THEN", KeywordCategory.Expression, "Case result"),
        new KeywordInfo("ELSE", KeywordCategory.Expression, "Default case result"),
        new KeywordInfo("END", KeywordCategory.Expression, "End of CASE/BEGIN"),

        // Transaction/Control
        new KeywordInfo("BEGIN", KeywordCategory.Control, "Starts transaction/block"),
        new KeywordInfo("COMMIT", KeywordCategory.Control, "Commits transaction"),
        new KeywordInfo("ROLLBACK", KeywordCategory.Control, "Rolls back transaction"),
        new KeywordInfo("TRANSACTION", KeywordCategory.Control, "Transaction keyword"),
        new KeywordInfo("TRAN", KeywordCategory.Control, "Transaction abbreviation"),
        new KeywordInfo("EXEC", KeywordCategory.Control, "Executes procedure"),
        new KeywordInfo("EXECUTE", KeywordCategory.Control, "Executes procedure"),
        new KeywordInfo("DECLARE", KeywordCategory.Control, "Declares variable"),
        new KeywordInfo("RETURN", KeywordCategory.Control, "Returns value"),
        new KeywordInfo("IF", KeywordCategory.Control, "Conditional statement"),
        new KeywordInfo("WHILE", KeywordCategory.Control, "Loop statement"),
        new KeywordInfo("BREAK", KeywordCategory.Control, "Exits loop"),
        new KeywordInfo("CONTINUE", KeywordCategory.Control, "Next loop iteration"),

        // Window Functions
        new KeywordInfo("WITH", KeywordCategory.Window, "CTE or hint"),
        new KeywordInfo("OVER", KeywordCategory.Window, "Window function"),
        new KeywordInfo("PARTITION", KeywordCategory.Window, "Window partition"),
        new KeywordInfo("ROW_NUMBER", KeywordCategory.Window, "Row number function"),
        new KeywordInfo("RANK", KeywordCategory.Window, "Rank function"),
        new KeywordInfo("DENSE_RANK", KeywordCategory.Window, "Dense rank function"),

        // Constraints
        new KeywordInfo("PRIMARY", KeywordCategory.Constraint, "Primary key"),
        new KeywordInfo("KEY", KeywordCategory.Constraint, "Key constraint"),
        new KeywordInfo("FOREIGN", KeywordCategory.Constraint, "Foreign key"),
        new KeywordInfo("REFERENCES", KeywordCategory.Constraint, "FK references"),
        new KeywordInfo("CONSTRAINT", KeywordCategory.Constraint, "Named constraint"),
        new KeywordInfo("DEFAULT", KeywordCategory.Constraint, "Default value"),
        new KeywordInfo("CHECK", KeywordCategory.Constraint, "Check constraint"),
        new KeywordInfo("IDENTITY", KeywordCategory.Constraint, "Identity column"),
        new KeywordInfo("CLUSTERED", KeywordCategory.Constraint, "Clustered index"),
        new KeywordInfo("NONCLUSTERED", KeywordCategory.Constraint, "Non-clustered index"),
        new KeywordInfo("UNIQUE", KeywordCategory.Constraint, "Unique constraint"),
        new KeywordInfo("CASCADE", KeywordCategory.Constraint, "Cascade action"),

        // Security
        new KeywordInfo("GRANT", KeywordCategory.Security, "Grants permissions"),
        new KeywordInfo("REVOKE", KeywordCategory.Security, "Revokes permissions"),
        new KeywordInfo("DENY", KeywordCategory.Security, "Denies permissions"),
        new KeywordInfo("SCHEMA", KeywordCategory.DDL, "Schema object"),
        new KeywordInfo("DATABASE", KeywordCategory.DDL, "Database object"),
        new KeywordInfo("USE", KeywordCategory.Control, "Changes database context"),
        new KeywordInfo("GO", KeywordCategory.Control, "Batch separator")
    };

    // Common SQL functions
    private static readonly FunctionInfo[] SqlFunctions = new[]
    {
        // Aggregate Functions
        new FunctionInfo("COUNT", "Aggregate", "COUNT(expression)", "Returns the number of rows"),
        new FunctionInfo("SUM", "Aggregate", "SUM(expression)", "Returns the sum of values"),
        new FunctionInfo("AVG", "Aggregate", "AVG(expression)", "Returns the average of values"),
        new FunctionInfo("MIN", "Aggregate", "MIN(expression)", "Returns the minimum value"),
        new FunctionInfo("MAX", "Aggregate", "MAX(expression)", "Returns the maximum value"),

        // String Functions
        new FunctionInfo("LEN", "String", "LEN(string)", "Returns the length of a string"),
        new FunctionInfo("SUBSTRING", "String", "SUBSTRING(string, start, length)", "Returns part of a string"),
        new FunctionInfo("REPLACE", "String", "REPLACE(string, old, new)", "Replaces occurrences in a string"),
        new FunctionInfo("TRIM", "String", "TRIM(string)", "Removes leading and trailing spaces"),
        new FunctionInfo("LTRIM", "String", "LTRIM(string)", "Removes leading spaces"),
        new FunctionInfo("RTRIM", "String", "RTRIM(string)", "Removes trailing spaces"),
        new FunctionInfo("UPPER", "String", "UPPER(string)", "Converts to uppercase"),
        new FunctionInfo("LOWER", "String", "LOWER(string)", "Converts to lowercase"),
        new FunctionInfo("LEFT", "String", "LEFT(string, length)", "Returns left part of string"),
        new FunctionInfo("RIGHT", "String", "RIGHT(string, length)", "Returns right part of string"),
        new FunctionInfo("CHARINDEX", "String", "CHARINDEX(search, string)", "Returns position of substring"),
        new FunctionInfo("CONCAT", "String", "CONCAT(str1, str2, ...)", "Concatenates strings"),
        new FunctionInfo("STRING_AGG", "String", "STRING_AGG(expression, separator)", "Concatenates string values"),

        // NULL Handling
        new FunctionInfo("COALESCE", "Null", "COALESCE(val1, val2, ...)", "Returns the first non-null value"),
        new FunctionInfo("ISNULL", "Null", "ISNULL(expression, replacement)", "Replaces NULL with specified value"),
        new FunctionInfo("NULLIF", "Null", "NULLIF(expr1, expr2)", "Returns NULL if arguments are equal"),
        new FunctionInfo("IIF", "Conditional", "IIF(condition, true_val, false_val)", "Inline IF expression"),

        // Conversion Functions
        new FunctionInfo("CAST", "Conversion", "CAST(expression AS datatype)", "Converts expression to specified data type"),
        new FunctionInfo("CONVERT", "Conversion", "CONVERT(datatype, expression, style)", "Converts expression to specified data type"),
        new FunctionInfo("TRY_CAST", "Conversion", "TRY_CAST(expression AS datatype)", "Casts or returns NULL on failure"),
        new FunctionInfo("TRY_CONVERT", "Conversion", "TRY_CONVERT(datatype, expression)", "Converts or returns NULL on failure"),
        new FunctionInfo("FORMAT", "Conversion", "FORMAT(value, format)", "Formats a value"),

        // Date Functions
        new FunctionInfo("GETDATE", "DateTime", "GETDATE()", "Returns current date and time"),
        new FunctionInfo("GETUTCDATE", "DateTime", "GETUTCDATE()", "Returns current UTC date and time"),
        new FunctionInfo("SYSDATETIME", "DateTime", "SYSDATETIME()", "Returns current datetime2"),
        new FunctionInfo("DATEADD", "DateTime", "DATEADD(part, number, date)", "Adds interval to a date"),
        new FunctionInfo("DATEDIFF", "DateTime", "DATEDIFF(part, start, end)", "Returns difference between dates"),
        new FunctionInfo("DATEPART", "DateTime", "DATEPART(part, date)", "Returns part of a date"),
        new FunctionInfo("DATENAME", "DateTime", "DATENAME(part, date)", "Returns part name of a date"),
        new FunctionInfo("YEAR", "DateTime", "YEAR(date)", "Returns year from a date"),
        new FunctionInfo("MONTH", "DateTime", "MONTH(date)", "Returns month from a date"),
        new FunctionInfo("DAY", "DateTime", "DAY(date)", "Returns day from a date"),
        new FunctionInfo("EOMONTH", "DateTime", "EOMONTH(date)", "Returns last day of month"),

        // Window Functions
        new FunctionInfo("ROW_NUMBER", "Window", "ROW_NUMBER() OVER(...)", "Returns row number in result set"),
        new FunctionInfo("RANK", "Window", "RANK() OVER(...)", "Returns rank of a row"),
        new FunctionInfo("DENSE_RANK", "Window", "DENSE_RANK() OVER(...)", "Returns dense rank of a row"),
        new FunctionInfo("NTILE", "Window", "NTILE(n) OVER(...)", "Distributes rows into groups"),
        new FunctionInfo("LEAD", "Window", "LEAD(expr, offset) OVER(...)", "Returns value from following row"),
        new FunctionInfo("LAG", "Window", "LAG(expr, offset) OVER(...)", "Returns value from preceding row"),
        new FunctionInfo("FIRST_VALUE", "Window", "FIRST_VALUE(expr) OVER(...)", "Returns first value in window"),
        new FunctionInfo("LAST_VALUE", "Window", "LAST_VALUE(expr) OVER(...)", "Returns last value in window"),

        // JSON Functions
        new FunctionInfo("JSON_VALUE", "JSON", "JSON_VALUE(json, path)", "Extracts scalar value from JSON"),
        new FunctionInfo("JSON_QUERY", "JSON", "JSON_QUERY(json, path)", "Extracts object/array from JSON"),
        new FunctionInfo("JSON_MODIFY", "JSON", "JSON_MODIFY(json, path, value)", "Modifies JSON value"),
        new FunctionInfo("ISJSON", "JSON", "ISJSON(string)", "Tests if string is valid JSON"),
        new FunctionInfo("OPENJSON", "JSON", "OPENJSON(json)", "Parses JSON and returns rows"),

        // Math Functions
        new FunctionInfo("ABS", "Math", "ABS(number)", "Returns absolute value"),
        new FunctionInfo("CEILING", "Math", "CEILING(number)", "Rounds up to nearest integer"),
        new FunctionInfo("FLOOR", "Math", "FLOOR(number)", "Rounds down to nearest integer"),
        new FunctionInfo("ROUND", "Math", "ROUND(number, decimals)", "Rounds to specified decimals"),
        new FunctionInfo("POWER", "Math", "POWER(base, exponent)", "Returns power of a number"),
        new FunctionInfo("SQRT", "Math", "SQRT(number)", "Returns square root")
    };

    public CompletionService(
        ILogger<CompletionService> logger,
        IDocumentManager documentManager,
        IMetadataService metadataService,
        ISqlParserService parserService,
        SqlContextAnalyzer contextAnalyzer,
        MetadataCache metadataCache)
    {
        _logger = logger;
        _documentManager = documentManager;
        _metadataService = metadataService;
        _parserService = parserService;
        _contextAnalyzer = contextAnalyzer;
        _metadataCache = metadataCache;
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
        var stopwatch = Stopwatch.StartNew();
        var items = new List<CompletionItem>();

        try
        {
            // Calculate offset from line/column
            var offset = text?.LineColumnToOffset(line, column) ?? 0;

            // Analyze context using AST-based analyzer
            var context = _contextAnalyzer.AnalyzeContext(text ?? string.Empty, offset);
            var filterText = context.FilterText;

            _logger.LogDebug("Getting completions: Context={ContextType}, Filter='{Filter}', Offset={Offset}",
                context.ContextType, filterText, offset);

            // Get completions based on context
            switch (context.ContextType)
            {
                case SqlContextType.AfterFrom:
                case SqlContextType.AfterJoin:
                case SqlContextType.AfterUpdate:
                case SqlContextType.AfterInto:
                    // Show tables and views, with keywords as fallback
                    await AddTableCompletionsAsync(items, filterText, connectionString, cancellationToken);
                    AddKeywordCompletions(items, filterText, context.ContextType);
                    break;

                case SqlContextType.AfterSelect:
                case SqlContextType.AfterOrderBy:
                case SqlContextType.AfterGroupBy:
                    // Show columns from available tables, then functions, then keywords
                    await AddColumnCompletionsAsync(items, filterText, context, connectionString, cancellationToken);
                    AddFunctionCompletions(items, filterText);
                    AddKeywordCompletions(items, filterText, context.ContextType);
                    break;

                case SqlContextType.AfterWhere:
                case SqlContextType.AfterCondition:
                case SqlContextType.AfterOn:
                case SqlContextType.AfterHaving:
                    // Show columns, then operators, then keywords
                    await AddColumnCompletionsAsync(items, filterText, context, connectionString, cancellationToken);
                    AddComparisonOperators(items, filterText);
                    AddKeywordCompletions(items, filterText, context.ContextType);
                    break;

                case SqlContextType.AfterDot:
                    // Alias resolution - show columns from the specific table/alias
                    await AddAliasColumnsAsync(items, context.TableOrAliasPrefix, context, connectionString, cancellationToken);
                    break;

                case SqlContextType.AfterExec:
                    // Show stored procedures and functions
                    await AddProcedureCompletionsAsync(items, filterText, connectionString, cancellationToken);
                    AddKeywordCompletions(items, filterText, context.ContextType);
                    break;

                case SqlContextType.AfterSet:
                    // Show columns from the UPDATE table
                    await AddColumnCompletionsAsync(items, filterText, context, connectionString, cancellationToken);
                    AddKeywordCompletions(items, filterText, context.ContextType);
                    break;

                default:
                    // General context - show keywords, functions, and tables
                    AddKeywordCompletions(items, filterText, context.ContextType);
                    AddFunctionCompletions(items, filterText);
                    if (!string.IsNullOrEmpty(connectionString))
                    {
                        await AddTableCompletionsAsync(items, filterText, connectionString, cancellationToken);
                    }
                    break;
            }

            // Sort by relevance and take top items
            var sorted = items
                .OrderByDescending(i => i.RelevanceScore)
                .ThenBy(i => i.SortText ?? i.Label)
                .Take(maxItems)
                .ToList();

            stopwatch.Stop();
            _logger.LogDebug("Completion took {ElapsedMs}ms, returned {Count} items",
                stopwatch.ElapsedMilliseconds, sorted.Count);

            return sorted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting completions");
            return items;
        }
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
        var offset = text?.LineColumnToOffset(line, column) ?? 0;
        var context = _contextAnalyzer.AnalyzeContext(text ?? string.Empty, offset);
        var filterText = context.FilterText;

        // Stream keywords first (fastest)
        foreach (var result in KeywordsTrie.FuzzySearch(filterText, 50))
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            yield return new CompletionItem
            {
                Label = result.Key,
                Kind = CompletionItemKind.Keyword,
                Detail = result.Value.Description,
                InsertText = result.Key,
                FilterText = result.Key,
                SortText = $"0_{result.Key}",
                RelevanceScore = result.Score
            };
        }

        // Stream functions
        foreach (var result in FunctionsTrie.FuzzySearch(filterText, 50))
        {
            if (cancellationToken.IsCancellationRequested) yield break;

            yield return new CompletionItem
            {
                Label = result.Key,
                Kind = CompletionItemKind.Function,
                Detail = result.Value.Signature,
                Documentation = result.Value.Description,
                InsertText = $"{result.Key}($1)",
                InsertTextFormat = CompletionItemInsertTextFormat.Snippet,
                FilterText = result.Key,
                SortText = $"1_{result.Key}",
                RelevanceScore = result.Score
            };
        }

        // Stream schema objects
        if (!string.IsNullOrEmpty(connectionString))
        {
            var dbCache = GetOrLoadCache(connectionString);
            if (dbCache != null)
            {
                foreach (var result in dbCache.SearchTables(filterText, 50))
                {
                    if (cancellationToken.IsCancellationRequested) yield break;

                    yield return new CompletionItem
                    {
                        Label = result.Value.Name,
                        Kind = result.Value.IsView ? CompletionItemKind.View : CompletionItemKind.Table,
                        Detail = result.Value.FullName,
                        InsertText = result.Value.Schema == "dbo" ? result.Value.Name : result.Value.FullName,
                        FilterText = result.Value.Name,
                        SortText = $"2_{result.Value.Name}",
                        RelevanceScore = result.Score
                    };
                }
            }
        }
    }

    private void AddKeywordCompletions(List<CompletionItem> items, string filterText, SqlContextType contextType)
    {
        var relevantCategories = GetRelevantCategories(contextType);

        // Use larger limit when no filter to ensure all keywords are available
        var maxResults = string.IsNullOrEmpty(filterText) ? 200 : 50;
        var minScore = string.IsNullOrEmpty(filterText) ? 0 : 20;

        foreach (var result in KeywordsTrie.FuzzySearch(filterText, maxResults, minScore: minScore))
        {
            // Boost score for contextually relevant keywords
            var score = result.Score;
            if (relevantCategories.Contains(result.Value.Category))
                score += 20;

            items.Add(new CompletionItem
            {
                Label = result.Key,
                Kind = CompletionItemKind.Keyword,
                Detail = result.Value.Description,
                InsertText = result.Key,
                FilterText = result.Key,
                SortText = $"0_{result.Key}",
                RelevanceScore = score
            });
        }
    }

    private HashSet<KeywordCategory> GetRelevantCategories(SqlContextType contextType)
    {
        return contextType switch
        {
            SqlContextType.AfterSelect => new HashSet<KeywordCategory> { KeywordCategory.Clause, KeywordCategory.Expression },
            SqlContextType.AfterFrom => new HashSet<KeywordCategory> { KeywordCategory.Join, KeywordCategory.DML },
            SqlContextType.AfterWhere or SqlContextType.AfterCondition =>
                new HashSet<KeywordCategory> { KeywordCategory.Logical, KeywordCategory.Comparison },
            SqlContextType.AfterJoin => new HashSet<KeywordCategory> { KeywordCategory.Join },
            _ => new HashSet<KeywordCategory>()
        };
    }

    private void AddFunctionCompletions(List<CompletionItem> items, string filterText)
    {
        // Use larger limit when no filter
        var maxResults = string.IsNullOrEmpty(filterText) ? 100 : 30;
        var minScore = string.IsNullOrEmpty(filterText) ? 0 : 20;

        foreach (var result in FunctionsTrie.FuzzySearch(filterText, maxResults, minScore: minScore))
        {
            items.Add(new CompletionItem
            {
                Label = result.Key,
                Kind = CompletionItemKind.Function,
                Detail = result.Value.Signature,
                Documentation = result.Value.Description,
                InsertText = $"{result.Key}($1)",
                InsertTextFormat = CompletionItemInsertTextFormat.Snippet,
                FilterText = result.Key,
                SortText = $"1_{result.Key}",
                RelevanceScore = result.Score
            });
        }
    }

    private void AddComparisonOperators(List<CompletionItem> items, string filterText)
    {
        var operators = new[]
        {
            ("=", "Equals"),
            ("<>", "Not equals"),
            ("!=", "Not equals"),
            ("<", "Less than"),
            (">", "Greater than"),
            ("<=", "Less than or equal"),
            (">=", "Greater than or equal"),
            ("IS NULL", "Is null"),
            ("IS NOT NULL", "Is not null"),
            ("LIKE", "Pattern match"),
            ("IN", "In list"),
            ("BETWEEN", "Range comparison"),
            ("EXISTS", "Subquery exists")
        };

        foreach (var (op, desc) in operators)
        {
            if (string.IsNullOrEmpty(filterText) || op.StartsWith(filterText, StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new CompletionItem
                {
                    Label = op,
                    Kind = CompletionItemKind.Keyword,
                    Detail = desc,
                    InsertText = op + " ",
                    FilterText = op,
                    SortText = $"0_{op}",
                    RelevanceScore = 70
                });
            }
        }
    }

    private async Task AddTableCompletionsAsync(
        List<CompletionItem> items,
        string filterText,
        string? connectionString,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(connectionString))
            return;

        var dbCache = GetOrLoadCache(connectionString);
        if (dbCache == null)
        {
            // Fallback to metadata service
            await AddSchemaCompletionsFallbackAsync(items, filterText, connectionString, cancellationToken);
            return;
        }

        foreach (var result in dbCache.SearchTablesAndViews(filterText, 50))
        {
            items.Add(new CompletionItem
            {
                Label = result.Value.Name,
                Kind = result.Value.IsView ? CompletionItemKind.View : CompletionItemKind.Table,
                Detail = result.Value.FullName,
                Documentation = result.Value.IsView ? "View" : "Table",
                InsertText = result.Value.Schema == "dbo" ? result.Value.Name : result.Value.FullName,
                FilterText = result.Value.Name,
                SortText = $"2_{result.Value.Name}",
                RelevanceScore = result.Score + 10 // Boost tables in table context
            });
        }
    }

    private async Task AddColumnCompletionsAsync(
        List<CompletionItem> items,
        string filterText,
        SqlContext context,
        string? connectionString,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(connectionString))
            return;

        var dbCache = GetOrLoadCache(connectionString);
        if (dbCache == null)
            return;

        // Get columns from tables referenced in current statement
        var addedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (context.AvailableTables.Any())
        {
            foreach (var tableInfo in context.AvailableTables)
            {
                var columns = dbCache.GetColumnsForTable($"{tableInfo.SchemaName}.{tableInfo.TableName}");
                foreach (var col in columns)
                {
                    var key = $"{tableInfo.Alias}.{col.Name}";
                    if (addedColumns.Contains(key))
                        continue;

                    var score = col.Name.FuzzyMatchScore(filterText, caseSensitive: false);
                    if (score > 0 || string.IsNullOrEmpty(filterText))
                    {
                        addedColumns.Add(key);

                        // Include table alias prefix for disambiguation
                        var insertText = context.AvailableTables.Count > 1
                            ? $"{tableInfo.Alias}.{col.Name}"
                            : col.Name;

                        items.Add(new CompletionItem
                        {
                            Label = col.Name,
                            Kind = CompletionItemKind.Column,
                            Detail = $"{col.DataTypeDisplay}{(col.IsNullable ? " NULL" : "")}",
                            Documentation = $"Column in {tableInfo.TableName}" +
                                (col.IsPrimaryKey ? " (PK)" : "") +
                                (col.IsForeignKey ? " (FK)" : ""),
                            InsertText = insertText,
                            FilterText = col.Name,
                            SortText = $"3_{col.Name}",
                            RelevanceScore = score > 0 ? score + 15 : 40
                        });
                    }
                }
            }
        }
        else
        {
            // No tables in context, search all columns
            foreach (var result in dbCache.SearchColumns(filterText, 30))
            {
                var key = $"{result.Value.TableFullName}.{result.Value.Name}";
                if (addedColumns.Contains(key))
                    continue;

                addedColumns.Add(key);
                items.Add(new CompletionItem
                {
                    Label = result.Value.Name,
                    Kind = CompletionItemKind.Column,
                    Detail = $"{result.Value.DataTypeDisplay} ({result.Value.TableName})",
                    InsertText = result.Value.Name,
                    FilterText = result.Value.Name,
                    SortText = $"3_{result.Value.Name}",
                    RelevanceScore = result.Score
                });
            }
        }
    }

    private async Task AddAliasColumnsAsync(
        List<CompletionItem> items,
        string? aliasOrTable,
        SqlContext context,
        string? connectionString,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(aliasOrTable))
            return;

        var dbCache = GetOrLoadCache(connectionString);
        if (dbCache == null)
            return;

        // Find the table for this alias
        var tableInfo = context.AvailableTables.FirstOrDefault(t =>
            t.Alias.Equals(aliasOrTable, StringComparison.OrdinalIgnoreCase) ||
            t.TableName.Equals(aliasOrTable, StringComparison.OrdinalIgnoreCase));

        if (tableInfo == null)
        {
            // Try to find as schema.table or just table name
            var table = dbCache.GetTable("dbo", aliasOrTable);
            if (table != null)
            {
                tableInfo = new TableAliasInfo
                {
                    SchemaName = table.Schema,
                    TableName = table.Name,
                    Alias = aliasOrTable
                };
            }
        }

        if (tableInfo != null)
        {
            var columns = dbCache.GetColumnsForTable($"{tableInfo.SchemaName}.{tableInfo.TableName}");
            foreach (var col in columns)
            {
                items.Add(new CompletionItem
                {
                    Label = col.Name,
                    Kind = CompletionItemKind.Column,
                    Detail = col.DataTypeDisplay,
                    Documentation = col.IsPrimaryKey ? "Primary Key" : (col.IsForeignKey ? "Foreign Key" : null),
                    InsertText = col.Name,
                    FilterText = col.Name,
                    SortText = $"0_{col.OrdinalPosition:D3}_{col.Name}",
                    RelevanceScore = 90 + (col.IsPrimaryKey ? 10 : 0)
                });
            }
        }

        // Also add * for SELECT alias.*
        items.Add(new CompletionItem
        {
            Label = "*",
            Kind = CompletionItemKind.Keyword,
            Detail = "All columns",
            InsertText = "*",
            FilterText = "*",
            SortText = "0_000_*",
            RelevanceScore = 100
        });
    }

    private async Task AddProcedureCompletionsAsync(
        List<CompletionItem> items,
        string filterText,
        string? connectionString,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(connectionString))
            return;

        var dbCache = GetOrLoadCache(connectionString);
        if (dbCache == null)
            return;

        foreach (var result in dbCache.SearchProcedures(filterText, 50))
        {
            var paramList = string.Join(", ", result.Value.Parameters.Select(p => $"@{p.Name}"));
            items.Add(new CompletionItem
            {
                Label = result.Value.Name,
                Kind = CompletionItemKind.Procedure,
                Detail = result.Value.FullName,
                Documentation = paramList.Length > 0 ? $"Parameters: {paramList}" : "No parameters",
                InsertText = result.Value.Schema == "dbo" ? result.Value.Name : result.Value.FullName,
                FilterText = result.Value.Name,
                SortText = $"2_{result.Value.Name}",
                RelevanceScore = result.Score + 10
            });
        }

        foreach (var result in dbCache.SearchFunctions(filterText, 30))
        {
            items.Add(new CompletionItem
            {
                Label = result.Value.Name,
                Kind = CompletionItemKind.Function,
                Detail = $"{result.Value.FullName} -> {result.Value.ReturnType}",
                InsertText = result.Value.Schema == "dbo" ? result.Value.Name : result.Value.FullName,
                FilterText = result.Value.Name,
                SortText = $"2_{result.Value.Name}",
                RelevanceScore = result.Score
            });
        }
    }

    private async Task AddSchemaCompletionsFallbackAsync(
        List<CompletionItem> items,
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
                    Scope = MetadataScope.Tables
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
                        InsertText = table.SchemaName == "dbo" ? table.TableName : $"{table.SchemaName}.{table.TableName}",
                        FilterText = table.TableName,
                        SortText = $"2_{table.TableName}",
                        RelevanceScore = score > 0 ? score : 40
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting schema completions via fallback");
        }
    }

    private DatabaseCache? GetOrLoadCache(string connectionString)
    {
        // Generate cache key from connection string
        var cacheKey = GenerateCacheKey(connectionString);

        if (_metadataCache.HasValidCache(cacheKey))
        {
            return _metadataCache.GetOrCreateCache(cacheKey);
        }

        // Cache miss - need to load metadata
        // This would typically be done asynchronously
        return null;
    }

    private string GenerateCacheKey(string connectionString)
    {
        // Extract server and database from connection string
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        return $"{builder.DataSource}|{builder.InitialCatalog}".ToLowerInvariant();
    }

    private static Trie<KeywordInfo> BuildKeywordsTrie()
    {
        var trie = new Trie<KeywordInfo>(caseSensitive: false);
        foreach (var keyword in SqlKeywords)
        {
            trie.Add(keyword.Name, keyword);
        }
        return trie;
    }

    private static Trie<FunctionInfo> BuildFunctionsTrie()
    {
        var trie = new Trie<FunctionInfo>(caseSensitive: false);
        foreach (var func in SqlFunctions)
        {
            trie.Add(func.Name, func);
        }
        return trie;
    }
}

/// <summary>
/// Keyword information for completion.
/// </summary>
public class KeywordInfo
{
    public string Name { get; }
    public KeywordCategory Category { get; }
    public string Description { get; }

    public KeywordInfo(string name, KeywordCategory category, string description)
    {
        Name = name;
        Category = category;
        Description = description;
    }
}

/// <summary>
/// SQL keyword categories for context filtering.
/// </summary>
public enum KeywordCategory
{
    DML,
    DDL,
    Join,
    Clause,
    Expression,
    Logical,
    Comparison,
    Control,
    Window,
    Constraint,
    Security
}

/// <summary>
/// Function information for completion.
/// </summary>
public class FunctionInfo
{
    public string Name { get; }
    public string Category { get; }
    public string Signature { get; }
    public string Description { get; }

    public FunctionInfo(string name, string category, string signature, string description)
    {
        Name = name;
        Category = category;
        Signature = signature;
        Description = description;
    }
}
