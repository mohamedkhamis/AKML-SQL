# Data Model: M2 — Web Edition Formatter & Analyser MVP Closure

**Branch**: `024-m2-web-closure` | **Date**: 2026-05-26 | **Spec**: [spec.md](./spec.md)

Five entities. None are runtime persistence — these are the shapes of the five artefacts the closure produces.

---

## Entity 1 — Theme parity audit document (`M2-THEME-PARITY-AUDIT.md`)

The single audit document replacing the existing placeholder at `specs/021-web-edition/M2-THEME-PARITY-AUDIT.md`.

| Section | Content | Required |
|---------|---------|----------|
| Header | Date, capturing maintainer, master commit hash, IDE plugin build version, web edition build version | Yes |
| Host environment | OS version, DPI scaling factor, font-smoothing setting (ClearType on/off), monitor model + resolution | Yes |
| Theme matrix | 3 × 2 paired screenshot table (Light/Dark/HighContrast × WPF/Web), each cell links into `specs/021-web-edition/screenshots/` | Yes |
| Deltas table | One row per visible delta: `Surface element`, `IDE rendering`, `Web rendering`, `Disposition` (Closed / Accepted-with-reason / Filed as follow-up) | Yes — even if empty (with explicit "no deltas observed" note) |
| Closed deltas | List of CSS edits applied to `src/AkmlSql.Web/wwwroot/css/` (file, before/after snippets) — top five only | Conditional on deltas existing |
| Filed follow-ups | Remaining deltas with names and rationale for deferral | Conditional on > 5 deltas |
| Procedure | Step-by-step reproduction notes so a second reviewer can re-capture | Yes |

**Validation rules**:

- The screenshot matrix is **complete** (no empty cells) or the audit is invalid.
- The deltas table sums of dispositions equal the count of deltas; orphan rows reject the audit.
- The host environment block is **non-empty** so the audit is reproducible.

---

## Entity 2 — Parity corpus item

Reused from `tests/format-parity/` (spec-020 corpus). For the closure, each item is paired with one or more baselines under `tests/format-parity/baselines/<profile>/`.

| Field | Type | Notes |
|-------|------|-------|
| `id` | string | Stable identifier (e.g. `01-select`, `02-batch`, …); matches the `.sql` filename minus extension |
| `sqlPath` | path | Relative to `tests/format-parity/corpus/` |
| `construct` | enum | `SELECT`, `Batch`, `StoredProcedure`, `CTE`, `WindowFunction`, `MERGE`, `DDL`, `CommentHeavy`, etc. |
| `baselines.<profile>` | `BaselineFile` | One per supported profile (default / compact / expanded for FR-007) |

Each `BaselineFile` carries an baseline-revision stamp (Decision 2 / Edge Case "Baseline-revision drift") and:

- For formatter baselines (`*.expected.sql`): the formatted SQL, byte-exact.
- For analyser baselines (`*.expected.json`): a JSON array of findings sorted by `(line, column, ruleId)`. Each finding has `RuleId`, `Severity`, `Message`, `Line`, `Column`.

---

## Entity 3 — Parity test record

Per `(corpus-item × profile)` pair, the in-memory result of one parity test run. Not persisted between runs; constructed at test time by `ParityCorpusLoader`, consumed by the assertion code, summarised in xUnit output on failure.

| Field | Type | Notes |
|-------|------|-------|
| `corpusItemId` | string | From Entity 2 |
| `profileId` | string | `default` / `compact` / `expanded` |
| `webOutput` | string \| `Findings[]` | What the web edition produced for this pair |
| `baselineOutput` | string \| `Findings[]` | What `<baseline>.expected.*` carries |
| `diff` | `UnifiedDiff?` | Null if identical |
| `disposition` | enum | `MATCH`, `RESOLVED` (matches after fix-up), or `ACCEPTED_WITH_REASON` |
| `reasonLink` | `string?` | If `ACCEPTED_WITH_REASON`, points to a spec-020 tasks.md entry or equivalent (FR-008) |

**Validation rules**:

- A test failure is produced for any record with a non-null `diff` and disposition ≠ `ACCEPTED_WITH_REASON`.
- `ACCEPTED_WITH_REASON` requires a non-null `reasonLink`; otherwise the record is treated as a failure (no silent acceptance).

---

## Entity 4 — Browser test scenario

One of the four M2 PRD User Story 1 acceptance scenarios, encoded as a Playwright test method under `tests/AkmlSql.Web.E2E.Tests/UserStory1Tests.cs`.

| Field | Source | Notes |
|-------|--------|-------|
| `name` | xUnit test method name | e.g. `PasteAndFormat_100LineProc_FormatsAndAnalyses_Under5Seconds` |
| `scenarioRef` | spec/021/spec.md US1 scenario number | 1–4 |
| `preconditions` | Code in the `[Fact]` body | E.g. "engine process not running" — preconditions are asserted before driving the browser |
| `actions` | Playwright API calls | `page.Locator(...).FillAsync(...)`, `page.Keyboard.PressAsync("Control+K"); page.Keyboard.PressAsync("Control+F")`, etc. |
| `assertions` | `Assert.*` calls | Includes the `HeadlineFlowTimer` assertion against the 5-second ceiling for the headline flow |

The four scenarios are independent (each launches a fresh browser context via the shared `DotnetRunFixture`); they share the underlying `dotnet run` process to amortise startup cost.

---

## Entity 5 — Bundle-size audit document (`M2-BUNDLE-SIZE.md`)

The single audit document replacing the existing placeholder at `specs/021-web-edition/M2-BUNDLE-SIZE.md`.

| Section | Content | Required |
|---------|---------|----------|
| Header | Date, capturing maintainer, master commit hash, web edition build version | Yes |
| Host environment | Windows version, .NET SDK version, `wasm-tools` workload version, "Brotli confirmed active" line | Yes |
| Publish command | The exact `dotnet publish` command run, including any `-p:` overrides | Yes |
| Per-asset breakdown | Sortable table of `_framework/*.br` files with file name + size (KB / MB) | Yes |
| Compressed total | Single number in MB, summed from the breakdown | Yes |
| Verdict | `WITHIN_TARGET` / `OVER_TARGET` with the M1 target number cited | Yes |
| Headroom | If `WITHIN_TARGET`, remaining MB before hitting the M1 target | Yes |
| Lazy-loading plan | If `OVER_TARGET`, which asset(s) move to lazy-load and why; applied before the audit is committed | Conditional on `OVER_TARGET` |
| Next checkpoint | Trigger that requires re-measurement (e.g. "M3 must re-measure before merge") | Yes |

**Validation rules**:

- The audit is invalid if "Brotli confirmed active" cannot be asserted (every relevant `.dll` / `.wasm` / `.dat` under `_framework/` must have a sibling `.br` file).
- `WITHIN_TARGET` requires a numeric headroom; `OVER_TARGET` requires a non-empty lazy-loading plan that has been applied to `src/AkmlSql.Web/`.
