using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Tabs;
using MessagePack;
using Serilog;

namespace AkmlSql.Engine.Sessions;

/// <summary>
/// Handles session save/restore/delete IPC requests from the shell extension.
/// Delegates persistence to <see cref="SessionStorage"/>.
/// </summary>
public class SessionRequestHandler
{
    private readonly SessionStorage _storage = new();

    /// <summary>
    /// Dispatches a session-related IPC message to the appropriate handler method.
    /// </summary>
    public async Task<RpcMessage?> HandleAsync(RpcMessage request, int messageType)
    {
        switch (messageType)
        {
            case MessageTypes.SessionSave:
                return await HandleSaveAsync(request);

            case MessageTypes.SessionRestore:
                return await HandleRestoreAsync(request);

            case MessageTypes.SessionDelete:
                return await HandleDeleteAsync(request);

            default:
                Log.Warning("SessionRequestHandler received unknown message type: {Type}", messageType);
                return null;
        }
    }

    private async Task<RpcMessage?> HandleSaveAsync(RpcMessage request)
    {
        try
        {
            if (request.Payload == null)
            {
                return CreateResponse(MessageTypes.SessionSaveResult, request.RequestId,
                    new SessionSaveResponse { Success = false, Error = "Payload required" });
            }

            var saveReq = MessagePackSerializer.Deserialize<SessionSaveRequest>(request.Payload);

            // Convert DTO to domain model
            var snapshot = new SessionSnapshot
            {
                SessionId = saveReq.SessionId,
                CapturedAt = DateTime.UtcNow,
                SsmsProcessId = saveReq.SsmsProcessId,
                IsNormalShutdown = saveReq.IsNormalShutdown,
                Tabs = ConvertTabs(saveReq.Tabs)
            };

            await _storage.SaveAsync(snapshot);

            // Purge old sessions to prevent unbounded disk usage
            await _storage.PurgeOldSessionsAsync();

            return CreateResponse(MessageTypes.SessionSaveResult, request.RequestId,
                new SessionSaveResponse { Success = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SessionSave handler failed");
            return CreateResponse(MessageTypes.SessionSaveResult, request.RequestId,
                new SessionSaveResponse { Success = false, Error = ex.Message });
        }
    }

    private async Task<RpcMessage?> HandleRestoreAsync(RpcMessage request)
    {
        try
        {
            var allSessions = await _storage.LoadAllAsync();

            // Filter to sessions that were NOT cleanly shut down (crash recovery candidates)
            var recoverable = allSessions
                .Where(s => !s.IsNormalShutdown)
                .Select(s => new RecoverableSessionDto
                {
                    SessionId = s.SessionId,
                    CapturedAt = s.CapturedAt.ToString("o"), // ISO 8601
                    TabCount = s.Tabs.Count,
                    Tabs = s.Tabs.Select(t => new TabSnapshotDto
                    {
                        TabIndex = t.TabIndex,
                        Title = t.Title,
                        Content = t.Content,
                        FilePath = t.FilePath,
                        Server = t.Server,
                        Database = t.Database,
                        AuthType = t.AuthType,
                        CursorLine = t.CursorLine,
                        CursorColumn = t.CursorColumn,
                        IsPinned = t.IsPinned
                    }).ToArray()
                })
                .ToArray();

            var response = new SessionRestoreResponse
            {
                HasRecoverableSessions = recoverable.Length > 0,
                Sessions = recoverable
            };

            Log.Information("SessionRestore: found {Count} recoverable sessions", recoverable.Length);

            return CreateResponse(MessageTypes.SessionRestoreResult, request.RequestId, response);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SessionRestore handler failed");
            return CreateResponse(MessageTypes.SessionRestoreResult, request.RequestId,
                new SessionRestoreResponse
                {
                    HasRecoverableSessions = false,
                    Error = ex.Message
                });
        }
    }

    private async Task<RpcMessage?> HandleDeleteAsync(RpcMessage request)
    {
        try
        {
            if (request.Payload == null)
            {
                return CreateResponse(MessageTypes.SessionDeleteResult, request.RequestId,
                    new SessionDeleteResponse { Success = false, Error = "Payload required" });
            }

            var deleteReq = MessagePackSerializer.Deserialize<SessionDeleteRequest>(request.Payload);
            await _storage.DeleteAsync(deleteReq.SessionId);

            return CreateResponse(MessageTypes.SessionDeleteResult, request.RequestId,
                new SessionDeleteResponse { Success = true });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SessionDelete handler failed");
            return CreateResponse(MessageTypes.SessionDeleteResult, request.RequestId,
                new SessionDeleteResponse { Success = false, Error = ex.Message });
        }
    }

    private static List<TabSnapshot> ConvertTabs(TabSnapshotDto[] dtos)
    {
        var tabs = new List<TabSnapshot>(dtos.Length);
        foreach (var dto in dtos)
        {
            tabs.Add(new TabSnapshot
            {
                TabIndex = dto.TabIndex,
                Title = dto.Title,
                Content = dto.Content,
                FilePath = dto.FilePath,
                Server = dto.Server,
                Database = dto.Database,
                AuthType = dto.AuthType,
                CursorLine = dto.CursorLine,
                CursorColumn = dto.CursorColumn,
                IsPinned = dto.IsPinned
            });
        }
        return tabs;
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
