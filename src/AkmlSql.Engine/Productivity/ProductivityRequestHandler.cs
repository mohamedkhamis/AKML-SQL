using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using MessagePack;
using Serilog;

namespace AkmlSql.Engine.Productivity;

/// <summary>
/// Handles productivity-related IPC requests: StatementBoundary (65) and DocumentOutline (64).
/// </summary>
public class ProductivityRequestHandler
{
    private readonly StatementBoundaryDetector _boundaryDetector;
    private readonly DocumentOutlineBuilder _outlineBuilder;

    public ProductivityRequestHandler(TsqlParserService parserService)
    {
        ArgumentNullException.ThrowIfNull(parserService);
        _boundaryDetector = new StatementBoundaryDetector(parserService);
        _outlineBuilder = new DocumentOutlineBuilder(parserService);
    }

    /// <summary>
    /// Handles MessageType 65 (StatementBoundary): finds the statement at the cursor
    /// and/or returns all statement boundaries.
    /// </summary>
    public Task<RpcMessage?> HandleStatementBoundaryAsync(RpcMessage request)
    {
        try
        {
            if (request.Payload == null)
            {
                return Task.FromResult<RpcMessage?>(CreateResponse(
                    MessageTypes.StatementBoundaryResult, request.RequestId,
                    new StatementBoundaryResponse { Success = false, Error = "Payload required" }));
            }

            var req = MessagePackSerializer.Deserialize<StatementBoundaryRequest>(request.Payload);

            if (string.IsNullOrWhiteSpace(req.SqlText))
            {
                return Task.FromResult<RpcMessage?>(CreateResponse(
                    MessageTypes.StatementBoundaryResult, request.RequestId,
                    new StatementBoundaryResponse { Success = true }));
            }

            var response = new StatementBoundaryResponse { Success = true };

            if (req.AllStatements)
            {
                var allRanges = _boundaryDetector.GetAllStatements(req.SqlText);
                response.AllStatements = allRanges;

                // Also find the current statement if cursor offset is provided
                if (req.CursorOffset >= 0)
                {
                    response.CurrentStatement = _boundaryDetector.FindStatementAtOffset(
                        req.SqlText, req.CursorOffset);
                }
            }
            else
            {
                response.CurrentStatement = _boundaryDetector.FindStatementAtOffset(
                    req.SqlText, req.CursorOffset);
            }

            Log.Debug("StatementBoundary: current={HasCurrent}, allCount={AllCount}",
                response.CurrentStatement != null,
                response.AllStatements?.Length ?? 0);

            return Task.FromResult<RpcMessage?>(CreateResponse(
                MessageTypes.StatementBoundaryResult, request.RequestId, response));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ProductivityRequestHandler: StatementBoundary failed");
            return Task.FromResult<RpcMessage?>(CreateResponse(
                MessageTypes.StatementBoundaryResult, request.RequestId,
                new StatementBoundaryResponse { Success = false, Error = ex.Message }));
        }
    }

    /// <summary>
    /// Handles MessageType 64 (DocumentOutline): builds and returns the outline tree.
    /// </summary>
    public Task<RpcMessage?> HandleDocumentOutlineAsync(RpcMessage request)
    {
        try
        {
            if (request.Payload == null)
            {
                return Task.FromResult<RpcMessage?>(CreateResponse(
                    MessageTypes.DocumentOutlineResult, request.RequestId,
                    new DocumentOutlineResponse { Success = false, Error = "Payload required" }));
            }

            var req = MessagePackSerializer.Deserialize<DocumentOutlineRequest>(request.Payload);

            if (string.IsNullOrWhiteSpace(req.SqlText))
            {
                return Task.FromResult<RpcMessage?>(CreateResponse(
                    MessageTypes.DocumentOutlineResult, request.RequestId,
                    new DocumentOutlineResponse { Success = true, RootNodes = [] }));
            }

            var rootNodes = _outlineBuilder.BuildOutline(req.SqlText);

            var response = new DocumentOutlineResponse
            {
                Success = true,
                RootNodes = rootNodes
            };

            Log.Debug("DocumentOutline: {NodeCount} root nodes", rootNodes.Length);

            return Task.FromResult<RpcMessage?>(CreateResponse(
                MessageTypes.DocumentOutlineResult, request.RequestId, response));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ProductivityRequestHandler: DocumentOutline failed");
            return Task.FromResult<RpcMessage?>(CreateResponse(
                MessageTypes.DocumentOutlineResult, request.RequestId,
                new DocumentOutlineResponse { Success = false, Error = ex.Message }));
        }
    }

    private static RpcMessage CreateResponse<T>(int messageType, int requestId, T payload)
    {
        return new RpcMessage
        {
            MessageType = messageType,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(payload)
        };
    }
}
