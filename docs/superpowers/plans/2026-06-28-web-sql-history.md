# Web SQL History Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the desktop SQL History feature to the Blazor web edition (`AkmlSql.Web`) at parity, reusing the engine's existing history IPC over the bridge.

**Architecture:** A thin `HistoryService` wraps the engine bridge (`HistoryRecord`=40 write, `HistorySearch`=41 read, `HistoryAction`=42 read+mutate — all already reachable over the WebSocket transport). A `/history` Razor page ports the desktop 2-region layout. Pure logic (date bucketing, source derivation, record-request construction, status mapping) lives in a testable `WebHistoryLogic` static class. No engine changes; no new IPC types.

**Tech Stack:** .NET 10 Blazor WASM, MessagePack IPC, AKML-Blue CSS tokens, xUnit (`AkmlSql.Web.Tests`).

## Global Constraints

- **NO git commits/add/push without explicit user approval** (project hard rule). Each task ends at a testable deliverable; commits are deferred and batched until the user says "commit". Do NOT run git write commands.
- Web services are `AddSingleton` (single-user WASM).
- All UI chrome uses `var(--akml-*)` CSS custom properties; no hardcoded hex (semantic status colors excepted). `History*` tokens already exist in `ThemeTokens.cs`.
- Every engine call guards on `_bridge.State == BridgeState.Open`; degrade to a clear "connect an engine" state otherwise. Wrap calls in try/catch; never throw to the UI.
- Reuse `AkmlSql.Core.Ipc.Messages` history DTOs verbatim. No new message types.
- Build web: `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj`. Unit tests: `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj`.

---

## File Structure

- **Create** `src/AkmlSql.Web/Services/WebHistoryLogic.cs` — pure, testable helpers.
- **Create** `src/AkmlSql.Web/Services/IHistoryService.cs` — interface + `HistoryService` impl (bridge wrapper).
- **Create** `src/AkmlSql.Web/Pages/History.razor` — the `/history` page (UI).
- **Create** `src/AkmlSql.Web/wwwroot/js/akml-history-export.js` — client-side file download helper.
- **Create** `tests/AkmlSql.Web.Tests/History/WebHistoryLogicTests.cs` — unit tests for the pure helpers.
- **Modify** `src/AkmlSql.Web/Program.cs` — register `IHistoryService`.
- **Modify** `src/AkmlSql.Web/Shared/NavMenu.razor` — add the History nav link.
- **Modify** `src/AkmlSql.Web/Pages/Editor.razor` — capture user-initiated executions.

---

## Task 1: `WebHistoryLogic` pure helpers (TDD)

**Files:**
- Create: `src/AkmlSql.Web/Services/WebHistoryLogic.cs`
- Test: `tests/AkmlSql.Web.Tests/History/WebHistoryLogicTests.cs`

**Interfaces — Produces:**
- `static class WebHistoryLogic`
  - `const string BucketToday="Today", BucketThisWeek="This Week", BucketTwoMonths="Two Months Ago", BucketOlder="Older";`
  - `static string DateBucket(string? executedAtIso, DateTime now)`
  - `static (IReadOnlyList<string> servers, IReadOnlyList<string> databases) DeriveSources(IEnumerable<HistoryEntryDto> entries)`
  - `static long DeriveRowCount(ExecuteQueryResult result)`
  - `static bool ShouldRecord(ExecuteStatus status)`
  - `static int MapStatus(ExecuteStatus status)` (→ `ExecutionStatus` int: Success=0,Error=1,Cancelled=2)
  - `static HistoryRecordRequest BuildRecordRequest(string sql, ExecuteQueryResult result, string? server, string? database)`

**Pre-step:** Open `src/AkmlSql.Core/Ipc/Messages/ExecuteQueryMessages.cs` and confirm exact member names on `ExecuteQueryResult` (`Status` of type `ExecuteStatus`, `ElapsedMs`, `TotalRowsAffected`, `ResultSets[]` with `.Rows`) and the `ExecuteStatus` enum members (`Ok, Error, Cancelled, TimedOut, NoConnection`). Adjust the code below if a name differs.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AkmlSql.Web.Tests/History/WebHistoryLogicTests.cs
using System;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Web.Services;
using Xunit;

