# Quickstart: M2 Web Edition Closure

**Branch**: `024-m2-web-closure` | **Date**: 2026-05-26 | **Spec**: [spec.md](./spec.md)

How to run each of the five user stories end-to-end. Each section is self-contained: a maintainer can complete US1 without US2–US5, etc.

---

## Prerequisites (one-time setup)

- Windows 11 workstation with the full .NET SDK (`dotnet --version` returns the same SDK that built spec 023 — e.g. `11.0.100-preview.4.26230.115`)
- `wasm-tools` (or `wasm-tools-net10`) workload installed: `dotnet workload list`
- `Microsoft.Playwright` browser binaries: `pwsh tests/AkmlSql.Web.E2E.Tests/bin/Release/net10.0/playwright.ps1 install` after first build
- The WPF IDE plugin built and installed (for US1, US2, US3 baselines): the latest `AKMLSQLSetup.exe` from PR #241 or its successor, with SSMS 22 closed during install
- The spec-020 parity corpus present at `tests/format-parity/corpus/` (already checked in per spec 020)

---

## US1 — Theme parity audit (≈ 45 min)

**Goal**: produce `specs/021-web-edition/M2-THEME-PARITY-AUDIT.md` with 6 paired screenshots, a deltas table, top-5 CSS closures, and a pass verdict.

1. **Boot both surfaces side-by-side.**
    - SSMS 22 open with `tests/format-parity/corpus/03-stored-proc.sql` loaded in a query window.
    - `dotnet run --project src/AkmlSql.Web -c Release` in a separate terminal; open the served URL in Chromium with the same `03-stored-proc.sql` pasted.

2. **Capture the three theme pairs.** For each of Light, Dark, HighContrast (switch via Windows Settings → Personalization → Colors):
    - Win+Left for SSMS, Win+Right for Chromium.
    - Capture **editor region only** (exclude OS title bar) using Snipping Tool.
    - Save as `specs/021-web-edition/screenshots/<theme>-wpf.png` and `<theme>-web.png`.

3. **Diff the pairs.** Open each pair side-by-side; record every visible delta into the audit document's §3 table per the [theme-audit-format.md](./contracts/theme-audit-format.md) contract.

4. **Close the top-5 deltas.** For each closure, edit the appropriate file under `src/AkmlSql.Web/wwwroot/css/`; record the `before`/`after` snippet in §4.

5. **File the rest.** For each delta beyond the top-5, record it in §5 with a name and rationale for deferral.

6. **Verify.** Re-capture the affected pairs to confirm the closures landed. Mark the audit `AUDIT PASSES` in §7.

7. **Flip spec 021 T036.** Change the checkbox from `[ ]` to `[X]` and remove the deferral note.

**Done when**: `M2-THEME-PARITY-AUDIT.md` exists with the full schema; spec 021 T036 is checked.

---

## US2 + US3 — Formatter + Analyser parity (≈ 60 min, runs unattended afterwards)

**Goal**: a `dotnet test` run produces a PASS verdict over `tests/format-parity/corpus/*.sql × 3 profiles` for the formatter and `× 1 default profile` for the analyser.

1. **Generate the desktop baselines (one-time).**
    - In an SSMS 22 session with the AKML SQL extension active, run a "Format Document" + "Run Analysis" pass on each script in `tests/format-parity/corpus/`. (Or, equivalently, invoke the desktop golden generator the same way spec 023 T017 did its corpus.)
    - Write the outputs as `tests/format-parity/baselines/<profile>/<script-id>.expected.sql` and `tests/format-parity/baselines/default/<script-id>.expected.json` per the [parity-baseline-format.md](./contracts/parity-baseline-format.md) contract.
    - Record the baseline revision in `tests/format-parity/baseline-revision.txt`.

2. **Run the opt-in regenerator (later changes).**

   ```powershell
   dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj `
     --filter "Category=ParityBaseline"
   ```

    This regenerates every baseline against the current desktop pipeline. Run it whenever the IDE plugin updates and commit the resulting changes alongside the bump.

3. **Run the parity test.**

   ```powershell
   dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj `
     --filter "FullyQualifiedName~FormatterServiceTests|FullyQualifiedName~AnalyserServiceTests"
   ```

    Expect PASS over every (script × profile) pair. On failure, the test output names the offending pair and embeds a unified diff.

4. **Handle divergences.**
    - **True regression**: fix the formatter / analyser; re-run.
    - **Accepted-with-reason**: add the disposition to `ParityDispositionsRegistry.cs` with a `reasonLink` pointing at a spec-020 tasks.md entry or equivalent. The test re-runs green.

5. **Flip spec 021 T041 and T047.** Both checkboxes from `[ ]` to `[X]`.

**Done when**: the parity test passes on a clean `dotnet test` run; spec 021 T041 + T047 are checked.

---

