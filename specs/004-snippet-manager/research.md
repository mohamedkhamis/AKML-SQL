# Research: Snippet Manager

**Branch**: `004-snippet-manager` | **Date**: 2026-03-20

## R1: Snippet Expansion Implementation

### Decision: Custom tab-stop implementation using ITrackingSpan + IOleCommandTarget

**Rationale**: The VS SDK's built-in expansion API (`IVsExpansionManager`/`IVsExpansionSession`/`IVsExpansionClient`) is tightly coupled to the `.snippet` XML format, prevents schema-aware IntelliSense during placeholder navigation, and is fragile across SSMS versions (especially SSMS 20 IsolatedShell). A custom implementation gives full control.

**Architecture — Shell/Engine split**:
- **Engine (out-of-proc)**: Snippet loading, indexing, searching, built-in variable resolution, body template expansion (string substitution of built-in vars), format-on-expand via the formatting pipeline. Returns expanded text with custom placeholder markers.
- **Shell (in-proc)**: Tab-stop navigation, ITrackingSpan management, linked placeholder synchronization, visual adornments, undo/redo integration, keyboard interception. Must be in-proc because it requires direct ITextView/ITextBuffer access.

**Tab-stop implementation pattern**:
1. **Trigger**: CompletionCommandHandler intercepts Tab. If word before caret matches a shortcode, initiate expansion.
2. **Expansion**: Send SnippetExpandRequest to engine → receive expanded text with placeholder positions → replace shortcode span with expanded text via ITextEdit.
3. **Tracking spans**: For each placeholder, create ITrackingSpan with SpanTrackingMode.EdgeInclusive. Group by variable name for linked placeholders.
4. **Navigation**: Tab advances to next group, Shift+Tab to previous. Active placeholder is selected.
5. **Linked sync**: Subscribe to ITextBuffer.Changed. When text changes within one span of a linked group, propagate to all other spans via ITextBuffer.CreateEdit() with reentrancy guard.
6. **Undo**: Wrap entire expansion in ITextUndoTransaction. Escape reverts.
7. **Schema-aware**: When landing on a schema-aware placeholder, trigger ICompletionBroker.TriggerCompletion() with filtered schema objects.

**Alternatives considered**:
- VS SDK `IVsExpansionSession`: Rejected — coupled to .snippet XML, no schema-aware placeholders, SSMS version fragility.
- Pure engine-side expansion (no shell tab-stops): Rejected — cannot do interactive placeholder navigation without ITextView access.

---

## R2: FileSystemWatcher for Hot-Reload

### Decision: FileSystemWatcher with 200ms debounce + 30-second polling fallback for network shares

**Rationale**: FileSystemWatcher works reliably for local folders but can silently drop notifications on network shares (SMB notification limitations). A polling fallback catches missed changes.

**Implementation**:
- One FileSystemWatcher per snippet source folder. Filter: `*.akmlsnippet`. NotifyFilter: LastWrite, FileName, DirectoryName.
- **Debounce**: On any Changed/Created/Deleted/Renamed event, reset a 200ms timer. When timer fires, reload affected files.
- **Network fallback**: For Team folder, add a 30-second polling timer comparing file timestamps against last known state.
- **Error handling**: On FileSystemWatcher.Error (common on network disconnect), dispose and recreate. Log warning, do not surface to user.
- **Buffer size**: Set InternalBufferSize to 16KB (handles ~250 simultaneous changes).
- **Location**: Engine process (owns the snippet index).
- **Graceful degradation**: If Team folder is unreachable, catch constructor exception, skip that source, retry every 60 seconds.

**Alternatives considered**:
- Polling only: Rejected — too slow for local folders where users expect instant reload.
- FileSystemWatcher only: Rejected — unreliable on network shares.

---

## R3: Snippet Context Filtering

### Decision: `context` field on snippet JSON mapping to existing CursorContextAnalyzer.ClauseType enum

**Rationale**: The Phase 2 CursorContextAnalyzer already determines clause type. Snippet context filtering reuses this infrastructure with no new parser needed.

**Context mapping**:

| Snippet Context | Maps to ClauseType | When Shown |
|---|---|---|
| `global` | Unknown | Batch start, after GO, empty line |
| `after_select` | Select | Inside SELECT clause |
| `after_from` | From | After FROM/JOIN |
| `after_where` | Where | Inside WHERE clause |
| `after_join_on` | JoinOn | Inside ON condition |
| `after_group_by` | GroupBy | Inside GROUP BY |
| `after_order_by` | OrderBy | Inside ORDER BY |
| `after_insert` | InsertColumns | After INSERT INTO |
| `after_update` | UpdateSet | Inside UPDATE SET |
| `after_exec` | Exec | After EXEC/EXECUTE |
| `after_create` | Create | After CREATE |
| `after_with` | With | Inside WITH (CTE) |

**Default**: If a snippet omits the `context` field, default to `["global"]`.

**Surround-with filtering**: Add `HasSelection` boolean to CompletionRequest IPC message. Surround-with snippets only appear when `HasSelection == true`.

**Alternatives considered**:
- New context enum: Rejected — existing ClauseType covers all needed contexts.
- No filtering: Rejected — DDL snippets cluttering WHERE clause suggestions hurts UX.

---

## R4: Import Format Analysis

### Decision: Three import parsers for SQL Prompt XML, SQL Prompt JSON (v10.5+), and SSMS native

**SQL Prompt XML** (`.sqlpromptsnippet`, pre-v10.5):
- Uses VS CodeSnippet XML schema. Placeholders in `<Declarations>/<Literal>`. Built-in variables inline in body.
- Stored in `%LocalAppData%\Red Gate\SQL Prompt <version>\Snippets\`

**SQL Prompt JSON** (v10.5+):
- Body as single `\n`-delimited string. Placeholders in `placeholders` array.
- Same storage location as XML.

**SSMS Native** (`.snippet`):
- VS CodeSnippet XML schema with `$end$` (→ `$CURSOR$`) and `$selected$` (→ `$SELECTEDTEXT$`).
- Often has no shortcode (invoked via menu, not typing).

**Variable mapping**:

| Source | Variable | AKML SQL | Action |
|---|---|---|---|
| SQL Prompt | `$DBNAME$` | `$DATABASE$` | Rename |
| SQL Prompt | `$PASTE$` | `$CLIPBOARD$` | Rename |
| SQL Prompt | `$DATE(format)$` | `$DATE$` | Strip format, use ISO |
| SSMS | `$end$` | `$CURSOR$` | Rename |
| SSMS | `$selected$` | `$SELECTEDTEXT$` | Rename |
| Both | `$CURSOR$`, `$DATE$`, `$TIME$`, `$USER$`, `$GUID$`, `$MACHINE$` | Same | Direct |

**Schema-aware on import**: Left unset — source formats don't support it. Users can add schema-awareness post-import via Snippet Manager.

**Detection strategy**: Content sniffing — `{` for JSON, `<?xml` or `<CodeSnippets` for XML.

**Alternatives considered**:
- Single XML-only parser: Rejected — SQL Prompt v10.5+ uses JSON; would miss newer installations.
- Auto-detect schema-aware types from variable names: Rejected — unreliable name-to-type guessing.
