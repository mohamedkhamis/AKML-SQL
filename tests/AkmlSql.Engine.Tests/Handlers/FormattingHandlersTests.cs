using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine;
using AkmlSql.Engine.Formatter;
using AkmlSql.Engine.Handlers.Formatting;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Server;
using AkmlSql.Engine.Transports;
using AkmlSql.Formatting.Profiles;
using MessagePack;
using Serilog;
using Xunit;

namespace AkmlSql.Engine.Tests.Handlers;

/// <summary>
/// Spec 021 (web edition) -- M0.3 task T013. Exercises the nine typed formatting handlers
/// through both <see cref="InProcessTransport"/> + <see cref="RpcRouter"/> and the
/// <see cref="TypedHandlerAdapter{TRequest,TResponse}"/> named-pipe path.
/// </summary>
public sealed class FormattingHandlersTests : IDisposable
{
    private readonly string _builtInDir;
    private readonly string _customDir;
    private readonly FormatRequestHandler _inner;

    public FormattingHandlersTests()
    {
        _builtInDir = Path.Combine(Path.GetTempPath(), $"akml_fh_builtin_{Guid.NewGuid():N}");
        _customDir = Path.Combine(Path.GetTempPath(), $"akml_fh_custom_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_builtInDir);
        Directory.CreateDirectory(_customDir);
        var profileManager = new ProfileManager(_builtInDir, _customDir);
        _inner = new FormatRequestHandler(profileManager);
    }

    public void Dispose()
    {
        try { Directory.Delete(_builtInDir, recursive: true); } catch { }
        try { Directory.Delete(_customDir, recursive: true); } catch { }
    }

    private static RpcContext CreateContext() => new()
    {
        Sessions = new SessionManager(),
        SchemaCache = new SchemaCacheManager(),
        Logger = Log.Logger,
    };

    [Fact]
    public async Task FormatDocumentHandler_round_trips_through_RpcRouter()
    {
        var router = new RpcRouter();
        router.Register(new FormatDocumentHandler(_inner));
        var ctx = CreateContext();

        await using var transport = new InProcessTransport();
        transport.RequestReceived += (msg, ct) => router.RouteAsync(msg, ctx, ct);
        await transport.StartAsync(CancellationToken.None);

        var msg = new RpcMessage
        {
            MessageType = MessageTypes.FormatDocument,
            RequestId = 1,
            Payload = MessagePackSerializer.Serialize(new FormatRequest { Text = "select 1", ProfileName = null }),
        };

        var response = await transport.SendAsync(msg, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(MessageTypes.FormatDocumentResult, response!.MessageType);
        var typed = MessagePackSerializer.Deserialize<FormatResponse>(response.Payload!);
        Assert.True(typed.Success);
    }

    [Fact]
    public async Task ProfileListHandler_tolerates_null_payload()
    {
        // ProfileList is the one format handler that ships with null Payload from the shell.
        var handler = new ProfileListHandler(_inner);
        Assert.True(((IRpcRequestHandler<ProfileListRequest, ProfileListResponse>)handler).AllowsEmptyPayload);

        var ctx = CreateContext();
        var adapter = new TypedHandlerAdapter<ProfileListRequest, ProfileListResponse>(handler, ctx);

        var response = await adapter.HandleAsync(new RpcMessage
        {
            MessageType = MessageTypes.ProfileList,
            RequestId = 2,
            Payload = null,
        }, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(MessageTypes.ProfileListResult, response!.MessageType);
    }

    [Fact]
    public async Task TypedHandlerAdapter_passes_default_request_when_AllowsEmptyPayload_is_true()
    {
        // Same coverage as above but via the adapter contract, not the convenience constructor.
        var handler = new ProfileListHandler(_inner);
        var ctx = CreateContext();
        var adapter = new TypedHandlerAdapter<ProfileListRequest, ProfileListResponse>(handler, ctx);

        var responseNull = await adapter.HandleAsync(new RpcMessage { MessageType = 0, RequestId = 0, Payload = null }, CancellationToken.None);
        var responseEmpty = await adapter.HandleAsync(new RpcMessage { MessageType = 0, RequestId = 0, Payload = Array.Empty<byte>() }, CancellationToken.None);

        Assert.NotNull(responseNull);
        Assert.NotNull(responseEmpty);
    }

    [Fact]
    public void All_formatting_handlers_advertise_correct_message_type_pair()
    {
        // Sanity check: each handler's request/response codes match MessageTypes.
        Assert.Equal((MessageTypes.FormatDocument, MessageTypes.FormatDocumentResult),
            (new FormatDocumentHandler(_inner).RequestMessageType, new FormatDocumentHandler(_inner).ResponseMessageType));
        Assert.Equal((MessageTypes.FormatSelection, MessageTypes.FormatSelectionResult),
            (new FormatSelectionHandler(_inner).RequestMessageType, new FormatSelectionHandler(_inner).ResponseMessageType));
        Assert.Equal((MessageTypes.FormatPreview, MessageTypes.FormatPreviewResult),
            (new FormatPreviewHandler(_inner).RequestMessageType, new FormatPreviewHandler(_inner).ResponseMessageType));
        Assert.Equal((MessageTypes.FormatAction, MessageTypes.FormatActionResult),
            (new FormatActionHandler(_inner).RequestMessageType, new FormatActionHandler(_inner).ResponseMessageType));
        Assert.Equal((MessageTypes.ProfileList, MessageTypes.ProfileListResult),
            (new ProfileListHandler(_inner).RequestMessageType, new ProfileListHandler(_inner).ResponseMessageType));
        Assert.Equal((MessageTypes.ProfileSave, MessageTypes.ProfileSaveResult),
            (new ProfileSaveHandler(_inner).RequestMessageType, new ProfileSaveHandler(_inner).ResponseMessageType));
        Assert.Equal((MessageTypes.ProfileDelete, MessageTypes.ProfileDeleteResult),
            (new ProfileDeleteHandler(_inner).RequestMessageType, new ProfileDeleteHandler(_inner).ResponseMessageType));
        Assert.Equal((MessageTypes.ProfileImport, MessageTypes.ProfileImportResult),
            (new ProfileImportHandler(_inner).RequestMessageType, new ProfileImportHandler(_inner).ResponseMessageType));
        Assert.Equal((MessageTypes.RequestStyleEditorSchema, MessageTypes.StyleEditorSchemaResult),
            (new StyleEditorSchemaHandler(_inner).RequestMessageType, new StyleEditorSchemaHandler(_inner).ResponseMessageType));
    }

    [Fact]
    public async Task FormatDocumentHandler_returns_error_response_on_null_payload()
    {
        // Default AllowsEmptyPayload=false path must still surface Error.
        var handler = new FormatDocumentHandler(_inner);
        Assert.False(((IRpcRequestHandler<FormatRequest, FormatResponse>)handler).AllowsEmptyPayload);
        var ctx = CreateContext();
        var adapter = new TypedHandlerAdapter<FormatRequest, FormatResponse>(handler, ctx);

        var response = await adapter.HandleAsync(new RpcMessage
        {
            MessageType = MessageTypes.FormatDocument,
            RequestId = 99,
            Payload = null,
        }, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal(MessageTypes.Error, response!.MessageType);
        Assert.Equal(99, response.RequestId);
    }
}
