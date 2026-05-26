# Research: M2 — Web Edition Formatter & Analyser MVP Closure

**Branch**: `024-m2-web-closure` | **Date**: 2026-05-26 | **Spec**: [spec.md](./spec.md)

This document records the five technical decisions the closure spec depends on. There are no `NEEDS CLARIFICATION` items — the closure scope is well-defined (five deferred verification tasks), and every choice below has a single defensible default given the surrounding spec-020/021/023 work already on disk.

---

## Decision 1 — Theme parity audit capture method

**Decision**: Manual paired-screenshot capture via the Windows Snipping Tool (or PowerToys Screen Ruler for pixel-perfect alignment), saved as PNG to `specs/021-web-edition/screenshots/`. No automated screenshot framework.

**Rationale**:

1. The audit's purpose is **side-by-side human visual comparison**, not pixel-diffing. A reviewer needs to look at the WPF rendering and the web rendering and judge whether the deltas matter. Automated screenshot diffing reports pixel deltas that are usually noise from anti-aliasing / sub-pixel positioning.
2. The spec-021 T036 deferral note explicitly says the audit "needs an interactive workstation session that can run the IDE plugin and the web edition side-by-side" — manual capture is what the deferral envisioned.
3. Both surfaces use the same `theme-tokens.json` source per spec 021; meaningful drift is rare and likely to be visible to the eye. Tooling overhead would exceed the value.

**Alternatives considered**:

- **Playwright `page.screenshot()` for the web + a WPF Snoop / Playwright Inspector dump for the IDE**: Cross-surface pixel-diff hits the same anti-aliasing noise problem; tooling cost is several days of harness work for evidence the reviewer would still validate by eye.
- **Visual regression service (Percy, Chromatic, etc.)**: Out of scope for an internal closure; no external SaaS introduced.
- **Single screenshot per theme, web only**: Defeats the parity-audit purpose.

**Consumer**: US1 / FR-001 / FR-002 / FR-004 / FR-005.

---

## Decision 2 — Parity-corpus baseline format

**Decision**: For each script in `tests/format-parity/corpus/*.sql`, generate two siblings: `<script-id>.expected.sql` (the formatted IDE-plugin output) and `<script-id>.expected.json` (the IDE-plugin findings list, sorted by line/column). One pair per format profile; analyser baselines use the default profile only. Baselines live under `tests/format-parity/baselines/<profile>/`.

Each baseline file embeds the IDE-plugin build version as a leading comment / JSON property so the parity test can refuse to compare against a mismatched build (Edge Case "IDE-plugin baseline drift").

**Rationale**:

1. Mirrors the M1 spike's baseline pattern (`spike-corpus/{id}.expected.sql` + `{id}.expected.json` per spec 023 T017) — same shape, same generator pattern, same reviewer mental model.
2. Plain text + JSON enables fast diffing on failure (`git diff` style) without bespoke tooling; failure reports embed the unified diff per FR-008.
3. The build-version stamp gives a deterministic fail signal when the IDE plugin moves ahead of the web edition.

**Alternatives considered**:

- **A single combined JSON manifest per script**: Less greppable; reviewers want to read the formatted SQL directly.
- **MessagePack baselines**: Binary; can't diff in PRs.
- **In-memory baseline generation at test time** (re-run the IDE plugin during the test): Couples the test to a Windows + WPF environment; the existing baseline-on-disk approach decouples test runs from a working IDE-plugin install.

**Consumer**: US2 / US3 / FR-006 / FR-009 / FR-010 / FR-012 / FR-013.

---

## Decision 3 — Parity-baseline generator: opt-in xUnit `[Trait]`

**Decision**: A new `ParityBaselineGenerator` test class under `tests/AkmlSql.Web.Tests/Parity/` carries `[Trait("Category", "ParityBaseline")]`, excluded from default `dotnet test` runs by the existing `--filter` convention. Running `dotnet test ... --filter "Category=ParityBaseline"` regenerates every baseline in place. The generator embeds the IDE-plugin build version into each emitted file.

**Rationale**:

