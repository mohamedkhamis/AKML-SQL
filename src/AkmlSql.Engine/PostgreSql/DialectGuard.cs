using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using MessagePack;

namespace AkmlSql.Engine.PostgreSql;

/// <summary>
/// Spec 042 (FR-092, research R19) - the router-level check that keeps T-SQL-only surfaces away from PostgreSQL
/// sessions. A request for a surface that has no PostgreSQL implementation (the refactorings, Find invalid objects,
/// script-as, document outline, CRUD generation and the like) is answered with the standard error envelope,
/// <see cref="DialectErrors.Unsupported"/> / "Not available for PostgreSQL", before any handler runs, so the T-SQL
/// parser never sees PostgreSQL text and the SQL Server path is not touched at all.
///
/// <para>
/// Everything is decided from the message type and the request's <c>SessionId</c>. A session the engine does not
/// know, a SQL Server session and a payload that does not deserialise all fall through to the handler, which then
/// behaves exactly as it did before the feature.
/// </para>
/// </summary>
internal static class DialectGuard
{
    // Message type -> reads the SessionId out of that request's payload. One entry per guarded surface.
    private static readonly Dictionary<int, Func<byte[], string?>> SessionIdReaders = new()
    {
        [MessageTypes.RequestRefactorPreview] = payload => MessagePackSerializer.Deserialize<RefactorPreviewRequest>(payload).SessionId,
        [MessageTypes.RequestRefactorApply] = payload => MessagePackSerializer.Deserialize<RefactorApplyRequest>(payload).SessionId,
        [MessageTypes.FindInvalidObjects] = payload => MessagePackSerializer.Deserialize<FindInvalidObjectsRequest>(payload).SessionId,
        [MessageTypes.FindReferences] = payload => MessagePackSerializer.Deserialize<FindReferencesRequest>(payload).SessionId,
        [MessageTypes.FindUnusedVariables] = payload => MessagePackSerializer.Deserialize<FindUnusedVariablesRequest>(payload).SessionId,
        [MessageTypes.CrudGeneration] = payload => MessagePackSerializer.Deserialize<CrudGenerationRequest>(payload).SessionId,
        [MessageTypes.EncryptedObjectDecryption] = payload => MessagePackSerializer.Deserialize<EncryptedObjectDecryptionRequest>(payload).SessionId,
        [MessageTypes.ScriptAs] = payload => MessagePackSerializer.Deserialize<ScriptAsRequest>(payload).SessionId,
        [MessageTypes.DocumentOutline] = payload => MessagePackSerializer.Deserialize<DocumentOutlineRequest>(payload).SessionId,
    };

    /// <summary>The request message types this guard answers for a PostgreSQL session.</summary>
    internal static IReadOnlyCollection<int> GuardedMessageTypes => SessionIdReaders.Keys;

    /// <summary>The function to assign to <see cref="RpcRouter.DialectGuard"/>.</summary>
    internal static Func<RpcMessage, RpcContext, RpcMessage?> Create() => TryRefuse;

    internal static RpcMessage? TryRefuse(RpcMessage message, RpcContext context)
    {
        if (!SessionIdReaders.TryGetValue(message.MessageType, out var read)) return null;
        if (message.Payload == null || message.Payload.Length == 0) return null;

        string? sessionId;
        try { sessionId = read(message.Payload); }
        catch { return null; } // not our business: the handler reports a malformed payload as it always did

        if (context.Sessions.KindOf(sessionId) != DatabaseKind.PostgreSql) return null;

        return RpcResponseFactory.CreateResponse(
            MessageTypes.Error,
            message.RequestId,
            new ErrorInfo { Code = DialectErrors.UnsupportedCode, Message = DialectErrors.UnsupportedMessage });
    }
}
