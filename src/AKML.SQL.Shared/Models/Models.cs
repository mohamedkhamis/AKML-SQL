using System.Text.Json.Serialization;

namespace AKML.SQL.Shared.Models;

/// <summary>
/// Represents a document being edited in SSMS.
/// </summary>
public class DocumentState
{
    public string DocumentId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int CursorLine { get; set; }
    public int CursorColumn { get; set; }
    public int CursorOffset { get; set; }
    public string? ConnectionString { get; set; }
    public string? DatabaseName { get; set; }
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
    public bool IsDirty { get; set; }
}

/// <summary>
/// User settings for AKML-SQL.
/// </summary>
public class UserSettings
{
    [JsonPropertyName("enableIntelliSense")]
    public bool EnableIntelliSense { get; set; } = true;
    
    [JsonPropertyName("enableAutoComplete")]
    public bool EnableAutoComplete { get; set; } = true;
    
    [JsonPropertyName("autoCompleteDelay")]
    public int AutoCompleteDelay { get; set; } = 150;
    
    [JsonPropertyName("maxCompletionItems")]
    public int MaxCompletionItems { get; set; } = 50;
    
    [JsonPropertyName("enableFuzzyMatching")]
    public bool EnableFuzzyMatching { get; set; } = true;
    
    [JsonPropertyName("caseSensitiveMatching")]
    public bool CaseSensitiveMatching { get; set; } = false;
    
    [JsonPropertyName("formatOnSave")]
    public bool FormatOnSave { get; set; } = false;
    
    [JsonPropertyName("formatOptions")]
    public FormatSettings FormatOptions { get; set; } = new();
    
    [JsonPropertyName("enableTabColoring")]
    public bool EnableTabColoring { get; set; } = true;
    
    [JsonPropertyName("enableQueryHistory")]
    public bool EnableQueryHistory { get; set; } = true;
    
    [JsonPropertyName("maxHistoryItems")]
    public int MaxHistoryItems { get; set; } = 1000;
    
    [JsonPropertyName("enableCodeAnalysis")]
    public bool EnableCodeAnalysis { get; set; } = true;
    
    [JsonPropertyName("telemetryEnabled")]
    public bool TelemetryEnabled { get; set; } = true;
}

/// <summary>
/// SQL formatting settings.
/// </summary>
public class FormatSettings
{
    [JsonPropertyName("indentSize")]
    public int IndentSize { get; set; } = 4;
    
    [JsonPropertyName("useTabs")]
    public bool UseTabs { get; set; } = false;
    
    [JsonPropertyName("keywordCasing")]
    public KeywordCasingOption KeywordCasing { get; set; } = KeywordCasingOption.Uppercase;
    
    [JsonPropertyName("alignAliases")]
    public bool AlignAliases { get; set; } = true;
    
    [JsonPropertyName("placeCommasBefore")]
    public bool PlaceCommasBefore { get; set; } = false;
    
    [JsonPropertyName("maxLineLength")]
    public int MaxLineLength { get; set; } = 120;
    
    [JsonPropertyName("expandInLists")]
    public bool ExpandInLists { get; set; } = true;
    
    [JsonPropertyName("expandCaseStatements")]
    public bool ExpandCaseStatements { get; set; } = true;
    
    [JsonPropertyName("spaceBetweenFunctionAndParenthesis")]
    public bool SpaceBetweenFunctionAndParenthesis { get; set; } = false;
    
    [JsonPropertyName("preserveComments")]
    public bool PreserveComments { get; set; } = true;
}

public enum KeywordCasingOption
{
    Uppercase,
    Lowercase,
    PascalCase,
    Unchanged
}

/// <summary>
/// Connection information extracted from SSMS.
/// </summary>
public class ConnectionInfo
{
    public string ServerName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public bool IntegratedSecurity { get; set; } = true;
    public string ConnectionString { get; set; } = string.Empty;
    public int ConnectionTimeout { get; set; } = 30;
    public DateTime LastConnected { get; set; }
    public bool IsConnected { get; set; }
}

/// <summary>
/// Represents a code snippet.
/// </summary>
public class CodeSnippet
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("shortcut")]
    public string Shortcut { get; set; } = string.Empty;
    
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
    
    [JsonPropertyName("category")]
    public string? Category { get; set; }
    
    [JsonPropertyName("isBuiltIn")]
    public bool IsBuiltIn { get; set; }
    
    [JsonPropertyName("placeholders")]
    public List<SnippetPlaceholder> Placeholders { get; set; } = new();
}

public class SnippetPlaceholder
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    
    [JsonPropertyName("defaultValue")]
    public string? DefaultValue { get; set; }
    
    [JsonPropertyName("tooltip")]
    public string? Tooltip { get; set; }
}

/// <summary>
/// Query history entry.
/// </summary>
public class QueryHistoryEntry
{
    public long Id { get; set; }
    public string Query { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = string.Empty;
    public DateTime ExecutedAt { get; set; }
    public long ExecutionTimeMs { get; set; }
    public int RowsAffected { get; set; }
    public bool WasSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsFavorite { get; set; }
    public string? Tags { get; set; }
}

/// <summary>
/// Tab coloring configuration.
/// </summary>
public class TabColorConfig
{
    [JsonPropertyName("serverPatterns")]
    public List<TabColorPattern> ServerPatterns { get; set; } = new()
    {
        new TabColorPattern { Pattern = "*prod*", Color = "#FF6B6B", Label = "Production" },
        new TabColorPattern { Pattern = "*uat*", Color = "#FFE66D", Label = "UAT" },
        new TabColorPattern { Pattern = "*dev*", Color = "#4ECDC4", Label = "Development" },
        new TabColorPattern { Pattern = "*test*", Color = "#95E1D3", Label = "Test" },
        new TabColorPattern { Pattern = "*local*", Color = "#A8E6CF", Label = "Local" }
    };
}

public class TabColorPattern
{
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = string.Empty;
    
    [JsonPropertyName("color")]
    public string Color { get; set; } = "#808080";
    
    [JsonPropertyName("label")]
    public string? Label { get; set; }
    
    [JsonPropertyName("isRegex")]
    public bool IsRegex { get; set; } = false;
}