## US4 — Playwright User Story 1 E2E (≈ 30 min)

**Goal**: a `dotnet test` run of `tests/AkmlSql.Web.E2E.Tests/` exercises four scenarios in a real Chromium browser, all pass, and the headline flow is recorded as ≤ 5 s.

1. **Verify the `data-testid` contract.** The [playwright-harness-contract.md](./contracts/playwright-harness-contract.md) lists seven required `data-testid` attributes. If any is missing in the M2 DOM (`Editor.razor`, `ProblemsListComponent.razor`, `ProfilePickerComponent.razor`), add it as the first task; this is the only `src/` exception this closure permits beyond the US1 CSS edits.

2. **Install Playwright browsers** (one-time):

   ```powershell
   pwsh tests/AkmlSql.Web.E2E.Tests/bin/Release/net10.0/playwright.ps1 install chromium
   ```

3. **Run the suite.**

   ```powershell
   dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj `
     --filter "FullyQualifiedName~UserStory1Tests"
   ```

    The shared `DotnetRunFixture` builds → launches → readiness-probes → drives → tears down. Watch the test output for the four scenario names; all four must pass.

4. **Record the headline flow.** Scenario 1's output line `Headline flow took X.XXs` goes into the M2 PRD's success-metric record. Track the trend over time.

5. **Flip spec 021 T053.** Change the checkbox.

**Done when**: `UserStory1Tests` all green; spec 021 T053 is checked.

---

## US5 — Bundle-size audit (≈ 15 min on a clean workstation)

**Goal**: produce `specs/021-web-edition/M2-BUNDLE-SIZE.md` with the compressed `_framework/*.br` total, host metadata, and a verdict against the M1 target.

1. **Verify the host** per the [bundle-measurement-protocol.md](./contracts/bundle-measurement-protocol.md) Step 1.

2. **Publish.**

   ```powershell
   dotnet publish src/AkmlSql.Web/AkmlSql.Web.csproj -c Release -nologo
   ```

   Exit code must be 0.

3. **Verify Brotli.**

   ```powershell
   $framework = 'src/AkmlSql.Web/bin/Release/net10.0/publish/wwwroot/_framework'
   $missing = Get-ChildItem $framework -Recurse -Include *.dll, *.wasm, *.dat, *.js, *.pdb |
       Where-Object { -not (Test-Path "$($_.FullName).br") }
   if ($missing) { throw "Brotli sibling missing for: $($missing -join ', ')" }
   ```

   No exception → record `Brotli confirmed active: yes` in the audit.

4. **Sum the compressed total.**

   ```powershell
   $total = (Get-ChildItem $framework -Recurse -Filter *.br | Measure-Object -Property Length -Sum).Sum
   '{0:N2} MB' -f ($total / 1MB)
   ```

5. **Write the audit.** Fill every section of `M2-BUNDLE-SIZE.md` per the bundle-measurement-protocol.md contract:
    - Header (date, capturer, commit, build version)
    - §1 host environment
    - §2 publish command + exit code
    - §3 per-asset breakdown (sorted descending, top-5 called out)
    - §4 compressed total
    - §5 verdict (`WITHIN_TARGET` with headroom, OR `OVER_TARGET` with applied lazy-loading plan)

6. **If `OVER_TARGET`, apply the lazy-loading plan first** before committing the audit — the committed verdict must reflect a state that's within target.

7. **Flip spec 021 T054.** Change the checkbox.

**Done when**: `M2-BUNDLE-SIZE.md` exists with the full schema and a green verdict; spec 021 T054 is checked.

---

## US6 — File-I/O UI affordances (≈ 90 min)

**Goal**: wire the M2 PRD §5 feature-scope rows (`Open .sql`, `Import .akmlstyle / .sqlpromptstylev2`, `Export current profile`) into the UI so a Chromium user can click them. No DevTools, no `/spike` page.

1. **Land the service-layer extensions.**
    - Edit `src/AkmlSql.Web/Services/IProfileStore.cs`: add `ImportFromStreamAsync(filename, content)` + `ExportAsync(id, format)` to the interface; implement on `ProfileStore`; add `enum ProfileExportFormat { AkmlStyle, SqlPromptStyleV2 }`. Verify `src/AkmlSql.Web/AkmlSql.Web.csproj` references `AkmlSql.Analysis` and `AkmlSql.Formatting`.

2. **Wire the Open button into the editor.**
    - Edit `src/AkmlSql.Web/Pages/Editor.razor`: add `<InputFile accept=".sql" OnChange="OnOpenFileAsync" data-testid="open-file" />` to the toolbar; handler reads via `e.File.OpenReadStream(DocumentSizeLimit.MaxDocumentBytes)`, calls `_editor.SetTextAsync`, resets findings, sets `_status`.

3. **Wire Import + Export into the profile picker.**
    - Edit `src/AkmlSql.Web/Shared/ProfilePickerComponent.razor`: add `<InputFile accept=".akmlstyle,.sqlpromptstylev2" data-testid="import-profile" />` + `<button data-testid="export-profile">Export`. Import calls `ProfileStore.ImportFromStreamAsync`. Export against a built-in profile renders the button `disabled`; against a user profile, opens an inline format-chooser and calls `JS.InvokeVoidAsync("akmlDownload.saveFile", filename, bytes)`.
    - Verify `wwwroot/js/akml-download.js` exposes `akmlDownload.saveFile(filename, bytes)`; if not, add the Blob+download helper per the contract.

4. **Add the bUnit tests.**
    - `tests/AkmlSql.Web.Tests/Services/ProfileStoreImportExportTests.cs` — `.akmlstyle` round-trip, `.sqlpromptstylev2` import, export against built-in vs user, malformed-content rejection.
    - `tests/AkmlSql.Web.Tests/Pages/EditorOpenFileTests.cs` — happy path + oversize-file rejection.
    - `tests/AkmlSql.Web.Tests/Shared/ProfilePickerImportExportTests.cs` — import surfaces in matching option-group; export blocked for built-ins; export against user invokes `akmlDownload.saveFile`.

5. **Run the new tests + full suite.**

   ```powershell
   dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj `
     --filter "FullyQualifiedName~ProfileStoreImportExportTests|FullyQualifiedName~EditorOpenFileTests|FullyQualifiedName~ProfilePickerImportExportTests"
   ```

    Expect green. Then run `dotnet test` without filter to confirm no regression in the existing 51 parity tests.

6. **Manual click-through.**
    - `dotnet run --project src/AkmlSql.Web -c Release`; open Chromium.
    - Click Open → pick `tests/format-parity/corpus/03-stored-proc.sql` → confirm the editor replaces.
    - Click profile-picker Import → pick a known `.akmlstyle` from `%AppData%/AKML SQL/profiles/` → confirm it appears under **User**.
    - Click Import → pick a `.sqlpromptstylev2` exported from SQL Prompt → confirm it appears under **SQL Prompt**.
    - Select the imported user profile → click Export → choose `.akmlstyle` → confirm a download is offered.
    - Switch the active profile back to `builtin.default` → confirm Export is `disabled`.

7. **Update quickstart.md user-facing usage section.** Per tasks.md T057, add the three bulleted entries to `doc/WEB/quickstart-m2.md`.

**Done when**: the three new bUnit test classes pass; manual click-through completes all five steps; `doc/WEB/quickstart-m2.md` documents the new affordances. No spec-021 task flip — US6 closes M2 PRD §5 feature-scope rows, not a tracked spec-021 task.

---

## Closure verification (end-to-end)

After all six stories are done, verify the closure is complete:

```powershell
# 1. All five deferred tasks in spec 021 Phase 3 are now [X]
grep -c "^- \[ \] T036\|^- \[ \] T041\|^- \[ \] T047\|^- \[ \] T053\|^- \[ \] T054" specs/021-web-edition/tasks.md
# Expected output: 0

# 2. The four audit / verification artefacts exist
test -f specs/021-web-edition/M2-THEME-PARITY-AUDIT.md
test -f specs/021-web-edition/M2-BUNDLE-SIZE.md
test -d tests/format-parity/baselines
test -f tests/AkmlSql.Web.E2E.Tests/UserStory1Tests.cs

# 3. The three US6 test classes exist
test -f tests/AkmlSql.Web.Tests/Services/ProfileStoreImportExportTests.cs
test -f tests/AkmlSql.Web.Tests/Pages/EditorOpenFileTests.cs
test -f tests/AkmlSql.Web.Tests/Shared/ProfilePickerImportExportTests.cs

# 4. The US6 UI affordances have the expected data-testid attributes
grep -q 'data-testid="open-file"' src/AkmlSql.Web/Pages/Editor.razor
grep -q 'data-testid="import-profile"' src/AkmlSql.Web/Shared/ProfilePickerComponent.razor
grep -q 'data-testid="export-profile"' src/AkmlSql.Web/Shared/ProfilePickerComponent.razor

# 5. The standard test run is green across verification + US6
dotnet test --filter "FullyQualifiedName~FormatterServiceTests|FullyQualifiedName~AnalyserServiceTests|FullyQualifiedName~UserStory1Tests|FullyQualifiedName~ProfileStoreImportExportTests|FullyQualifiedName~EditorOpenFileTests|FullyQualifiedName~ProfilePickerImportExportTests"
```

When all five checks pass, the M2 PRD's Definition of Done has recorded evidence behind every checkbox (SC-007) **and** every PRD §5 feature-scope row marked **Yes** has a clickable UI affordance behind it (SC-009). Commit, push, open a PR; merge closes the M2 milestone.
