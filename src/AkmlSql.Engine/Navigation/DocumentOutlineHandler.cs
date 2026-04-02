using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Productivity;
using MessagePack;
using Serilog;

namespace AkmlSql.Engine.Navigation;

/// <summary>
/// Handles DocumentOutline IPC requests (MessageType 64).
/// Parses SQL with <see cref="TsqlParserService"/> via <see cref="DocumentOutlineBuilder"/>
/// and returns a hierarchical <see cref="OutlineNodeDto"/> tree representing the document structure.
///
/// Detected node types:
/// - CREATE/ALTER PROCEDURE, FUNCTION, VIEW, TRIGGER
/// - CTEs (CommonTableExpression)
/// - Temp tables (CREATE TABLE #name)
/// - BEGIN...END blocks, TRY...CATCH blocks
/// - --region / --endregion comment markers
/// </summary>
public class DocumentOutlineHandler
{
    private readonly DocumentOutlineBuilder _outlineBuilder;

    public DocumentOutlineHandler(TsqlParserService parserService)
    {
        ArgumentNullException.ThrowIfNull(parserService);
        _outlineBuilder = new DocumentOutlineBuilder(parserService);
    }

    /// <summary>
    /// Handles a DocumentOutline request (MessageType 64).
    /// Parses the SQL text and builds the outline tree.
    /// </summary>
    public Task<RpcMessage?> HandleAsync(RpcMessage request)
    {
        try
        {
            if (request.Payload == null)
            {
                return Task.FromResult<RpcMessage?>(CreateResponse(request.RequestId,
                    new DocumentOutlineResponse { Success = false, Error = "Payload required" }));
            }

            var req = MessagePackSerializer.Deserialize<DocumentOutlineRequest>(request.Payload);

            if (string.IsNullOrWhiteSpace(req.SqlText))
            {
                return Task.FromResult<RpcMessage?>(CreateResponse(request.RequestId,
                    new DocumentOutlineResponse { Success = true, RootNodes = [] }));
            }

            var rootNodes = _outlineBuilder.BuildOutline(req.SqlText);

            var response = new DocumentOutlineResponse
            {
                Success = true,
                RootNodes = rootNodes
            };

            Log.Debug("DocumentOutlineHandler: {NodeCount} root nodes for session {SessionId}",
                rootNodes.Length, req.SessionId);

            return Task.FromResult<RpcMessage?>(CreateResponse(request.RequestId, response));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "DocumentOutlineHandler: failed to build outline");
            return Task.FromResult<RpcMessage?>(CreateResponse(request.RequestId,
                new DocumentOutlineResponse { Success = false, Error = ex.Message }));
        }
    }

    /// <summary>
    /// Builds the outline tree directly from SQL text without IPC deserialization.
    /// Useful for testing and direct invocation.
    /// </summary>
    public OutlineNodeDto[] BuildOutline(string sql)
    {
        return _outlineBuilder.BuildOutline(sql);
    }

    private static RpcMessage CreateResponse(int requestId, DocumentOutlineResponse response)
    {
        return new RpcMessage
        {
            MessageType = MessageTypes.DocumentOutlineResult,
            RequestId = requestId,
            Payload = MessagePackSerializer.Serialize(response)
        };
    }
}
