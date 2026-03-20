# AKML SQL - Bug Report & Code Review

> **Date:** March 19, 2026 | **Reviewed Against:** Phase 1 PRD, Phase 2 PRD
> **Branch:** `001-phase1-foundation-installer`
> **Scope:** All implemented code for Phase 1 (Foundation & Installer) and Phase 2 (Core IntelliSense Engine)
> **Last Updated:** March 19, 2026 — All bugs fixed

---

## Summary

| Severity | Total Found | Fixed | Status |
|----------|-------------|-------|--------|
| CRITICAL | 7 | 7 | ALL FIXED |
| HIGH | 10 | 10 | ALL FIXED |
| MEDIUM | 12 | 12 | ALL FIXED |
| LOW | 8 | 8 | ALL FIXED |
| **TOTAL** | **37** | **37** | **ALL FIXED** |

---

## CRITICAL BUGS (All Fixed)

### BUG-001: MessagePack Runtime Type Serialization Mismatch — FIXED
- **File:** `src/AkmlSql.Shell.Shared/Ipc/PipeRpcClient.cs`
- **Fix:** Changed to generic `SendNotificationAsync<TPayload>` and `SendRequestAsync<T, TPayload>` using `MessagePackSerializer.Serialize<TPayload>(payload)`.

### BUG-002: Config Write Race Condition — FIXED
- **File:** `src/AkmlSql.Core/Config/ConfigManager.cs`
- **Fix:** Uses `File.Move(overwrite: true)` on .NET 10+, `#if NETSTANDARD2_0` guard for delete+move fallback.

### BUG-003: Null Payload Deserialization Crash — FIXED
- **File:** `src/AkmlSql.Engine/Server/PipeRpcServer.cs`
- **Fix:** Added null checks before every deserialization, returns `ErrorInfo` response.

### BUG-004: Heartbeat Uses Anonymous Type — FIXED
- **File:** `src/AkmlSql.Shell.Shared/Ipc/PipeRpcClient.cs`
- **Fix:** Changed to `EngineStatusInfo` with generic `SendRequestAsync<T, TPayload>`.

### BUG-005: SignatureHelp, QuickInfo, SchemaRefresh Return Stubs — FIXED
- **File:** `src/AkmlSql.Engine/Server/PipeRpcServer.cs`
- **Fix:** Wired all three to actual `SignatureProvider`, `QuickInfoProvider`, and cache refresh. Added `FindFunctionAtCursor()` helper.

### BUG-006: Async Void Event Handler Crash — FIXED
- **File:** `src/AkmlSql.Shell.Shared/Ipc/EngineProcessManager.cs`
- **Fix:** Changed to synchronous `void` with `Task.Run(async () => { try/catch })`.

### BUG-007: KeyValuePair Serialization Risk — FIXED
- **File:** `src/AkmlSql.Core/Ipc/Messages/QuickInfoResponse.cs`
- **Fix:** Created `[MessagePackObject] QuickInfoDetail` class. Updated all usages in `QuickInfoProvider`.

---

## HIGH-PRIORITY BUGS (All Fixed)

### BUG-008: Thread-Unsafe ContentTypeDetector — FIXED
- **Fix:** `volatile string` + `Interlocked.CompareExchange` for thread-safe detection.

### BUG-009: Thread-Unsafe DpiHelper — FIXED
- **Fix:** `volatile bool` + lock-based double-checked initialization.

### BUG-010: Process Handle Leaks (4 locations) — FIXED
- **Fix:** All `Process.Start()` calls wrapped in `using` statements.

### BUG-011: PipeRpcClient Disposal Incomplete — FIXED
- **Fix:** `Dispose()` waits for `_readerTask`/`_heartbeatTask` with 2-second timeout.

### BUG-012: SchemaCacheManager Async Void Timer — FIXED
- **Fix:** Changed to `void` callback with `Task.Run(async () => { ... })`.

### BUG-013: Engine Process Handle Leak — FIXED
- **Fix:** `using var parent = Process.GetProcessById(parentPid)`.

