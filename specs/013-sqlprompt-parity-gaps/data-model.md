# Data Model: SQL Prompt Parity — Remaining Gaps

**Date**: 2026-04-03  
**Feature**: `013-sqlprompt-parity-gaps`

## Entities

### 1. NoformatRegion (Existing — Extended)

Represents a formatting-disabled region detected by NoformatScanner.

| Field | Type | Description |
|-------|------|-------------|
| StartOffset | int | Byte offset of opening directive |
| EndOffset | int | Byte offset of closing directive (or EOF) |
| HasClosingTag | bool | Whether the region has a matching close marker |

**Extension**: The regex patterns in NoformatScanner gain two new alias groups:
- `-- AKML formatting off/on` → maps to same region semantics
- `-- SQL Prompt formatting off/on` → maps to same region semantics

No schema change — same `NoformatRegion` data structure.

### 2. SearchToken (New)

A parsed element from the History advanced search input.

| Field | Type | Description |
|-------|------|-------------|
| Type | enum | Literal, Wildcard, ExactPhrase, BooleanOr, BooleanNot, Prefix, CamelCase |
| Value | string | The raw token text |
| FtsQuery | string | The FTS5-compatible query fragment |

**Token types**:
- `Literal` → passed as-is to FTS5
- `Wildcard` → `Product*` → FTS5 prefix query `Product*`
- `ExactPhrase` → `"create view"` → FTS5 phrase `"create view"`
- `BooleanOr` → `A OR B` → FTS5 `A OR B`
- `BooleanNot` → `NOT DROP` → FTS5 `NOT DROP`
- `Prefix` → `server:PROD` → SQL WHERE clause (existing)
- `CamelCase` → `PC` → post-filter on result set

### 3. HistoryEntry (Existing — No Schema Change)

The `tab_title` column already stores custom names (via existing Rename action). The `is_favorite` column already stores starred state. No database schema changes needed for rename or starring — both are already implemented.

### 4. CompletionObjectType (Existing — Color Values Updated)

The 12 object types retain their integer IDs. Only the color mapping changes:

| ID | Type | New Color | New Background (20% opacity) |
|----|------|-----------|------------------------------|
| 0 | Table | #E5C04B | rgba(229,192,75,0.20) |
| 1 | View | #56B6C2 | rgba(86,182,194,0.20) |
| 2 | Column | #61AFEF | rgba(97,175,239,0.20) |
| 3 | Keyword | #ABB2BF | rgba(171,178,191,0.15) |
| 4 | Snippet | #3DD68C | rgba(61,214,140,0.20) |
| 5 | Function | #D19A66 | rgba(209,154,102,0.20) |
| 6 | Procedure | #C678DD | rgba(198,120,221,0.20) |
| 7 | Schema | #98C379 | rgba(152,195,121,0.20) |
| 8 | Database | #E06C75 | rgba(224,108,117,0.20) |
| 9 | Variable | #56B6C2 | rgba(86,182,194,0.20) |
| 10 | Alias | #61AFEF | rgba(97,175,239,0.20) |
| 11 | Parameter | #C678DD | rgba(198,120,221,0.20) |

### 5. ThemeBrushSet (Existing — Hex Values Updated)

**Light palette target**:
| Property | Current | Target |
|----------|---------|--------|
| Main | #F5F5F5 | #F0F0F0 |
| Panel | #FFFFFF | #FFFFFF |
| Selected | #CCE8FF | #0078D4 |
| Button | #0078D4 | #0078D4 |
| Border | #E0E0E0 | #CCCCCC |

**Dark palette target**:
| Property | Current | Target |
|----------|---------|--------|
| Main | #1E1E1E | #2D2D3B |
| Panel | #2D2D30 | #1E1E2E |
| Text Secondary | #888888 | #8892A8 |
| Border | #3C3C3C | #3A3F4E |

## Relationships

- `SearchToken` is parsed from user input in `HistorySearchParser` and assembled into FTS5 query string
- `NoformatRegion` is detected by `NoformatScanner` and propagated to all formatting pipeline stages via `LayoutNode.IsInNoformatRegion`
- `CompletionObjectType` color values are consumed by `AkmlCompletionPopup.CreateItemVisual()` for badge rendering
- `ThemeBrushSet` values are consumed by `SettingsWindow` constructor for dialog styling

## State Transitions

### SearchToken Lifecycle
```
User types search → HistorySearchParser tokenizes
  → Prefix tokens → SQL WHERE clauses
  → FTS5 tokens → MATCH query string
  → CamelCase tokens → post-filter predicate
  → Results returned → Highlighting applied in preview
```

### NoformatRegion Lifecycle
```
SQL input → NoformatScanner detects directives
  → Regions list created (sorted, non-overlapping)
  → LayoutEngine skips formatting in regions
  → CasingEngine skips casing in regions
  → TextEmitter preserves original text in regions
  → Output: formatted SQL with preserved regions
```
