# Phase 1 Contracts — Wire Delta, Completion Behavior, Editor Keys, Formatter & Status

The feature's external interfaces: (1) the shell/web ↔ engine **IPC wire** (MessagePack DTOs in `AkmlSql.Core/Ipc`), (2) the **completion behavior contract** (position → suggestion set — what the corpus gate and the campaign re-run verify), (3) the **web editor trigger/keyboard contract**, (4) the **formatter Stage-7 contract**, and (5) the **web status/profile contracts**.

## 1. IPC wire delta (additive only — no new message types, no code changes)

| DTO | Change | Compat rule | FR |
|---|---|---|---|
| `CompletionItem` (`CompletionResponse.cs`) | **Add** `[Key(7)] string? FilterText` — the text fuzzy filtering scores against; engine-side consumer (`CompletionEngine`), hosts may ignore | Append-only key; old peer round-trip test required (missing key → null → engine falls back to `DisplayText`) | FR-026 |

Everything else (message codes, `CompletionRequest`, format/profile messages) is unchanged. New `ClauseType` members and providers are engine-internal.

## 2. Completion behavior contract (engine — verified by `CorpusGateTests` + campaign re-run)

Given a document, caret, and a schema cache seeded with `dbo.Orders(OrderID PK, CustomerID FK, OrderDate, …)`, `dbo.Customers`, `Sales.Invoices`, procs `usp_GetCustomerOrders(@CustomerID,@FromDate,@ToDate)` / `Sales.usp_MarkInvoicePaid(@InvoiceID)`, functions `fn_OrderItemCount` (scalar) / `fn_OrdersByCustomer` (TVF):

| # | Position (caret = `\|`) | MUST contain | MUST NOT contain | FR |
|---|---|---|---|---|
| P1 | `SELECT o.\| FROM dbo.Orders o` (also inside subquery/CTE body) | Orders columns | other tables' columns | FR-001/006 |
| P2 | `UPDATE o SET \| FROM dbo.Orders o` / `DELETE o FROM dbo.Orders o WHERE o.\|` | Orders columns | zero-item result | FR-008 |
| P3 | `SELECT (SELECT \| FROM dbo.OrderDetails od) FROM dbo.Orders o` | `od` columns + outer alias `o` | — | FR-006/007 |
| P4 | `… FROM A UNION SELECT \| FROM B` | B scope | A tables/aliases | FR-010 |
| P5 | `EXEC \|` | proc names incl. `Sales.usp_MarkInvoicePaid` | tables as first-class noise above procs | FR-012 |
| P6 | `EXEC dbo.usp_GetCustomerOrders @\|` | `@CustomerID`, `@FromDate`, `@ToDate` | `@@`-doubling on accept | FR-016, FR-005 |
| P7 | `DECLARE @id INT; SELECT \|` … typing `@` | `@id` | — | FR-017 |
| P8 | `INSERT INTO dbo.Customers (\|` | Customers columns (no IDENTITY/computed) | procs/functions/generic objects | FR-015 |
| P9 | `INSERT INTO \|` | tables/views | procs/functions | FR-015 |
| P10 | `ORDER \|` / `GROUP \|` | `BY` | tables, HAVING | FR-013 |
| P11 | `… o LEFT \|` | `JOIN`, `OUTER` | tables, ON | FR-013 |
| P12 | `UNION \|` | `SELECT`, `ALL` | previous branch scope | FR-013 |
| P13 | `DELETE \|` | `FROM` | SET/DECLARE-style GeneralKeywords ranking first | FR-013 |
| P14 | `WHERE OrderDate >= \|` / `SET Price = \|` / `VALUES (\|` | built-in functions (GETDATE, DATEADD, ISNULL…) + in-scope columns | — | FR-018 |
| P15 | `UPDATE TOP (5) dbo.Orders SET \|` | Orders assignable columns | SET-options (ANSI_NULLS list) | FR-014 |
| P16 | `WITH cte AS (SELECT OrderID FROM dbo.Orders) SELECT x.\| FROM cte x` | `OrderID` (CTE column) | schema-cache miss / empty | FR-019 |
| P17 | `WITH cte AS (…) SELECT 1; SELECT \| FROM \|` (2nd statement) | — | `cte` | FR-020 |
| P18 | `CREATE TABLE #t (A INT); SELECT \| FROM #\|` | `#t` name; `#t` columns | — | FR-022 |
| P19 | `SELECT * INTO #t FROM dbo.Orders; SELECT \| FROM #t` | Orders-shaped columns | empty column list | FR-023 |
| P20 | `SELECT [Cust\|` / `"dbo"."\|` / `JOIN [Sales].[\|` | filtered matches; Sales-schema-scoped joins | zero items; cross-schema FK joins | FR-024/025 |
| P21 | `ORDER BY` with typed prefix matching a **table** name only | — | flood of that table's unrelated columns | FR-026 |
| P22 | `UPDATE t SET \|` on table with IDENTITY/computed | assignable columns | IDENTITY/computed as targets | FR-027 |
| P23 | `CROSS APPLY fn_\|` | `fn_OrdersByCustomer` (TVF) | zero items | FR-028 |
| P24 | comments/strings; `-- akml` cases | (suppressed — unchanged) | any items | regression guard |

