using System;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.History;
using MessagePack;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 040 (T059, contracts/ipc.md) — the History action handler routes the new request keys:
/// group-scoped Delete / ToggleFavorite / GetVersions, open state by session, and ReconcileOpen;
/// requests without the new keys behave as before.
/// </summary>
public sealed class HistoryRequestHandlerTests : IAsyncLifetime
{
    private const int DeadPid = int.MaxValue - 1;
    private HistoryTestDb _db = null!;
    private HistoryRequestHandler _handler = null!;
    private long _a1, _a2, _a3, _b1;

    public async Task InitializeAsync()
    {
        _db = await HistoryTestDb.CreateAsync("akml-handler");
        _handler = new HistoryRequestHandler(_db.Database);
        _a1 = await _db.RunAsync("SELECT one", "tab-A");
        _a2 = await _db.RunAsync("SELECT two", "tab-A");
        _a3 = await _db.RunAsync("SELECT one", "tab-A");
        _b1 = await _db.RunAsync("SELECT other", "tab-B");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private async Task<HistoryActionResponse> SendAsync(HistoryActionRequest request)
    {
        var reply = await _handler.HandleActionAsync(new RpcMessage
        {
            MessageType = MessageTypes.HistoryAction,
            RequestId = 7,
            Payload = MessagePackSerializer.Serialize(request),
        });
        Assert.NotNull(reply);
        return MessagePackSerializer.Deserialize<HistoryActionResponse>(reply!.Payload!);
    }

    [Fact]
    public async Task Group_delete_removes_the_whole_session_and_reports_the_count()
    {
        var r = await SendAsync(new HistoryActionRequest { Action = HistoryActions.Delete, EntryIds = new[] { _a2 }, GroupScope = true });

        Assert.True(r.Success);
        Assert.Equal(3, r.DeletedCount);
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM history"));
    }

    [Fact]
    public async Task Group_delete_of_several_selected_rows_removes_every_one()
    {
        // Delete on three selected rows sends their three ids; each row's whole query goes.
        var c1 = await _db.RunAsync("SELECT third", "tab-C");
        var r = await SendAsync(new HistoryActionRequest { Action = HistoryActions.Delete, EntryIds = new[] { _a3, _b1, c1 }, GroupScope = true });

        Assert.True(r.Success);
        Assert.Equal(5, r.DeletedCount);
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history"));
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM query_sessions"));
    }

    [Fact]
    public async Task Two_ids_of_one_query_delete_it_once()
    {
        var r = await SendAsync(new HistoryActionRequest { Action = HistoryActions.Delete, EntryIds = new[] { _a1, _a3 }, GroupScope = true });

        Assert.True(r.Success);
        Assert.Equal(3, r.DeletedCount);
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM history"));
    }

    [Fact]
    public async Task Per_id_delete_is_unchanged_and_now_reports_the_count()
    {
        var r = await SendAsync(new HistoryActionRequest { Action = HistoryActions.Delete, EntryIds = new[] { _a2, _b1 } });

        Assert.True(r.Success);
        Assert.Equal(2, r.DeletedCount);
        Assert.Equal(2, await _db.CountAsync("SELECT COUNT(*) FROM history"));
    }

    [Fact]
    public async Task Group_favorite_stars_the_session_and_returns_the_new_state()
    {
        var on = await SendAsync(new HistoryActionRequest { Action = HistoryActions.ToggleFavorite, EntryIds = new[] { _a1 }, GroupScope = true });
        Assert.True(on.IsFavorite);
        Assert.Equal(3, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_favorite = 1"));

        var off = await SendAsync(new HistoryActionRequest { Action = HistoryActions.ToggleFavorite, EntryIds = new[] { _a3 }, GroupScope = true });
        Assert.False(off.IsFavorite);
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_favorite = 1"));
    }

    [Fact]
    public async Task Per_id_favorite_is_unchanged_and_returns_the_new_state()
    {
        var r = await SendAsync(new HistoryActionRequest { Action = HistoryActions.ToggleFavorite, EntryIds = new[] { _a1 } });

        Assert.True(r.IsFavorite);
        Assert.Equal(1, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_favorite = 1"));
    }

    [Fact]
    public async Task Group_versions_cover_the_whole_session()
    {
        var r = await SendAsync(new HistoryActionRequest { Action = HistoryActions.GetVersions, EntryIds = new[] { _a1 }, GroupScope = true });

        Assert.Equal(new[] { "SELECT one", "SELECT two" }, r.Versions!.Select(v => v.SqlText).ToArray());
    }

    [Fact]
    public async Task Per_id_versions_are_that_entrys_snapshots()
    {
        await _db.Database.InsertVersionAsync(_a1, "SELECT one -- edit");

        var r = await SendAsync(new HistoryActionRequest { Action = HistoryActions.GetVersions, EntryIds = new[] { _a1 } });

        Assert.Equal("SELECT one -- edit", Assert.Single(r.Versions!).SqlText);
    }

    [Fact]
    public async Task Open_status_by_session_and_by_ids()
    {
        await SendAsync(new HistoryActionRequest { Action = HistoryActions.SetOpenStatus, SessionKey = "tab-A", OwnerPid = 77, IsOpen = true });
        Assert.Equal(3, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_open = 1 AND open_pid = 77"));

        await SendAsync(new HistoryActionRequest { Action = HistoryActions.SetOpenStatus, SessionKey = "tab-A", OwnerPid = 77, IsOpen = false });
        Assert.Equal(0, await _db.CountAsync("SELECT COUNT(*) FROM history WHERE is_open = 1"));

        var legacy = await SendAsync(new HistoryActionRequest { Action = HistoryActions.SetOpenStatus, EntryIds = new[] { _b1 }, IsOpen = true });
        Assert.True(legacy.Success);
        Assert.Equal(1, await _db.CountAsync($"SELECT COUNT(*) FROM history WHERE is_open = 1 AND id = {_b1}"));
    }

    [Fact]
    public async Task Reconcile_returns_the_queries_open_when_their_shell_died()
    {
        await _db.Database.SetOpenStatusBySessionAsync("tab-A", true, DeadPid);
        await _db.Database.SetOpenStatusBySessionAsync("tab-B", true, DeadPid);

        var r = await SendAsync(new HistoryActionRequest
        {
            Action = HistoryActions.ReconcileOpen,
            OwnerPid = Environment.ProcessId,
            OpenSessionKeys = Array.Empty<string>(),
        });

        Assert.True(r.Success);
        Assert.Equal(new[] { _b1, _a3 }, r.RestorableEntryIds);
    }
}
