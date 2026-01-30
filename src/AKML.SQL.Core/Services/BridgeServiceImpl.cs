using AKML.SQL.Shared;
using AKML.SQL.Shared.Contracts;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace AKML.SQL.Core.Services;

/// <summary>
/// gRPC Bridge Service implementation.
/// This is the main entry point for all VSIX -> Core communication.
/// </summary>
public class BridgeServiceImpl : BridgeService.BridgeServiceBase
{
    private readonly ILogger<BridgeServiceImpl> _logger;
    private readonly ISqlParserService _parserService;
    private readonly ICompletionService _completionService;
    private readonly IMetadataService _metadataService;
    private readonly IDocumentManager _documentManager;

    public BridgeServiceImpl(
        ILogger<BridgeServiceImpl> logger,
        ISqlParserService parserService,
        ICompletionService completionService,
        IMetadataService metadataService,
        IDocumentManager documentManager)
    {
        _logger = logger;
        _parserService = parserService;
        _completionService = completionService;
        _metadataService = metadataService;
        _documentManager = documentManager;
    }

    /// <summary>
    /// Health check / connectivity test.
    /// </summary>
    public override Task<PingResponse> Ping(PingRequest request, ServerCallContext context)
    {
        _logger.LogInformation("Ping received from client v{Version}, SSMS {SsmsVersion}",
            request.ClientVersion, request.SsmsVersion);

        return Task.FromResult(new PingResponse
        {
            Success = true,
            ServerVersion = AkmlConstants.ProductVersion,
            Message = $"Welcome to {AkmlConstants.ProductName}!",
            ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
    }

    /// <summary>
    /// Synchronize editor text content.
    /// </summary>
    public override async Task<SyncTextResponse> SyncText(SyncTextRequest request, ServerCallContext context)
    {
        _logger.LogDebug("SyncText for document {DocumentId}, {Length} chars, cursor at ({Line},{Col})",
            request.DocumentId, request.FullText?.Length ?? 0, request.CursorLine, request.CursorColumn);

        try
        {
            // Store document state
            await _documentManager.UpdateDocumentAsync(
                request.DocumentId,
                request.FullText,
                request.CursorLine,
                request.CursorColumn,
                request.ConnectionString);

            // Parse and get diagnostics
            var parseResult = await _parserService.ParseAsync(request.FullText, context.CancellationToken);

            var response = new SyncTextResponse
            {
                Success = true,
                DocumentId = request.DocumentId,
                ProcessedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            // Add diagnostics (parse errors/warnings)
            foreach (var error in parseResult.Errors)
            {
                response.Diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = error.Message,
                    StartLine = error.Line,
                    StartColumn = error.Column,
                    EndLine = error.Line,
                    EndColumn = error.Column + 1,
                    Code = $"SQL{error.Number}"
                });
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncText for document {DocumentId}", request.DocumentId);
            return new SyncTextResponse
            {
                Success = false,
                DocumentId = request.DocumentId,
                ProcessedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
        }
    }

    /// <summary>
    /// Get code completions at cursor position.
    /// </summary>
    public override async Task<CompletionResponse> GetCompletions(CompletionRequest request, ServerCallContext context)
    {
        _logger.LogDebug("GetCompletions at ({Line},{Col}), trigger: '{Trigger}'",
            request.CursorLine, request.CursorColumn, request.TriggerCharacter);

        try
        {
            var items = await _completionService.GetCompletionsAsync(
                request.DocumentId,
                request.Text,
                request.CursorLine,
                request.CursorColumn,
                request.TriggerCharacter,
                request.ConnectionString,
                request.MaxItems > 0 ? request.MaxItems : 50,
                context.CancellationToken);

            var response = new CompletionResponse
            {
                Success = true,
                IsIncomplete = items.Count >= (request.MaxItems > 0 ? request.MaxItems : 50)
            };

            response.Items.AddRange(items);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetCompletions");
            return new CompletionResponse { Success = false };
        }
    }

    /// <summary>
    /// Parse SQL and return AST information.
    /// </summary>
    public override async Task<ParseResponse> ParseSql(ParseRequest request, ServerCallContext context)
    {
        _logger.LogDebug("ParseSql for document {DocumentId}", request.DocumentId);

        try
        {
            var result = await _parserService.ParseAsync(request.Text, context.CancellationToken);

            var response = new ParseResponse
            {
                Success = true,
                HasErrors = result.Errors.Any()
            };

            // Add diagnostics
            foreach (var error in result.Errors)
            {
                response.Diagnostics.Add(new Diagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = error.Message,
                    StartLine = error.Line,
                    StartColumn = error.Column,
                    Code = $"SQL{error.Number}"
                });
            }

            // Add statement info
            foreach (var stmt in result.Statements)
            {
                response.Statements.Add(stmt);
            }

            if (request.IncludeAst)
            {
                response.AstJson = result.AstJson;
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ParseSql");
            return new ParseResponse { Success = false, HasErrors = true };
        }
    }

    /// <summary>
    /// Format SQL code.
    /// </summary>
    public override async Task<FormatResponse> FormatSql(FormatRequest request, ServerCallContext context)
    {
        _logger.LogDebug("FormatSql, {Length} chars", request.Text?.Length ?? 0);

        try
        {
            var formatted = await _parserService.FormatAsync(request.Text, request.Options, context.CancellationToken);

            return new FormatResponse
            {
                Success = true,
                FormattedText = formatted
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in FormatSql");
            return new FormatResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Get database metadata (tables, columns, etc.).
    /// </summary>
    public override async Task<MetadataResponse> GetMetadata(MetadataRequest request, ServerCallContext context)
    {
        _logger.LogDebug("GetMetadata, scope: {Scope}", request.Scope);

        try
        {
            return await _metadataService.GetMetadataAsync(request, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetMetadata");
            return new MetadataResponse
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Stream completions for real-time suggestions.
    /// </summary>
    public override async Task StreamCompletions(
        CompletionRequest request,
        IServerStreamWriter<CompletionItem> responseStream,
        ServerCallContext context)
    {
        _logger.LogDebug("StreamCompletions started");

        try
        {
            await foreach (var item in _completionService.StreamCompletionsAsync(
                request.DocumentId,
                request.Text,
                request.CursorLine,
                request.CursorColumn,
                request.TriggerCharacter,
                request.ConnectionString,
                context.CancellationToken))
            {
                await responseStream.WriteAsync(item);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in StreamCompletions");
            throw;
        }
    }

    /// <summary>
    /// Bidirectional streaming for continuous sync.
    /// </summary>
    public override async Task SyncSession(
        IAsyncStreamReader<SyncTextRequest> requestStream,
        IServerStreamWriter<SyncTextResponse> responseStream,
        ServerCallContext context)
    {
        _logger.LogInformation("SyncSession started");

        try
        {
            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                var response = await SyncText(request, context);
                await responseStream.WriteAsync(response);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SyncSession");
            throw;
        }
        finally
        {
            _logger.LogInformation("SyncSession ended");
        }
    }
}
