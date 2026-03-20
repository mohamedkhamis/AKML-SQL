# Data Model: Snippet Manager

**Branch**: `004-snippet-manager` | **Date**: 2026-03-20

## Entity Overview

```
Snippet ──has──► SnippetMetadata
   │                   │
   ├──has many──► SnippetVariable (custom placeholders)
   │
   └──belongs to──► SnippetSource (Personal, Team, Built-in)

SnippetIndex ──contains many──► Snippet (in-memory, searchable)
   │
   └──watched by──► SnippetFileWatcher (per source)

SnippetExpansionSession (shell-side, per text view)
   │
   ├──has many──► TabStopGroup (one per variable)
   │                  │
   │                  └──has many──► PlaceholderSpan (ITrackingSpan)
   │
   └──uses──► SnippetExpansionResult (from engine)

UsageRecord ──tracks──► Snippet (usage count per snippet ID)
```

## Core Entities

### Snippet

The central entity. A reusable T-SQL code template stored as a JSON file.

| Field | Type | Constraints |
|---|---|---|
| Metadata | SnippetMetadata | Required |
| Variables | SnippetVariable[] | Optional (may be empty if only built-in vars used) |
| Body | string[] | Required, array of lines (template text with `$VarName$` markers) |

**Identity**: `Metadata.Id` (GUID, auto-generated on creation)
**Uniqueness**: `Metadata.Shortcode` should be unique within a source; cross-source conflicts resolved by priority
**File**: One `.akmlsnippet` file per snippet

### SnippetMetadata

| Field | Type | Constraints |
|---|---|---|
| Id | string (GUID) | Required, immutable after creation |
| Shortcode | string | Required, 1-30 chars, alphanumeric + underscore, case-insensitive |
| Name | string | Required, 1-100 chars |
| Description | string | Optional, max 500 chars |
| Author | string | Optional, max 100 chars |
| Version | string | Optional, semver format |
| Created | DateTime (UTC) | Required, set on creation |
| Modified | DateTime (UTC) | Required, updated on save |
| Category | string | Required: DML, DDL, DBA, ControlFlow, SurroundWith, Custom |
| Tags | string[] | Optional, for search |
| Context | string[] | Optional, defaults to `["global"]`. Valid values: global, after_select, after_from, after_where, after_join_on, after_group_by, after_order_by, after_insert, after_update, after_exec, after_create, after_with |
| SurroundsWith | bool | Required, false for normal snippets, true for surround-with |

### SnippetVariable

A custom placeholder within the snippet body.

| Field | Type | Constraints |
|---|---|---|
| Name | string | Required, matches `$Name$` in body, case-insensitive |
| Default | string | Optional, default placeholder text |
| Tooltip | string | Optional, shown during tab-stop navigation |
| SchemaAware | string | Optional: schemas, tables, views, columns, procedures, functions, datatypes, databases, indexes. Null for regular text input |

**Note**: Built-in variables (`$CURSOR$`, `$DATE$`, `$USER$`, etc.) are NOT listed in the Variables array. They are recognized by convention in the body and resolved automatically.

### SnippetSource

A folder-based collection of snippets.

| Field | Type | Constraints |
|---|---|---|
| Type | enum | Personal, Team, BuiltIn |
| Priority | int | Personal=1 (highest), Team=2, BuiltIn=3 |
| Path | string | Absolute directory path |
| IsWriteable | bool | Personal=true, Team=configurable, BuiltIn=false |
| IsAvailable | bool | true if path exists and is accessible |

### SnippetIndex

In-memory searchable collection of all loaded snippets.

| Field | Type | Constraints |
|---|---|---|
| Snippets | Dictionary<string, Snippet> | Keyed by ID |
| ShortcodeMap | Dictionary<string, List<Snippet>> | Keyed by lowercase shortcode, ordered by source priority |
| CategoryMap | Dictionary<string, List<Snippet>> | Keyed by category |
| LastReloadTime | DateTime | For staleness detection |

**Operations**:
- `Search(query)`: Full-text search across name, shortcode, description, tags, body
- `GetByShortcode(shortcode)`: Returns highest-priority snippet for a shortcode
- `GetByContext(clauseType, hasSelection)`: Returns snippets valid for the cursor context
- `Reload(source)`: Reload all snippets from a specific source

