using System;
using System.IO;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;

namespace AkmlSql.Engine.Tests.History;

/// <summary>
/// Spec 040 — a throwaway history database for the US2/US5 tests: one temp file per test class,
/// deleted (with its WAL/SHM side files) on dispose, plus small helpers for raw SQL.
/// </summary>
internal sealed class HistoryTestDb : IAsyncDisposable
{
    private HistoryTestDb(string path)
    {
        Path = path;
        Database = new HistoryDatabase(path);
    }

    public string Path { get; }
    public HistoryDatabase Database { get; }
    public string ConnectionString => $"Data Source={Path}";

    public static async Task<HistoryTestDb> CreateAsync(string prefix)
    {
        var db = new HistoryTestDb(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db"));
        await db.Database.InitializeAsync();
        return db;
    }

    /// <summary>A file path for a database that has not been initialised yet.</summary>
    public static HistoryTestDb Uninitialised(string prefix) =>
        new HistoryTestDb(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.db"));

    public Task<long> RunAsync(string sql, string sessionKey, string? source = null, string server = "srv1", string database = "Northwind") =>
        Database.InsertEntryAsync(sql, false, server, database, null, 5, 1, (int)ExecutionStatus.Success, null,
            source: source, tabTitle: null, sessionKey: sessionKey);

    public async Task ExecAsync(string sql)
    {
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqliteCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<object?> ScalarAsync(string sql)
    {
        await using var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new SqliteCommand(sql, conn);
        return await cmd.ExecuteScalarAsync();
    }

    public async Task<long> CountAsync(string sql) => Convert.ToInt64(await ScalarAsync(sql));

    public ValueTask DisposeAsync()
    {
        Database.Dispose();
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { Path, Path + "-wal", Path + "-shm" })
        {
            try { if (File.Exists(p)) File.Delete(p); } catch { /* best effort */ }
        }
        return ValueTask.CompletedTask;
    }
}
