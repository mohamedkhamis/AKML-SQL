# Research: Core IntelliSense Engine

**Date**: 2026-03-19 | **Branch**: `002-core-intellisense-engine`

## R1: T-SQL Parser (ScriptDom)

### Decision: Use Microsoft.SqlServer.TransactSql.ScriptDom with two-tier parsing strategy

**Rationale**: ScriptDom is the official Microsoft T-SQL parser with full grammar coverage (SQL Server 2016-2025). It does NOT support incremental parsing — every `Parse()` call re-parses the entire document. For a 10K-line document, full parse takes ~300ms (too slow per keystroke) but tokenization takes ~60ms (acceptable).

**Architecture**:
- **Fast tier (every keystroke)**: `GetTokenStream()` at ~50-70ms for clause detection, comment/string awareness, and keyword-before-cursor analysis
- **Full tier (debounced 300-500ms idle)**: Full `Parse()` with suffix-completion heuristics for AST. Walk AST for alias resolution, CTE scoping, JOIN analysis
- **Suffix completion**: Appending dummy tokens to incomplete SQL makes ScriptDom produce valid ASTs (e.g., `"SELECT * FROM " + "__dummy__"` → valid SelectStatement). Some cases still fail — fall back to token-stream analysis

**Key findings**:
- NuGet: `Microsoft.SqlServer.TransactSql.ScriptDom` v170.191.0, targets net472/netstandard2.0/net8.0. MIT license. Zero dependencies
- Version parsers: `TSql130Parser` (2016) through `TSql170Parser` (2025). Select based on connected server version
- Cursor context: Every `TSqlFragment` has `StartOffset`/`FragmentLength`. Visitor pattern walks ancestry chain (e.g., `SelectStatement > QuerySpecification > FromClause`) to determine clause
- Alias resolution: `NamedTableReference.Alias.Value` and `SchemaObject.BaseIdentifier.Value` give alias→table mapping
- CTE support: `CommonTableExpression.ExpressionName` + `Columns` list + `QueryExpression` body
- Error recovery: Multi-statement docs skip invalid statements and continue. Single incomplete statements return 0 statements — requires suffix completion workaround
- Token stream: `TSqlParserToken` has `TokenType` enum (Select, From, Where, Join, SingleLineComment, MultilineComment, AsciiStringLiteral, etc.)

**Alternatives considered**:
- Custom ANTLR grammar: Full control over incremental parsing but enormous maintenance burden for T-SQL's complex grammar. Rejected
- TSqlParser from DACFx: Same ScriptDom library, no incremental advantage. Same thing
- Hand-written tokenizer only: Fast but insufficient for alias/CTE resolution, subquery scoping. Used only for hot path

---

## R2: Named Pipe IPC

### Decision: Named pipes with MessagePack-CSharp and length-prefix framing

**Rationale**: Named pipes are the optimal IPC for local Windows-only communication between .NET Fx 4.7.2 (shell) and .NET 10 (engine). Sub-millisecond latency for typical payloads, no firewall prompts (critical for SSMS users), built-in Windows ACL security.

**Key findings**:
- Cross-runtime: Named pipes work across .NET Fx 4.7.2 and .NET 10 — OS-level mechanism
- Pipe name: `akmlsql-engine-{userSid}-{ssmsPid}` for per-user, per-instance isolation
- Mode: `PipeTransmissionMode.Byte` (not Message) with manual 4-byte length-prefix framing
- MessagePack-CSharp 2.x: Supports netstandard2.0. Use `[MessagePackObject]` with integer `[Key]` attributes. 2-4x faster than JSON, 50-70% payload size
- Multiplexing: Request ID correlation with `ConcurrentDictionary<int, TaskCompletionSource<T>>` and single reader loop
- Write serialization: `SemaphoreSlim` to prevent interleaved writes
- Security: Explicit `PipeSecurity` ACL restricting to current user SID + deny NETWORK SID
- Performance: <150 microsecond round-trip for small messages, <1ms for typical completion payloads
- Buffer sizes: 64KB for in/out buffers

**Engine lifecycle**:
- Shell launches engine process with `--pipe {name} --parent-pid {pid}`
- Engine monitors parent PID and self-terminates if parent exits (orphan protection)
- Shell reconnects with retry on pipe break (crash detection via IOException/EndOfStreamException)
- Heartbeat: Ping/pong every 15-30 seconds for health monitoring

**Alternatives considered**:
- TCP sockets: Would trigger Windows Firewall prompts — dealbreaker for SSMS users. Rejected
- gRPC: Heavy dependency, requires Grpc.Core (C++ native) on .NET Fx 4.7.2, overkill for local IPC. Rejected
- Shared memory: Lowest latency but requires manual synchronization (mutexes, ring buffers). Too error-prone. Rejected
- stdin/stdout (LSP-style): Line-buffered, awkward for binary protocols, tight process coupling. Rejected

