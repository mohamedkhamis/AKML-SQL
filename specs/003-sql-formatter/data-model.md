# Data Model: SQL Formatter & Code Beautifier

**Branch**: `003-sql-formatter` | **Date**: 2026-03-20

## Entity Overview

```
FormattingProfile ──has──► ProfileMetadata
       │                         │
       ├──has many──► FormattingOption (250+, organized by OptionCategory)
       │
       ├──has──► FormatActionConfig (which actions run during full format)
       │
       └──based on──► FormattingProfile (base profile, nullable)

FormatterPipeline ──uses──► FormattingProfile
       │
       ├──produces──► FormatResult
       │
       ├──uses──► NoformatRegion[]
       │
       └──uses──► SqlcmdDirective[] (extracted during preprocessing)

BulkFormatOperation ──produces──► BulkFormatReport
       │                                │
       └──contains many──► FileFormatResult[]

FormatAction (standalone) ──produces──► FormatResult
```

## Core Entities

### FormattingProfile

The central entity. A named collection of all formatting options that defines a complete style.

| Field | Type | Constraints |
|---|---|---|
| Metadata | ProfileMetadata | Required |
| Whitespace | WhitespaceOptions | Required, defaults from base profile |
| Casing | CasingOptions | Required |
| Lists | ListOptions | Required |
| Parentheses | ParenthesisOptions | Required |
| Dml | DmlOptions | Required |
| Join | JoinOptions | Required |
| Ddl | DdlOptions | Required |
| ControlFlow | ControlFlowOptions | Required |
| Case | CaseOptions | Required |
| Cte | CteOptions | Required |
| Expressions | ExpressionOptions | Required |
| FormatActions | FormatActionConfig | Required |
| ExtensionData | Dictionary | Nullable, preserves unknown fields for forward compatibility |

**Identity**: `Metadata.Id` (GUID, auto-generated on creation)
**Uniqueness**: `Metadata.Name` must be unique within a profile storage location
**Read-only flag**: Built-in profiles are read-only; custom profiles are editable

### ProfileMetadata

| Field | Type | Constraints |
|---|---|---|
| Id | string (GUID) | Required, immutable after creation |
| SchemaVersion | int | Required, current = 1 |
| Name | string | Required, 1-100 chars, unique per storage |
| Description | string | Optional, max 500 chars |
| Author | string | Optional, max 100 chars |
| Version | string | Optional, semver format (e.g., "1.2") |
| Created | DateTime (UTC) | Required, set on creation |
| Modified | DateTime (UTC) | Required, updated on save |
| BasedOn | string | Optional, name of parent profile |
| IsBuiltIn | bool | Required, true for shipped profiles |

### OptionCategory (enum)

| Value | Description | Approx. Option Count |
|---|---|---|
| Whitespace | Tabs, indentation, line breaks, spacing | 21 |
| Casing | Keywords, functions, types, identifiers, DB sync | 10 |
| Lists | Comma position, alignment, collapse, one-per-line | 10 |
| Parentheses | Open/close placement, collapse, indentation | 10 |
| Dml | SELECT, INSERT, UPDATE, DELETE, MERGE formatting | 20 |
| Join | JOIN/ON placement, alignment, type normalization | 9 |
| Ddl | CREATE TABLE/PROC/FUNC formatting, parameter alignment | 15 |
| ControlFlow | IF/ELSE, WHILE, TRY/CATCH, CASE, CTE, expressions | 20+ |

### FormatActionConfig

Controls which standalone actions are included in the full Format SQL command.

| Field | Type | Default |
|---|---|---|
| ApplyLayout | bool | true |
| ApplyCasing | bool | true |
| InsertSemicolons | bool | false |
| RemoveSemicolons | bool | false |
| ExpandWildcards | bool | false |
| QualifyObjectNames | bool | false |
| AddAsKeyword | bool | true |
| AddSquareBrackets | bool | false |

## Pipeline Entities

### NoformatRegion

Identified during pre-scan of the raw document text.

| Field | Type | Constraints |
|---|---|---|
| StartOffset | int | Character offset of the opening tag (inclusive) |
| EndOffset | int | Character offset of the closing tag end (exclusive), or EOF |
| OpenTagType | TagType | LineComment or BlockComment |
| HasClosingTag | bool | false if region extends to EOF |

**Lifecycle**: Created during pre-scan, immutable, consumed by all pipeline stages.

### SqlcmdDirective

Extracted during SQLCMD preprocessing.

