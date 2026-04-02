# Data Model: Static Code Analysis Engine

**Branch**: `005-static-code-analysis` | **Date**: 2026-03-22

---

## Entities

### AnalysisDiagnostic *(in-process, Engine)*

The result of a single rule firing on a specific span of SQL text. Lives inside the Engine process only — never serialized directly.

| Field | Type | Description |
|-------|------|-------------|
| `RuleId` | `string` | Rule identifier, e.g. `PE001` |
| `CategoryCode` | `string` | Category prefix: `PE`, `BP`, `SE`, `ST`, `DEP`, `DE`, `EX`, `NM` |
| `Severity` | `DiagnosticSeverity` | Error / Warning / Information / Hint |
| `Message` | `string` | Human-readable description |
| `StartOffset` | `int` | Byte offset into document text where violation begins |
| `EndOffset` | `int` | Byte offset where violation ends |
| `Line` | `int` | 1-based line number |
| `Column` | `int` | 1-based column number |
| `FixActions` | `AnalysisFixAction[]` | Zero or more auto-fix options |

---

### AnalysisFixAction *(in-process, Engine)*

A code transformation that resolves a diagnostic.

| Field | Type | Description |
|-------|------|-------------|
| `Label` | `string` | Display text for lightbulb menu (e.g. "Replace with IS NULL") |
| `FixType` | `FixType` | Transform / Insert / Remove / Suppress |
| `ReplacementStart` | `int` | Start offset for the text edit |
| `ReplacementEnd` | `int` | End offset for the text edit |
| `ReplacementText` | `string` | New text to insert at the span |
| `SuppressRuleId` | `string?` | Populated when FixType = Suppress; the rule to suppress |
| `SuppressScope` | `SuppressScope?` | Line / File / Global (when FixType = Suppress) |

---

### AnalysisContext *(in-process, Engine — per analysis run)*

The immutable input given to every rule during a single analysis pass. Constructed once per batch analysis and shared (read-only) across all parallel rule executions.

| Field | Type | Description |
|-------|------|-------------|
| `Script` | `TSqlScript` | Parsed AST for the entire document |
| `CurrentBatch` | `TSqlBatch` | The specific batch being analyzed |
| `Tokens` | `IList<TSqlParserToken>` | Full flat token stream |
| `DocumentText` | `string` | Raw SQL document text |
| `SessionId` | `string` | Session identifier (for schema cache lookup) |
| `SchemaCache` | `DatabaseCache?` | Nullable; populated when connection is active |
| `Settings` | `ResolvedAnalysisSettings` | Merged rule configuration for this document |
| `Suppressions` | `SuppressionMap` | Pre-parsed noqa markers keyed by line number |
| `CancellationToken` | `CancellationToken` | Cancelled when a new analysis supersedes this run |

---

### IAnalysisRule *(interface, Engine)*

The contract all 200+ rules implement.

| Member | Description |
|--------|-------------|
| `string RuleId` | Unique rule identifier (`PE001`, `BP004`, etc.) |
| `string Category` | Display category name |
| `DiagnosticSeverity DefaultSeverity` | Default severity (can be overridden by CAsettings) |
| `bool RequiresSchema` | If true, rule is skipped when `SchemaCache` is null |
| `IEnumerable<AnalysisDiagnostic> Analyze(AnalysisContext ctx)` | Pure function; must not mutate ctx |

---

### CodeIssueInfo *(IPC-serializable, Core)*

The MessagePack-serialized form of `AnalysisDiagnostic` sent from Engine to Shell over the named pipe.

| Field | Type | Key | Description |
|-------|------|-----|-------------|
| `RuleId` | `string` | 0 | Rule identifier |
| `Severity` | `int` | 1 | 0=Hint, 1=Information, 2=Warning, 3=Error |
| `Message` | `string` | 2 | Human-readable message |
| `StartOffset` | `int` | 3 | Start byte offset |
| `EndOffset` | `int` | 4 | End byte offset |
| `Line` | `int` | 5 | 1-based line |
| `Column` | `int` | 6 | 1-based column |
| `FixActions` | `FixActionInfo[]` | 7 | Serialized fix actions |

---

### FixActionInfo *(IPC-serializable, Core)*

Serialized fix action transmitted to the shell.

| Field | Type | Key | Description |
|-------|------|-----|-------------|
| `Label` | `string` | 0 | Menu display text |
| `FixType` | `int` | 1 | 0=Transform, 1=Insert, 2=Remove, 3=Suppress |
| `ReplacementStart` | `int` | 2 | Start offset |
| `ReplacementEnd` | `int` | 3 | End offset |
| `ReplacementText` | `string` | 4 | Replacement text |
| `SuppressRuleId` | `string?` | 5 | Rule to suppress (when FixType=3) |
| `SuppressScopeCode` | `int?` | 6 | 0=Line, 1=File, 2=Global |

---

### CodeAnalysisRequest *(IPC, Core)*

Request sent by the shell to the Engine for real-time analysis.

