# Contract: Offline-IntelliSense E2E + visual parity audit (US6)

**Spec**: [spec.md](../spec.md) · **Research**: [research.md](../research.md) Decision 6 · **FRs**: FR-025, FR-026, FR-027

## Part A — Offline-IntelliSense E2E (the deferred T113)

Reuses the spec-025 `EngineLaunchFixture` (`IAsyncLifetime`: build engine from source → free port → launch → readiness probe → teardown) and the `[Trait("Category","BridgeE2E")]` opt-in so the default `dotnet test` skips it.

**Location**: `tests/AkmlSql.Web.E2E.Tests/UserStory4Tests.cs` (the name T113 reserved) — Playwright + engine.

**Scenario (maps spec 021 US4 acceptance 1–4 + this spec's SC-008)**:

1. Build engine + web from current source (FR-025; no stale-build false-positive).
2. Launch engine on a free port; pair the browser; select a database.
3. Type SQL → assert live completions resolve; assert the status indicator reads **Live**.
4. Confirm the schema was cached (Settings → Schema cache shows the db).
5. **Kill the engine.** Assert the indicator transitions to **Cached** (not blank/Disconnected-only).
6. Type SQL → assert completions **still resolve** from the cache (offline parity — SC-008).
7. Relaunch the engine (fixture `RelaunchAsync`). Assert the indicator returns to **Live** within the reconnect budget without a re-pair prompt.

**Also exercises the heavyweight online path** (Part B of the refactoring contract / FR-014): with the engine live, drive a Smart Rename preview → apply and assert the rename committed — the first end-to-end coverage of that path.

**Opt-in (FR-026 trait rule)**: `[Trait("Category","BridgeE2E")]`; default `dotnet test` does not run it; `dotnet test --filter Category=BridgeE2E` does.

## Part B — Visual parity audit

A checked-in markdown doc following the spec-024 `M2-THEME-PARITY-AUDIT.md` shape.

**Location**: `specs/027-m5-offline-closure/M5-PARITY-AUDIT.md`.

**Surfaces compared (web vs WPF)**:

1. Snippet picker + expansion (and the management surface vs the WPF `SnippetManagerDialog`).
2. Refactoring menu + preview (vs the WPF `RefactoringPreviewDialog` / lightbulb).
3. Suppression menu (vs the WPF lightbulb suppression action).
4. Cache-aware status indicator (vs the WPF connection/status affordance).

**Required content (FR-026)**: paired screenshots per surface; a deltas table (`surface element | WPF | web | disposition`); list of closed deltas; list of accepted-with-reason deltas; host OS / theme / DPI / font-smoothing metadata (reproducibility, matching the M2 audit's edge-case handling).

**Bar (SC-009)**: ≤ 3 deltas remain open (excluding the deferred multi-tab gap). Top-impact deltas closed in `wwwroot/css/`; the rest filed as named follow-ups.

**Constraint**: developer-side, interactive workstation running both surfaces at the same OS theme (same constraint as the M2 audit).

## DoD closure (FR-027)

After this lands, every M5 PRD §11 DoD checkbox maps to either an already-shipped feature (the spec Overview reality table) or one of FR-001 … FR-026. The two reconciled items are recorded as scoped:

- "All 3 heavyweight refactorings work in browser (with cache)" → closed as **live-engine** (bridge); cached-schema execution is a named follow-up (Decision 3).
- "Inline suppression editing" → closed for **line (cross-surface) + global (browser-local)**; file-scope-per-rule is a named follow-up (Decision 4).
