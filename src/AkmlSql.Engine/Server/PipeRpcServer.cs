using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Completion;
using AkmlSql.Engine.Completion.Providers;
using AkmlSql.Engine.Formatter;
using AkmlSql.Engine.Snippets;
using System.Linq;
using AkmlSql.Engine.Parser;
using AkmlSql.Engine.Schema;
using AkmlSql.Formatting.Profiles;
using MessagePack;
using Serilog;

namespace AkmlSql.Engine.Server;

public class PipeRpcServer
{
    private readonly string _pipeName;
    private readonly SessionManager _sessionManager = new();
    private readonly TsqlParserService _parserService = new();
    private readonly CompletionEngine _completionEngine;
    private readonly SchemaCacheManager _schemaCacheManager = new();
    private readonly SchemaMetadataService _schemaMetadataService = new();
    private readonly SignatureProvider _signatureProvider = new();
    private readonly QuickInfoProvider _quickInfoProvider = new();
    private readonly FormatRequestHandler _formatHandler;
    private readonly SnippetRequestHandler _snippetHandler;

    public PipeRpcServer(string pipeName)
    {
        _pipeName = pipeName;
        _completionEngine = new CompletionEngine(_parserService);
        _formatHandler = new FormatRequestHandler(ProfileManager.CreateDefault());

        var appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AKML SQL");
        var personalSnippets = Path.Combine(appDataFolder, "snippets", "personal");
        var builtInSnippets = Path.Combine(AppContext.BaseDirectory, "snippets");
        _snippetHandler = new SnippetRequestHandler(personalSnippets, builtInSnippets);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var pipeSecurity = CreatePipeSecurity();
            await using var pipe = NamedPipeServerStreamAcl.Create(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                inBufferSize: 65536,
                outBufferSize: 65536,
                pipeSecurity);

            Log.Information("Waiting for client connection on pipe {Pipe}", _pipeName);
            await pipe.WaitForConnectionAsync(ct);
            Log.Information("Client connected.");

            try
            {
                await HandleClientAsync(pipe, ct);
            }
            catch (IOException ex)
            {
                Log.Warning(ex, "Client disconnected (pipe broken).");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error handling client.");
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        while (pipe.IsConnected && !ct.IsCancellationRequested)
        {
            var message = await FrameProtocol.ReadFramedAsync(pipe, ct);
            if (message == null)
            {
                break;
            }

            var response = await DispatchAsync(message, ct);
            if (response != null)
            {
                await FrameProtocol.WriteFramedAsync(pipe, response, ct);
            }
        }
    }

    private async Task<RpcMessage?> DispatchAsync(RpcMessage message, CancellationToken ct)
    {
        try
        {
            switch (message.MessageType)
            {
                case MessageTypes.ConnectionChanged:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var connInfo = MessagePackSerializer.Deserialize<ConnectionInfo>(message.Payload);
                    _sessionManager.UpdateSession(connInfo);
                    _parserService.SetServerVersion(connInfo.ServerVersion);
                    Log.Information("Connection changed: {Session} -> {Db}", connInfo.SessionId, connInfo.DatabaseName);

                    // Fire-and-forget: populate schema cache Phase A
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var cache = _schemaCacheManager.GetOrCreateCache(
                                connInfo.SessionId, connInfo.DatabaseName);
                            if (cache.Phase == PopulationPhase.NotLoaded)
                            {
                                Log.Information("Starting Phase A schema population for {Db}", connInfo.DatabaseName);
                                await _schemaMetadataService.PopulatePhaseAAsync(
                                    cache, connInfo.ConnectionString, CancellationToken.None);
                                _schemaCacheManager.EvictLru();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Background Phase A population failed for {Db}", connInfo.DatabaseName);
                        }
                    });
                    return null; // notification, no response

                case MessageTypes.DocumentChanged:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var docChange = MessagePackSerializer.Deserialize<DocumentChange>(message.Payload);
                    _sessionManager.UpdateDocument(docChange);
                    return null;

                case MessageTypes.RequestCompletion:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var compReq = MessagePackSerializer.Deserialize<CompletionRequest>(message.Payload);
                    var session = _sessionManager.GetSession(compReq.SessionId);
                    var documentText = session?.DocumentText ?? string.Empty;
                    var dbCache = session != null
                        ? _schemaCacheManager.GetCache(compReq.SessionId, session.DatabaseName)
                        : null;
                    var compResp = _completionEngine.GetCompletions(documentText, compReq.CursorOffset, dbCache);
                    return CreateResponse(MessageTypes.CompletionResult, message.RequestId, compResp);

                case MessageTypes.RequestSignatureHelp:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var sigReq = MessagePackSerializer.Deserialize<SignatureRequest>(message.Payload);
                    var sigSession = _sessionManager.GetSession(sigReq.SessionId);
                    var sigText = sigSession?.DocumentText ?? string.Empty;
                    var sigCache = sigSession != null
                        ? _schemaCacheManager.GetCache(sigReq.SessionId, sigSession.DatabaseName)
                        : null;
                    // Extract function name and parameter index from the document text at cursor
                    var sigTokens = _parserService.GetTokenStream(sigText);
                    var (funcName, parenOffset) = FindFunctionAtCursor(sigTokens, sigReq.CursorOffset);
                    var paramIdx = parenOffset >= 0
                        ? SignatureProvider.CountCommasBeforeCursor(sigTokens, sigReq.CursorOffset, parenOffset)
                        : 0;
                    var sigResp = _signatureProvider.GetSignature(funcName, paramIdx, sigCache);
                    return CreateResponse(MessageTypes.SignatureHelpResult, message.RequestId, sigResp);

                case MessageTypes.RequestQuickInfo:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var qiReq = MessagePackSerializer.Deserialize<QuickInfoRequest>(message.Payload);
                    var qiSession = _sessionManager.GetSession(qiReq.SessionId);
                    var qiText = qiSession?.DocumentText ?? string.Empty;
                    var qiCache = qiSession != null
                        ? _schemaCacheManager.GetCache(qiReq.SessionId, qiSession.DatabaseName)
                        : null;
                    var qiResp = _quickInfoProvider.GetQuickInfo(qiText, qiReq.CursorOffset, qiCache, _parserService);
                    return CreateResponse(MessageTypes.QuickInfoResult, message.RequestId, qiResp);

                case MessageTypes.SchemaRefreshRequest:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var refreshReq = MessagePackSerializer.Deserialize<RefreshRequest>(message.Payload);
                    var refreshSession = !string.IsNullOrEmpty(refreshReq.SessionId)
                        ? _sessionManager.GetSession(refreshReq.SessionId) : null;
                    int refreshedCount = 0;
                    if (refreshSession != null)
                    {
                        var refCache = _schemaCacheManager.GetCache(refreshReq.SessionId, refreshSession.DatabaseName);
                        if (refCache != null)
                        {
                            refCache.IsStale = true;
                            refreshedCount = refCache.Schemas.Values.Sum(s => s.Objects.Count);
                        }
                    }
                    var refResp = new RefreshResponse { Success = true, ObjectCount = refreshedCount };
                    return CreateResponse(MessageTypes.SchemaRefreshComplete, message.RequestId, refResp);

                case MessageTypes.Ping:
                    var status = new EngineStatusInfo
                    {
                        MemoryUsageMb = (int)(GC.GetTotalMemory(false) / (1024 * 1024)),
                        CachedDatabases = _schemaCacheManager.CacheCount,
                        ActiveSessions = _sessionManager.SessionCount,
                        UptimeSeconds = 0
                    };
                    return CreateResponse(MessageTypes.Pong, message.RequestId, status);

                case MessageTypes.FormatDocument:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var fmtReq = MessagePackSerializer.Deserialize<FormatRequest>(message.Payload);
                    var fmtResp = _formatHandler.HandleFormat(fmtReq);
                    return CreateResponse(MessageTypes.FormatDocumentResult, message.RequestId, fmtResp);

                case MessageTypes.FormatSelection:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var fmtSelReq = MessagePackSerializer.Deserialize<FormatSelectionRequest>(message.Payload);
                    var fmtSelResp = _formatHandler.HandleFormatSelection(fmtSelReq);
                    return CreateResponse(MessageTypes.FormatSelectionResult, message.RequestId, fmtSelResp);

                case MessageTypes.FormatPreview:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var fmtPrevReq = MessagePackSerializer.Deserialize<FormatPreviewRequest>(message.Payload);
                    var fmtPrevResp = _formatHandler.HandleFormatPreview(fmtPrevReq);
                    return CreateResponse(MessageTypes.FormatPreviewResult, message.RequestId, fmtPrevResp);

                case MessageTypes.FormatAction:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var fmtActReq = MessagePackSerializer.Deserialize<FormatActionRequest>(message.Payload);
                    var fmtActResp = _formatHandler.HandleFormatAction(fmtActReq);
                    return CreateResponse(MessageTypes.FormatActionResult, message.RequestId, fmtActResp);

                case MessageTypes.ProfileList:
                    var profListResp = _formatHandler.HandleProfileList();
                    return CreateResponse(MessageTypes.ProfileListResult, message.RequestId, profListResp);

                case MessageTypes.ProfileSave:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var profSaveReq = MessagePackSerializer.Deserialize<ProfileSaveRequest>(message.Payload);
                    var profSaveResp = _formatHandler.HandleProfileSave(profSaveReq);
                    return CreateResponse(MessageTypes.ProfileSaveResult, message.RequestId, profSaveResp);

                case MessageTypes.ProfileDelete:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var profDelReq = MessagePackSerializer.Deserialize<ProfileDeleteRequest>(message.Payload);
                    var profDelResp = _formatHandler.HandleProfileDelete(profDelReq);
                    return CreateResponse(MessageTypes.ProfileDeleteResult, message.RequestId, profDelResp);

                case MessageTypes.ProfileImport:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var profImpReq = MessagePackSerializer.Deserialize<ProfileImportRequest>(message.Payload);
                    var profImpResp = _formatHandler.HandleProfileImport(profImpReq);
                    return CreateResponse(MessageTypes.ProfileImportResult, message.RequestId, profImpResp);

                case MessageTypes.BulkFormat:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var bulkReq = MessagePackSerializer.Deserialize<BulkFormatRequest>(message.Payload);
                    var bulkResp = _formatHandler.HandleBulkFormatAsync(bulkReq).GetAwaiter().GetResult();
                    return CreateResponse(MessageTypes.BulkFormatResult, message.RequestId, bulkResp);

                case MessageTypes.BulkFormatCancel:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var bulkCancelReq = MessagePackSerializer.Deserialize<BulkFormatCancelRequest>(message.Payload);
                    _formatHandler.HandleBulkFormatCancel(bulkCancelReq);
                    return null; // notification, no response

                case MessageTypes.SnippetExpand:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var snipExpandReq = MessagePackSerializer.Deserialize<SnippetExpandRequest>(message.Payload);
                    var snipSession = _sessionManager.GetSession(snipExpandReq.SessionId);
                    var snipExpandResp = _snippetHandler.HandleExpand(
                        snipExpandReq, snipSession?.DatabaseName);
                    return CreateResponse(MessageTypes.SnippetExpandResult, message.RequestId, snipExpandResp);

                case MessageTypes.SnippetList:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var snipListReq = MessagePackSerializer.Deserialize<SnippetListRequest>(message.Payload);
                    var snipListResp = _snippetHandler.HandleList(snipListReq);
                    return CreateResponse(MessageTypes.SnippetListResult, message.RequestId, snipListResp);

                case MessageTypes.SnippetSave:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var snipSaveReq = MessagePackSerializer.Deserialize<SnippetSaveRequest>(message.Payload);
                    var snipSaveResp = _snippetHandler.HandleSave(snipSaveReq);
                    return CreateResponse(MessageTypes.SnippetSaveResult, message.RequestId, snipSaveResp);

                case MessageTypes.SnippetDelete:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var snipDelReq = MessagePackSerializer.Deserialize<SnippetDeleteRequest>(message.Payload);
                    var snipDelResp = _snippetHandler.HandleDelete(snipDelReq);
                    return CreateResponse(MessageTypes.SnippetDeleteResult, message.RequestId, snipDelResp);

                case MessageTypes.SnippetImport:
                    if (message.Payload == null)
                    {
                        return CreateErrorResponse("Payload required", message.RequestId);
                    }

                    var snipImpReq = MessagePackSerializer.Deserialize<SnippetImportRequest>(message.Payload);
                    var snipImpResp = _snippetHandler.HandleImport(snipImpReq);
                    return CreateResponse(MessageTypes.SnippetImportResult, message.RequestId, snipImpResp);

                case MessageTypes.Shutdown:
                    Log.Information("Shutdown requested by client.");
                    throw new OperationCanceledException("Shutdown requested");

                default:
                    Log.Warning("Unknown message type: {Type}", message.MessageType);
                    return null;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error dispatching message type {Type}", message.MessageType);
            var errorInfo = new ErrorInfo { Code = -1, Message = ex.Message };
            return CreateResponse(MessageTypes.Error, message.RequestId, errorInfo);
        }
    }

