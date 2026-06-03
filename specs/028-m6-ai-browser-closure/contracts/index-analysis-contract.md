# Contract: Index Analysis — the fifth AI action (US4)

**Spec**: [spec.md](../spec.md) · **Research**: [research.md](../research.md) Decision 4 · **FRs**: FR-019 … FR-021

## Prompt service (FR-019)

Add to `IAiPromptService`:

```csharp
Task<string> IndexAnalysisAsync(string schemaText, string selectedSql, string? executionPlanXml, CancellationToken ct);
```

Implementation builds the prompt from the existing `AkmlSql.AI` `IndexAnalysisPrompt.Build(schemaText, selectedSql, executionPlanXml)` (namespace `AkmlSql.Engine.Ai.Prompts`) and funnels through the same `CallAsync(system, user, ct)` path as the other four actions (active provider via `IAiPreference`, fetch via `IAiClientFactory`). The browser has no execution plan offline ⇒ pass `executionPlanXml: null` (the prompt degrades to schema + SQL, as the engine does for a missing plan). Streaming overload mirrors the other actions (streaming-contract).

## Panel action (FR-020)

`AiPanel.razor` gains a fifth button (`Index Analysis`) beside Explain / Fix / Optimize / NL→SQL, rendering the returned `CREATE INDEX` statements (+ rationale) in the result pane with Accept (insert/copy) / Discard, consistent with the other actions.

## Privacy (FR-021)

`schemaText` comes from `IAiSchemaContextProvider.GetSchemaTextAsync("indexanalysis", ct)`, so Index Analysis honours its resolved privacy mode (e.g. `NoSchema` ⇒ only the selected query is sent).

## Test contract

- `AiPanelTests.cs` (US7 bUnit) covers the fifth action's wiring (button → service → result render → Accept).
- `PrivacyModeTests.cs` includes `indexanalysis` in the per-mode disclosure assertions.

## Out of scope

- Execution-plan capture in the browser (no plan offline; `null` is passed).