namespace AkmlSql.Web.Tests.History;

public class WebHistoryLogicTests
{
    private static readonly DateTime Now = new(2026, 6, 28, 12, 0, 0, DateTimeKind.Local);
    private static string Iso(DateTime local) => local.ToUniversalTime().ToString("o");

    [Fact]
    public void DateBucket_Today() =>
        Assert.Equal(WebHistoryLogic.BucketToday, WebHistoryLogic.DateBucket(Iso(Now.AddHours(-2)), Now));

    [Fact]
    public void DateBucket_ThisWeek() =>
        Assert.Equal(WebHistoryLogic.BucketThisWeek, WebHistoryLogic.DateBucket(Iso(Now.AddDays(-3)), Now));

    [Fact]
    public void DateBucket_TwoMonths() =>
        Assert.Equal(WebHistoryLogic.BucketTwoMonths, WebHistoryLogic.DateBucket(Iso(Now.AddDays(-30)), Now));

    [Fact]
    public void DateBucket_Older() =>
        Assert.Equal(WebHistoryLogic.BucketOlder, WebHistoryLogic.DateBucket(Iso(Now.AddDays(-90)), Now));

    [Fact]
    public void DateBucket_Unparseable_Older() =>
        Assert.Equal(WebHistoryLogic.BucketOlder, WebHistoryLogic.DateBucket("not-a-date", Now));

    [Fact]
    public void DeriveSources_DistinctSortedNonEmpty()
    {
        var entries = new[]
        {
            new HistoryEntryDto { Server = "S2", Database = "D1" },
            new HistoryEntryDto { Server = "s2", Database = "" },
            new HistoryEntryDto { Server = "S1", Database = "D2" },
            new HistoryEntryDto { Server = null,  Database = "D1" },
        };
        var (servers, databases) = WebHistoryLogic.DeriveSources(entries);
        Assert.Equal(new[] { "S1", "S2" }, servers);
        Assert.Equal(new[] { "D1", "D2" }, databases);
    }

    [Fact]
    public void DeriveRowCount_Select_SumsResultRows()
    {
        var result = new ExecuteQueryResult
        {
            TotalRowsAffected = -1,
            ResultSets = new[] { new ResultSet { Rows = new string[3][] }, new ResultSet { Rows = new string[2][] } }
        };
        Assert.Equal(5, WebHistoryLogic.DeriveRowCount(result));
    }

    [Fact]
    public void DeriveRowCount_Dml_UsesAffected()
    {
        var result = new ExecuteQueryResult { TotalRowsAffected = 7, ResultSets = Array.Empty<ResultSet>() };
        Assert.Equal(7, WebHistoryLogic.DeriveRowCount(result));
    }

    [Theory]
    [InlineData(ExecuteStatus.Ok, true, 0)]
    [InlineData(ExecuteStatus.Error, true, 1)]
    [InlineData(ExecuteStatus.TimedOut, true, 1)]
    [InlineData(ExecuteStatus.Cancelled, true, 2)]
    [InlineData(ExecuteStatus.NoConnection, false, 1)]
    public void StatusMapping(ExecuteStatus status, bool shouldRecord, int mapped)
    {
        Assert.Equal(shouldRecord, WebHistoryLogic.ShouldRecord(status));
        Assert.Equal(mapped, WebHistoryLogic.MapStatus(status));
    }

