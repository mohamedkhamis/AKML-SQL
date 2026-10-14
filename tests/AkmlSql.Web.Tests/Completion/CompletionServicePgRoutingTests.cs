using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using AkmlSql.Web.Tests.Bridge;
using MessagePack;
using Xunit;

namespace AkmlSql.Web.Tests.Completion;

/// <summary>
/// Spec 042 (task T097, FR-061, FR-065) - for a PostgreSQL connection the web asks the engine and only the engine: completion,
/// hover and signature help go over the bridge unchanged, and when the engine is gone there is no offline T-SQL fallback.
/// </summary>
public sealed class CompletionServicePgRoutingTests
{
    private sealed class Gate : IDialectGate
    {
        public Gate(DatabaseKind kind) => Kind = kind;
        public DatabaseKind Kind { get; }
        public bool IsPostgres => Kind == DatabaseKind.PostgreSql;
    }

    /// <summary>An open bridge that records what it is asked and answers from a table.</summary>
    private sealed class Bridge : IEngineBridge
    {
        public readonly List<(int Type, object Request)> Sent = new();
        public Func<int, object, object?> Answer { get; set; } = (_, _) => null;
        public bool Throw { get; set; }

        public BridgeState State { get; set; } = BridgeState.Open;
        public string[] EngineCapabilities { get; } = Array.Empty<string>();
        public string? EngineVersion => null;
        public event Action<BridgeState>? StateChanged { add { } remove { } }
        public event Action<DateTimeOffset?>? RetryScheduled { add { } remove { } }
        public event Action<TlsFingerprintMismatch>? FingerprintMismatchDetected { add { } remove { } }

        public Task<HandshakeResponse> ConnectAsync(EngineConnection connection, string? bearerToken, string? pairingPin, CancellationToken ct) =>
            Task.FromResult(new HandshakeResponse { Status = HandshakeStatus.Ok });

        public Task<TResponse> SendAsync<TRequest, TResponse>(int requestMessageType, TRequest request, CancellationToken ct)
            where TRequest : class
            where TResponse : class
        {
            Sent.Add((requestMessageType, request));
            if (Throw) throw new InvalidOperationException("bridge dropped");
            return Task.FromResult((TResponse)(Answer(requestMessageType, request) ?? Activator.CreateInstance(typeof(TResponse))!));
        }

        public Task SendNotificationAsync<TPayload>(int messageType, TPayload payload, CancellationToken ct) where TPayload : class => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }


    /// <summary>A schema cache holding one snapshot with a table, so an offline fallback WOULD have something to say.</summary>
    private static async Task<(ISchemaCacheStore Cache, IEditorSessionStore Session)> StoresWithASnapshotAsync()
    {
        var db = new InMemoryIndexedDbAdapter();
        var cache = new SchemaCacheStore(db);
        var payload = new SchemaPhasePayload
        {
            DatabaseName = "db", Phase = 1, Checksum = "PhaseA:test",
            Schemas = new[] { new SchemaPhaseSchema { Name = "dbo", Objects = new[] { new SchemaPhaseObject { SchemaName = "dbo", ObjectName = "Customers", ObjectType = 0, Columns = System.Array.Empty<SchemaPhaseColumn>() } } } },
        };
        await cache.SetAsync(new SchemaSnapshot { ServerCanonicalIdentity = "s", DatabaseName = "db", PhaseA = MessagePackSerializer.Serialize(payload) });
        var session = new EditorSessionStore(db);
        await session.SaveAsync(new EditorSessionRecord { DocumentText = "SELECT * FROM Customers", CursorOffset = 8 });
        await session.FlushAsync();
        return (cache, session);
    }

    private static CompletionItem Item(string text) => new() { DisplayText = text, InsertText = text, ObjectType = (int)CompletionObjectType.Table };

    [Fact]
    public async Task A_PostgreSQL_completion_goes_to_the_engine_and_comes_back_unchanged()
    {
        var bridge = new Bridge { Answer = (_, _) => new CompletionResponse { Items = new[] { Item("customers"), Item("orders") }, IsIncomplete = true } };
        var service = new CompletionService(bridge, cache: null, new Gate(DatabaseKind.PostgreSql));
        var request = new CompletionRequest { SessionId = "pg-1", CursorOffset = 14 };

        var response = await service.CompleteAsync(request, CancellationToken.None, liveDocumentText: "SELECT * FROM ");

        var sent = Assert.Single(bridge.Sent);
        Assert.Equal(MessageTypes.RequestCompletion, sent.Type);
        Assert.Same(request, sent.Request);
        Assert.Equal(new[] { "customers", "orders" }, response.Items.Select(i => i.DisplayText).ToArray());
        Assert.True(response.IsIncomplete);
    }