| Field | Type | Constraints |
|---|---|---|
| Index | int | Unique sequential identifier |
| OriginalText | string | Exact original text |
| Type | DirectiveType | Line (`:setvar`) or Inline (`$(Var)`) |
| StartOffset | int | Original character offset |
| Length | int | Original character length |
| SentinelText | string | Replacement text inserted into cleaned SQL |

### LayoutNode

Internal representation during the layout stage.

| Field | Type | Constraints |
|---|---|---|
| TokenIndex | int | Index into ScriptTokenStream |
| TokenType | TSqlTokenType | From ScriptDom |
| OriginalText | string | Original token text |
| FormattedText | string | After casing/formatting rules applied |
| IndentLevel | int | Computed indentation depth |
| PrecedingBreak | BreakType | None, NewLine, EmptyLine |
| PrecedingSpaces | int | Spaces before this token |
| TrailingComment | CommentAttachment | Nullable, attached trailing comment |
| IsInNoformatRegion | bool | If true, emit OriginalText verbatim |

### CommentAttachment

| Field | Type | Constraints |
|---|---|---|
| TokenIndex | int | Index of the comment token |
| Text | string | Full comment text |
| AttachmentType | enum | Trailing, Leading, Standalone |
| OriginalIndent | int | Original indentation (for leading comments) |

## Result Entities

### FormatResult

Output of a single formatting operation.

| Field | Type | Constraints |
|---|---|---|
| Success | bool | true if formatting completed |
| FormattedText | string | Formatted SQL (or original on failure) |
| WasModified | bool | true if output differs from input |
| ValidationPassed | bool | true if AST equivalence confirmed |
| Diagnostics | FormatDiagnostic[] | Warnings and errors |
| ElapsedMs | long | Formatting duration |

### FormatDiagnostic

| Field | Type | Constraints |
|---|---|---|
| Severity | enum | Info, Warning, Error |
| Message | string | Human-readable description |
| Offset | int | Character offset (optional) |
| Line | int | Line number (optional) |

### BulkFormatReport

Output of a bulk formatting operation.

| Field | Type | Constraints |
|---|---|---|
| Timestamp | DateTime (UTC) | When the operation ran |
| ProfileName | string | Profile used |
| TotalFiles | int | Files discovered |
| FormattedCount | int | Files that were modified |
| AlreadyFormattedCount | int | Files unchanged (already formatted) |
| ParseErrorCount | int | Files with parse errors |
| SkippedCount | int | Read-only or other skips |
| TotalLinesChanged | int | Sum of lines changed |
| ElapsedMs | long | Total operation time |
| Details | FileFormatResult[] | Per-file results |

### FileFormatResult

| Field | Type | Constraints |
|---|---|---|
| FilePath | string | Absolute path |
| Status | enum | Formatted, AlreadyFormatted, ParseError, Skipped, Error |
| LinesChanged | int | 0 if not modified |
| ErrorMessage | string | Nullable, set on error |
| ElapsedMs | long | Per-file duration |

## State Transitions

### FormattingProfile Lifecycle

```
[Created] → [Draft] → [Saved] → [Active] → [Modified] → [Saved]
                                    ↑                        │
                                    └────────────────────────┘
```

- **Created**: User creates or duplicates a profile. Assigned a GUID.
- **Draft**: Being edited in profile editor. Changes not yet persisted.
- **Saved**: Written to `.akmlstyle` file on disk.
- **Active**: Set as the current formatting profile in config.
- **Modified**: User changes options. Re-enters Draft→Saved cycle.
- **Deleted**: Custom profile removed. If it was active, revert to Default.

### Built-in Profile Lifecycle

```
[Shipped] → [Active] (can be set as active, never modified or deleted)
```

### DatabaseCache for Identifier Sync

Reuses Phase 2's `DatabaseCache` states. Formatter reads from cache when `casing.syncWithDatabase = true`:

```
[NotLoaded] → formatter uses fallback casing rule (AsIs)
[PhaseA/B/Complete] → formatter uses cached identifiers for case sync
[Stale] → formatter uses cached identifiers (stale data better than no data)
```

## Validation Rules

- Profile name: 1-100 characters, no path separators (`/`, `\`, `:`), unique per storage location
- SchemaVersion: must be >= 1; if > current supported, load with best-effort + warning
- All numeric options: must be within their defined min/max range; out-of-range → clamp to nearest valid
- Casing mode: must be one of `UPPERCASE`, `lowercase`, `PascalCase`, `camelCase`, `AsIs`
- Tab size: 1-8 integer
- Max line width: 80-200 or 0 (unlimited)
- Collapse thresholds: positive integers within category-specific ranges
- Profile file extension: must be `.akmlstyle`