Explicit vs typing trigger MUST return the same item set at the same caret (web trigger changes are presentation-layer only).

## 3. Web editor trigger & keyboard contract (`akml-editor.js`)

| Gesture | Contract | FR |
|---|---|---|
| Type `.` after identifier/`]`/`"` | popup opens automatically; contents = explicit invoke at same caret; no popup after `.` in comments/strings/numeric literals | FR-001 |
| Type space after `UPDATE`, `INSERT [INTO]`, `DELETE [FROM]`, `EXEC(UTE)` | popup opens (parity with existing `WHERE`/`FROM`/`AND`) | FR-002 |
| `Tab` with popup open + selection | accepts selection. Precedence: AI ghost-text accept → completion accept → wildcard `*` expand → indent | FR-003 |
| `Tab` with no popup | indents (unchanged) | FR-003 |
| `Ctrl/Cmd+Enter` | executes current query (works with editor focus; binding precedes `defaultKeymap`'s Mod-Enter) | FR-004 |
| Accepting `@Name`/`#name` over typed `@p`/`#p` prefix | replaces the full sigil token (span regex `/[@#\w]+/`) | FR-005 |
| `Enter`/arrows/`Escape`/mouse on popup | unchanged (already correct) | regression guard |
| Offline (no engine bridge) | triggers degrade to keyword/snippet items; zero-item results never open an empty popup | edge case |

## 4. Formatter Stage-7 contract

| Condition | Output | Diagnostics | FR |
|---|---|---|---|
| Second pass == first pass | first pass (unchanged) | none | — |
| Second pass differs, non-empty, passes Stage-6 re-validation | **second pass** | Warning ("converged on second pass") — surfaced by the web UI, not dropped | FR-030 |
| Second pass differs and fails re-validation (or errors) | first pass | Warning (as today) | FR-030 |
| `EnableIdempotencyCheck = false` | first pass, no second parse | none | perf escape hatch (unchanged) |

FMTA-006 (chained-CTE JOIN shape) MUST be byte-idempotent on the **first** pass after J1; the Stage-7 change is the safety net, not the fix.

## 5. Web profile & connection-status contracts

**Profile store built-ins** (`IProfileStore`): `ListAsync` returns, in order: `builtin.khamis` ("Khamis Style"), `builtin.collapsed` ("Collapsed"), `builtin.default`, `builtin.ansi` — all `ProfileOrigin.BuiltIn`, contents identical to the desktop `.akmlstyle` definitions. `GetActiveIdAsync` on a fresh install → `builtin.khamis`; a dangling persisted active id → fallback `builtin.khamis`.

**Status pill** (`StatusBar`): three states — `Offline` / `BridgeOnly` ("Live · not connected to SQL", visually distinct) / `SqlConnected`. Invariant: `SqlConnected` iff a SQL session exists (never inferred from bridge connectivity alone).

**Boot restore** (`ISqlConnectionService`): when a last-used saved connection exists and is Windows-auth → non-blocking reconnect attempt on boot (loopback guard re-run; canonical single SessionId); on failure or SQL-auth → `BridgeOnly` + the connect UI reachable in one click.

**Saved-connection selection** (`ConnectionManagerModal`/`Picker`): the Database dropdown's option list contains and displays the saved database at selection time (bound value and display never diverge); the database list renders a hint that only engine-service-account-visible databases are listed.

## 6. Contract test obligations

- **Wire**: `CompletionItem.FilterText` MessagePack round-trip + old-payload (no Key 7) deserialization test (`AkmlSql.Core.Tests`).
- **Behavior matrix**: each P-row lands as an xunit case (existing per-cluster test classes) + the corpus gate covers the long tail; family thresholds per SC-003 asserted in `CorpusGateTests`.
- **Editor**: Playwright keystroke checks for each gesture row (campaign harness / `AkmlSql.Web.E2E.Tests`); the same-items-as-explicit invariant checked by comparing popup contents.
- **Formatter**: double-format property test (byte-equal) on FMTA-006 + formatting corpus; full `tests/format-parity` goldens green with **zero regenerations**.
- **Web state**: ProfileStore built-in listing/active-fallback unit tests; status-state transition tests (bridge up/down × SQL session present/absent × restore success/failure).
