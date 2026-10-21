using AkmlSql.Core;
using AkmlSql.Core.Config;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.PostgreSql;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using MessagePack;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 042 (FR-092): surfaces with no PostgreSQL implementation (refactorings, Find invalid objects, ...)
/// answer a PostgreSQL session with <c>dialect.unsupported</c> before any handler runs, so the T-SQL
/// implementation never sees PostgreSQL text. The guard sits in the router, so it covers typed and raw
/// handlers alike; SQL Server sessions and unknown sessions take the existing path untouched.
/// </summary>
public sealed class DialectGuardTests
{
    public static IEnumerable<object[]> GuardedMessages => new[]
    {
        new object[] { MessageTypes.RequestRefactorPreview, typeof(RefactorPreviewRequest) },
        new object[] { MessageTypes.RequestRefactorApply, typeof(RefactorApplyRequest) },
        new object[] { MessageTypes.FindInvalidObjects, typeof(FindInvalidObjectsRequest) },
        new object[] { MessageTypes.FindReferences, typeof(FindReferencesRequest) },
        new object[] { MessageTypes.FindUnusedVariables, typeof(FindUnusedVariablesRequest) },
        new object[] { MessageTypes.CrudGeneration, typeof(CrudGenerationRequest) },
        new object[] { MessageTypes.EncryptedObjectDecryption, typeof(EncryptedObjectDecryptionRequest) },
        new object[] { MessageTypes.ScriptAs, typeof(ScriptAsRequest) },
        new object[] { MessageTypes.DocumentOutline, typeof(DocumentOutlineRequest) },
    };

    private static RpcContext Context(SessionManager sessions) => new()
    {
        Sessions = sessions,
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
        SettingsLoader = () => new AppSettings(),
    };

    private static SessionManager SessionsWith(string id, DatabaseKind kind)
    {
        var sessions = new SessionManager();
        sessions.UpdateSession(new ConnectionInfo { SessionId = id, ConnectionString = "x", DatabaseName = "d", Kind = (int)kind });
        return sessions;
    }

    private static RpcMessage Message(int type, Type requestType, string sessionId)
    {
        var request = Activator.CreateInstance(requestType)!;
        requestType.GetProperty("SessionId")!.SetValue(request, sessionId);
        return new RpcMessage { MessageType = type, RequestId = 7, Payload = MessagePackSerializer.Serialize(requestType, request) };
    }

    private sealed class Spy
    {
        public int Calls;
    }

    private static (RpcRouter Router, Spy Spy) RouterWithSpy(int type)
    {
        var spy = new Spy();
        var router = new RpcRouter();
        router.RegisterRaw(type, (m, _) =>
        {
            Interlocked.Increment(ref spy.Calls);
            return Task.FromResult<RpcMessage?>(new RpcMessage { MessageType = 1000, RequestId = m.RequestId });
        });
        router.DialectGuard = DialectGuard.Create();
        return (router, spy);
    }

    [Theory]
    [MemberData(nameof(GuardedMessages))]
    public async Task A_PostgreSQL_session_is_refused_before_the_handler_runs(int type, Type requestType)
    {
        var (router, spy) = RouterWithSpy(type);
        var context = Context(SessionsWith("pg", DatabaseKind.PostgreSql));

        var response = await router.RouteAsync(Message(type, requestType, "pg"), context, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(MessageTypes.Error, response!.MessageType);
        Assert.Equal(7, response.RequestId);
        var error = MessagePackSerializer.Deserialize<ErrorInfo>(response.Payload!);
        Assert.Equal(DialectErrors.UnsupportedCode, error.Code);
        Assert.StartsWith("dialect.unsupported:", error.Message);
        Assert.Contains("Not available for PostgreSQL", error.Message);
        Assert.Equal(0, spy.Calls);
    }

    [Theory]
    [MemberData(nameof(GuardedMessages))]
    public async Task A_SQL_Server_session_reaches_the_handler(int type, Type requestType)
    {
        var (router, spy) = RouterWithSpy(type);
        var context = Context(SessionsWith("ms", DatabaseKind.SqlServer));

        var response = await router.RouteAsync(Message(type, requestType, "ms"), context, CancellationToken.None);

        Assert.Equal(1000, response!.MessageType);
        Assert.Equal(1, spy.Calls);
    }

    [Fact]
    public async Task An_unknown_session_reaches_the_handler()
    {
        var (router, _) = RouterWithSpy(MessageTypes.ScriptAs);

        var response = await router.RouteAsync(Message(MessageTypes.ScriptAs, typeof(ScriptAsRequest), "nobody"), Context(new SessionManager()), CancellationToken.None);

        Assert.Equal(1000, response!.MessageType);
    }

    [Fact]
    public async Task Surfaces_that_exist_for_PostgreSQL_are_not_guarded()
    {
        var (router, _) = RouterWithSpy(MessageTypes.RequestCompletion);
        var context = Context(SessionsWith("pg", DatabaseKind.PostgreSql));

        var response = await router.RouteAsync(Message(MessageTypes.RequestCompletion, typeof(CompletionRequest), "pg"), context, CancellationToken.None);

        Assert.Equal(1000, response!.MessageType);
    }

    [Fact]
    public async Task A_payload_that_does_not_parse_is_left_to_the_handler()
    {
        var (router, _) = RouterWithSpy(MessageTypes.ScriptAs);
        var bad = new RpcMessage { MessageType = MessageTypes.ScriptAs, RequestId = 3, Payload = new byte[] { 0xC1 } };

        var response = await router.RouteAsync(bad, Context(SessionsWith("pg", DatabaseKind.PostgreSql)), CancellationToken.None);

        Assert.Equal(1000, response!.MessageType);
    }

    [Fact]
    public void Every_guarded_type_is_listed_in_the_guard()
    {
        var guarded = DialectGuard.GuardedMessageTypes;
        foreach (var row in GuardedMessages)
            Assert.Contains((int)row[0], guarded);
        Assert.Equal(GuardedMessages.Count(), guarded.Count);
    }
}
