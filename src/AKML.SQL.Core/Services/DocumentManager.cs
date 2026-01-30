using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AKML.SQL.Core.Services;

/// <summary>
/// Manages document state for open SQL documents.
/// Thread-safe storage for document text and cursor positions.
/// </summary>
public class DocumentManager : IDocumentManager
{
    private readonly ILogger<DocumentManager> _logger;
    private readonly ConcurrentDictionary<string, DocumentState> _documents = new();
    private readonly ISqlParserService _parserService;

    public DocumentManager(ILogger<DocumentManager> logger, ISqlParserService parserService)
    {
        _logger = logger;
        _parserService = parserService;
    }

    public async Task UpdateDocumentAsync(
        string documentId,
        string? text,
        int cursorLine,
        int cursorColumn,
        string? connectionString)
    {
        var state = _documents.GetOrAdd(documentId, _ => new DocumentState { DocumentId = documentId });

        state.Text = text ?? string.Empty;
        state.CursorLine = cursorLine;
        state.CursorColumn = cursorColumn;
        state.ConnectionString = connectionString;
        state.LastUpdated = DateTime.UtcNow;

        // Parse in background
        try
        {
            state.LastParseResult = await _parserService.ParseAsync(text, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing document {DocumentId}", documentId);
        }

        _logger.LogDebug("Updated document {DocumentId}: {Length} chars, cursor at ({Line},{Col})",
            documentId, text?.Length ?? 0, cursorLine, cursorColumn);
    }

    public Task<DocumentState?> GetDocumentAsync(string documentId)
    {
        _documents.TryGetValue(documentId, out var state);
        return Task.FromResult(state);
    }

    public Task RemoveDocumentAsync(string documentId)
    {
        if (_documents.TryRemove(documentId, out _))
        {
            _logger.LogDebug("Removed document {DocumentId}", documentId);
        }
        return Task.CompletedTask;
    }

    public IEnumerable<string> GetOpenDocuments()
    {
        return _documents.Keys;
    }
}
