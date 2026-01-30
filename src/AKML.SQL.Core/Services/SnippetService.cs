using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using AKML.SQL.Shared;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Service for managing SQL code snippets.
/// Implements Story 8.2: Code Snippet Management.
/// </summary>
public class SnippetService : ISnippetService
{
    private readonly ILogger<SnippetService> _logger;
    private readonly ConcurrentDictionary<string, CodeSnippet> _snippets;
    private readonly string _snippetsFilePath;
    private static readonly Regex PlaceholderRegex = new(@"\$\{(\w+)(?::([^}]*))?\}", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SnippetService(ILogger<SnippetService> logger)
    {
        _logger = logger;
        _snippets = new ConcurrentDictionary<string, CodeSnippet>(StringComparer.OrdinalIgnoreCase);
        _snippetsFilePath = Path.Combine(AkmlConstants.Paths.SettingsFolder, "snippets.json");

        LoadBuiltInSnippets();
        LoadCustomSnippets();
    }

    /// <summary>
    /// Gets all snippets.
    /// </summary>
    public IReadOnlyList<CodeSnippet> GetAll()
    {
        return _snippets.Values
            .OrderBy(s => s.Category)
            .ThenBy(s => s.Name)
            .ToList();
    }

    /// <summary>
    /// Gets snippets by category.
    /// </summary>
    public IReadOnlyList<CodeSnippet> GetByCategory(string category)
    {
        if (string.IsNullOrWhiteSpace(category))
            return GetAll();

        return _snippets.Values
            .Where(s => s.Category?.Equals(category, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(s => s.Name)
            .ToList();
    }

    /// <summary>
    /// Gets a snippet by ID.
    /// </summary>
    public CodeSnippet? GetById(string id)
    {
        _snippets.TryGetValue(id, out var snippet);
        return snippet;
    }

    /// <summary>
    /// Gets a snippet by shortcut.
    /// </summary>
    public CodeSnippet? GetByShortcut(string shortcut)
    {
        if (string.IsNullOrWhiteSpace(shortcut))
            return null;

        return _snippets.Values.FirstOrDefault(
            s => s.Shortcut.Equals(shortcut, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Searches snippets by name, shortcut, or description.
    /// </summary>
    public IReadOnlyList<CodeSnippet> Search(string searchText, int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(searchText))
            return GetAll().Take(limit).ToList();

        var searchUpper = searchText.ToUpperInvariant();

        return _snippets.Values
            .Where(s =>
                s.Name.ToUpperInvariant().Contains(searchUpper) ||
                s.Shortcut.ToUpperInvariant().Contains(searchUpper) ||
                (s.Description?.ToUpperInvariant().Contains(searchUpper) == true))
            .OrderBy(s => s.Name)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// Adds or updates a custom snippet.
    /// </summary>
    public void SaveSnippet(CodeSnippet snippet)
    {
        if (snippet == null)
            throw new ArgumentNullException(nameof(snippet));

        if (string.IsNullOrWhiteSpace(snippet.Name))
            throw new ArgumentException("Snippet name cannot be empty", nameof(snippet));

        if (string.IsNullOrWhiteSpace(snippet.Shortcut))
            throw new ArgumentException("Snippet shortcut cannot be empty", nameof(snippet));

        if (string.IsNullOrWhiteSpace(snippet.Code))
            throw new ArgumentException("Snippet code cannot be empty", nameof(snippet));

        // Check for shortcut conflict
        var existing = GetByShortcut(snippet.Shortcut);
        if (existing != null && existing.Id != snippet.Id)
            throw new InvalidOperationException($"Shortcut '{snippet.Shortcut}' is already used by snippet '{existing.Name}'");

        if (string.IsNullOrWhiteSpace(snippet.Id))
            snippet.Id = Guid.NewGuid().ToString();

        snippet.IsBuiltIn = false;
        snippet.Placeholders = ExtractPlaceholders(snippet.Code);

        _snippets[snippet.Id] = snippet;
        _logger.LogInformation("Saved snippet: {Name} ({Shortcut})", snippet.Name, snippet.Shortcut);

        SaveCustomSnippets();
    }

    /// <summary>
    /// Deletes a custom snippet.
    /// </summary>
    public bool DeleteSnippet(string id)
    {
        if (_snippets.TryGetValue(id, out var snippet))
        {
            if (snippet.IsBuiltIn)
                throw new InvalidOperationException("Cannot delete built-in snippets");

            var removed = _snippets.TryRemove(id, out _);
            if (removed)
            {
                _logger.LogInformation("Deleted snippet: {Id}", id);
                SaveCustomSnippets();
            }
            return removed;
        }
        return false;
    }

    /// <summary>
    /// Expands a snippet with placeholder values.
    /// </summary>
    public string ExpandSnippet(string snippetId, Dictionary<string, string>? placeholderValues = null)
    {
        var snippet = GetById(snippetId);
        if (snippet == null)
            throw new ArgumentException($"Snippet not found: {snippetId}", nameof(snippetId));

        return ExpandSnippetCode(snippet.Code, placeholderValues);
    }

    /// <summary>
    /// Expands snippet code with placeholder values.
    /// </summary>
    public string ExpandSnippetCode(string code, Dictionary<string, string>? placeholderValues = null)
    {
        if (string.IsNullOrEmpty(code))
            return code;

        return PlaceholderRegex.Replace(code, match =>
        {
            var name = match.Groups[1].Value;
            var defaultValue = match.Groups[2].Success ? match.Groups[2].Value : name;

            if (placeholderValues?.TryGetValue(name, out var value) == true)
                return value;

            return defaultValue;
        });
    }

    /// <summary>
    /// Gets all categories.
    /// </summary>
    public IReadOnlyList<string> GetCategories()
    {
        return _snippets.Values
            .Select(s => s.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c)
            .ToList()!;
    }

    /// <summary>
    /// Exports snippets to JSON.
    /// </summary>
    public string ExportSnippets(IEnumerable<string>? snippetIds = null)
    {
        var snippets = snippetIds == null
            ? _snippets.Values.Where(s => !s.IsBuiltIn).ToList()
            : snippetIds.Select(id => GetById(id)).Where(s => s != null).Cast<CodeSnippet>().ToList();

        return JsonSerializer.Serialize(snippets, _jsonOptions);
    }

    /// <summary>
    /// Imports snippets from JSON.
    /// </summary>
    public int ImportSnippets(string json, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("JSON cannot be empty", nameof(json));

        var snippets = JsonSerializer.Deserialize<List<CodeSnippet>>(json, _jsonOptions);
        if (snippets == null || snippets.Count == 0)
            return 0;

        var imported = 0;
        foreach (var snippet in snippets)
        {
            try
            {
                var existing = GetById(snippet.Id);
                if (existing != null && !overwrite)
                {
                    // Generate new ID
                    snippet.Id = Guid.NewGuid().ToString();
                }

                // Check shortcut conflict
                var shortcutOwner = GetByShortcut(snippet.Shortcut);
                if (shortcutOwner != null && shortcutOwner.Id != snippet.Id)
                {
                    // Append number to shortcut
                    var counter = 1;
                    var newShortcut = $"{snippet.Shortcut}{counter}";
                    while (GetByShortcut(newShortcut) != null)
                    {
                        counter++;
                        newShortcut = $"{snippet.Shortcut}{counter}";
                    }
                    snippet.Shortcut = newShortcut;
                }

                snippet.IsBuiltIn = false;
                snippet.Placeholders = ExtractPlaceholders(snippet.Code);
                _snippets[snippet.Id] = snippet;
                imported++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to import snippet: {Name}", snippet.Name);
            }
        }

        if (imported > 0)
            SaveCustomSnippets();

        _logger.LogInformation("Imported {Count} snippets", imported);
        return imported;
    }

    /// <summary>
    /// Total snippet count.
    /// </summary>
    public int Count => _snippets.Count;

    private List<SnippetPlaceholder> ExtractPlaceholders(string code)
    {
        var placeholders = new List<SnippetPlaceholder>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in PlaceholderRegex.Matches(code))
        {
            var name = match.Groups[1].Value;
            if (seen.Add(name))
            {
                placeholders.Add(new SnippetPlaceholder
                {
                    Name = name,
                    DefaultValue = match.Groups[2].Success ? match.Groups[2].Value : null
                });
            }
        }

        return placeholders;
    }

    private void LoadBuiltInSnippets()
    {
        var builtIn = GetBuiltInSnippets();
        foreach (var snippet in builtIn)
        {
            snippet.IsBuiltIn = true;
            snippet.Placeholders = ExtractPlaceholders(snippet.Code);
            _snippets[snippet.Id] = snippet;
        }

        _logger.LogDebug("Loaded {Count} built-in snippets", builtIn.Count);
    }

    private void LoadCustomSnippets()
    {
        try
        {
            if (!File.Exists(_snippetsFilePath))
                return;

            var json = File.ReadAllText(_snippetsFilePath);
            var snippets = JsonSerializer.Deserialize<List<CodeSnippet>>(json, _jsonOptions);

            if (snippets != null)
            {
                foreach (var snippet in snippets)
                {
                    snippet.IsBuiltIn = false;
                    snippet.Placeholders = ExtractPlaceholders(snippet.Code);
                    _snippets[snippet.Id] = snippet;
                }

                _logger.LogDebug("Loaded {Count} custom snippets", snippets.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading custom snippets");
        }
    }

    private void SaveCustomSnippets()
    {
        try
        {
            var custom = _snippets.Values.Where(s => !s.IsBuiltIn).ToList();
            var json = JsonSerializer.Serialize(custom, _jsonOptions);

            Directory.CreateDirectory(Path.GetDirectoryName(_snippetsFilePath)!);
            File.WriteAllText(_snippetsFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving custom snippets");
        }
    }

    private static List<CodeSnippet> GetBuiltInSnippets() => new()
    {
        // SELECT Snippets
        new CodeSnippet
        {
            Id = "sel",
            Name = "SELECT",
            Shortcut = "sel",
            Description = "Basic SELECT statement",
            Category = "Query",
            Code = "SELECT ${columns:*}\nFROM ${table:TableName}\nWHERE ${condition:1=1}"
        },
        new CodeSnippet
        {
            Id = "seltop",
            Name = "SELECT TOP",
            Shortcut = "seltop",
            Description = "SELECT TOP N rows",
            Category = "Query",
            Code = "SELECT TOP ${count:100} ${columns:*}\nFROM ${table:TableName}\nORDER BY ${orderBy:1}"
        },
        new CodeSnippet
        {
            Id = "selcount",
            Name = "SELECT COUNT",
            Shortcut = "selcount",
            Description = "COUNT with GROUP BY",
            Category = "Query",
            Code = "SELECT ${column:ColumnName}, COUNT(*) AS Count\nFROM ${table:TableName}\nGROUP BY ${column:ColumnName}\nORDER BY Count DESC"
        },
        new CodeSnippet
        {
            Id = "seljoin",
            Name = "SELECT with JOIN",
            Shortcut = "seljoin",
            Description = "SELECT with INNER JOIN",
            Category = "Query",
            Code = "SELECT ${columns:a.*, b.*}\nFROM ${table1:Table1} a\nINNER JOIN ${table2:Table2} b ON a.${joinCol:Id} = b.${joinCol:Id}\nWHERE ${condition:1=1}"
        },
        new CodeSnippet
        {
            Id = "selleft",
            Name = "SELECT with LEFT JOIN",
            Shortcut = "selleft",
            Description = "SELECT with LEFT JOIN",
            Category = "Query",
            Code = "SELECT ${columns:a.*, b.*}\nFROM ${table1:Table1} a\nLEFT JOIN ${table2:Table2} b ON a.${joinCol:Id} = b.${joinCol:Id}\nWHERE ${condition:1=1}"
        },

        // INSERT Snippets
        new CodeSnippet
        {
            Id = "ins",
            Name = "INSERT",
            Shortcut = "ins",
            Description = "Basic INSERT statement",
            Category = "DML",
            Code = "INSERT INTO ${table:TableName} (${columns:Column1, Column2})\nVALUES (${values:Value1, Value2})"
        },
        new CodeSnippet
        {
            Id = "insselect",
            Name = "INSERT SELECT",
            Shortcut = "insselect",
            Description = "INSERT from SELECT",
            Category = "DML",
            Code = "INSERT INTO ${target:TargetTable} (${columns:Column1, Column2})\nSELECT ${sourceColumns:Column1, Column2}\nFROM ${source:SourceTable}\nWHERE ${condition:1=1}"
        },

        // UPDATE Snippets
        new CodeSnippet
        {
            Id = "upd",
            Name = "UPDATE",
            Shortcut = "upd",
            Description = "Basic UPDATE statement",
            Category = "DML",
            Code = "UPDATE ${table:TableName}\nSET ${column:ColumnName} = ${value:'value'}\nWHERE ${condition:Id = 1}"
        },
        new CodeSnippet
        {
            Id = "updjoin",
            Name = "UPDATE with JOIN",
            Shortcut = "updjoin",
            Description = "UPDATE with JOIN",
            Category = "DML",
            Code = "UPDATE a\nSET a.${column:ColumnName} = b.${sourceColumn:SourceColumn}\nFROM ${table:Table1} a\nINNER JOIN ${source:Table2} b ON a.${joinCol:Id} = b.${joinCol:Id}\nWHERE ${condition:1=1}"
        },

        // DELETE Snippets
        new CodeSnippet
        {
            Id = "del",
            Name = "DELETE",
            Shortcut = "del",
            Description = "Basic DELETE statement",
            Category = "DML",
            Code = "DELETE FROM ${table:TableName}\nWHERE ${condition:Id = 1}"
        },
        new CodeSnippet
        {
            Id = "trunc",
            Name = "TRUNCATE",
            Shortcut = "trunc",
            Description = "TRUNCATE table",
            Category = "DML",
            Code = "TRUNCATE TABLE ${table:TableName}"
        },

        // DDL Snippets
        new CodeSnippet
        {
            Id = "crtbl",
            Name = "CREATE TABLE",
            Shortcut = "crtbl",
            Description = "Create a new table",
            Category = "DDL",
            Code = "CREATE TABLE ${schema:dbo}.${table:TableName}\n(\n    ${column:Id} INT IDENTITY(1,1) PRIMARY KEY,\n    ${column2:Name} NVARCHAR(100) NOT NULL,\n    CreatedAt DATETIME2 DEFAULT GETUTCDATE()\n)"
        },
        new CodeSnippet
        {
            Id = "cridx",
            Name = "CREATE INDEX",
            Shortcut = "cridx",
            Description = "Create an index",
            Category = "DDL",
            Code = "CREATE ${type:NONCLUSTERED} INDEX IX_${table:TableName}_${column:ColumnName}\nON ${schema:dbo}.${table:TableName} (${column:ColumnName})"
        },
        new CodeSnippet
        {
            Id = "crproc",
            Name = "CREATE PROCEDURE",
            Shortcut = "crproc",
            Description = "Create a stored procedure",
            Category = "DDL",
            Code = "CREATE PROCEDURE ${schema:dbo}.${name:ProcedureName}\n    @${param:Parameter1} ${type:INT}\nAS\nBEGIN\n    SET NOCOUNT ON;\n    \n    ${body:SELECT 1}\nEND"
        },
        new CodeSnippet
        {
            Id = "crview",
            Name = "CREATE VIEW",
            Shortcut = "crview",
            Description = "Create a view",
            Category = "DDL",
            Code = "CREATE VIEW ${schema:dbo}.${name:ViewName}\nAS\nSELECT ${columns:*}\nFROM ${table:TableName}\nWHERE ${condition:1=1}"
        },

        // CTE Snippets
        new CodeSnippet
        {
            Id = "cte",
            Name = "CTE",
            Shortcut = "cte",
            Description = "Common Table Expression",
            Category = "Query",
            Code = "WITH ${name:CTE_Name} AS\n(\n    SELECT ${columns:*}\n    FROM ${table:TableName}\n    WHERE ${condition:1=1}\n)\nSELECT *\nFROM ${name:CTE_Name}"
        },
        new CodeSnippet
        {
            Id = "rcte",
            Name = "Recursive CTE",
            Shortcut = "rcte",
            Description = "Recursive Common Table Expression",
            Category = "Query",
            Code = "WITH ${name:CTE_Recursive} AS\n(\n    -- Anchor\n    SELECT ${columns:Id, ParentId, Name, 0 AS Level}\n    FROM ${table:TableName}\n    WHERE ${anchor:ParentId IS NULL}\n    \n    UNION ALL\n    \n    -- Recursive\n    SELECT ${columns:c.Id, c.ParentId, c.Name, r.Level + 1}\n    FROM ${table:TableName} c\n    INNER JOIN ${name:CTE_Recursive} r ON c.ParentId = r.Id\n)\nSELECT *\nFROM ${name:CTE_Recursive}\nOPTION (MAXRECURSION 100)"
        },

        // Transaction Snippets
        new CodeSnippet
        {
            Id = "tran",
            Name = "Transaction",
            Shortcut = "tran",
            Description = "Transaction with try-catch",
            Category = "Control Flow",
            Code = "BEGIN TRY\n    BEGIN TRANSACTION;\n    \n    ${statements:-- Your statements here}\n    \n    COMMIT TRANSACTION;\nEND TRY\nBEGIN CATCH\n    IF @@TRANCOUNT > 0\n        ROLLBACK TRANSACTION;\n    \n    THROW;\nEND CATCH"
        },

        // Utility Snippets
        new CodeSnippet
        {
            Id = "ifex",
            Name = "IF EXISTS",
            Shortcut = "ifex",
            Description = "IF EXISTS check",
            Category = "Control Flow",
            Code = "IF EXISTS (SELECT 1 FROM ${table:TableName} WHERE ${condition:Id = 1})\nBEGIN\n    ${action:PRINT 'Exists'}\nEND"
        },
        new CodeSnippet
        {
            Id = "merge",
            Name = "MERGE",
            Shortcut = "merge",
            Description = "MERGE statement",
            Category = "DML",
            Code = "MERGE ${target:TargetTable} AS t\nUSING ${source:SourceTable} AS s\n    ON t.${key:Id} = s.${key:Id}\nWHEN MATCHED THEN\n    UPDATE SET t.${column:Name} = s.${column:Name}\nWHEN NOT MATCHED BY TARGET THEN\n    INSERT (${columns:Id, Name})\n    VALUES (s.${columns:Id, Name})\nWHEN NOT MATCHED BY SOURCE THEN\n    DELETE;"
        }
    };
}

/// <summary>
/// Interface for snippet service.
/// </summary>
public interface ISnippetService
{
    IReadOnlyList<CodeSnippet> GetAll();
    IReadOnlyList<CodeSnippet> GetByCategory(string category);
    CodeSnippet? GetById(string id);
    CodeSnippet? GetByShortcut(string shortcut);
    IReadOnlyList<CodeSnippet> Search(string searchText, int limit = 20);
    void SaveSnippet(CodeSnippet snippet);
    bool DeleteSnippet(string id);
    string ExpandSnippet(string snippetId, Dictionary<string, string>? placeholderValues = null);
    string ExpandSnippetCode(string code, Dictionary<string, string>? placeholderValues = null);
    IReadOnlyList<string> GetCategories();
    string ExportSnippets(IEnumerable<string>? snippetIds = null);
    int ImportSnippets(string json, bool overwrite = false);
    int Count { get; }
}

/// <summary>
/// Code snippet definition.
/// </summary>
public class CodeSnippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Shortcut { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Category { get; set; }
    public bool IsBuiltIn { get; set; }
    public List<SnippetPlaceholder> Placeholders { get; set; } = new();
}

/// <summary>
/// Placeholder in a snippet.
/// </summary>
public class SnippetPlaceholder
{
    public string Name { get; set; } = string.Empty;
    public string? DefaultValue { get; set; }
    public string? Tooltip { get; set; }
}