---

## R3: VS SDK Editor Extensibility

### Decision: Legacy synchronous API (ICompletionSource, IOleCommandTarget) for cross-version compatibility

**Rationale**: SSMS 20 is based on VS 2017 SDK 15.x which only supports legacy synchronous MEF APIs. The async APIs (IAsyncCompletionSource) were introduced in VS 2017 15.8+ but are not guaranteed available in SSMS 20's isolated shell. Legacy APIs work across all target VS SDK versions (15.x through 17.x).

**Key findings**:

**Editor hooks**:
- `IWpfTextViewCreationListener` (MEF export) fires when editor views are created
- `ITextBuffer.Changed` event provides incremental text changes with old/new text and positions
- `ITextView.Caret.Position.BufferPosition` for cursor offset
- Bridge MEF and Package via `IComponentModel` / `SComponentModel`

**Completion source**:
- Implement `ICompletionSource` + `ICompletionSourceProvider` with `[ContentType("T-SQL")]`
- Use `[Order(Before = "default")]` to take priority over SSMS built-in provider
- `AugmentCompletionSession` adds `CompletionSet` with completion items

**Signature help**: `ISignatureHelpSource` + `ISignatureHelpSourceProvider` with `ISignature` objects containing `Parameters`, `Content`, `Documentation`

**Quick Info**: `IQuickInfoSource` + `IQuickInfoSourceProvider`, return WPF elements or strings for hover tooltips

**Content type**: Most likely `"T-SQL"` or `"SQL Server Tools"` — must be verified at runtime by logging `textView.TextBuffer.ContentType.TypeName` from a diagnostic build

**Keystroke handling**: `IOleCommandTarget` command filter via `IVsTextView.AddCommandFilter()`. Intercept `TYPECHAR` for dot/parenthesis triggers, `RETURN`/`TAB` for commit, `ESC` for dismiss

**WPF popup positioning**: `IWpfTextViewLine.GetCharacterBounds()` → `VisualElement.PointToScreen()` for screen coordinates. Theme detection via `VSColorTheme.GetThemedColor()` + `EnvironmentColors` keys

**SSMS connection detection**: Via internal `SQLEditors.dll` — `ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo`. Not a public API — reference the DLL with `CopyLocal = false`

**Disabling native IntelliSense**: Primary approach is command filter suppression (don't pass completion commands to next handler). Supplementary: disable via DTE options `Text Editor > Transact-SQL > IntelliSense > Auto List Members = false`

---

## R4: Schema Metadata Queries

### Decision: Batch sys catalog queries in single round-trip with 4-level permission degradation

**Rationale**: All queries use standard `sys.*` catalog views stable from SQL Server 2016 through 2025 and Azure SQL DB. Batch as single command with `NextResult()` iteration for one round-trip. Fall back through 4 permission levels gracefully.

**Key findings**:

**Queries (one round-trip)**:
1. Tables/views with row counts: `sys.objects` + `sys.schemas` + `sys.dm_db_partition_stats`
2. All columns with types: `sys.columns` + `sys.types` + `sys.default_constraints` + `sys.computed_columns` + PK detection via `sys.index_columns`
3. Foreign keys with column mappings: `sys.foreign_keys` + `sys.foreign_key_columns` + column joins
4. Procedures/functions + parameters: `sys.objects` + `sys.parameters` + `sys.types`
5. Extended properties: `sys.extended_properties` (class=1, name='MS_Description')

**Performance (warm cache, SSD)**:

| Database size | Total initial load | Memory |
|---|---|---|
| 500 tables, 5K columns | 50-150ms | 2-5 MB |
| 5,000 tables, 50K columns | 300-800ms | 15-40 MB |
| 10,000 tables, 100K columns | 600ms-2s | 30-80 MB |

**Change detection**: `CHECKSUM_AGG(BINARY_CHECKSUM(object_id, modify_date, type))` on `sys.objects` — costs 1-5ms, poll every 30-60 seconds. If changed, use `modify_date > @LastRefresh` to find affected objects

**Permission degradation levels**:
1. Full (VIEW DEFINITION + VIEW DATABASE STATE): All sys queries + DMVs
2. No DMV (VIEW DEFINITION only): sys catalog views + `sys.partitions` for row counts
3. No sys access: INFORMATION_SCHEMA fallback (no row counts, no change detection, no extended properties)
4. Public only: INFORMATION_SCHEMA showing only accessible objects

**Azure SQL DB**: All schema catalog queries work identically. Differences: no cross-database queries, no `sys.databases` full list, no linked servers. Detect via `SERVERPROPERTY('EngineEdition') = 5`

**Version compatibility**: Core catalog views unchanged 2016-2025. Optional: `temporal_type` (2016+), `is_node`/`is_edge` (2017+), `ledger_type` (2022+)