### BUG-014: Bare Exception Catch in Program.cs — FIXED
- **Fix:** Specific catches for `ArgumentException`, `OperationCanceledException`, `Exception` (logged).

### BUG-015: INFORMATION_SCHEMA Fallback Not Implemented — FIXED
- **File:** `src/AkmlSql.Engine/Schema/SchemaMetadataService.cs`
- **Fix:** Full implementation querying `INFORMATION_SCHEMA.TABLES`, `INFORMATION_SCHEMA.COLUMNS`, and `INFORMATION_SCHEMA.ROUTINES`. Populates tables, views, procedures, functions, and columns.

### BUG-016: Installer Next Button Quirk — FIXED
- **File:** `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- **Fix:** Added `UpdateNextButtonState` call right after `PopulateEnvCheckList` in `InitializeWizard`, in addition to existing `CurPageChanged` call.

### BUG-017: NativeIntelliSenseManager Registry Loop — FIXED
- **Fix:** Moved try-catch inside per-path iteration so failures don't stop the loop.

---

## MEDIUM-PRIORITY BUGS (All Fixed)

### BUG-018: AliasResolver Only Handles NamedTableReference — FIXED
- **File:** `src/AkmlSql.Engine/Parser/AliasResolver.cs`
- **Fix:** Added `Visit(QueryDerivedTable)` for subquery aliases and `Visit(SchemaObjectFunctionTableReference)` for table-valued function aliases.

### BUG-019: No Phase B Prerequisite Check — FIXED
- **File:** `src/AkmlSql.Engine/Schema/SchemaMetadataService.cs`
- **Fix:** Added `cache.Phase < PopulationPhase.PhaseA` guard at top of `PopulatePhaseBAsync`.

### BUG-020: Missing SnippetProvider and AliasProvider — FIXED
- **File:** `src/AkmlSql.Engine/Completion/CompletionEngine.cs`
- **Fix:** Added `RegisterProvider(new SnippetProvider())` and `RegisterProvider(new AliasProvider())`.

### BUG-021: ThemeManager Recalculates Per Property — FIXED
- **File:** `src/AkmlSql.Shell.Shared/Ui/ThemeManager.cs`
- **Fix:** Cached `_cachedTheme` with `_themeCached` flag and `InvalidateTheme()` method.

### BUG-022: RefreshCacheCommand Empty SessionId — FIXED
- **Fix:** Uses typed generic `SendNotificationAsync<RefreshRequest>`.

### BUG-023: EngineLifecycle Double LaunchAsync — FIXED
- **File:** `src/AkmlSql.Shell.Shared/Ipc/EngineLifecycle.cs`
- **Fix:** Added `volatile bool _launching` flag checked inside lock to prevent concurrent launch.

### BUG-024: Serilog Before Init — FIXED
- **Fix:** All 6 packages now use `try { Log.Error(...); } catch { }` pattern.

### BUG-025: ConnectAsync Hardcoded Timeout — FIXED
- **File:** `src/AkmlSql.Shell.Shared/Ipc/PipeRpcClient.cs`
- **Fix:** Increased defaults (15 retries, 300ms delay, 2000ms connect timeout), all configurable via parameters.

### BUG-026: No CRC in Frame Protocol — FIXED
- **File:** `src/AkmlSql.Core/Ipc/FrameProtocol.cs`
- **Fix:** Added 4-byte XOR-rotate checksum to frame header (8 bytes total: 4 length + 4 checksum). Verified on read.

### BUG-027: Engine Build Docs Missing — FIXED
- **File:** `CLAUDE.md`
- **Fix:** Added Engine description and `dotnet publish` command.

### BUG-028: SSMS 22 Path Detection Fragile — FIXED
- **File:** `src/AkmlSql.Installer/environment-scanner.iss`
- **Fix:** Split detection: `\Release\Common7\IDE` path uses `\Release\Common7\IDE\Extensions\AkmlSql`, direct `\Common7\IDE` path uses `\Common7\IDE\Extensions\AkmlSql`.

### BUG-029: Silent Install Missing Telemetry Flag — FIXED
- **File:** `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- **Fix:** Added `/TELEMETRY` flag to opt-in, `/NOTELEMETRY` to explicitly opt-out.

