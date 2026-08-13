# SQL History Session Grouping + `query-NN` Naming — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the SQL History list show one entry per editor-tab query session, auto-named `query-01`, `query-02`, … resetting each local day, and regroup existing rows to match.

**Architecture:** A new `query_sessions` table owns the grouping identity and the display name. Clients send an opaque `SessionKey` (stable for one editor document's lifetime) on each `HistoryRecord` notification; the engine maps it to a session row, assigning `query-NN` on first sight. Per-execution `history` rows are unchanged — grouping happens at read time by `session_id` instead of `content_hash`. A one-time migration backfills `session_id` for pre-existing rows by inferring sessions from (local date, tab title, server, database).

**Tech Stack:** .NET 10 engine (`AkmlSql.Engine`), SQLite via `Microsoft.Data.Sqlite`, MessagePack IPC (`AkmlSql.Core`), .NET Framework 4.7.2 shell (`AkmlSql.Shell.Shared`), Blazor Server web (`AkmlSql.Web`), xunit tests.

**Spec:** `docs/superpowers/specs/2026-08-12-history-session-grouping-design.md`

## Global Constraints

- **Git rule (project CLAUDE.md, absolute):** NEVER run `git add`, `git commit`, or `git push` without the user's explicit approval. Every "Commit" step below means: stage nothing, summarise the change, and **ask**. Do not commit on your own initiative.
- Storage stays **one `history` row per execution**. No task may collapse rows at write time.
- `executed_at` is stored as **UTC** (`DateTime.UtcNow.ToString("o")`, e.g. `2026-08-10T14:54:02.9160000Z`). All day bucketing is by **local** date and must convert explicitly.
- Day boundary is **local midnight**.
- Deleting an entry never renumbers other entries; gaps are expected and correct.
- `name_source` precedence is absolute: `2` (manual) is never overwritten by `1` (file) or `0` (auto).
- Engine test command: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "<Name>" -v:q --nologo`
- Shell test command: `dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj --filter "<Name>" -v:q --nologo`
- Shell projects must NOT be built with `dotnet build` (VSSDK CodeTaskFactory needs full MSBuild); `dotnet test` on the test project is fine and is what the suite already uses.

## Spec deviation (approved during planning)

The spec says the shell caches `SessionKey` in a `ConditionalWeakTable` keyed by `ITextBuffer`. The actual capture path (`ExecutionCapture`) is **DTE-based** and never sees a text buffer — it reads `_dte.ActiveDocument`. Task 8 therefore keys the cache by document full name and clears the entry on the existing document-close hook, which preserves the intended semantics ("reopening a file starts a new session") using machinery that exists.

## File Structure

| File | Responsibility |
|------|----------------|
| `src/AkmlSql.Engine/History/QuerySessionNamer.cs` | **New.** Pure functions: local-date key, `query-NN` formatting, scratch-title detection. No I/O — fully unit-testable. |
| `src/AkmlSql.Engine/History/QuerySessionStore.cs` | **New.** Owns `query_sessions`: get-or-create by `SessionKey`, ordinal assignment with retry, name-precedence updates. |
| `src/AkmlSql.Engine/History/HistoryDatabase.cs` | Modified. Schema v2 (table, column, indexes), backfill migration, `AddAsync` accepts `sessionKey`, `SearchAsync` groups by `session_id`. |
| `src/AkmlSql.Core/Ipc/Messages/HistoryRecordRequest.cs` | Modified. Adds `SessionKey` at `[Key(11)]`. |
| `src/AkmlSql.Engine/Handlers/History/*` | Modified. Passes `SessionKey` through to `AddAsync`. |
| `src/AkmlSql.Shell.Shared/History/ExecutionCapture.cs` | Modified. Per-document `SessionKey`; `TabTitle` only for saved documents. |
| `src/AkmlSql.Web/Shared/EditorComponent.razor` + session store | Modified. Persisted `SessionKey`, sent on execute. |
| `src/AkmlSql.Web/Pages/History.razor`, `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs` | Modified. Show session name + `×runs · N versions`. |

---

### Task 1: `QuerySessionNamer` — pure naming rules

**Files:**
- Create: `src/AkmlSql.Engine/History/QuerySessionNamer.cs`
- Test: `tests/AkmlSql.Engine.Tests/History/QuerySessionNamerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `internal static class QuerySessionNamer` with
  `string LocalDateKey(DateTime utcInstant)` → `"yyyy-MM-dd"` local;
  `string FormatName(int ordinal)` → `"query-01"`;
  `bool IsScratchTabTitle(string? tabTitle)` → true when the title is absent or looks like an SSMS scratch document.

- [ ] **Step 1: Write the failing test**

```csharp
using AkmlSql.Engine.History;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

public class QuerySessionNamerTests
{
    [Theory]
    [InlineData(1, "query-01")]
    [InlineData(9, "query-09")]
    [InlineData(10, "query-10")]
    [InlineData(99, "query-99")]
    // Past 99 the name widens rather than truncating — a 100th session in one day
    // must still get a unique, sortable name.
    [InlineData(100, "query-100")]
    public void FormatName_pads_to_two_digits_then_widens(int ordinal, string expected)
        => Assert.Equal(expected, QuerySessionNamer.FormatName(ordinal));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SQLQuery1.sql")]
    [InlineData("SQLQuery17.sql")]
    [InlineData("dwnhdxfq.sql")]   // SSMS random 8-char scratch name — the reported case
    [InlineData("DWNHDXFQ.SQL")]   // matching is case-insensitive
    public void IsScratchTabTitle_true_for_unsaved_scratch_documents(string? title)
        => Assert.True(QuerySessionNamer.IsScratchTabTitle(title));

    [Theory]
    [InlineData("MonthlyReport.sql")]
    [InlineData("customer-cleanup.sql")]
    [InlineData("a.sql")]
    public void IsScratchTabTitle_false_for_real_file_names(string title)
        => Assert.False(QuerySessionNamer.IsScratchTabTitle(title));

    /// <summary>
    /// Known false positive, asserted so the limitation stays visible instead of being
    /// rediscovered as a bug. Applies to the backfill of pre-migration rows ONLY: new rows
    /// carry TabTitle only for genuinely saved documents (Task 8), so this never fires on them.
    /// </summary>
    [Fact]
    public void IsScratchTabTitle_known_false_positive_is_documented()
        => Assert.True(QuerySessionNamer.IsScratchTabTitle("report01.sql"));

    [Fact]
    public void LocalDateKey_converts_utc_to_local_day()
    {
        // Pick an instant and assert against the machine's own local conversion, so the test
        // is correct in every timezone rather than only in the author's.
        var utc = new DateTime(2026, 8, 12, 21, 30, 0, DateTimeKind.Utc);
        var expected = utc.ToLocalTime().ToString("yyyy-MM-dd");
        Assert.Equal(expected, QuerySessionNamer.LocalDateKey(utc));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "QuerySessionNamerTests" -v:q --nologo`
Expected: FAIL — compile error, `QuerySessionNamer` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AkmlSql.Engine.History;

/// <summary>
/// Pure naming rules for query sessions. No I/O, so every rule here is directly unit-testable —
/// the ordinal/persistence side lives in <see cref="QuerySessionStore"/>.
/// </summary>
internal static class QuerySessionNamer
{
    /// <summary>
    /// SSMS names an UNSAVED query document either "SQLQuery&lt;n&gt;.sql" or with a random
    /// 8-character token ("dwnhdxfq.sql"). Neither is a name a user chose, so both are treated
    /// as "no name" and replaced by query-NN.
    /// </summary>
    private static readonly Regex ScratchName = new(
        @"^(SQLQuery\d+|[a-z0-9]{8})\.sql$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Local calendar day ("yyyy-MM-dd") of a UTC instant. History stores UTC; the
    /// counter resets at LOCAL midnight, so the conversion must be explicit.</summary>
    internal static string LocalDateKey(DateTime utcInstant)
    {
        var utc = utcInstant.Kind == DateTimeKind.Utc
            ? utcInstant
            : utcInstant.ToUniversalTime();
        return utc.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Zero-padded to two digits, widening past 99 ("query-100") rather than truncating.</summary>
    internal static string FormatName(int ordinal) =>
        "query-" + ordinal.ToString("00", CultureInfo.InvariantCulture);

    /// <summary>
    /// True when the title carries no user intent. See the regex remark for the two SSMS forms.
    /// HEURISTIC — used for the one-time backfill of pre-migration rows, where the saved/unsaved
    /// distinction is already lost. A genuinely saved file named with eight alphanumeric
    /// characters ("report01.sql") is a known false positive, correctable with one rename.
    /// </summary>
    internal static bool IsScratchTabTitle(string? tabTitle) =>
        string.IsNullOrWhiteSpace(tabTitle) || ScratchName.IsMatch(tabTitle!.Trim());
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "QuerySessionNamerTests" -v:q --nologo`
Expected: PASS — 16 tests.

- [ ] **Step 5: Commit**

Summarise the change and **ask the user before committing** (see Global Constraints).

```bash
# only after explicit approval
git add src/AkmlSql.Engine/History/QuerySessionNamer.cs tests/AkmlSql.Engine.Tests/History/QuerySessionNamerTests.cs
git commit -m "feat(history): add pure query-session naming rules"
```

---

### Task 2: Schema v2 — `query_sessions` table and `session_id` column

**Files:**
- Modify: `src/AkmlSql.Engine/History/HistoryDatabase.cs:19` (SchemaVersion), `:128-155` (after the history table / alongside the `is_open` migration)
- Test: `tests/AkmlSql.Engine.Tests/History/QuerySessionSchemaTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: table `query_sessions(id, session_key, local_date, ordinal, name, name_source, server, database_name, created_at)`; column `history.session_id`; indexes `IX_qs_session_key`, `IX_qs_date_ordinal`, `IX_history_session`.

- [ ] **Step 1: Write the failing test**

```csharp
using System.IO;
using System.Threading.Tasks;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

public class QuerySessionSchemaTests
{
    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"akml-hist-{System.Guid.NewGuid():N}.db");

    [Fact]
    public async Task Initialize_creates_query_sessions_and_session_id_column()
    {
        var path = TempDbPath();
        try
        {
            var db = new HistoryDatabase(path);
            await db.InitializeAsync();

            await using var conn = new SqliteConnection($"Data Source={path}");
            await conn.OpenAsync();

            Assert.True(await TableExists(conn, "query_sessions"));
            Assert.True(await ColumnExists(conn, "history", "session_id"));
            Assert.True(await IndexExists(conn, "IX_qs_date_ordinal"));
            Assert.True(await IndexExists(conn, "IX_history_session"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Initialize_is_idempotent()
    {
        var path = TempDbPath();
        try
        {
            var db = new HistoryDatabase(path);
            await db.InitializeAsync();
            await db.InitializeAsync();   // must not throw on the ALTER TABLE
            await using var conn = new SqliteConnection($"Data Source={path}");
            await conn.OpenAsync();
            Assert.True(await ColumnExists(conn, "history", "session_id"));
        }
        finally { File.Delete(path); }
    }

    private static async Task<bool> TableExists(SqliteConnection c, string name)
    {
        await using var cmd = new SqliteCommand(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@n", c);
        cmd.Parameters.AddWithValue("@n", name);
        return System.Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> IndexExists(SqliteConnection c, string name)
    {
        await using var cmd = new SqliteCommand(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@n", c);
        cmd.Parameters.AddWithValue("@n", name);
        return System.Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task<bool> ColumnExists(SqliteConnection c, string table, string column)
    {
        await using var cmd = new SqliteCommand($"PRAGMA table_info({table});", c);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
            if (string.Equals(r.GetString(1), column, System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "QuerySessionSchemaTests" -v:q --nologo`
Expected: FAIL — `query_sessions` table does not exist.

- [ ] **Step 3: Write minimal implementation**

Change `SchemaVersion` at `HistoryDatabase.cs:19`:

```csharp
    private const int SchemaVersion = 2;   // v2: query_sessions + history.session_id
```

Insert after the existing `is_open` migration block (currently ending near line 155):

```csharp
        // ── Schema v2: query-session grouping ────────────────────────────────
        // One row per editor-tab query session. The display name lives HERE, not on the
        // history rows, so a rename is a single UPDATE and survives every later execution.
        await ExecuteNonQueryAsync(conn, @"
            CREATE TABLE IF NOT EXISTS query_sessions (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                session_key   TEXT    NOT NULL,
                local_date    TEXT    NOT NULL,
                ordinal       INTEGER NOT NULL,
                name          TEXT    NOT NULL,
                name_source   INTEGER NOT NULL,
                server        TEXT,
                database_name TEXT,
                created_at    TEXT    NOT NULL
            );");

        await ExecuteNonQueryAsync(conn,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_qs_session_key ON query_sessions (session_key);");
        // Backstop for the ordinal race: two shell windows can read the same MAX(ordinal).
        await ExecuteNonQueryAsync(conn,
            "CREATE UNIQUE INDEX IF NOT EXISTS IX_qs_date_ordinal ON query_sessions (local_date, ordinal);");

        // Column BEFORE its index — an index cannot reference a column that does not exist yet.
        try
        {
            await ExecuteNonQueryAsync(conn,
                "ALTER TABLE history ADD COLUMN session_id INTEGER REFERENCES query_sessions(id);");
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.Message.Contains("duplicate column"))
        {
            // Already migrated — expected on every start after the first.
        }

        await ExecuteNonQueryAsync(conn,
            "CREATE INDEX IF NOT EXISTS IX_history_session ON history (session_id);");
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "QuerySessionSchemaTests" -v:q --nologo`
Expected: PASS — 2 tests.

- [ ] **Step 5: Run the whole history suite for regressions**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "History" -v:q --nologo`
Expected: PASS — no existing history test regresses.

- [ ] **Step 6: Commit** — summarise and **ask first**.

---

### Task 3: `QuerySessionStore` — get-or-create with ordinal assignment

**Files:**
- Create: `src/AkmlSql.Engine/History/QuerySessionStore.cs`
- Test: `tests/AkmlSql.Engine.Tests/History/QuerySessionStoreTests.cs`

**Interfaces:**
- Consumes: `QuerySessionNamer.LocalDateKey`, `.FormatName`, `.IsScratchTabTitle` (Task 1); schema from Task 2.
- Produces: `internal sealed class QuerySessionStore` with constructor `QuerySessionStore(string connectionString)` and
  `Task<long> GetOrCreateAsync(string sessionKey, DateTime executedAtUtc, string? tabTitle, string? server, string? database)` returning the `query_sessions.id`.

Name precedence on an EXISTING session: a non-scratch `tabTitle` upgrades `name_source` 0 → 1 and rewrites `name`; `name_source = 2` (manual) is never touched.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
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

    public Task DisposeAsync() { File.Delete(_path); return Task.CompletedTask; }

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
        // 10:00 and 11:00 LOCAL on one day, then 10:00 LOCAL the next.
        var day1 = DateTime.SpecifyKind(DateTime.Today.AddHours(10), DateTimeKind.Local).ToUniversalTime();
        var day1b = DateTime.SpecifyKind(DateTime.Today.AddHours(11), DateTimeKind.Local).ToUniversalTime();
        var day2 = DateTime.SpecifyKind(DateTime.Today.AddDays(1).AddHours(10), DateTimeKind.Local).ToUniversalTime();

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

    [Fact]
    public async Task Concurrent_creation_never_duplicates_an_ordinal()
    {
        var now = DateTime.UtcNow;
        var ids = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(i => _store.GetOrCreateAsync($"concurrent-{i}", now, null, null, null)));

        Assert.Equal(12, ids.Distinct().Count());

        await using var c = new SqliteConnection(_cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(
            "SELECT COUNT(*), COUNT(DISTINCT ordinal) FROM query_sessions WHERE local_date=@d", c);
        cmd.Parameters.AddWithValue("@d", QuerySessionNamerProbe.LocalDate(now));
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.True(await r.ReadAsync());
        Assert.Equal(r.GetInt32(0), r.GetInt32(1));   // every ordinal unique
    }
}

/// <summary>Test-only shim so the test can compute the same local-date key the store uses.</summary>
internal static class QuerySessionNamerProbe
{
    internal static string LocalDate(DateTime utc) => QuerySessionNamer.LocalDateKey(utc);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "QuerySessionStoreTests" -v:q --nologo`
Expected: FAIL — `QuerySessionStore` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Serilog;

namespace AkmlSql.Engine.History;

/// <summary>
/// Owns the <c>query_sessions</c> table: maps a client-supplied SessionKey to a session row,
/// assigning the per-local-day ordinal and display name on first sight.
/// </summary>
internal sealed class QuerySessionStore
{
    private const int SqliteConstraint = 19;   // SQLITE_CONSTRAINT

    private readonly string _connectionString;

    internal QuerySessionStore(string connectionString) => _connectionString = connectionString;

    /// <summary>
    /// Returns the id of the session for <paramref name="sessionKey"/>, creating it if new.
    /// On an existing session a real (non-scratch) title upgrades an auto name; a manual
    /// rename (name_source = 2) is never overwritten.
    /// </summary>
    internal async Task<long> GetOrCreateAsync(
        string sessionKey, DateTime executedAtUtc, string? tabTitle, string? server, string? database)
    {
        var localDate = QuerySessionNamer.LocalDateKey(executedAtUtc);

        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var existing = await FindAsync(conn, sessionKey);
        if (existing.HasValue)
        {
            await MaybeUpgradeNameAsync(conn, existing.Value, tabTitle);
            return existing.Value;
        }

        // Two windows can read the same MAX(ordinal); IX_qs_date_ordinal turns that into a
        // constraint violation. Retry re-reads the new maximum. The second arm of the retry
        // also covers the case where a concurrent caller created THIS key first.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return await InsertAsync(conn, sessionKey, localDate, tabTitle, server, database);
            }
            catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteConstraint)
            {
                var raced = await FindAsync(conn, sessionKey);
                if (raced.HasValue)
                {
                    await MaybeUpgradeNameAsync(conn, raced.Value, tabTitle);
                    return raced.Value;
                }
                // Ordinal collision only — loop and take the next one.
            }
        }

        throw new InvalidOperationException(
            $"QuerySessionStore: could not allocate an ordinal for {localDate} after 5 attempts.");
    }

    private static async Task<long?> FindAsync(SqliteConnection conn, string sessionKey)
    {
        await using var cmd = new SqliteCommand(
            "SELECT id FROM query_sessions WHERE session_key = @key", conn);
        cmd.Parameters.AddWithValue("@key", sessionKey);
        var result = await cmd.ExecuteScalarAsync();
        return result == null || result == DBNull.Value ? null : Convert.ToInt64(result);
    }

    private static async Task<long> InsertAsync(
        SqliteConnection conn, string sessionKey, string localDate,
        string? tabTitle, string? server, string? database)
    {
        var isScratch = QuerySessionNamer.IsScratchTabTitle(tabTitle);

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await using var maxCmd = new SqliteCommand(
                "SELECT COALESCE(MAX(ordinal), 0) + 1 FROM query_sessions WHERE local_date = @d",
                conn, (SqliteTransaction)tx);
            maxCmd.Parameters.AddWithValue("@d", localDate);
            var ordinal = Convert.ToInt32(await maxCmd.ExecuteScalarAsync());

            var name = isScratch ? QuerySessionNamer.FormatName(ordinal) : tabTitle!.Trim();
            var nameSource = isScratch ? 0 : 1;

            await using var insert = new SqliteCommand(@"
                INSERT INTO query_sessions
                    (session_key, local_date, ordinal, name, name_source, server, database_name, created_at)
                VALUES (@key, @d, @ord, @name, @src, @server, @db, @created);
                SELECT last_insert_rowid();", conn, (SqliteTransaction)tx);
            insert.Parameters.AddWithValue("@key", sessionKey);
            insert.Parameters.AddWithValue("@d", localDate);
            insert.Parameters.AddWithValue("@ord", ordinal);
            insert.Parameters.AddWithValue("@name", name);
            insert.Parameters.AddWithValue("@src", nameSource);
            insert.Parameters.AddWithValue("@server", (object?)server ?? DBNull.Value);
            insert.Parameters.AddWithValue("@db", (object?)database ?? DBNull.Value);
            insert.Parameters.AddWithValue("@created",
                DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));

            var id = Convert.ToInt64(await insert.ExecuteScalarAsync());
            await tx.CommitAsync();

            Log.Debug("QuerySession created: id={Id} name={Name} date={Date}", id, name, localDate);
            return id;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    /// <summary>auto (0) → file (1) only. Manual (2) is final.</summary>
    private static async Task MaybeUpgradeNameAsync(SqliteConnection conn, long id, string? tabTitle)
    {
        if (QuerySessionNamer.IsScratchTabTitle(tabTitle)) return;

        await using var cmd = new SqliteCommand(@"
            UPDATE query_sessions
               SET name = @name, name_source = 1
             WHERE id = @id AND name_source = 0;", conn);
        cmd.Parameters.AddWithValue("@name", tabTitle!.Trim());
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "QuerySessionStoreTests" -v:q --nologo`
Expected: PASS — 7 tests.

- [ ] **Step 5: Commit** — summarise and **ask first**.

---

### Task 4: Record executions against a session

**Files:**
- Modify: `src/AkmlSql.Engine/History/HistoryDatabase.cs:278-345` (`AddAsync`)
- Test: `tests/AkmlSql.Engine.Tests/History/HistorySessionRecordingTests.cs`

**Interfaces:**
- Consumes: `QuerySessionStore.GetOrCreateAsync` (Task 3).
- Produces: `AddAsync(..., string? tabTitle, string? sessionKey = null)` — a trailing optional parameter, so every existing caller compiles unchanged.

- [ ] **Step 1: Write the failing test**

```csharp
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

    public Task DisposeAsync() { File.Delete(_path); return Task.CompletedTask; }

    private Task<long> Add(string sql, string? sessionKey, string? tabTitle = null) =>
        _db.AddAsync(sql, false, "localhost", "Northwind", null, 5, 1,
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "HistorySessionRecordingTests" -v:q --nologo`
Expected: FAIL — `AddAsync` has no `sessionKey` parameter.

- [ ] **Step 3: Write minimal implementation**

Add the parameter at the end of the `AddAsync` signature (`HistoryDatabase.cs:290`):

```csharp
        string? tabTitle,
        string? sessionKey = null)
```

Immediately after `var executedAt = ...` (line 301), resolve the session:

```csharp
        // Resolve (or create) the query session BEFORE the insert, so session_id is written in
        // the same statement. A null/empty key means a client that predates session grouping;
        // the row is stored with session_id NULL and the backfill will infer one later.
        long? sessionId = null;
        if (!string.IsNullOrEmpty(sessionKey))
        {
            try
            {
                sessionId = await new QuerySessionStore(_connectionString)
                    .GetOrCreateAsync(sessionKey!, DateTime.UtcNow, tabTitle, server, database);
            }
            catch (Exception ex)
            {
                // History capture is best-effort and must never break query execution.
                Log.Warning(ex, "History: session resolution failed for key {Key}", sessionKey);
            }
        }
```

Extend the INSERT column list and values (lines 311-319):

```csharp
                INSERT INTO history (
                    sql_text, truncated, server, database_name, username,
                    executed_at, duration_ms, row_count, status, error_msg,
                    source, tab_title, content_hash, is_favorite, session_id
                ) VALUES (
                    @sqlText, @truncated, @server, @database, @username,
                    @executedAt, @durationMs, @rowCount, @status, @errorMsg,
                    @source, @tabTitle, @contentHash, 0, @sessionId
                );
                SELECT last_insert_rowid();
```

And bind it next to `@contentHash` (line 335):

```csharp
            cmd.Parameters.AddWithValue("@sessionId", (object?)sessionId ?? DBNull.Value);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "HistorySessionRecordingTests" -v:q --nologo`
Expected: PASS — 2 tests.

- [ ] **Step 5: Commit** — summarise and **ask first**.

---

### Task 5: Backfill legacy rows

**Files:**
- Modify: `src/AkmlSql.Engine/History/HistoryDatabase.cs` (new private method + call at the end of `InitializeAsync`)
- Test: `tests/AkmlSql.Engine.Tests/History/HistoryBackfillTests.cs`

**Interfaces:**
- Consumes: schema from Task 2, `QuerySessionNamer` from Task 1.
- Produces: `private async Task BackfillSessionsAsync(SqliteConnection conn)` — idempotent; only touches rows where `session_id IS NULL`.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using AkmlSql.Engine.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

public class HistoryBackfillTests
{
    private static string TempDbPath() =>
        Path.Combine(Path.GetTempPath(), $"akml-bf-{Guid.NewGuid():N}.db");

    /// <summary>Inserts a legacy row directly (session_id left NULL), as a v1 database would have.</summary>
    private static async Task InsertLegacy(
        string cs, string sql, string? tabTitle, DateTime whenLocal, string db = "aqmar")
    {
        await using var c = new SqliteConnection(cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(@"
            INSERT INTO history (sql_text, truncated, server, database_name, username,
                                 executed_at, duration_ms, row_count, status, error_msg,
                                 source, tab_title, content_hash, is_favorite)
            VALUES (@sql, 0, '(local)', @db, NULL, @at, 1, 1, 0, NULL, NULL, @title, @hash, 0);", c);
        cmd.Parameters.AddWithValue("@sql", sql);
        cmd.Parameters.AddWithValue("@db", db);
        cmd.Parameters.AddWithValue("@at",
            DateTime.SpecifyKind(whenLocal, DateTimeKind.Local).ToUniversalTime()
                .ToString("o", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@title", (object?)tabTitle ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hash", HistoryDatabase.ComputeContentHash(sql));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(int Sessions, int Unassigned)> Counts(string cs)
    {
        await using var c = new SqliteConnection(cs);
        await c.OpenAsync();
        await using var cmd = new SqliteCommand(
            "SELECT (SELECT COUNT(*) FROM query_sessions), " +
            "       (SELECT COUNT(*) FROM history WHERE session_id IS NULL)", c);
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return (r.GetInt32(0), r.GetInt32(1));
    }

    [Fact]
    public async Task Backfill_groups_legacy_rows_and_names_them()
    {
        var path = TempDbPath();
        var cs = $"Data Source={path}";
        try
        {
            await new HistoryDatabase(path).InitializeAsync();

            var day = DateTime.Today.AddDays(-1);
            // Two scratch-named groups on the same day + one genuinely saved file.
            await InsertLegacy(cs, "SELECT 1", "dwnhdxfq.sql", day.AddHours(9));
            await InsertLegacy(cs, "SELECT 2", "dwnhdxfq.sql", day.AddHours(10));
            await InsertLegacy(cs, "SELECT 3", "othernam.sql", day.AddHours(11));
            await InsertLegacy(cs, "SELECT 4", "MonthlyReport.sql", day.AddHours(12));

            await new HistoryDatabase(path).InitializeAsync();   // triggers backfill

            var (sessions, unassigned) = await Counts(cs);
            Assert.Equal(3, sessions);      // two scratch groups + one file group
            Assert.Equal(0, unassigned);

            await using var c = new SqliteConnection(cs);
            await c.OpenAsync();
            await using var cmd = new SqliteCommand(
                "SELECT name FROM query_sessions ORDER BY ordinal", c);
            await using var r = await cmd.ExecuteReaderAsync();
            var names = new System.Collections.Generic.List<string>();
            while (await r.ReadAsync()) names.Add(r.GetString(0));

            Assert.Contains("query-01", names);
            Assert.Contains("query-02", names);
            Assert.Contains("MonthlyReport.sql", names);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Backfill_is_idempotent()
    {
        var path = TempDbPath();
        var cs = $"Data Source={path}";
        try
        {
            await new HistoryDatabase(path).InitializeAsync();
            await InsertLegacy(cs, "SELECT 1", "dwnhdxfq.sql", DateTime.Today.AddDays(-1).AddHours(9));

            await new HistoryDatabase(path).InitializeAsync();
            var first = await Counts(cs);
            await new HistoryDatabase(path).InitializeAsync();
            var second = await Counts(cs);

            Assert.Equal(first.Sessions, second.Sessions);   // no renumbering, no duplicates
            Assert.Equal(0, second.Unassigned);
        }
        finally { File.Delete(path); }
    }
}
```

Note: `ComputeContentHash` is already `internal static` (`HistoryDatabase.cs:1196`); the Engine.Tests project must therefore already have `InternalsVisibleTo`. If it does not, add it to `src/AkmlSql.Engine/AkmlSql.Engine.csproj` alongside the existing test-seam configuration.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "HistoryBackfillTests" -v:q --nologo`
Expected: FAIL — sessions count is 0; legacy rows stay unassigned.

- [ ] **Step 3: Write minimal implementation**

Add to `HistoryDatabase`, and call `await BackfillSessionsAsync(conn);` as the last statement of `InitializeAsync`:

```csharp
    /// <summary>
    /// One-time regrouping of rows written before session tracking existed. Those rows carry no
    /// tab identity, so a session is INFERRED from (local date, tab_title, server, database).
    /// Nothing is deleted and no column other than session_id is touched.
    ///
    /// <para>Idempotent: only rows with session_id IS NULL are considered, so a second run is a
    /// no-op and never renumbers an existing session.</para>
    ///
    /// <para>executed_at is stored as UTC ISO-8601 with 7 fractional digits, which SQLite's date
    /// functions will not parse; substr(...,1,19) trims it to 'YYYY-MM-DDTHH:MM:SS', which SQLite
    /// treats as UTC-naive, and 'localtime' then converts it to the user's day.</para>
    /// </summary>
    private async Task BackfillSessionsAsync(SqliteConnection conn)
    {
        await using (var probe = new SqliteCommand(
            "SELECT COUNT(*) FROM history WHERE session_id IS NULL", conn))
        {
            if (Convert.ToInt32(await probe.ExecuteScalarAsync()) == 0) return;
        }

        Log.Information("History: backfilling query sessions for legacy rows…");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            // Ordered so ordinals follow first-execution time within each local day.
            var groups = new System.Collections.Generic.List<(string Date, string Title, string Server, string Db)>();
            await using (var cmd = new SqliteCommand(@"
                SELECT date(substr(executed_at, 1, 19), 'localtime') AS local_date,
                       COALESCE(tab_title, '')      AS title,
                       COALESCE(server, '')         AS server,
                       COALESCE(database_name, '')  AS db
                  FROM history
                 WHERE session_id IS NULL
                 GROUP BY local_date, title, server, db
                 ORDER BY local_date, MIN(executed_at);", conn, (SqliteTransaction)tx))
            await using (var r = await cmd.ExecuteReaderAsync())
            {
                while (await r.ReadAsync())
                    groups.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
            }

            var perDay = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
            var created = 0;

            foreach (var g in groups)
            {
                await using (var maxCmd = new SqliteCommand(
                    "SELECT COALESCE(MAX(ordinal), 0) FROM query_sessions WHERE local_date = @d",
                    conn, (SqliteTransaction)tx))
                {
                    maxCmd.Parameters.AddWithValue("@d", g.Date);
                    if (!perDay.ContainsKey(g.Date))
                        perDay[g.Date] = Convert.ToInt32(await maxCmd.ExecuteScalarAsync());
                }

                var ordinal = ++perDay[g.Date];
                var isScratch = QuerySessionNamer.IsScratchTabTitle(g.Title);
                var name = isScratch ? QuerySessionNamer.FormatName(ordinal) : g.Title;

                long sessionId;
                await using (var ins = new SqliteCommand(@"
                    INSERT INTO query_sessions
                        (session_key, local_date, ordinal, name, name_source, server, database_name, created_at)
                    VALUES (@key, @d, @ord, @name, @src, @server, @db, @created);
                    SELECT last_insert_rowid();", conn, (SqliteTransaction)tx))
                {
                    // Synthetic key: stable, unique, and obviously not a client-issued GUID.
                    ins.Parameters.AddWithValue("@key",
                        $"legacy:{g.Date}|{g.Title}|{g.Server}|{g.Db}");
                    ins.Parameters.AddWithValue("@d", g.Date);
                    ins.Parameters.AddWithValue("@ord", ordinal);
                    ins.Parameters.AddWithValue("@name", name);
                    ins.Parameters.AddWithValue("@src", isScratch ? 0 : 1);
                    ins.Parameters.AddWithValue("@server", g.Server.Length == 0 ? DBNull.Value : g.Server);
                    ins.Parameters.AddWithValue("@db", g.Db.Length == 0 ? DBNull.Value : g.Db);
                    ins.Parameters.AddWithValue("@created",
                        DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                    sessionId = Convert.ToInt64(await ins.ExecuteScalarAsync());
                }

                await using (var upd = new SqliteCommand(@"
                    UPDATE history
                       SET session_id = @sid
                     WHERE session_id IS NULL
                       AND date(substr(executed_at, 1, 19), 'localtime') = @d
                       AND COALESCE(tab_title, '')     = @title
                       AND COALESCE(server, '')        = @server
                       AND COALESCE(database_name, '') = @db;", conn, (SqliteTransaction)tx))
                {
                    upd.Parameters.AddWithValue("@sid", sessionId);
                    upd.Parameters.AddWithValue("@d", g.Date);
                    upd.Parameters.AddWithValue("@title", g.Title);
                    upd.Parameters.AddWithValue("@server", g.Server);
                    upd.Parameters.AddWithValue("@db", g.Db);
                    await upd.ExecuteNonQueryAsync();
                }

                created++;
            }

            await tx.CommitAsync();
            Log.Information("History: backfill created {Count} sessions in {Ms} ms",
                created, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            Log.Error(ex, "History: session backfill failed; legacy rows remain ungrouped");
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "HistoryBackfillTests" -v:q --nologo`
Expected: PASS — 2 tests.

- [ ] **Step 5: Commit** — summarise and **ask first**.

---

### Task 6: Read model — group by session, drop the `tab_title` window hack

**Files:**
- Modify: `src/AkmlSql.Engine/History/HistoryDatabase.cs:516-520` (NameFilter), `:528-535` + `:569-571` (counts), `:597-644` (dedup data query)
- Modify: `src/AkmlSql.Core/Models/History/HistoryEntry.cs` (add `VersionCount`)
- Test: `tests/AkmlSql.Engine.Tests/History/HistorySessionSearchTests.cs`

**Interfaces:**
- Consumes: `session_id` from Task 4.
- Produces: de-duplicated search results carrying `TabTitle` = session name, `ExecutionCount` = executions in the session, `VersionCount` = distinct `content_hash` in the session.

- [ ] **Step 1: Write the failing test**

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Models.History;
using AkmlSql.Engine.History;
using Xunit;

namespace AkmlSql.Engine.Tests.History;

public class HistorySessionSearchTests : IAsyncLifetime
{
    private string _path = string.Empty;
    private HistoryDatabase _db = null!;

    public async Task InitializeAsync()
    {
        _path = Path.Combine(Path.GetTempPath(), $"akml-search-{Guid.NewGuid():N}.db");
        _db = new HistoryDatabase(_path);
        await _db.InitializeAsync();
    }

    public Task DisposeAsync() { File.Delete(_path); return Task.CompletedTask; }

    private Task Add(string sql, string sessionKey) =>
        _db.AddAsync(sql, false, "localhost", "Northwind", null, 5, 1,
                     (int)ExecutionStatus.Success, null, null, null, sessionKey);

    [Fact]
    public async Task One_row_per_session_with_run_and_version_counts()
    {
        await Add("SELECT 1", "tab-A");
        await Add("SELECT 1", "tab-A");   // identical re-run
        await Add("SELECT 2", "tab-A");   // edited — same session, new version
        await Add("SELECT 9", "tab-B");

        var result = await _db.SearchAsync(new HistoryFilter { Deduplicate = true, PageSize = 50 });

        Assert.Equal(2, result.Entries.Count);

        var a = result.Entries.Single(e => e.TabTitle == "query-01");
        Assert.Equal(3, a.ExecutionCount);   // three executions
        Assert.Equal(2, a.VersionCount);     // two distinct texts
    }

    [Fact]
    public async Task Raw_view_still_lists_every_execution()
    {
        await Add("SELECT 1", "tab-A");
        await Add("SELECT 1", "tab-A");

        var result = await _db.SearchAsync(new HistoryFilter { Deduplicate = false, PageSize = 50 });
        Assert.Equal(2, result.Entries.Count);   // storage unchanged
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "HistorySessionSearchTests" -v:q --nologo`
Expected: FAIL — entries are grouped by content hash, so three rows come back and `VersionCount` does not exist.

- [ ] **Step 3: Write minimal implementation**

Add to `HistoryEntry`:

```csharp
        /// <summary>Distinct SQL texts recorded within this query session.</summary>
        public int VersionCount { get; set; }
```

Replace the `filter.Deduplicate` data query (`HistoryDatabase.cs:597-644`). The partition key becomes `session_id`, the name comes from the join, and the `FIRST_VALUE(h.tab_title)` window is deleted — the name now lives in exactly one row, so nothing needs to reconstruct it:

```csharp
            dataSql = $@"
                SELECT
                    ranked.id,
                    substr(ranked.sql_text, 1, 500) as sql_text,
                    ranked.server,
                    ranked.database_name,
                    ranked.username,
                    ranked.executed_at,
                    ranked.duration_ms,
                    ranked.row_count,
                    ranked.status,
                    ranked.error_msg,
                    ranked.source,
                    ranked.session_name as tab_title,
                    ranked.is_favorite,
                    ranked.exec_count,
                    ranked.version_count,
                    ranked.content_hash,
                    ranked.is_open
                FROM (
                    SELECT
                        h.id, h.sql_text, h.server, h.database_name, h.username,
                        h.executed_at, h.duration_ms, h.row_count, h.status, h.error_msg,
                        h.source, h.content_hash,
                        COALESCE(qs.name, h.tab_title, '') as session_name,
                        COUNT(*)                    OVER (PARTITION BY {GroupKey}) as exec_count,
                        COUNT(DISTINCT h.content_hash) OVER (PARTITION BY {GroupKey}) as version_count,
                        MAX(h.is_favorite)          OVER (PARTITION BY {GroupKey}) as is_favorite,
                        MAX(h.is_open)              OVER (PARTITION BY {GroupKey}) as is_open,
                        ROW_NUMBER()                OVER (PARTITION BY {GroupKey}
                                                          ORDER BY h.executed_at DESC, h.id DESC) as rn
                    FROM {fromClause}
                    LEFT JOIN query_sessions qs ON qs.id = h.session_id
                    {whereClause}
                ) AS ranked
                WHERE ranked.rn = 1
                ORDER BY ranked.executed_at DESC, ranked.id DESC
                LIMIT @limit OFFSET @offset";
```

Define the partition key once, above the query — rows that somehow still lack a session (a legacy client mid-upgrade, before the next restart backfills them) fall back to the old content-hash behaviour rather than collapsing into a single giant NULL group:

```csharp
            // COALESCE so a NULL session_id degrades to per-content grouping instead of
            // lumping every ungrouped row together.
            const string GroupKey = "COALESCE(CAST(h.session_id AS TEXT), 'hash:' || h.content_hash)";
```

Update the two count queries (`:530` and `:570`) to match:

```csharp
            countSql = $"SELECT COUNT(DISTINCT {GroupKey}) FROM {fromClause} {whereClause}";
```

Update the NameFilter clause (`:518`) to search the session name:

```csharp
            whereClauses.Add(
                "COALESCE((SELECT qs2.name FROM query_sessions qs2 WHERE qs2.id = h.session_id), h.tab_title) " +
                "LIKE '%' || @nameFilter || '%'");
```

Finally, read the new column in the row mapper alongside `exec_count`:

```csharp
                VersionCount = reader.IsDBNull(reader.GetOrdinal("version_count"))
                    ? 1 : reader.GetInt32(reader.GetOrdinal("version_count")),
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "HistorySessionSearchTests" -v:q --nologo`
Expected: PASS — 2 tests.

- [ ] **Step 5: Run the whole history suite**

Run: `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "History" -v:q --nologo`
Expected: PASS. Existing dedup tests that assert content-hash grouping will need their expectations updated to session grouping — that is the intended behaviour change, not a regression; update them and say so in the commit message.

- [ ] **Step 6: Commit** — summarise and **ask first**.

---

### Task 7: IPC — carry `SessionKey` shell → engine

**Files:**
- Modify: `src/AkmlSql.Core/Ipc/Messages/HistoryRecordRequest.cs:54`
- Modify: the `HistoryRecord` handler under `src/AkmlSql.Engine/Handlers/History/`
- Test: `tests/AkmlSql.Core.Tests/Ipc/HistoryRecordRequestTests.cs`

**Interfaces:**
- Consumes: `AddAsync(..., sessionKey)` (Task 4).
- Produces: `HistoryRecordRequest.SessionKey` at `[Key(11)]`.

- [ ] **Step 1: Write the failing test**

```csharp
using AkmlSql.Core.Ipc.Messages;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Ipc;

public class HistoryRecordRequestTests
{
    [Fact]
    public void SessionKey_round_trips()
    {
        var original = new HistoryRecordRequest { SqlText = "SELECT 1", SessionKey = "tab-A" };
        var bytes = MessagePackSerializer.Serialize(original);
        var back = MessagePackSerializer.Deserialize<HistoryRecordRequest>(bytes);
        Assert.Equal("tab-A", back.SessionKey);
    }

    /// <summary>
    /// A payload written by an older shell has no Key 11. It must still deserialize, with
    /// SessionKey null — that is the compatibility contract Task 4 relies on.
    /// </summary>
    [Fact]
    public void Missing_session_key_deserializes_as_null()
    {
        var legacy = MessagePackSerializer.Serialize(new HistoryRecordRequestLegacyShape
        {
            SqlText = "SELECT 1"
        });
        var back = MessagePackSerializer.Deserialize<HistoryRecordRequest>(legacy);
        Assert.Null(back.SessionKey);
    }

    /// <summary>Mirror of HistoryRecordRequest as it existed BEFORE Key 11 was added.</summary>
    [MessagePackObject]
    public class HistoryRecordRequestLegacyShape
    {
        [Key(0)] public string SqlText { get; set; } = string.Empty;
        [Key(1)] public bool Truncated { get; set; }
        [Key(2)] public string? Server { get; set; }
        [Key(3)] public string? Database { get; set; }
        [Key(4)] public string? Username { get; set; }
        [Key(5)] public long DurationMs { get; set; }
        [Key(6)] public long RowCount { get; set; }
        [Key(7)] public int Status { get; set; }
        [Key(8)] public string? ErrorMessage { get; set; }
        [Key(9)] public string? Source { get; set; }
        [Key(10)] public string? TabTitle { get; set; }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj --filter "HistoryRecordRequestTests" -v:q --nologo`
Expected: FAIL — `SessionKey` does not exist.

- [ ] **Step 3: Write minimal implementation**

Append to `HistoryRecordRequest` (after `TabTitle`):

```csharp
        /// <summary>
        /// Opaque, client-owned identifier that is stable for the lifetime of ONE editor
        /// document. Null/empty from clients that predate session grouping — the engine then
        /// stores the row unassigned and the backfill infers a session on the next start.
        /// </summary>
        [Key(11)]
        public string? SessionKey { get; set; }
```

Then pass it through in the `HistoryRecord` handler's `AddAsync` call:

```csharp
            sessionKey: request.SessionKey);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj --filter "HistoryRecordRequestTests" -v:q --nologo`
Expected: PASS — 2 tests.

- [ ] **Step 5: Commit** — summarise and **ask first**.

---

### Task 8: Shell — per-document `SessionKey`, `TabTitle` only when saved

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/History/ExecutionCapture.cs:237-269` (capture), `:520-581` (request build), document-close hook near `:299`
- Test: `tests/AkmlSql.Shell.Shared.Tests/QuerySessionKeyTests.cs`

**Interfaces:**
- Consumes: `HistoryRecordRequest.SessionKey` (Task 7).
- Produces: `internal static class DocumentSessionKeys` with
  `string ForDocument(string documentFullName)` and `void Forget(string documentFullName)`.

Extracting the key logic into its own class is what makes it testable — `ExecutionCapture` itself needs a live DTE and cannot be unit-tested.

- [ ] **Step 1: Write the failing test**

```csharp
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    public class QuerySessionKeyTests
    {
        [Fact]
        public void Same_document_yields_a_stable_key()
        {
            var a = DocumentSessionKeys.ForDocument(@"C:\temp\dwnhdxfq.sql");
            var b = DocumentSessionKeys.ForDocument(@"C:\temp\dwnhdxfq.sql");
            Assert.Equal(a, b);
            Assert.False(string.IsNullOrWhiteSpace(a));
        }

        [Fact]
        public void Different_documents_yield_different_keys()
        {
            var a = DocumentSessionKeys.ForDocument(@"C:\temp\one.sql");
            var b = DocumentSessionKeys.ForDocument(@"C:\temp\two.sql");
            Assert.NotEqual(a, b);
        }

        /// <summary>Closing and reopening a file is a NEW session — that is the "one tab, one entry" rule.</summary>
        [Fact]
        public void Reopening_after_close_yields_a_new_key()
        {
            var path = @"C:\temp\reopen.sql";
            var first = DocumentSessionKeys.ForDocument(path);
            DocumentSessionKeys.Forget(path);
            var second = DocumentSessionKeys.ForDocument(path);
            Assert.NotEqual(first, second);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj --filter "QuerySessionKeyTests" -v:q --nologo`
Expected: FAIL — `DocumentSessionKeys` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/AkmlSql.Shell.Shared/History/DocumentSessionKeys.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace AkmlSql.Shell.Shared.History
{
    /// <summary>
    /// Maps an open editor document to a stable query-session key. The key lives only as long as
    /// the document stays open: <see cref="Forget"/> is called from the document-close hook, so
    /// reopening the same file starts a new session ("one tab, one history entry").
    /// </summary>
    internal static class DocumentSessionKeys
    {
        private static readonly Dictionary<string, string> Keys =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new object();

        internal static string ForDocument(string documentFullName)
        {
            if (string.IsNullOrEmpty(documentFullName))
                return Guid.NewGuid().ToString("N");   // unidentifiable doc — its own session

            lock (Gate)
            {
                if (!Keys.TryGetValue(documentFullName, out var key))
                {
                    key = Guid.NewGuid().ToString("N");
                    Keys[documentFullName] = key;
                }
                return key;
            }
        }

        internal static void Forget(string documentFullName)
        {
            if (string.IsNullOrEmpty(documentFullName)) return;
            lock (Gate) { Keys.Remove(documentFullName); }
        }
    }
}
```

In `ExecutionCapture.cs`, replace the `source`/`tabTitle` capture block (`:237-252`):

```csharp
                // Source file, tab title, and session key.
                // TabTitle is sent ONLY for a document that is actually saved to disk: an unsaved
                // SSMS scratch document has a machine-generated name ("dwnhdxfq.sql") that carries
                // no user intent, and sending it would suppress the query-NN auto name.
                string? source = null;
                string? tabTitle = null;
                string? sessionKey = null;
                try
                {
                    var activeDoc = _dte.ActiveDocument;
                    if (activeDoc != null)
                    {
                        source = activeDoc.FullName;
                        sessionKey = DocumentSessionKeys.ForDocument(source);

                        var isSavedFile = !string.IsNullOrEmpty(activeDoc.Path)
                                          && System.IO.File.Exists(activeDoc.FullName);
                        tabTitle = isSavedFile ? activeDoc.Name : null;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "ExecutionCapture: failed to read active document info");
                }
```

Thread `sessionKey` through `OnExecutionCompleted` (add a `string? sessionKey` parameter and pass it at the `:259-269` call site) and set it on the request at `:580`:

```csharp
                        TabTitle = tabTitle,
                        SessionKey = sessionKey
```

In the document-close handler (near `:299`), release the key:

```csharp
                DocumentSessionKeys.Forget(document.FullName);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj --filter "QuerySessionKeyTests" -v:q --nologo`
Expected: PASS — 3 tests.

- [ ] **Step 5: Run the full shell suite**

Run: `dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj -v:q --nologo`
Expected: PASS — 143+ tests.

- [ ] **Step 6: Commit** — summarise and **ask first**.

---

### Task 9: Web — persisted `SessionKey`

**Files:**
- Modify: `src/AkmlSql.Web/Services/EditorSessionRecord` (add `SessionKey`), `src/AkmlSql.Web/Pages/Editor.razor:800-807`
- Modify: the web execute path that builds `HistoryRecordRequest`
- Test: `tests/AkmlSql.Web.Tests/EditorSessionKeyTests.cs`

**Interfaces:**
- Consumes: `HistoryRecordRequest.SessionKey` (Task 7).
- Produces: `EditorSessionRecord.SessionKey` (string, GUID "N" format), minted on first use and persisted with the editor session.

- [ ] **Step 1: Write the failing test**

```csharp
using System.Threading.Tasks;
using Xunit;

namespace AkmlSql.Web.Tests;

public class EditorSessionKeyTests
{
    [Fact]
    public async Task Session_key_survives_a_reload()
    {
        var store = new FakeEditorSessionStore();
        var first = await EditorSessionKeys.GetOrCreateAsync(store);
        var second = await EditorSessionKeys.GetOrCreateAsync(store);   // simulates a reload
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Reset_mints_a_new_key()
    {
        var store = new FakeEditorSessionStore();
        var first = await EditorSessionKeys.GetOrCreateAsync(store);
        await EditorSessionKeys.ResetAsync(store);
        var second = await EditorSessionKeys.GetOrCreateAsync(store);
        Assert.NotEqual(first, second);
    }
}
```

Implement `FakeEditorSessionStore` as an in-memory `IEditorSessionStore` mirroring the real interface — follow the existing fake pattern in `tests/AkmlSql.Web.Tests`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter "EditorSessionKeyTests" -v:q --nologo`
Expected: FAIL — `EditorSessionKeys` does not exist.

- [ ] **Step 3: Write minimal implementation**

Add `SessionKey` to `EditorSessionRecord`, then:

```csharp
namespace AkmlSql.Web.Services;

/// <summary>
/// The web has one editor, but its Blazor circuit is destroyed by a full page reload. Persisting
/// the key with the editor session keeps a reload inside ONE history entry; "Reset editor session"
/// deliberately starts a new one.
/// </summary>
internal static class EditorSessionKeys
{
    internal static async Task<string> GetOrCreateAsync(IEditorSessionStore store)
    {
        var record = await store.LoadAsync() ?? new EditorSessionRecord();
        if (string.IsNullOrEmpty(record.SessionKey))
        {
            record.SessionKey = Guid.NewGuid().ToString("N");
            await store.SaveAsync(record);
        }
        return record.SessionKey!;
    }

    internal static async Task ResetAsync(IEditorSessionStore store)
    {
        var record = await store.LoadAsync() ?? new EditorSessionRecord();
        record.SessionKey = null;
        await store.SaveAsync(record);
    }
}
```

Set `SessionKey = await EditorSessionKeys.GetOrCreateAsync(SessionStore)` on the `HistoryRecordRequest` built by the web execute path, and call `ResetAsync` from the existing "Reset editor session" button handler.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter "EditorSessionKeyTests" -v:q --nologo`
Expected: PASS — 2 tests.

- [ ] **Step 5: Commit** — summarise and **ask first**.

---

### Task 10: UI — show the session name, run count, and versions

**Files:**
- Modify: `src/AkmlSql.Web/Pages/History.razor:625` (`DisplayName`), row template, rename handler
- Modify: `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs` (same rebind)
- Test: `tests/AkmlSql.Web.Tests/HistoryRowDisplayTests.cs`

**Interfaces:**
- Consumes: `HistoryEntry.TabTitle` (now the session name) and `HistoryEntry.VersionCount` (Task 6).
- Produces: no new types.

- [ ] **Step 1: Write the failing test**

```csharp
using AkmlSql.Web.Pages;
using Xunit;

namespace AkmlSql.Web.Tests;

public class HistoryRowDisplayTests
{
    [Fact]
    public void Display_name_is_the_session_name()
        => Assert.Equal("query-01", History.DisplayNameFor(new HistoryEntryDto
        {
            TabTitle = "query-01",
            SqlText = "SELECT * FROM dbo.Customers"
        }));

    [Fact]
    public void Falls_back_to_sql_only_when_unnamed()
        => Assert.StartsWith("SELECT", History.DisplayNameFor(new HistoryEntryDto
        {
            TabTitle = null,
            SqlText = "SELECT * FROM dbo.Customers"
        }));

    [Theory]
    [InlineData(1, 1, "")]                     // single run, single version — no noise
    [InlineData(276, 1, "×276")]
    [InlineData(276, 12, "×276 · 12 versions")]
    [InlineData(3, 2, "×3 · 2 versions")]
    public void Meta_line_summarises_runs_and_versions(int runs, int versions, string expected)
        => Assert.Equal(expected, History.MetaFor(runs, versions));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter "HistoryRowDisplayTests" -v:q --nologo`
Expected: FAIL — `DisplayNameFor` / `MetaFor` do not exist.

- [ ] **Step 3: Write minimal implementation**

In `History.razor`'s `@code` block, replace the private `DisplayName` with testable statics:

```csharp
    /// <summary>
    /// The session name (query-NN, a saved file name, or the user's rename). The raw-SQL fallback
    /// now only fires for a row that somehow has no session at all.
    /// </summary>
    internal static string DisplayNameFor(HistoryEntryDto e) =>
        !string.IsNullOrWhiteSpace(e.TabTitle)
            ? e.TabTitle!
            : (e.SqlText ?? string.Empty).Trim();

    /// <summary>"×276 · 12 versions". Both halves are omitted when they carry no information.</summary>
    internal static string MetaFor(int executionCount, int versionCount)
    {
        var parts = new List<string>(2);
        if (executionCount > 1) parts.Add($"×{executionCount}");
        if (versionCount > 1) parts.Add($"{versionCount} versions");
        return string.Join(" · ", parts);
    }
```

Bind the row template to `DisplayNameFor(entry)` and `MetaFor(entry.ExecutionCount, entry.VersionCount)`. Apply the same two helpers in `HistoryToolWindowControl.cs` so both surfaces read identically. The rename handler is unchanged in shape — it already calls `HistoryActions.Rename`, which now updates the session row.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter "HistoryRowDisplayTests" -v:q --nologo`
Expected: PASS — 6 tests.

- [ ] **Step 5: Full-suite regression run**

```bash
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -v:q --nologo
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj -v:q --nologo
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj -v:q --nologo
dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj -v:q --nologo
```

Expected: PASS across all four.

- [ ] **Step 6: Live verification (web)**

The web edition is the only surface that can be driven end-to-end without deploying to SSMS. Note that the installed `AkmlSqlWebEngine` service currently crash-loops on an unrelated, already-fixed LAN-HTTPS bind bug; run the engine from the repo build in loopback mode to test:

```bash
dotnet src/AkmlSql.Engine/bin/Debug/net10.0/win-x64/AkmlSql.Engine.dll --web --config <loopback-config.json>
```

Then in the browser: pair the engine, connect to SQL, execute a query three times, edit it, execute again. Expect **one** history row named `query-01` showing `×4 · 2 versions`. Execute from a second browser session and expect `query-02`.

- [ ] **Step 7: Commit** — summarise and **ask first**.

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| Session identity (`SessionKey`) | 7 (contract), 8 (shell), 9 (web) |
| Schema (`query_sessions`, `session_id`, indexes) | 2 |
| Naming, ordinal race, precedence | 1, 3 |
| Storage stays per-execution | 4 (asserted), 6 (raw view asserted) |
| Read model, `FIRST_VALUE` removal, NameFilter | 6 |
| Backfill (UTC→local, idempotent) | 5 |
| UI rebind | 10 |
| Testing (all bullets) | 1, 3, 4, 5, 6, 7, 8, 9, 10 |
| Out of scope | No task adds a versions table, renumbering, or sync |

**Placeholder scan:** none — every code step carries real code. Task 9 Step 1 defers `FakeEditorSessionStore` to the existing fake pattern rather than inventing an interface shape blind; that is the one place the implementer must read neighbouring test code first, and it is called out explicitly.

**Type consistency:** `QuerySessionNamer.{LocalDateKey, FormatName, IsScratchTabTitle}`, `QuerySessionStore.GetOrCreateAsync`, `AddAsync(..., sessionKey)`, `HistoryEntry.VersionCount`, `HistoryRecordRequest.SessionKey`, `DocumentSessionKeys.{ForDocument, Forget}`, `EditorSessionKeys.{GetOrCreateAsync, ResetAsync}`, `History.{DisplayNameFor, MetaFor}` — each defined once and referenced with the same name and signature everywhere.

**Known risk carried from the spec:** Task 6's `GroupKey` uses `COALESCE(session_id, 'hash:'||content_hash)` so that a row which never got a session degrades to the old behaviour instead of collapsing every unassigned row into one entry. This matters during the window between upgrading the engine and the first restart that runs the backfill.