| Field | Type | Key | Description |
|-------|------|-----|-------------|
| `SessionId` | `string` | 0 | Identifies the session/connection |
| `RequestId` | `string` | 1 | Correlates response to request |
| `DocumentText` | `string` | 2 | Full current document text |
| `DocumentVersion` | `int` | 3 | Monotonically increasing version; Engine discards if stale |

---

### CodeAnalysisResponse *(IPC, Core)*

Response from Engine to Shell.

| Field | Type | Key | Description |
|-------|------|-----|-------------|
| `RequestId` | `string` | 0 | Echoed from request |
| `Issues` | `CodeIssueInfo[]` | 1 | All diagnostics for the document |
| `AnalyzedVersion` | `int` | 2 | DocumentVersion that was analyzed (shell discards if stale) |

---

### CaSettings *(JSON file on disk, Core)*

A named configuration document that controls rule behavior for a project or user.

| Field | Type | Description |
|-------|------|-------------|
| `Metadata.Name` | `string` | Human-readable settings name, e.g. "Team Standard" |
| `Metadata.Version` | `string` | Semantic version of this settings document |
| `Rules` | `Dictionary<string, RuleConfig>` | Per-rule overrides keyed by rule ID |
| `GlobalSuppressions` | `GlobalSuppression[]` | Project-wide suppressions with reasons |

---

### RuleConfig *(in CaSettings)*

| Field | Type | Description |
|-------|------|-------------|
| `Enabled` | `bool` | Whether the rule fires |
| `Severity` | `string` | Override severity: `"error"`, `"warning"`, `"information"`, `"hint"`, `"ignore"` |

---

### GlobalSuppression *(in CaSettings)*

| Field | Type | Description |
|-------|------|-------------|
| `Rule` | `string` | Rule ID to suppress globally |
| `Reason` | `string` | Documentation string for why this suppression exists |

---

### SuppressionMap *(in-process, Engine)*

Pre-computed per-document index of inline suppression comments.

| Field | Type | Description |
|-------|------|-------------|
| `SuppressedLines` | `Dictionary<int, HashSet<string>>` | Line → set of suppressed rule IDs (null set = all rules) |
| `SuppressedBlocks` | `List<(int startLine, int endLine)>` | Line ranges where all rules are suppressed |

---

### ResolvedAnalysisSettings *(in-process, Engine)*

The effective settings after merging defaults + global config + project CAsettings.

| Field | Type | Description |
|-------|------|-------------|
| `Enabled` | `bool` | Master analysis on/off |
| `RunOnType` | `bool` | Trigger on keystrokes |
| `RunOnSave` | `bool` | Trigger on file save |
| `AutoFixOnFormat` | `bool` | Apply safe fixes when Format SQL runs |
| `EffectiveRules` | `Dictionary<string, ResolvedRuleConfig>` | Per-rule effective config after all merges |

---

## State Transitions

### Analysis Lifecycle

```
                      keystroke
                         │
                    [debounce 300ms]
                         │
                   DocumentChanged
                         │
                 ┌───────▼────────┐
                 │  Hash batches  │
                 │ (split on GO)  │
                 └───────┬────────┘
                         │
            ┌────────────▼──────────────┐
            │ For each batch:           │
            │  hash == cached?          │
            │    YES → use cached diags │
            │    NO  → re-analyze       │
            └────────────┬──────────────┘
                         │
              ┌──────────▼──────────┐
              │  Parse batch (AST)  │
              └──────────┬──────────┘
                         │
              ┌──────────▼──────────┐
              │  Parse suppressions │
              └──────────┬──────────┘
                         │
              ┌──────────▼──────────┐
              │  Load CaSettings    │
              │  (cached by dir)    │
              └──────────┬──────────┘
                         │
              ┌──────────▼──────────┐
              │  Run rules parallel │
              │  (up to 8 at once)  │
              │  ct.IsCancelled →   │
              │  abort              │
              └──────────┬──────────┘
                         │
              ┌──────────▼──────────┐
              │  Filter suppressions│
              │  Apply severity cfg │
              └──────────┬──────────┘
                         │
              ┌──────────▼──────────┐
              │  Serialize to IPC   │
              │  CodeAnalysisResponse│
              └──────────┬──────────┘
                         │
              ┌──────────▼──────────┐
              │  Shell: update tags │
              │  + Error List panel │
              └─────────────────────┘
```

### Fix Application Lifecycle

```
cursor on squiggle → lightbulb visible
         │
    user clicks lightbulb
         │
    fix menu appears (per FixActionInfo[])
         │
    ┌────┴──────────────────────────────────────────┐
    │                                               │
    ▼                                               ▼
"Fix this instance"                    "Suppress for this line"
    │                                               │
ITextBuffer.Replace(span, newText)     Insert "-- noqa: RULEID\n"
    │                                  before the violating line
    ▼                                               │
squiggle removed                       SuppressionMap updated on
(re-analysis clears diagnostic)        next analysis run
```