    [Fact]
    public void BuildRecordRequest_ComposesFields()
    {
        var result = new ExecuteQueryResult
        {
            Status = ExecuteStatus.Ok, ElapsedMs = 42, TotalRowsAffected = -1,
            ResultSets = new[] { new ResultSet { Rows = new string[4][] } }
        };
        var req = WebHistoryLogic.BuildRecordRequest("SELECT 1", result, "localhost", "Northwind");
        Assert.Equal("SELECT 1", req.SqlText);
        Assert.Equal("localhost", req.Server);
        Assert.Equal("Northwind", req.Database);
        Assert.Equal(42, req.DurationMs);
        Assert.Equal(4, req.RowCount);
        Assert.Equal(0, req.Status);
        Assert.Equal("web", req.Source);
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter WebHistoryLogicTests`
Expected: FAIL — `WebHistoryLogic` does not exist.

- [ ] **Step 3: Implement `WebHistoryLogic`**

```csharp
// src/AkmlSql.Web/Services/WebHistoryLogic.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>
/// Pure, browser-independent helpers for the web SQL History feature: date bucketing (mirrors the
/// desktop DateBucketConverter), source-filter derivation, and turning an execute result into a
/// HistoryRecordRequest. Kept separate from HistoryService so it is unit-testable without a bridge.
/// </summary>
public static class WebHistoryLogic
{
    public const string BucketToday = "Today";
    public const string BucketThisWeek = "This Week";
    public const string BucketTwoMonths = "Two Months Ago";
    public const string BucketOlder = "Older";

    public static string DateBucket(string? executedAtIso, DateTime now)
    {
        if (DateTime.TryParse(executedAtIso, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt))
        {
            var d = dt.ToLocalTime().Date;
            var today = now.Date;
            if (d == today) return BucketToday;
            if (d > today.AddDays(-7)) return BucketThisWeek;
            if (d > today.AddDays(-60)) return BucketTwoMonths;
        }
        return BucketOlder;
    }

    public static (IReadOnlyList<string> servers, IReadOnlyList<string> databases) DeriveSources(
        IEnumerable<HistoryEntryDto> entries)
    {
        var servers = entries.Select(e => e.Server).Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Cast<string>().ToList();
        var databases = entries.Select(e => e.Database).Where(s => !string.IsNullOrEmpty(s))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Cast<string>().ToList();
        return (servers, databases);
    }

    public static long DeriveRowCount(ExecuteQueryResult result)
    {
        var read = result.ResultSets?.Sum(rs => (long)(rs.Rows?.Length ?? 0)) ?? 0;
        if (read > 0) return read;
        return result.TotalRowsAffected > 0 ? result.TotalRowsAffected : 0;
    }

    public static bool ShouldRecord(ExecuteStatus status) => status != ExecuteStatus.NoConnection;

    public static int MapStatus(ExecuteStatus status) => status switch
    {
        ExecuteStatus.Ok => 0,         // ExecutionStatus.Success
        ExecuteStatus.Cancelled => 2,  // ExecutionStatus.Cancelled
        _ => 1,                        // ExecutionStatus.Error (Error, TimedOut, NoConnection)
    };

    public static HistoryRecordRequest BuildRecordRequest(
        string sql, ExecuteQueryResult result, string? server, string? database) => new()
    {
        SqlText = sql ?? string.Empty,
        Truncated = false,
        Server = server,
        Database = database,
        DurationMs = result.ElapsedMs,
        RowCount = DeriveRowCount(result),
        Status = MapStatus(result.Status),
        ErrorMessage = result.ErrorMessage,
        Source = "web",
    };
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter WebHistoryLogicTests`
Expected: PASS (all). If a member name differs from the pre-step, fix and re-run.

- [ ] **Step 5: Deliverable ready** — do NOT commit (project rule). Note for batch commit later.

---

## Task 2: `IHistoryService` / `HistoryService` (bridge wrapper)

**Files:**
- Create: `src/AkmlSql.Web/Services/IHistoryService.cs`
- Modify: `src/AkmlSql.Web/Program.cs` (DI registration, near the other store singletons)

**Interfaces:**
- Consumes: `WebHistoryLogic` (Task 1), `IEngineBridge.SendAsync<,>` / `SendNotificationAsync` / `State`.
- Produces: `IHistoryService` with methods listed below.

- [ ] **Step 1: Implement the interface + service**

```csharp
// src/AkmlSql.Web/Services/IHistoryService.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;

namespace AkmlSql.Web.Services;

/// <summary>Browser-side facade over the engine's SQL History IPC (HistoryRecord=40,
/// HistorySearch=41, HistoryAction=42). All methods no-op/empty when the bridge is not Open.</summary>
public interface IHistoryService
{
    bool IsAvailable { get; }
    Task<HistorySearchResponse> SearchAsync(HistorySearchRequest request, CancellationToken ct);
    Task RecordAsync(HistoryRecordRequest request, CancellationToken ct);
    Task<bool> ToggleFavoriteAsync(long id, CancellationToken ct);
    Task<bool> RenameAsync(long id, string newName, CancellationToken ct);
    Task<int> DeleteAsync(long[] ids, CancellationToken ct);
    Task<int> RemoveOlderThanAsync(long anchorId, bool keepFavorites, CancellationToken ct);
    Task<string?> GetFullSqlAsync(long id, CancellationToken ct);
    Task<HistoryVersionDto[]> GetVersionsAsync(long id, CancellationToken ct);
    Task<(string left, string right)?> GetDiffAsync(long a, long b, CancellationToken ct);
}

internal sealed class HistoryService : IHistoryService
{
    private readonly IEngineBridge _bridge;
    private readonly IDiagnosticsRingBuffer _diagnostics;

    public HistoryService(IEngineBridge bridge, IDiagnosticsRingBuffer diagnostics)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    }

    public bool IsAvailable => _bridge.State == BridgeState.Open;

    public async Task<HistorySearchResponse> SearchAsync(HistorySearchRequest request, CancellationToken ct)
    {
        if (!IsAvailable) return new HistorySearchResponse { Success = false, Entries = Array.Empty<HistoryEntryDto>(), TotalCount = 0 };
        try
        {
            var r = await _bridge.SendAsync<HistorySearchRequest, HistorySearchResponse>(
                MessageTypes.HistorySearch, request, ct).ConfigureAwait(false);
            return r ?? new HistorySearchResponse { Success = false, Entries = Array.Empty<HistoryEntryDto>() };
        }
        catch (Exception ex) { return Fail(ex); }

        HistorySearchResponse Fail(Exception ex)
        {
            _diagnostics.Log(DiagnosticLevel.Warn, "history", $"HistorySearch failed: {ex.Message}");
            return new HistorySearchResponse { Success = false, Entries = Array.Empty<HistoryEntryDto>(), Error = ex.Message };
        }
    }

    public async Task RecordAsync(HistoryRecordRequest request, CancellationToken ct)
    {
        if (!IsAvailable) return;
        try { await _bridge.SendNotificationAsync(MessageTypes.HistoryRecord, request, ct).ConfigureAwait(false); }
        catch (Exception ex) { _diagnostics.Log(DiagnosticLevel.Trace, "history", $"HistoryRecord send failed: {ex.Message}"); }
    }

    public Task<bool> ToggleFavoriteAsync(long id, CancellationToken ct) =>
        ActionBool(new HistoryActionRequest { Action = HistoryActions.ToggleFavorite, EntryIds = new[] { id } }, ct);

    public Task<bool> RenameAsync(long id, string newName, CancellationToken ct) =>
        ActionBool(new HistoryActionRequest { Action = HistoryActions.Rename, EntryIds = new[] { id }, NewName = newName }, ct);

    public async Task<int> DeleteAsync(long[] ids, CancellationToken ct)
    {
        var r = await Action(new HistoryActionRequest { Action = HistoryActions.Delete, EntryIds = ids }, ct);
        return r?.DeletedCount ?? 0;
    }

    public async Task<int> RemoveOlderThanAsync(long anchorId, bool keepFavorites, CancellationToken ct)
    {
        var r = await Action(new HistoryActionRequest { Action = HistoryActions.RemoveOlderThan, EntryIds = new[] { anchorId }, KeepFavorites = keepFavorites }, ct);
        return r?.DeletedCount ?? 0;
    }

    public async Task<string?> GetFullSqlAsync(long id, CancellationToken ct)
    {
        var r = await Action(new HistoryActionRequest { Action = HistoryActions.GetFullSql, EntryIds = new[] { id } }, ct);
        return r?.FullSqlText;
    }

    public async Task<HistoryVersionDto[]> GetVersionsAsync(long id, CancellationToken ct)
    {
        var r = await Action(new HistoryActionRequest { Action = HistoryActions.GetVersions, EntryIds = new[] { id } }, ct);
        return r?.Versions ?? Array.Empty<HistoryVersionDto>();
    }

    public async Task<(string left, string right)?> GetDiffAsync(long a, long b, CancellationToken ct)
    {
        var r = await Action(new HistoryActionRequest { Action = HistoryActions.GetDiff, EntryIds = new[] { a, b } }, ct);
        return r is { Success: true } ? (r.DiffLeftSql ?? "", r.DiffRightSql ?? "") : null;
    }

    private async Task<HistoryActionResponse?> Action(HistoryActionRequest request, CancellationToken ct)
    {
        if (!IsAvailable) return null;
        try { return await _bridge.SendAsync<HistoryActionRequest, HistoryActionResponse>(MessageTypes.HistoryAction, request, ct).ConfigureAwait(false); }
        catch (Exception ex) { _diagnostics.Log(DiagnosticLevel.Warn, "history", $"HistoryAction {request.Action} failed: {ex.Message}"); return null; }
    }

    private async Task<bool> ActionBool(HistoryActionRequest request, CancellationToken ct)
    {
        var r = await Action(request, ct);
        return r?.Success == true;
    }
}
```

> Note: confirm `HistoryActions` is a static-int class or enum (from §1 it is `HistoryActions.ToggleFavorite` etc.). If the constants are `int`, the `Action = ...` assignment already matches the DTO's `int Action` field.

- [ ] **Step 2: Register in DI**

In `src/AkmlSql.Web/Program.cs`, next to the other `AddSingleton` store registrations, add:

```csharp
builder.Services.AddSingleton<IHistoryService, HistoryService>();
```

- [ ] **Step 3: Build the web project**

Run: `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj`
Expected: Build succeeded (0 errors).

- [ ] **Step 4: Deliverable ready** — do NOT commit.

---

## Task 3: Capture user-initiated executions (`Editor.razor`)

**Files:**
- Modify: `src/AkmlSql.Web/Pages/Editor.razor` (the user-initiated `ExecuteAsync` path ~line 608-648; NOT `OnAppliedReExecuteAsync`)

**Interfaces — Consumes:** `IHistoryService.RecordAsync`, `WebHistoryLogic.BuildRecordRequest`/`ShouldRecord`, `ISqlConnectionService.Server/Database`.

- [ ] **Step 1: Inject the service** — add near the other `@inject` directives:

```razor
@inject IHistoryService History
```

- [ ] **Step 2: Record after a user-initiated execute**

In `ExecuteAsync`, immediately after `var result = await QueryExec.ExecuteAsync(...)` returns and before/after the UI update, add:

```csharp
// Record into SQL History (best-effort, fire-and-forget) — user-initiated executes only.
if (WebHistoryLogic.ShouldRecord(result.Status))
{
    var rec = WebHistoryLogic.BuildRecordRequest(sql, result, SqlConn.Server, SqlConn.Database);
    _ = History.RecordAsync(rec, CancellationToken.None);
}
```

(`sql` is the already-resolved selection-or-document text in that method; `SqlConn` is the injected `ISqlConnectionService`. Do NOT add this to `OnAppliedReExecuteAsync`.)

- [ ] **Step 3: Build**

Run: `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Deliverable ready** — do NOT commit. (Capture is verified live in the verify phase.)

---

## Task 4: `History.razor` — page shell, search, grouped list, selection

**Files:**
- Create: `src/AkmlSql.Web/Pages/History.razor`

Model conventions on `Pages/Snippets.razor` (page directive, `@inject`, `OnInitializedAsync`, `@implements IAsyncDisposable`, inline `<style>` with `var(--akml-*)` tokens) and `Pages/Editor.razor` (subscribe to `SqlConn.StateChanged`, `IThemeService.InitializeAsync()`).

**Behavior:**
- `@page "/history"`. Inject `IHistoryService History`, `ISqlConnectionService SqlConn`, `IThemeService Theme`, `NavigationManager Nav`, `IEditorSessionStore` (for Open), `IQueryExecutionService` (for Re-execute).
- State: `List<HistoryEntryDto> _entries`, `HistoryEntryDto? _selected`, `string _search`, `bool _favoritesOnly`, `string? _server`, `string? _database`, `bool _loading`, `int _total`, `int _offset`, `const int PageSize=100`.
- `LoadAsync(bool reset)`: build `HistorySearchRequest { SearchText=_search (null if blank), FavoritesOnly=_favoritesOnly, Server=_server, Database=_database, Deduplicate=true, Offset, Limit=PageSize }`, call `History.SearchAsync`; on reset replace `_entries`, else append; set `_total`. Derive source dropdowns via `WebHistoryLogic.DeriveSources(_entries)`.
- Group rows for display with `WebHistoryLogic.DateBucket(e.ExecutedAt, DateTime.Now)`; render collapsible group headers (Today/This Week/Two Months Ago/Older) preserving entry order (newest-first from the engine). Track collapsed buckets in a `HashSet<string>`.
- Row template: line 1 = `TabTitle` or a 60-char whitespace-collapsed `SqlText` slice; line 2 = relative time + ` · Executed N times` (only when `ExecutionCount>1`) on the left, `● {Server}` (status-dot colored by `IsOpen`) on the right. Far-left favorite star (`★`/`☆`, `--akml-history-star-active` when favorite); clicking it toggles favorite (Task 6).
- Selecting a row sets `_selected` → drives preview + versions (Task 5).
- **Not-connected state:** when `!History.IsAvailable`, show a centered "Connect an engine to view SQL history" panel instead of the list.
- Top bar: search `<input>` (Enter → `LoadAsync(true)`), favorites toggle button, a source/server `<select>` (All + derived servers/databases), refresh button.
- Infinite scroll: on the list's scroll event, when near bottom and `_entries.Count < _total` and not `_loading`, call `LoadAsync(false)`.

- [ ] **Step 1: Create `History.razor`** with the markup, `@code` block, and inline `<style>` per the behavior above, using `var(--akml-*)` tokens (mirror `Snippets.razor` styling). Initialize: `await Theme.InitializeAsync(); SqlConn.StateChanged += OnConnChanged; await LoadAsync(true);` Dispose: unsubscribe.

- [ ] **Step 2: Build**

Run: `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Deliverable ready** — do NOT commit. (Live-verified later.)

---

## Task 5: `History.razor` — preview pane, version sub-panel, metadata, Open

**Files:**
- Modify: `src/AkmlSql.Web/Pages/History.razor`

**Behavior:**
- Right region: header (selected name + local timestamp), a read-only highlighted SQL preview, a metadata line (`● {Server} · {Database} · v N of M`), and a primary **Open** button.
- **Preview highlighting:** port the desktop lightweight tokenizer (`SqlPreviewTokenizer` in `HistoryToolWindowControl.cs`) to a small C# helper rendering `RenderFragment` spans with CSS classes `.tok-keyword/.tok-string/.tok-comment` (colored via `--akml-accent-primary` / `--akml-status-success` / `--akml-text-secondary`) and a `.search-hit` background (`--akml-history-match-highlight`) for the current `_search` terms. Keep it a pure helper so the keyword set/highlight logic is unit-testable; render with `@((MarkupString)...)` only via HtmlEncoder-escaped segments, or preferably emit `<span>` elements through a `RenderFragment` to avoid manual escaping.
- On selection: call `History.GetFullSqlAsync(_selected.Id)` to get the untruncated SQL for the preview (the list DTO truncates to 500 chars); fall back to `_selected.SqlText` if null.
- Version sub-panel (left, below the list): header "History for {name}"; on selection call `History.GetVersionsAsync(_selected.Id)`, list versions newest-first labeled `v{N} (current)` / `v{N}` with `SavedAt`; selecting a version renders that version's `SqlText` in the preview and updates the `v N of M` metadata.
- **Open** button: `History.GetFullSqlAsync` → load into the web editor via `IEditorSessionStore` (set document text) and `Nav.NavigateTo("/")`. If a richer "set text + connection" API exists on the editor session store, use it; otherwise set the document text and navigate.

- [ ] **Step 1: Add the preview tokenizer helper** (port from desktop) to `WebHistoryLogic` or a sibling `SqlPreviewHighlighter` with a `IReadOnlyList<(int start,int len,string kind)> Tokenize(string sql)` method + a couple of unit tests (keyword/string/comment classification, full-coverage spans). Run: `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter Highlight` → PASS.

- [ ] **Step 2: Build the right region** in `History.razor` consuming the helper + version panel.

- [ ] **Step 3: Build**

Run: `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Deliverable ready** — do NOT commit.

---

## Task 6: `History.razor` — row actions, favorites, source menu

**Files:**
- Modify: `src/AkmlSql.Web/Pages/History.razor`

**Behavior:** per-row actions (context menu or an overflow button): Copy SQL (JS clipboard), Open in editor (Task 5), Re-execute (`GetFullSqlAsync` → set editor text → `QueryExec.ExecuteAsync`, navigate to `/`), Rename (prompt → `History.RenameAsync` → reload), Toggle favorite (`History.ToggleFavoriteAsync` → reload), Delete (confirm → `History.DeleteAsync` → reload), Remove older than (confirm → `History.RemoveOlderThanAsync(_selected.Id, keepFavorites:true)` → reload). Toolbar favorites toggle re-runs the search; source `<select>` sets `_server`/`_database` and reloads.

- [ ] **Step 1: Implement actions** in the `@code` block + wire UI affordances.
- [ ] **Step 2: Build** — `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj` → succeeded.
- [ ] **Step 3: Deliverable ready** — do NOT commit.

---

## Task 7: Compare modal + client-side Export download

**Files:**
- Modify: `src/AkmlSql.Web/Pages/History.razor`
- Create: `src/AkmlSql.Web/wwwroot/js/akml-history-export.js`

**Behavior:**
- **Compare:** enabled when exactly two rows are selected; `History.GetDiffAsync(a,b)` → modal showing the two SQL texts side by side (read-only, monospace, theme tokens). (A line diff is a nice-to-have; side-by-side is sufficient for parity.)
- **Export:** a toolbar Export button with format choice (CSV / JSON / SQL). Build the file content in C# from the current filtered `_entries` (fetching full SQL per entry is optional; the 500-char preview is acceptable for CSV/JSON, full SQL for the SQL format via `GetFullSqlAsync`). Trigger a browser download via JS interop.

- [ ] **Step 1: Create the download helper**

```javascript
// src/AkmlSql.Web/wwwroot/js/akml-history-export.js
export function download(filename, mime, content) {
    const blob = new Blob([content], { type: mime });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url; a.download = filename;
    document.body.appendChild(a); a.click();
    document.body.removeChild(a); URL.revokeObjectURL(url);
}
```

- [ ] **Step 2: Implement Compare modal + Export builders** in `History.razor` (CSV/JSON/SQL string builders; `IJSRuntime` import of the module; `await module.InvokeVoidAsync("download", name, mime, content)`).
- [ ] **Step 3: Build** — `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj` → succeeded.
- [ ] **Step 4: Deliverable ready** — do NOT commit.

---

## Task 8: Nav link + final build + tests green

**Files:**
- Modify: `src/AkmlSql.Web/Shared/NavMenu.razor`

- [ ] **Step 1: Add the nav link** next to the other `akml-nav-link` entries:

```razor
<a class="akml-nav-link" href="/history">History</a>
```

- [ ] **Step 2: Full web build**

Run: `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj`
Expected: Build succeeded (0 errors).

- [ ] **Step 3: Run web unit tests**

Run: `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj`
Expected: PASS (existing + new `WebHistoryLogicTests` / highlighter tests).

- [ ] **Step 4: Deliverable ready** — do NOT commit. Summarize all changes and ask the user before any commit.

---

## Verification (separate phase — `verify` skill)

After Task 8, run the **verify** skill against the running web app (Playwright): pair an engine, connect to a loopback SQL Server, run a query (capture), open `/history`, confirm the entry appears, exercise search / grouping / selection / preview / versions / favorite / rename / open-in-editor / re-execute / remove-older / delete / export-download, plus probes (empty search, not-connected state, special characters, paging).

## Self-Review notes
- **Spec coverage:** storage (engine IPC) → Task 2; capture → Task 3; page/list/grouping/search → Task 4; preview/versions/Open → Task 5; actions → Task 6; compare/export → Task 7; nav/theming → Tasks 4/8. All design sections covered.
- **Types:** `WebHistoryLogic` member names match between Task 1 (definition) and Tasks 3/5 (use). `IHistoryService` methods match between Task 2 (definition) and Tasks 4-7 (use).
- **Placeholders:** none — pure-logic code is complete; UI tasks are spec'd with patterns + the file to model (live-verified, per project convention for shell/UI paths).
