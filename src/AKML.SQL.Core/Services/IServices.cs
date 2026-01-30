using AKML.SQL.Shared.Contracts;

namespace AKML.SQL.Core.Services;

/// <summary>
/// SQL parsing service interface.
/// </summary>
public interface ISqlParserService
{
    Task<ParseResult> ParseAsync(string? sql, CancellationToken cancellationToken = default);
    Task<string> FormatAsync(string? sql, FormatOptions? options, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of parsing SQL.
/// </summary>
public class ParseResult
{
    public bool Success { get; set; }
    public List<ParseError> Errors { get; set; } = new();
    public List<SqlStatement> Statements { get; set; } = new();
    public string? AstJson { get; set; }
}

/// <summary>
/// Parse error information.
/// </summary>
public class ParseError
{
    public int Number { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Line { get; set; }
    public int Column { get; set; }
    public int Offset { get; set; }
}

/// <summary>
/// Code completion service interface.
/// </summary>
public interface ICompletionService
{
    Task<List<CompletionItem>> GetCompletionsAsync(
        string documentId,
        string? text,
        int line,
        int column,
        string? triggerCharacter,
        string? connectionString,
        int maxItems,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<CompletionItem> StreamCompletionsAsync(
        string documentId,
        string? text,
        int line,
        int column,
        string? triggerCharacter,
        string? connectionString,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Database metadata service interface.
/// </summary>
public interface IMetadataService
{
    Task<MetadataResponse> GetMetadataAsync(MetadataRequest request, CancellationToken cancellationToken = default);
    Task RefreshMetadataAsync(string connectionString, CancellationToken cancellationToken = default);
    Task ClearCacheAsync(string? connectionString = null);
}

/// <summary>
/// Document state manager interface.
/// </summary>
public interface IDocumentManager
{
    Task UpdateDocumentAsync(string documentId, string? text, int cursorLine, int cursorColumn, string? connectionString);
    Task<DocumentState?> GetDocumentAsync(string documentId);
    Task RemoveDocumentAsync(string documentId);
    IEnumerable<string> GetOpenDocuments();
}

/// <summary>
/// Document state.
/// </summary>
public class DocumentState
{
    public string DocumentId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public int CursorLine { get; set; }
    public int CursorColumn { get; set; }
    public string? ConnectionString { get; set; }
    public DateTime LastUpdated { get; set; }
    public ParseResult? LastParseResult { get; set; }
}
