# IPC Contract: `FormatPreview` usage for live preview (EXTENDED — usage only)

**Feature**: `020-sqlprompt-visual-parity`
**Phase**: 1 — Design
**Status**: Design — no wire-format change; documents the editor-side usage pattern that meets SC-009

The Format Styles editor's live preview pane re-formats a sample SQL document whenever any setting changes. We reuse the existing `FormatPreview (12)` / `FormatPreviewResult (112)` IPC, but the usage pattern (debounce, supersession, sample selection) is new and must be specified so SC-009 (≤ 250 ms p95) is reliably met.

---

## Message numbers (unchanged)

| Direction | Type | Constant | Value |
|---|---|---|---|
| Shell → Engine | Request | `MessageTypes.FormatPreview` | **12** |
| Engine → Shell | Response | `MessageTypes.FormatPreviewResult` | **112** |

No wire change.

---

## Sample SQL document

The editor ships with a built-in 200-line sample (`SamplePreview.sql`, embedded resource) covering:

- A `SELECT` with multiple joins, CTE, CASE expressions, subqueries
- A `INSERT … SELECT` with column list
- A `CREATE PROCEDURE` with parameters, control flow, variables
- A `MERGE` statement
- A few comments and `GO` separators

This sample exercises every setting group in `FormatSettingSchema` so any one setting change produces a visible difference.

The user can paste their own SQL into the preview pane; the pasted SQL becomes the new sample for the session and is preserved in `%AppData%\AKML SQL\editor\preview-sample.sql` for next session.

---

## Debounce + supersession protocol

```text
On setting change in FormatStylesEditorViewModel:
  1. Compose a candidate FormatPreviewRequest (in-memory profile + current sample SQL).
  2. If a debounce timer is pending, reset it.
  3. Otherwise start a 100 ms one-shot timer.
  4. On timer fire:
     a. Increment a local `previewSequence` counter.
     b. Send FormatPreview with RequestId = previewSequence.
     c. Remember `inFlightSequence = previewSequence`.

On FormatPreviewResult arrival:
  1. If response.RequestId < inFlightSequence: discard (superseded).
  2. Else if response.RequestId == inFlightSequence: render into preview pane.
```

100 ms is below the human input cadence median; rapid setting changes (e.g. holding down a number-spinner arrow) coalesce into single preview refreshes.

---

## Request payload

`FormatPreviewRequest` (existing shape; the in-memory profile is the only edit):

```text
FormatPreviewRequest {
  Sql       : string   // the sample SQL
  Profile   : FormatProfile   // the in-memory edited profile — NOT the persisted file
  RequestId : int
}
```

The profile is sent in full each time (≈ 5 KB). MessagePack-serialised. No diffing — simplifies the protocol and the engine path; well under the 16 MB frame cap.

---

## Response payload

```text
FormatPreviewResult {
  RequestId       : int
  FormattedSql    : string?
  ValidationError : string?  // if FormatterPipeline stage 6 (SemanticValidator) failed
  ElapsedMs       : int      // engine-side time (excludes IPC)
}
```

If `ValidationError` is non-null, the preview pane renders the original SQL (unchanged) with an inline warning bar saying "Preview unavailable — the current settings produce semantically-different SQL". This matches the existing pipeline behaviour ("Stage 6 failure → return original SQL unchanged").

---

## Latency budget

| Stage | Budget | Source |
|---|---|---|
| UI debounce | 100 ms | This contract |
| Shell → Engine IPC | ≤ 30 ms p95 | Existing measurements |
| Engine pipeline (200-line sample) | ≤ 80 ms p95 | Existing FormatPreview measurements (warm engine) |
| Engine → Shell IPC | ≤ 30 ms p95 | Existing |
| WPF render | ≤ 40 ms p95 | Allocate generously |
| **Total p95** | **≤ 280 ms** (target 250 ms) | sum |

Slack at the rendering stage means SC-009 is achievable; cold-start is the only failure mode and the editor warms the engine on open with a no-op preview.

---

## Cancellation

No explicit cancellation token sent over IPC. Supersession by request-id is sufficient — the engine will finish the in-flight request (tens of ms), the shell will discard the late response, no resource leak. This keeps the engine code unchanged.

---

## Test coverage

| Test | What it validates |
|---|---|
| `LivePreviewDebounceTests.RapidChanges_Coalesce` | 20 setting changes in 50 ms produce ≤ 2 IPC requests |
| `LivePreviewDebounceTests.LateResponse_Discarded` | Inject delay; rapid second change; first response arrives after second is sent; first is discarded |
| `LivePreviewDebounceTests.SemanticValidationFailure_RendersOriginal` | Force stage-6 failure; preview pane shows original SQL + warning bar |
| `FormatPreviewBenchTests.LatencyUnder250Ms_P95` | 100 iterations on the 200-line sample; p95 < 250 ms end-to-end on dev hardware |
| `FormatPreviewBenchTests.WarmStart_AfterEditorOpen` | First preview after editor opens is within budget (warm-on-open behaviour) |

---

## Out of scope for this contract

- The setting controls themselves (`SettingControlsPanel`) — UI code.
- The schema descriptor used to build the editor — `ipc-style-editor-schema.md`.
- The `.sqlpromptstyle` round-trip — `ipc-profile-import-sqlprompt.md`.