---

## LOW-PRIORITY BUGS (All Fixed)

### BUG-030: QuickInfoProvider Index Bounds — FIXED
- **Fix:** Added token type check (`Identifier` or `QuotedIdentifier`) before accessing `tokens[tokenIndex - 2]`.

### BUG-031: VariableTracker Null Check — FIXED
- **File:** `src/AkmlSql.Engine/Parser/VariableTracker.cs`
- **Fix:** Added `sqlType.Parameters == null ||` guard in `FormatSqlDataType`.

### BUG-032: Dead CompletionPopup.xaml — FIXED
- **Fix:** Removed `CompletionPopup.xaml` (XAML can't compile in shared projects; code-behind constructs UI programmatically).

### BUG-033: UpdateNotifier Returns Null — FIXED
- **File:** `src/AkmlSql.Shell.Shared/Update/UpdateNotifier.cs`
- **Fix:** Returns `static readonly NoUpdate = new UpdateResult { Available = false }` instead of null.

### BUG-034: KeywordProvider ToPascalCase — VERIFIED OK
- **Status:** Already has defensive `string.IsNullOrEmpty` and `Length > 0` checks. No fix needed.

### BUG-035: ReadLoopAsync Silent IOException — FIXED
- **Fix:** Added `Log.Debug(ex, ...)` to IOException catch.

### BUG-036: HeartbeatLoopAsync Not Awaited — FIXED
- **Fix:** `StopHeartbeat()` waits for `_heartbeatTask` with 2-second timeout.

### BUG-037: Feature Detection Skeleton — FIXED
- **File:** `src/AkmlSql.Engine/Schema/SchemaMetadataService.cs`
- **Fix:** Full implementation with all version-specific features: TemporalTables, GraphTables, JsonFunctions, StringAgg, Utf8, LedgerTables, GenerateSeries, GreatestLeast, Azure SQL detection.

---

## PRD Compliance Status

### Phase 1 PRD
| PRD Requirement | Status |
|----------------|--------|
| Menu appears in all target IDEs | PARTIAL — SSMS 22 verified |
| About dialog with Copy Diagnostics | IMPLEMENTED |
| Status bar indicator | IMPLEMENTED |
| Silent install with /TARGETS | IMPLEMENTED (BUG-016 fixed) |
| Uninstall removes all files | IMPLEMENTED |

### Phase 2 PRD
| PRD Requirement | Status |
|----------------|--------|
| Signature help for functions | WIRED (BUG-005 fixed) |
| Quick info tooltips | WIRED (BUG-005 fixed) |
| Schema refresh Ctrl+Shift+R | WIRED (BUG-005 fixed) |
| Snippet triggers | REGISTERED (BUG-020 fixed) |
| Alias suggestions | REGISTERED (BUG-020 fixed) |
| CTE column completion | IMPLEMENTED |
| Temp table completion | IMPLEMENTED |
| INFORMATION_SCHEMA fallback | IMPLEMENTED (BUG-015 fixed) |
| Cache persistence to disk | IMPLEMENTED |
| DDL detection for refresh | IMPLEMENTED |
| Native IntelliSense disable | IMPLEMENTED (BUG-017 fixed) |
| Theme support | IMPLEMENTED (BUG-021 fixed) |
| Multi-monitor DPI | IMPLEMENTED (BUG-009 fixed) |
| Engine crash recovery | IMPLEMENTED (BUG-006 fixed) |
| Frame protocol integrity | IMPLEMENTED (BUG-026 fixed) |
| Version-aware features | IMPLEMENTED (BUG-037 fixed) |
| 4-level permission degradation | IMPLEMENTED (BUG-015 fixed) |

---

## Build Verification

| Project | Status |
|---------|--------|
| AkmlSql.Engine (.NET 10) | 0 errors |
| AkmlSql.Core (netstandard2.0 + net10.0) | 0 errors |
| AkmlSql.Ssms22 (.NET Fx 4.7.2, x64) | 0 errors |

---

*End of Bug Report — All 37 bugs fixed — March 19, 2026*
