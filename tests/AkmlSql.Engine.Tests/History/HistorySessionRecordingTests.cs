using System;
using System.IO;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

public class HistorySessionRecordingTests : IAsyncLifetime
{
    private string _path = string.Empty;
    private HistoryDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), $"akml-rec-{Guid.NewGuid():N}.db");
        _db = new HistoryDatabase(_path);
        await _db.InitializeAsync();
    }

    public Task DisposeAsync()
    {
        // Best-effort: Microsoft.Data.Sqlite pools the native handle per connection string, so
        // the OS file can still be open past our `await using` disposals. WAL mode also leaves
        // -wal/-shm sidecars next to the main file; delete those too. Same precedent as
        // QuerySessionStoreTests.DisposeAsync (Task 3).
        TryDelete(_path);
        TryDelete(_path + "-wal");
        TryDelete(_path + "-shm");
        return Task.CompletedTask;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private Task<long> Add(string sql, string? sessionKey, string? tabTitle = null) =>
        _db.InsertEntryAsync(sql, false, "localhost", "Northwind", null, 5, 1,
                     (int)ExecutionStatus.Success, null, null, tabTitle, sessionKey);

    [Fact]
    public async Task Executions_sharing_a_session_key_share_one_session_id()
    {
        await Add("SELECT 1", "tab-A");
        await Add("SELECT 2", "tab-A");     // edited query, SAME tab
        await Add("SELECT 3", "tab-B");

        await using var c = new SqliteConnection($"Data Source={_path}");
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(
            "SELECT COUNT(DISTINCT session_id), COUNT(*) FROM history", c);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.Equal(2, r.GetInt32(0));   // two sessions
        Assert.Equal(3, r.GetInt32(1));   // three execution rows — storage unchanged
    }

    [Fact]
    public async Task Null_session_key_still_records()
    {
        // Legacy shell paired with a new engine must keep working.
        var id = await Add("SELECT 1", null);
        Assert.True(id > 0);
    }
}
