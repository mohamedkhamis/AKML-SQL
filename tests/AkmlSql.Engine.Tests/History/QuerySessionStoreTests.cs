using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

public class QuerySessionStoreTests : IAsyncLifetime
{
    private string _path = string.Empty;
    private string _cs = string.Empty;
    private QuerySessionStore _store = null!;

    public async Task InitializeAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), $"akml-qs-{Guid.NewGuid():N}.db");
        _cs = $"Data Source={_path}";
        await new HistoryDatabase(_path).InitializeAsync();
        _store = new QuerySessionStore(_cs);
    }

    public Task DisposeAsync()
    {
        // Best-effort: Microsoft.Data.Sqlite pools the native handle per connection string, so
        // the OS file can still be open past our `await using` disposals. Deliberately NOT
        // calling SqliteConnection.ClearAllPools() here — it is process-global and would close
        // pooled connections held by other engine tests running in parallel (see the identical
        // precedent + rationale in HistoryDedupRepresentativeTests.Dispose). A still-pooled temp
        // file is harmless; the OS reclaims %TEMP% eventually. WAL mode also leaves -wal/-shm
        // sidecars next to the main file; delete those too (same precedent).
        TryDelete(_path);
        TryDelete(_path + "-wal");
        TryDelete(_path + "-shm");
        return Task.CompletedTask;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }

    private async Task<(string Name, int NameSource, string LocalDate)> Read(long id)
    {
        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(
            "SELECT name, name_source, local_date FROM query_sessions WHERE id=@id", c);
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        return (r.GetString(0), r.GetInt32(1), r.GetString(2));
    }

    [Fact]
    public async Task Same_key_returns_same_session()
    {
        var now = DateTime.UtcNow;
        var a = await _store.GetOrCreateAsync("key-A", now, null, "localhost", "Northwind");
        var b = await _store.GetOrCreateAsync("key-A", now, null, "localhost", "Northwind");
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Ordinals_increment_within_a_day_and_reset_on_the_next()
    {
        // Deliberately close to LOCAL midnight, not mid-day: at 10:00/11:00 local, the local
        // calendar day and the UTC calendar day coincide for almost every real-world UTC offset
        // (roughly -13..+11), so a regression that buckets by UTC day instead of local day would
        // still pass. Anchoring day1/day1b at 23:00/23:30 local (same local day) and day2 at
        // 00:30 local the NEXT day makes the two schemes diverge for essentially every nonzero
        // offset: day2's UTC-day instant lands on the SAME UTC day as day1/day1b for most
        // offsets (a UTC-bucketing bug would wrongly continue the sequence as query-03 instead
        // of resetting to query-01). Only an offset of exactly 0 (local == UTC) can't discriminate
        // here, which is an inherent limit, not a gap in this test.
        var day1 = DateTime.SpecifyKind(DateTime.Today.AddHours(23), DateTimeKind.Local).ToUniversalTime();
        var day1b = DateTime.SpecifyKind(DateTime.Today.AddHours(23).AddMinutes(30), DateTimeKind.Local).ToUniversalTime();
        var day2 = DateTime.SpecifyKind(DateTime.Today.AddDays(1).AddMinutes(30), DateTimeKind.Local).ToUniversalTime();

        var s1 = await _store.GetOrCreateAsync("k1", day1, null, null, null);
        var s2 = await _store.GetOrCreateAsync("k2", day1b, null, null, null);
        var s3 = await _store.GetOrCreateAsync("k3", day2, null, null, null);

        Assert.Equal("query-01", (await Read(s1)).Name);
        Assert.Equal("query-02", (await Read(s2)).Name);
        Assert.Equal("query-01", (await Read(s3)).Name);   // counter reset
    }

    [Fact]
    public async Task Real_file_name_wins_over_auto_name()
    {
        var id = await _store.GetOrCreateAsync("k", DateTime.UtcNow, "MonthlyReport.sql", null, null);
        var row = await Read(id);
        Assert.Equal("MonthlyReport.sql", row.Name);
        Assert.Equal(1, row.NameSource);
    }

    [Fact]
    public async Task Scratch_title_is_auto_named()
    {
        var id = await _store.GetOrCreateAsync("k", DateTime.UtcNow, "dwnhdxfq.sql", null, null);
        var row = await Read(id);
        Assert.Equal("query-01", row.Name);
        Assert.Equal(0, row.NameSource);
    }

    [Fact]
    public async Task File_name_upgrades_an_auto_named_session()
    {
        var id = await _store.GetOrCreateAsync("k", DateTime.UtcNow, null, null, null);
        Assert.Equal(0, (await Read(id)).NameSource);

        await _store.GetOrCreateAsync("k", DateTime.UtcNow, "MonthlyReport.sql", null, null);
        var row = await Read(id);
        Assert.Equal("MonthlyReport.sql", row.Name);
        Assert.Equal(1, row.NameSource);
    }

    [Fact]
    public async Task Manual_rename_is_never_overwritten()
    {
        var id = await _store.GetOrCreateAsync("k", DateTime.UtcNow, null, null, null);

        await using (var c = new SqliteConnection(_cs))
        {
            await c.OpenAsync();
            await using var cmd = new SqliteCommand(
                "UPDATE query_sessions SET name='Germany customers', name_source=2 WHERE id=@id", c);
            cmd.Parameters.AddWithValue("@id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // A later execution carrying a real file name must NOT clobber the manual name.
        await _store.GetOrCreateAsync("k", DateTime.UtcNow, "MonthlyReport.sql", null, null);
        var row = await Read(id);
        Assert.Equal("Germany customers", row.Name);
        Assert.Equal(2, row.NameSource);
    }

    /// <summary>
    /// Retry arm 1 ("a concurrent caller created THIS key first"): N callers race on the SAME
    /// session_key. Exactly one of them wins the INSERT; the UNIQUE index on session_key forces
    /// every other caller into SQLITE_CONSTRAINT_UNIQUE, and the retry must resolve that by
    /// finding and returning the winner's row — not throwing, and not minting extra rows.
    /// A TaskCompletionSource gate holds every caller at the first await until all are queued,
    /// then releases them together so they genuinely overlap inside the race window, instead of
    /// merely being *started* together (which Task.WhenAll alone does not guarantee).
    /// </summary>
    [Fact]
    public async Task Concurrent_creation_with_same_key_returns_one_session()
    {
        var now = DateTime.UtcNow;
        const int callers = 12;
        var gate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, callers)
            .Select(async _ =>
            {
                await gate.Task;
                return await _store.GetOrCreateAsync("same-key", now, null, null, null);
            })
            .ToArray();

        gate.SetResult();
        var ids = await Task.WhenAll(tasks);   // throws (fails the test) if any caller faulted

        Assert.Single(ids.Distinct());

        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(
            "SELECT COUNT(*) FROM query_sessions WHERE session_key = @key", c);
        cmd.Parameters.AddWithValue("@key", "same-key");
        var rowCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        Assert.Equal(1, rowCount);
    }

    /// <summary>
    /// N callers with DISTINCT session_keys race for the same local_date, gated to genuinely
    /// overlap (see the same-key test's doc comment for why a gate, not just Task.WhenAll).
    /// Unlike the retired assertion this replaces — "COUNT(*) == COUNT(DISTINCT ordinal)", which
    /// IX_qs_date_ordinal makes the database physically incapable of violating regardless of
    /// whether the retry logic works — this checks that the resulting ordinal set is EXACTLY
    /// {1..callers}: no gaps, no reused values, no caller silently dropped. That claim is
    /// falsifiable by a broken implementation (see the mutation-check note in the fix-round
    /// report for what was actually observed here).
    /// </summary>
    [Fact]
    public async Task Concurrent_creation_with_distinct_keys_yields_contiguous_ordinals()
    {
        var now = DateTime.UtcNow;
        const int callers = 12;
        var gate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, callers)
            .Select(async i =>
            {
                await gate.Task;
                return await _store.GetOrCreateAsync($"concurrent-{i}", now, null, null, null);
            })
            .ToArray();

        gate.SetResult();
        var ids = await Task.WhenAll(tasks);   // throws (fails the test) if any caller faulted

        Assert.Equal(callers, ids.Distinct().Count());

        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(
            "SELECT ordinal FROM query_sessions WHERE local_date = @d", c);
        cmd.Parameters.AddWithValue("@d", QuerySessionNamerProbe.LocalDate(now));
        var ordinals = new List<int>();
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            while (await r.ReadAsync()) ordinals.Add(r.GetInt32(0));
        }

        Assert.Equal(Enumerable.Range(1, callers), ordinals.OrderBy(o => o));
    }

    /// <summary>
    /// Sequential (non-racing) correctness check: an ordinal already occupied by a
    /// directly-inserted row must not be reused, and must not block the next caller — the
    /// following GetOrCreateAsync call has to land on the next free ordinal. This is a
    /// regression guard on the MAX(ordinal)+1 computation itself (e.g. against an off-by-one or
    /// a cached/stale max); the mutation-check note in the fix-round report records whether this
    /// specific test also exercises the exception-retry path or not.
    /// </summary>
    [Fact]
    public async Task Insert_after_existing_ordinal_lands_on_the_next_free_slot()
    {
        var now = DateTime.UtcNow;
        var localDate = QuerySessionNamerProbe.LocalDate(now);

        await using (var c = new SqliteConnection(_cs))
        {
            await c.OpenAsync();
            await using var cmd = new SqliteCommand(@"
                INSERT INTO query_sessions
                    (session_key, local_date, ordinal, name, name_source, server, database_name, created_at)
                VALUES ('occupant', @d, 1, 'query-01', 0, NULL, NULL, @created);", c);
            cmd.Parameters.AddWithValue("@d", localDate);
            cmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            await cmd.ExecuteNonQueryAsync();
        }

        var id = await _store.GetOrCreateAsync("victim", now, null, null, null);
        var row = await Read(id);
        Assert.Equal("query-02", row.Name);
    }

    /// <summary>
    /// Deterministically forces a genuine SQLITE_BUSY (primary code 5) — not a session_key
    /// race — by holding the writer lock open on a separate connection while the store's own
    /// BEGIN IMMEDIATE is blocked on it. Uses a short custom busy-timeout (1s), scoped to a
    /// throwaway store instance built just for this test, so the wait stays fast: the shared
    /// <c>_cs</c> / <c>_store</c> get Microsoft.Data.Sqlite's default 30s timeout (confirmed via
    /// a standalone probe — HistoryDatabase's own `PRAGMA busy_timeout=5000` only applies to its
    /// own init connection, not to connections QuerySessionStore opens itself from the same
    /// connection string), which would make forcing a real BUSY here impractically slow.
    /// This is the test that specifically exercises the busy/locked arm the CRITICAL fix added
    /// to IsRetryableRaceError, complementing the same-key test's coverage of the constraint arm.
    /// </summary>
    [Fact]
    public async Task Retry_recovers_from_a_genuine_SQLITE_BUSY_while_a_writer_holds_the_lock()
    {
        var now = DateTime.UtcNow;
        var localDate = QuerySessionNamerProbe.LocalDate(now);
        var shortTimeoutStore = new QuerySessionStore($"{_cs};Default Timeout=1");

        await using var blocker = new SqliteConnection(_cs);
        await blocker.OpenAsync();
        var blockerTx = blocker.BeginTransaction(deferred: false);   // acquires the write lock
        await using (var blockerCmd = new SqliteCommand(@"
            INSERT INTO query_sessions
                (session_key, local_date, ordinal, name, name_source, server, database_name, created_at)
            VALUES ('blocker', @d, 1, 'blocker', 0, NULL, NULL, @created);", blocker, blockerTx))
        {
            blockerCmd.Parameters.AddWithValue("@d", localDate);
            blockerCmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            await blockerCmd.ExecuteNonQueryAsync();
        }
        // Deliberately NOT committed yet — holds the RESERVED/EXCLUSIVE lock past the delay below.

        var storeCallTask = Task.Run(() => shortTimeoutStore.GetOrCreateAsync("victim", now, null, null, null));

        await Task.Delay(TimeSpan.FromSeconds(1.3));   // past the 1s custom timeout: forces a real SQLITE_BUSY
        await blockerTx.CommitAsync();                  // release the lock; the retry's next attempt succeeds

        var id = await storeCallTask;   // must complete via retry, not throw
        var row = await Read(id);
        Assert.Equal("query-02", row.Name);   // skipped past the blocker's committed ordinal 1
    }

    /// <summary>
    /// Finding 2 (PR #249 review): the BUSY/LOCKED retry arm must wait between attempts instead of
    /// hammering the lock immediately. The blocker's write lock is deliberately NEVER released, so
    /// all 5 attempts exhaust via genuine SQLITE_BUSY (busy_timeout=1s per attempt, confirmed
    /// empirically to land around ~1.1s per attempt on this machine) and the call throws. That
    /// makes the measured elapsed time the SUM of five real SQLite busy-waits PLUS the retry
    /// loop's own backoff between them, with no "an attempt succeeds early once the lock frees"
    /// ambiguity that would otherwise mask whether the backoff ran at all.
    /// </summary>
    [Fact]
    public async Task Retry_backs_off_between_busy_attempts_instead_of_retrying_instantly()
    {
        var now = DateTime.UtcNow;
        var localDate = QuerySessionNamerProbe.LocalDate(now);
        var shortTimeoutStore = new QuerySessionStore($"{_cs};Default Timeout=1");

        await using var blocker = new SqliteConnection(_cs);
        await blocker.OpenAsync();
        var blockerTx = blocker.BeginTransaction(deferred: false);   // acquires the write lock
        await using (var blockerCmd = new SqliteCommand(@"
            INSERT INTO query_sessions
                (session_key, local_date, ordinal, name, name_source, server, database_name, created_at)
            VALUES ('blocker2', @d, 1, 'blocker', 0, NULL, NULL, @created);", blocker, blockerTx))
        {
            blockerCmd.Parameters.AddWithValue("@d", localDate);
            blockerCmd.Parameters.AddWithValue("@created", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            await blockerCmd.ExecuteNonQueryAsync();
        }
        // Deliberately NEVER released for the rest of this test.

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => shortTimeoutStore.GetOrCreateAsync("victim2", now, null, null, null));
        sw.Stop();

        // Baseline with NO backoff (5 attempts x ~1.1s busy_timeout=1 wait each) lands around
        // 5.5-5.8s on this machine. The retry loop's own schedule -- 50ms+jitter, 100ms+jitter,
        // 150ms+jitter, 200ms+jitter for attempts 1..4 -- adds at least another 500ms on top of
        // that. 5900ms sits between the two: it only passes when the backoff genuinely ran.
        Assert.True(sw.ElapsedMilliseconds >= 5900,
            $"expected the exhausted busy retry loop to include its own backoff waits, only took {sw.ElapsedMilliseconds}ms");

        await blockerTx.RollbackAsync();
    }
}

/// <summary>Test-only shim so the test can compute the same local-date key the store uses.</summary>
internal static class QuerySessionNamerProbe
{
    internal static string LocalDate(DateTime utc) => QuerySessionNamer.LocalDateKey(utc);
}
