# Contract: Ghost Text — inline grey-text completion (US5)

**Spec**: [spec.md](../spec.md) · **Research**: [research.md](../research.md) Decision 5 · **FRs**: FR-022 … FR-029

## Editor layer (hand-rolled CM6, no new package)

In `wwwroot/js/akml-editor.js`, using **already-loaded** bundle modules:

- `StateField<DecorationSet>` holding the current suggestion; cleared on any `tr.docChanged`. `provide: f => EditorView.decorations.from(f)`.
- `WidgetType` rendering a grey/italic inline `<span>` (`pointer-events:none`, `white-space:pre`) via `Decoration.widget({ widget, side: 1 })` at the caret.
- `StateEffect` to set/clear the suggestion.
- `Prec.highest(keymap.of([{key:"Tab", run: acceptGhost}, {key:"Escape", run: dismissGhost}]))`.
- New exported functions: `setGhostText(hostId, text)`, `clearGhostText(hostId)`, `triggerGhostText(hostId)` (manual, `Ctrl+Alt+Up` parity).
- A **debounced** (`GhostTextDelayMs`, default 350 ms) change hook → suppression checks → `dotNetRef.invokeMethodAsync('RequestGhostTextFromJs', pos, docText)`; on resolve, **staleness check** (`view.state.selection.main.head === pos`) before `setGhostText`. An incrementing request-id drops out-of-order responses.

## Triggers / suppression (FR-022, FR-023)

- Trigger: cursor at end-of-line OR after a keyword, after debounce.
- Suppress: `syntaxTree(state).resolveInner(pos,-1)` (or ancestor) is `LineComment`/`BlockComment`/`String`/`QuotedIdentifier`; empty line; `completionStatus(state) !== null` (autocomplete open); active snippet.

## Accept / dismiss / Tab precedence (FR-024)

- Tab commits the suggestion as a single edit (`dispatch({changes, selection})`); Escape or continued typing dismisses.
- The Tab handler returns `false` (falls through to autocomplete-accept / snippet-tab-stop / `indentWithTab`) **unless** ghost text is active AND `completionStatus===null` AND no active snippet — so the existing Tab behaviours are unchanged.

## C# service (FR-025 … FR-029) — direct-to-provider

New `IAiGhostTextService.CompleteAsync(string schemaText, string precedingText, CancellationToken ct)` → `GhostTextPrompt.Build(schemaText, precedingText)` → `IAiClientFactory.SendAsync(activeProvider, new AiChatRequest{ SystemPrompt, UserPrompt, MaxTokens=150, Temperature=0.2 }, ct)` → strip code fences + `TrimEnd()`. (Engine path unavailable — keys are browser-side.) `EditorComponent.RequestGhostTextFromJs(cursorOffset, documentText)`:

- gate on `IAiFeatureSettings.GhostTextEnabled` (default **false**, opt-in — FR-027) and an active provider; return `null` on any failure (mirrors `RequestCompletionsFromJs` discipline).
- `precedingText` = last ~500 chars before the cursor; `schemaText` from `IAiSchemaContextProvider.GetSchemaTextAsync("ghosttext", ct)` (minimal slice — FR-029).
- **Cache** keyed by prompt+prefix (FR-025); identical request reuses cache (≥30 % hit, SC-006).
- **Cancellation**: a new keystroke cancels the in-flight request (FR-026).
- **Rate limit**: ≤ `GhostTextMaxRequestsPer3s` (default 1/3 s, configurable — FR-027).
- **Token counter**: per-session usage counter incremented per request, surfaced in the UI (FR-028).

## Test contract

- `tests/AkmlSql.Web.Tests/Ai/GhostTextControllerTests.cs` — debounce coalescing, suppression decisions (comment/string/empty/popup/snippet), cache hit on repeat prompt+prefix, rate-limit throttling, cancellation of in-flight on new keystroke, opt-in gate. (The CM6 decorator/keymap rendering is verified in the US7 interactive E2E.)

## Out of scope

- Routing ghost text through the engine (keys are browser-side).