1. Direct reuse of spec 023's `SpikeCorpusGoldenTests.cs` pattern (`[Trait("Category", "SpikeGenerator")]`). Same convention, zero learning cost for reviewers.
2. Opt-in means the standard `dotnet test` run is fast and deterministic; baselines only regenerate when the maintainer explicitly asks.
3. Keeps baseline generation in the test project where the assertion code lives — no separate runnable tool to maintain.

**Alternatives considered**:

- **A separate `tools/` console project**: Splits the regen logic from the assertion logic; reviewers must context-switch between two projects.
- **A shell script that calls the formatter via a dotnet-tool wrapper**: Adds tooling complexity for no gain.
- **Always re-generate at test time**: Couples normal test runs to a Windows + WPF environment; defeats the point of stable baselines.

**Consumer**: US2 / US3 / FR-006 / FR-010.

---

## Decision 4 — Playwright harness builds before launching

**Decision**: The E2E suite uses a single shared `DotnetRunFixture` (xUnit `IAsyncLifetime`) that runs `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Release` first; aborts the test run if the build is dirty; then launches `dotnet run --project src/AkmlSql.Web -c Release --no-build` and waits for the readiness probe before any browser scenario runs. Playwright drives Chromium via `Microsoft.Playwright`'s `IPlaywright.Chromium.LaunchAsync`. Teardown stops the `dotnet run` process and disposes the browser.

**Rationale**:

1. Eliminates the "stale build, false-positive test pass" failure mode called out in the spec's Edge Cases. A reviewer trusts a green CI run only if the same source produced both the built bundle and the test result.
2. xUnit `IAsyncLifetime` is the canonical fixture-scoped setup; reuses one `dotnet run` for all four scenarios — startup cost is paid once.
3. `--no-build` after an explicit `dotnet build` step guarantees Playwright runs against the just-built artefacts, not a cached incremental build.

**Alternatives considered**:

- **Run tests against a pre-deployed URL** (CI Azure Static Web App, Surge, etc.): Adds CI infrastructure for an internal verification spec.
- **Use Playwright's built-in `webServer` config (Node-style)**: Playwright .NET doesn't have the Node `playwright.config.ts` `webServer` field; we'd reimplement it.
- **Skip the build-before-browse**: Re-introduces the stale-build risk the Edge Cases explicitly call out.

**Consumer**: US4 / FR-014 / FR-017.

---

## Decision 5 — Bundle-size measurement on a verified-Brotli host

**Decision**: The bundle audit runs on a Windows 11 host with the full .NET SDK (matching the spec-023 environment — .NET 10 or 11-preview SDK) and the `wasm-tools` workload; the measurement procedure is `dotnet publish src/AkmlSql.Web -c Release` followed by a PowerShell sum of `_framework/*.br` file sizes. The audit document records, alongside the total, the SDK version, the WebAssembly tooling version, and an explicit "Brotli confirmed active" check (the procedure verifies every relevant `.dll`, `.wasm`, `.dat` has a `.br` sibling under `_framework/`).

**Rationale**:

1. Brotli compression is what real users download; uncompressed numbers are meaningless for the M1 budget comparison (FR-019).
2. Verifying every relevant asset has a `.br` sibling is a deterministic procedural check — no measurement-tooling guesswork.
3. The procedure is the same as spec 023's M1 measurement protocol (`contracts/measurement-protocol.md` M1 in spec 023), inheriting an already-reviewed approach.

**Alternatives considered**:

- **Measure uncompressed `_framework/` only**: Inherits the spec-021 placeholder's problem of being incomparable to the M1 compressed target.
- **Compute Brotli compression on the fly with `dotnet-brotli`**: Doesn't reflect what `dotnet publish` actually emits to the user.
- **Measure on Linux / WSL**: Spec-023 already noted the Windows toolchain produces the canonical artefacts; introducing a Linux path adds a confounder.

**Consumer**: US5 / FR-018 / FR-019 / FR-020 / FR-022.

---

## Open follow-ups (out of scope for this spec)

- **Lazy-loading of analysis rule packs** if the bundle audit shows the total over the M1 target: the audit produces the *plan* (FR-021); applying it lands as a follow-up task only triggered by the over-target verdict.
- **CI integration of the Playwright suite**: spec assumes developer-machine execution (Assumptions); CI wiring is a separate concern.
- **Firefox / Safari coverage**: spec 023 §7 explicitly defers; this closure inherits the same scope.
