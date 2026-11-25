using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.Completion;

/// <summary>
/// Spec 047 (SN-001, contracts/workspace-ai.md §1, data-model §10) — the user's own snippets are offered by completion,
/// so typing a shortcode and pressing Tab expands it. Before, the popup offered only the engine's built-ins online and no
/// snippet at all offline, so a snippet made on the Snippets page never expanded.
/// </summary>
public sealed class CompletionUserSnippetTests
{
    private sealed class Gate : IDialectGate
    {
        public Gate(DatabaseKind kind) => Kind = kind;
        public DatabaseKind Kind { get; }
        public bool IsPostgres => Kind == DatabaseKind.PostgreSql;
    }

    /// <summary>An open (or closed) bridge that answers completion with a fixed list.</summary>
    private sealed class Bridge : IEngineBridge
    {
        public CompletionItem[] Items { get; set; } = Array.Empty<CompletionItem>();
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
            object answer = requestMessageType == MessageTypes.RequestCompletion
                ? new CompletionResponse { Items = Items.ToArray() }
                : Activator.CreateInstance(typeof(TResponse))!;
            return Task.FromResult((TResponse)answer);
        }

        public Task SendNotificationAsync<TPayload>(int messageType, TPayload payload, CancellationToken ct) where TPayload : class => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => default;
    }

    private static WebSnippet Snippet(string shortcode, string title, DatabaseKind kind, params string[] body) => new()
    {
        Metadata = new WebSnippetMetadata { Shortcode = shortcode, Title = title, Kind = (int)kind },
        Body = body,
    };

    private static CompletionItem BuiltIn(string shortcode) => new()
    {
        DisplayText = shortcode, InsertText = shortcode + " body", ObjectType = (int)CompletionObjectType.Snippet,
        SecondaryText = "built-in", SortPriority = 500,
    };

    private static CompletionItem Keyword(string text) => new()
    {
        DisplayText = text, InsertText = text, ObjectType = (int)CompletionObjectType.Keyword, SortPriority = 50,
    };

    private static async Task<SnippetStore> StoreWithAsync(params WebSnippet[] snippets)
    {
        var store = new SnippetStore(new InMemoryIndexedDbAdapter());
        foreach (var s in snippets) await store.SaveAsync(s);
        return store;
    }

    private static Task<CompletionResponse> CompleteAsync(CompletionService service, string text) =>
        service.CompleteAsync(new CompletionRequest { CursorOffset = text.Length }, CancellationToken.None, text);

    private static IEnumerable<CompletionItem> Snippets(CompletionResponse response) =>
        response.Items.Where(i => i.ObjectType == (int)CompletionObjectType.Snippet);

    [Fact]
    public async Task Online_the_users_snippets_join_the_engines_snippet_list_in_the_mapped_shape()
    {
        var store = await StoreWithAsync(Snippet("qaz", "Region list", DatabaseKind.SqlServer, "SELECT $CURSOR$", "FROM dbo.Region;"));
        var bridge = new Bridge { Items = new[] { Keyword("SELECT"), BuiltIn("ssf") } };
        var service = new CompletionService(bridge, null, null, null, store);

        // Mid-statement text: only the engine's own snippet item says snippets belong here.
        var response = await CompleteAsync(service, "SELECT 1 q");

        var user = Assert.Single(response.Items, i => i.DisplayText == "qaz");
        Assert.Equal("SELECT $CURSOR$\nFROM dbo.Region;", user.InsertText);
        Assert.Equal((int)CompletionObjectType.Snippet, user.ObjectType);
        Assert.Equal("Region list", user.SecondaryText);
        Assert.Equal(500, user.SortPriority);
        Assert.Equal(string.Empty, user.SourceObject);
        Assert.Contains(response.Items, i => i.DisplayText == "ssf");   // the engine's items stay
    }

    [Fact]
    public async Task Online_mid_clause_with_no_engine_snippet_adds_none()
    {
        var store = await StoreWithAsync(Snippet("qaz", "Region list", DatabaseKind.SqlServer, "SELECT 1;"));
        var bridge = new Bridge { Items = new[] { Keyword("WHERE") } };
        var service = new CompletionService(bridge, null, null, null, store);

        var response = await CompleteAsync(service, "SELECT * FROM dbo.Region WHERE qa");

        Assert.Empty(Snippets(response));
    }

    [Fact]
    public async Task Online_at_a_statement_start_they_are_offered_even_when_the_engine_filtered_its_own_away()
    {
        // The engine filters its list by the typed word, so for "qa" it returns no built-in snippet; the caret is still at a
        // statement start, where a snippet belongs.
        var store = await StoreWithAsync(Snippet("qaz", "Region list", DatabaseKind.SqlServer, "SELECT 1;"));
        var bridge = new Bridge { Items = new[] { Keyword("QUOTENAME") } };
        var service = new CompletionService(bridge, null, null, null, store);

        var response = await CompleteAsync(service, "SELECT 1;\nqa");

        Assert.Single(Snippets(response), i => i.DisplayText == "qaz");
    }

    [Theory]
    [InlineData("qa")]                          // the document start
    [InlineData("SELECT 1;\nqa")]              // after a semicolon
    [InlineData("SELECT 1\nGO\nqa")]            // after GO
    [InlineData("SELECT 1\n\nqa")]              // after a blank line
    [InlineData("-- a note\n/* and more */\nqa")]   // comments are ignored
    public async Task Offline_they_are_offered_at_a_statement_start(string text)
    {
        var store = await StoreWithAsync(Snippet("qaz", "Region list", DatabaseKind.SqlServer, "SELECT 1;"));
        var service = new CompletionService(new Bridge { State = BridgeState.Disconnected }, new SchemaCacheStore(new InMemoryIndexedDbAdapter()), null, null, store);

        var response = await CompleteAsync(service, text);

        Assert.Single(Snippets(response), i => i.DisplayText == "qaz");
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.Region WHERE qa")]
    [InlineData("SELECT 1 -- note\nqa")]
    [InlineData("SELECT qa")]
    public async Task Offline_mid_clause_they_are_not(string text)
    {
        var store = await StoreWithAsync(Snippet("qaz", "Region list", DatabaseKind.SqlServer, "SELECT 1;"));
        var service = new CompletionService(new Bridge { State = BridgeState.Disconnected }, new SchemaCacheStore(new InMemoryIndexedDbAdapter()), null, null, store);

        var response = await CompleteAsync(service, text);

        Assert.Empty(Snippets(response));
    }

    [Fact]
    public async Task A_PostgreSQL_tab_gets_only_its_own_kind()
    {
        var store = await StoreWithAsync(
            Snippet("qaz", "T-SQL one", DatabaseKind.SqlServer, "SELECT TOP 1 1;"),
            Snippet("pgq", "PostgreSQL one", DatabaseKind.PostgreSql, "SELECT 1 LIMIT 1;"));
        var bridge = new Bridge { Items = new[] { BuiltIn("ssf") } };
        var service = new CompletionService(bridge, null, new Gate(DatabaseKind.PostgreSql), null, store);

        var response = await CompleteAsync(service, "q");

        var user = Snippets(response).Where(i => i.DisplayText != "ssf").Select(i => i.DisplayText).ToList();
        Assert.Equal(new[] { "pgq" }, user);
    }

    [Fact]
    public async Task A_user_snippet_with_a_built_in_shortcode_replaces_it()
    {
        var store = await StoreWithAsync(Snippet("ssf", "Mine", DatabaseKind.SqlServer, "SELECT * FROM dbo.Mine;"));
        var bridge = new Bridge { Items = new[] { BuiltIn("ssf"), BuiltIn("sel") } };
        var service = new CompletionService(bridge, null, null, null, store);

        var response = await CompleteAsync(service, "s");

        var ssf = Assert.Single(response.Items, i => i.DisplayText == "ssf");
        Assert.Equal("SELECT * FROM dbo.Mine;", ssf.InsertText);
        Assert.Contains(response.Items, i => i.DisplayText == "sel");
    }

    [Fact]
    public async Task The_list_is_kept_until_the_store_says_it_changed()
    {
        var db = new InMemoryIndexedDbAdapter();
        var store = new SnippetStore(db);
        await store.SaveAsync(Snippet("qaz", "First", DatabaseKind.SqlServer, "SELECT 1;"));
        var service = new CompletionService(new Bridge { State = BridgeState.Disconnected }, new SchemaCacheStore(new InMemoryIndexedDbAdapter()), null, null, store);
        Assert.Single(Snippets(await CompleteAsync(service, "q")));

        // Written behind the store's back: no Changed, so the kept list is used.
        var hidden = Snippet("qbx", "Hidden", DatabaseKind.SqlServer, "SELECT 2;");
        hidden.Metadata.Id = "hidden";
        await db.SetAsync(StoreNames.Snippets, "hidden", JsonSerializer.Serialize(hidden));
        Assert.DoesNotContain(Snippets(await CompleteAsync(service, "q")), i => i.DisplayText == "qbx");

        // A save through the store raises Changed: the list is read again, with both.
        await store.SaveAsync(Snippet("qcy", "Third", DatabaseKind.SqlServer, "SELECT 3;"));
        var names = Snippets(await CompleteAsync(service, "q")).Select(i => i.DisplayText).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "qaz", "qbx", "qcy" }, names);

        // A delete too.
        var all = await store.ListAsync();
        await store.DeleteAsync(all.Single(s => s.Metadata.Shortcode == "qaz").Metadata.Id!);
        Assert.DoesNotContain(Snippets(await CompleteAsync(service, "q")), i => i.DisplayText == "qaz");
    }
}