    [Fact]
    public async Task With_the_engine_gone_a_PostgreSQL_session_gets_no_offline_list()
    {
        var gone = new Bridge { State = BridgeState.Disconnected };
        var (cache, _) = await StoresWithASnapshotAsync();
        var service = new CompletionService(gone, cache, new Gate(DatabaseKind.PostgreSql));

        var response = await service.CompleteAsync(new CompletionRequest { SessionId = "pg-1", CursorOffset = 14 }, CancellationToken.None, "SELECT * FROM ");

        Assert.Empty(gone.Sent);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task A_bridge_that_drops_mid_call_gives_no_wrong_suggestions_for_PostgreSQL()
    {
        var dropping = new Bridge { Throw = true };
        var (cache, _) = await StoresWithASnapshotAsync();
        var service = new CompletionService(dropping, cache, new Gate(DatabaseKind.PostgreSql));

        var response = await service.CompleteAsync(new CompletionRequest { SessionId = "pg-1" }, CancellationToken.None, "SELECT ");

        Assert.Single(dropping.Sent);
        Assert.Empty(response.Items);
    }

    [Fact]
    public async Task A_SQL_Server_session_still_has_its_offline_keywords()
    {
        var gone = new Bridge { State = BridgeState.Disconnected };
        var (cache, _) = await StoresWithASnapshotAsync();
        var service = new CompletionService(gone, cache, new Gate(DatabaseKind.SqlServer));

        var response = await service.CompleteAsync(new CompletionRequest { SessionId = "s-1" }, CancellationToken.None, "SELECT ");

        Assert.Contains(response.Items, i => i.DisplayText == "SELECT");
        Assert.Contains(response.Items, i => i.DisplayText == "TOP");
    }

    [Fact]
    public async Task Hover_and_signature_help_for_PostgreSQL_are_the_engines_answers()
    {
        var bridge = new Bridge
        {
            Answer = (type, _) => type == MessageTypes.RequestQuickInfo
                ? new QuickInfoResponse { ObjectType = "Aggregate", Header = "string_agg", Description = "Concatenates the non-null input values into a string" }
                : new SignatureResponse { FunctionName = "date_trunc", ActiveParameter = 1, Overloads = new[] { new SignatureOverload { Label = "date_trunc(text, timestamp)" } } },
        };
        var gate = new Gate(DatabaseKind.PostgreSql);

        var info = await new QuickInfoService(bridge, null, null, gate).GetAsync(new QuickInfoRequest { SessionId = "pg-1", CursorOffset = 8 }, CancellationToken.None);
        var signature = await new SignatureHelpService(bridge, null, null, gate).GetAsync(new SignatureRequest { SessionId = "pg-1", CursorOffset = 20 }, CancellationToken.None);

        Assert.Equal("string_agg", info.Header);
        Assert.Equal("date_trunc", signature.FunctionName);
        Assert.Equal(1, signature.ActiveParameter);
        Assert.Equal(new[] { MessageTypes.RequestQuickInfo, MessageTypes.RequestSignatureHelp }, bridge.Sent.Select(s => s.Type).ToArray());
    }

    [Fact]
    public async Task Hover_and_signature_help_do_not_fall_back_to_the_t_sql_scanner_for_PostgreSQL()
    {
        var gone = new Bridge { State = BridgeState.Disconnected };
        var gate = new Gate(DatabaseKind.PostgreSql);

        var (cache, session) = await StoresWithASnapshotAsync();
        var info = await new QuickInfoService(gone, cache, session, gate).GetAsync(new QuickInfoRequest { CursorOffset = 16 }, CancellationToken.None);
        var signature = await new SignatureHelpService(gone, cache, session, gate).GetAsync(new SignatureRequest { CursorOffset = 16 }, CancellationToken.None);

        Assert.True(string.IsNullOrEmpty(info.Header));
        Assert.True(string.IsNullOrEmpty(signature.FunctionName));
    }

    [Fact]
    public async Task Wildcard_expansion_is_the_engines_for_either_kind()
    {
        var bridge = new Bridge
        {
            Answer = (_, _) => new WildcardExpansionResponse
            {
                Success = true,
                Tables = new[] { new WildcardTableGroup { TableName = "public.customers", Qualifier = "customers", Columns = new[] { new WildcardColumn { ColumnName = "customer_id" }, new WildcardColumn { ColumnName = "\"Event Name\"" } } } },
            },
        };

        var response = await new WildcardExpansionService(bridge).GetAsync(new WildcardExpansionRequest { SessionId = "pg-1", DocumentText = "SELECT * FROM customers", CursorOffset = 8 }, CancellationToken.None);

        Assert.True(response!.Success);
        Assert.Equal(new[] { "customer_id", "\"Event Name\"" }, response.Tables.Single().Columns.Select(c => c.ColumnName).ToArray());
        Assert.Equal(MessageTypes.WildcardExpansion, Assert.Single(bridge.Sent).Type);
    }
}