## Shell-Side Entities (Tab-Stop Navigation)

### SnippetExpansionSession

Active tab-stop navigation state for one text view.

| Field | Type | Constraints |
|---|---|---|
| TextView | IWpfTextView | The view where expansion is active |
| TabStopGroups | TabStopGroup[] | Ordered list of placeholder groups |
| CurrentGroupIndex | int | Index of the active tab-stop group |
| CursorPosition | ITrackingPoint | `$CURSOR$` final position |
| UndoTransaction | ITextUndoTransaction | For Escape revert |
| IsActive | bool | True while navigating placeholders |

### TabStopGroup

One variable's placeholder instances (may have multiple linked spans).

| Field | Type | Constraints |
|---|---|---|
| VariableName | string | The variable this group represents |
| Spans | ITrackingSpan[] | All instances of this variable in the expanded text |
| SchemaAwareType | string | Optional: schema-aware type for IntelliSense trigger |
| DefaultText | string | Initial placeholder text |

### SnippetExpansionResult

Response from engine after expansion request.

| Field | Type | Constraints |
|---|---|---|
| ExpandedText | string | Fully expanded text (built-in vars resolved, custom placeholders as markers) |
| Placeholders | PlaceholderInfo[] | Ordered list of placeholder positions in the expanded text |
| CursorOffset | int | Offset of `$CURSOR$` in expanded text (-1 if not present, defaults to end) |
| WasFormatted | bool | True if format-on-expand was applied |

### PlaceholderInfo

| Field | Type | Constraints |
|---|---|---|
| VariableName | string | Variable name |
| Offset | int | Character offset in expanded text |
| Length | int | Length of the default text |
| DefaultText | string | Default placeholder value |
| SchemaAwareType | string | Optional schema-aware type |
| GroupIndex | int | Tab-stop order (0-based) |

## Tracking Entities

### UsageRecord

Per-snippet usage tracking.

| Field | Type | Constraints |
|---|---|---|
| SnippetId | string (GUID) | References Snippet.Metadata.Id |
| Count | int | Times expanded, >= 0 |
| LastUsed | DateTime (UTC) | Most recent expansion |

**Storage**: Single JSON file at `%AppData%/AKML SQL/cache/snippet-usage.json`

## State Transitions

### Snippet Lifecycle

```
[Created] → [Saved] → [Available] → [Modified] → [Saved] → [Available]
                                                                ↓
                                                           [Deleted]
```

- Built-in snippets: `[Shipped] → [Available]` (never modified or deleted)
- Personal/Team snippets: Full CRUD lifecycle

### SnippetExpansionSession Lifecycle

```
[Idle] → [Triggered] → [Expanding] → [Navigating] → [Committed]
                                          │                ↑
                                          └──Tab/Shift+Tab─┘
                                          │
                                          └──Escape──► [Reverted]
```

- **Idle**: No active session on this text view
- **Triggered**: Shortcode + Tab detected
- **Expanding**: Engine resolving variables, formatting
- **Navigating**: User moving between tab-stops
- **Committed**: User pressed Tab past last placeholder or clicked elsewhere
- **Reverted**: User pressed Escape, undo transaction rolled back

### SnippetSource Availability

```
[Available] ←→ [Unavailable]
```

- Team source can become unavailable (network disconnect) and re-available
- Personal and Built-in are always available (local filesystem)

## Validation Rules

- Shortcode: 1-30 characters, alphanumeric + underscore, no spaces, case-insensitive
- Shortcode uniqueness: Unique within a source. Cross-source conflicts resolved by priority (personal > team > built-in)
- Variable name: 1-50 characters, alphanumeric + underscore, must not match built-in variable names
- Body: Must contain at least one line, max 5000 lines
- Schema-aware type: Must be one of: schemas, tables, views, columns, procedures, functions, datatypes, databases, indexes
- Context values: Must be from the defined set (global, after_select, etc.)
- Category: Must be from the defined set (DML, DDL, DBA, ControlFlow, SurroundWith, Custom)
- Surround-with snippets: Must contain `$SELECTEDTEXT$` in body when `surroundsWith` is true