    /// <summary>
    /// Walks backwards from cursorOffset to find the function name before the nearest '('.
    /// Returns (functionName, openParenOffset) or ("", -1) if not found.
    /// </summary>
    private static (string FunctionName, int ParenOffset) FindFunctionAtCursor(
        IList<Microsoft.SqlServer.TransactSql.ScriptDom.TSqlParserToken> tokens, int cursorOffset)
    {
        // Find the nearest open paren before cursor at the current nesting level
        int depth = 0;
        int parenTokenIndex = -1;
        for (int i = tokens.Count - 1; i >= 0; i--)
        {
            var t = tokens[i];
            if (t.Offset >= cursorOffset)
            {
                continue;
            }

            if (t.TokenType == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.RightParenthesis)
            {
                depth++;
            }
            else if (t.TokenType == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.LeftParenthesis)
            {
                if (depth == 0)
                {
                    parenTokenIndex = i;
                    break;
                }
                depth--;
            }
        }

        if (parenTokenIndex <= 0)
        {
            return (string.Empty, -1);
        }

        // Walk back from paren to find the identifier (function name)
        for (int i = parenTokenIndex - 1; i >= 0; i--)
        {
            var t = tokens[i];
            if (t.TokenType == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.WhiteSpace)
            {
                continue;
            }

            if (t.TokenType == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.Identifier ||
                t.TokenType == Microsoft.SqlServer.TransactSql.ScriptDom.TSqlTokenType.QuotedIdentifier)
            {
                return (t.Text.Trim('[', ']', '"'), tokens[parenTokenIndex].Offset);
            }
            // Could be a keyword-function like CONVERT, CAST
            return (t.Text, tokens[parenTokenIndex].Offset);
        }

        return (string.Empty, -1);
    }

    private static RpcMessage CreateErrorResponse(string message, int requestId)
    {
        var errorInfo = new ErrorInfo { Code = -1, Message = message };
        return CreateResponse(MessageTypes.Error, requestId, errorInfo);
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

    private static PipeSecurity CreatePipeSecurity()
    {
        var security = new PipeSecurity();
        var currentUser = WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));
        return security;
    }
}
