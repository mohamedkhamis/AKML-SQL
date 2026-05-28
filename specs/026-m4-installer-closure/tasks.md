---
description: "Tasks for M4 — Installer (IIS Deployment Option) Closure"
---

# Tasks: M4 — Installer (IIS Deployment Option) Closure

**Input**: Design documents from `/specs/026-m4-installer-closure/`
**Prerequisites**: plan.md, spec.md (6 user stories), research.md (6 decisions), data-model.md (E1–E6), contracts/ (5 contracts), quickstart.md

**Tests**: Included. The spec makes tests part of the deliverable — FR-013e requires the `EngineHostTests` auth-composition matrix, and US5 (FR-027..FR-032) *is* the installer-smoke test suite. They are not optional.

**Organization**: Tasks are grouped by user story (priority order). Phase 2 (Foundational) holds the installer integration wiring — the literal precondition for every installer-touching story (US1 happy path, US3 dialog, US4 silent flags, US5 smoke tests). US2's engine-side auth wiring is independent of the installer and can run in parallel with US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: different files, no dependency on incomplete tasks in the same phase
- **[Story]**: US1–US6 (only in user-story phases); Setup / Foundational / Polish have no story label
- Paths are repository-relative

## Path conventions

- Installer scripts under `src/AkmlSql.Installer/`
- Engine code under `src/AkmlSql.Engine/`; engine tests under `tests/AkmlSql.Engine.Tests/`
- New installer-smoke tests under `tests/AkmlSql.Installer.Tests/`
- Docs under `doc/`; this feature's run record under `specs/026-m4-installer-closure/`

---

## Phase 1: Setup (shared build prerequisites)

**Purpose**: Produce the artefacts the installer bundles and confirm the toolchain. The Web bundle MUST exist before ISCC compiles the post-`#include` installer (the `[Files]` wildcard errors if the publish output is absent).

- [ ] T001 [P] Publish the Web bundle: `dotnet publish src/AkmlSql.Web -c Release`; confirm `src/AkmlSql.Web/bin/Release/net10.0/publish/wwwroot/_framework/` exists (so the `[Files]` copy in `web-installer.iss` has content)
- [ ] T002 [P] Publish the engine: `dotnet publish src/AkmlSql.Engine -c Release -r win-x64`; confirm `AkmlSql.Engine.exe`
- [ ] T003 [P] Baseline compile the CURRENT installer: `& "C:\Program Files\Inno Setup 7\ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss`; confirm `Output/AKMLSQLSetup.exe` is produced (pre-change baseline — the current `.iss` does NOT reference the web bundle, so this works without T001)

**Checkpoint**: build toolchain confirmed; bundle + engine published.

---

## Phase 2: Foundational (installer integration wiring)

**Purpose**: Wire `web-installer.iss` into the shipping installer. This is the dominant M4 gap (FR-001, FR-002) and the **blocking prerequisite** for every installer-touching story — without it no Web wizard page appears, so US3's dialog, US4's silent flags, and US5's smoke tests have nothing to act on.

**⚠️ CRITICAL**: blocks US1 (beyond engine-side), US3, US4, US5, US6.

- [ ] T004 Add `#include "web-installer.iss"` at the end of the `[Code]` section in `src/AkmlSql.Installer/AkmlSqlSetup.iss` (FR-001; see `contracts/installer-integration-contract.md` C1)
- [ ] T005 Add the four hook calls inside the existing event procedures in `src/AkmlSql.Installer/AkmlSqlSetup.iss`: `Web_Init();` in `InitializeWizard` (@345), `if not Web_NextButton(CurPageID) then begin Result := False; Exit; end;` in `NextButtonClick` (@469), `if CurStep = ssPostInstall then Web_PostInstall();` in `CurStepChanged` (@579), `if CurUninstallStep = usUninstall then Web_Uninstall();` in `CurUninstallStepChanged` (@674) (FR-002). Reserve the `InitializeSetup` (@403) body for the US4 silent-flag parse.
- [ ] T006 Create the new top-level `function ShouldSkipPage(PageID: Integer): Boolean; begin Result := Web_Skip(PageID); end;` in `src/AkmlSql.Installer/AkmlSqlSetup.iss` — it does NOT exist today, so `Web_Skip` currently has no caller (FR-002; contract C1)
- [ ] T007 Compile gate: run `ISCC.exe src/AkmlSql.Installer/AkmlSqlSetup.iss` clean (zero errors) with the include + hooks present; launch the wizard and confirm a "Web edition" component now appears (depends on T001 for the `[Files]` bundle)

**Checkpoint**: the installer offers the Web component; the web wizard pages can render.

---

## Phase 3: User Story 1 - Install the Web edition end-to-end on a localhost-only host (Priority: P1) 🎯 MVP

**Goal**: A localhost-only Web-edition install completes from the wizard and the editor loads at `http://localhost/`.

**Independent Test**: On a Windows 11 host with IIS, run the compiled installer, tick Web edition with defaults (IIS 80, bridge 47291, localhost), finish, click the success URL → editor renders and formats SQL. ≤ 90 s (SC-001).

- [ ] T008 [US1] Split the single `WebPortPage` into `WebIisPortPage` (default `80`) and `WebBridgePortPage` (default `47291`) in `src/AkmlSql.Installer/web-installer.iss` (FR-003; contract `installer-integration-contract.md` C2)
- [ ] T009 [US1] In `Web_NextButton` (`src/AkmlSql.Installer/web-installer.iss`): validate each port's range (IIS = 80 or 1024..65535; bridge = 1024..65535) and reject `IisPort == BridgePort` with a clear `MsgBox` (FR-003)
- [ ] T009a [US1] In `Web_NextButton` on the Bridge Port page (`src/AkmlSql.Installer/web-installer.iss`): probe whether the chosen bridge port is in use (`Exec` a `Test-NetConnection -ComputerName 127.0.0.1 -Port <BridgePort>` check, or equivalent) and, if so, show a **non-blocking** dismissable warning `MsgBox` (proceed or go back) — never hard-block; degrade to no-warning when PowerShell is unavailable (FR-003a; closes spec 021 T083)
- [ ] T010 [US1] Split `GetWebPort` into `GetIisPort` / `GetBridgePort` and update the `[Run]` lines so `web-iis-setup.ps1` receives the IIS port while `web-config-bridge.ps1`, `web-tls-setup.ps1`, `web-firewall.ps1` receive the bridge port, in `src/AkmlSql.Installer/web-installer.iss` (FR-004)
- [ ] T011 [US1] Build the success-page `URL:` line in `Web_PostInstall`: `http://localhost[:IisPort]/` (omit `:80`) for localhost, `http://<hostname>[:IisPort]/` for LAN (the IIS bundle is HTTP in both modes; only the bridge is `wss`/TLS), in `src/AkmlSql.Installer/web-installer.iss` (FR-005; contract C4)
- [ ] T012 [US1] In `Web_Skip` (`src/AkmlSql.Installer/web-installer.iss`): return true for all four web pages (Hosting / Network / IIS Port / Bridge Port) when the `web` component is unticked, and early-return from `Web_PostInstall` in that case (FR-006)
- [ ] T013 [US1] Confirm the `[Files]` entries land the bundle at `{app}\Web\` (recursive, `_framework\` intact) and the engine at `{app}\Engine\` in `src/AkmlSql.Installer/web-installer.iss` (FR-007)
- [ ] T013a [US1] In `Web_PostInstall` (`src/AkmlSql.Installer/web-installer.iss`): after the `[Run]` `sc.exe start`, poll `Get-Service`/`sc query AkmlSqlWebEngine` for `Running` (≤ 10 s); if not Running, append a "service did not start — see Windows Event Log + `%CommonAppData%\AKML SQL Web\install.log`" line to the success page + `INSTALL-SUMMARY.txt`. MUST NOT fail the install; applies in both localhost and LAN modes (FR-007a)
- [ ] T014 [US1] Recompile with `ISCC.exe` and manually verify a localhost install: editor loads at `http://localhost/`, `Ctrl+K Ctrl+F` formats, no console errors (SC-001)

**Checkpoint**: localhost Web-edition install is demoable end-to-end (the MVP).

---

## Phase 4: User Story 2 - Enforced LAN pairing: the engine validates the PIN and a second machine pairs (Priority: P1)

**Goal**: In LAN mode the engine refuses a wrong PIN, mints a bearer on the right PIN, and the install summary shows a usable PIN. The security boundary, not just a printed number.

**Independent Test**: LAN install on Machine A shows a 6-digit PIN in the summary; from Machine B a wrong PIN → `PinInvalid`, the right PIN → `Open` within 10 s and a bearer that survives engine restart (SC-003, SC-010).

> Auth first (FR-013a..e), then PIN persistence (FR-008..013) — a persisted PIN is cosmetic until the handshake enforces it. Engine-side tasks (T015–T020) are independent of the installer and may run in parallel with US1.

- [X] T015 [US2] Carry the transport-observed remote endpoint (address only, port stripped) from `HttpListenerContext.Request.RemoteEndPoint` to the handshake `pinValidator` via an `AsyncLocal<System.Net.IPAddress?>` set by `WebSocketTransport` at the top of each connection's frame-handling flow (before the handshake frame is dispatched), in `src/AkmlSql.Engine/Transports/WebSocketTransport.cs`. Do NOT use `RpcContext` (confirmed a per-process shared singleton — would race across connections) and do NOT use a constant or a client-supplied value (FR-013a; contract `lan-auth-composition-contract.md` C1)
- [X] T016 [US2] In `src/AkmlSql.Engine/EngineHost.cs`: when `BridgeOptions.IsLoopback == false`, construct `new PairingService()` + `new BearerTokenStore(bridge.TokenStorePath, TimeSpan.FromDays(bridge.TokenTtlDays))` and build a `HandshakeHandler` via its full constructor (`pairingRequired: () => true`, `pinValidator` → `PairingService.ValidatePin(sourceIp, pin) == Valid`, `bearerValidator`/`bearerMinter` → `BearerTokenStore`, identity provider → existing resolver) (FR-013a, FR-013c, FR-013d)
- [X] T017 [US2] In `src/AkmlSql.Engine/EngineHandlerRegistry.cs`: replace the hardcoded `new HandshakeHandler()` at line 258 with a host-supplied handler via an optional `HandshakeHandler?` parameter (default null ⇒ parameterless registration, preserving loopback + named-pipe callers) (FR-013a, FR-013b)
- [X] T018 [US2] Extend `tests/AkmlSql.Engine.Tests/EngineHostTests.cs` with the composition matrix: LAN + wrong PIN → `PinInvalid` (no bearer); LAN + right PIN → `Ok` + non-null bearer + single-use (second use → `PinInvalid`); loopback + no PIN → `Ok`; LAN and loopback share one `RpcRouter` for non-handshake handlers (FR-013e, SC-010; contract C4)
- [X] T019 [P] [US2] Add `src/AkmlSql.Engine/Pairing/PairingPinFile.cs`: `Publish(string)` atomic temp+rename write (`File.Move(overwrite:true)`), UTF-8 no-BOM no-newline, catch+`Log.Error` on failure (never throws) (FR-008, FR-009, FR-013; contract `pairing-pin-file-contract.md` C2)
- [X] T020 [US2] In `src/AkmlSql.Engine/EngineHost.cs` (LAN mode): construct `PairingPinFile` at `%CommonAppData%\AKML SQL Web\pairing-pin.txt`, subscribe `PairingService.PinChanged += (_, pin) => pinFile.Publish(pin)`, then call `pinFile.Publish(pairing.CurrentPin)` once (the initial mint fires inside the ctor before subscription) (FR-008; contract C3)
- [ ] T021 [US2] In `src/AkmlSql.Installer/web-installer.iss`: create `%CommonAppData%\AKML SQL Web\` with an Administrators + SYSTEM read+write ACL (via `New-Item` + `Set-Acl`, or `icacls`) before `sc.exe start AkmlSqlWebEngine`; leave an existing dir's ACL intact on re-run (FR-010; contract C4)
- [ ] T022 [US2] In `Web_PostInstall` (`src/AkmlSql.Installer/web-installer.iss`): poll `pairing-pin.txt` for appearance (30 s timeout) after starting the service; on success bake `Pairing PIN: <value>` into `INSTALL-SUMMARY.txt` + the success page (LAN only); on timeout write the "not yet generated — start the AkmlSqlWebEngine service, then re-read this file" fallback; localhost installs omit the PIN line (FR-011, FR-012; contract C5)

**Checkpoint**: LAN pairing is enforced end-to-end and the install summary shows a usable PIN.

---

## Phase 5: User Story 3 - IIS-not-installed branch presents a clear remediation dialog (Priority: P2)

**Goal**: A host without IIS gets a three-path dialog (Enable IIS now / Switch to Don't host / Cancel) instead of a silent no-op.

**Independent Test**: On a host with IIS removed, ticking Web edition → Host on IIS fires the dialog; "Enable IIS now" runs `dism` and the install completes in one run (SC-002).

**Depends on**: Phase 2 (integration) + US1 (the Hosting page exists).

- [ ] T023 [US3] Add `function IsIisInstalled(): Boolean` returning `RegKeyExists(HKLM, 'SOFTWARE\Microsoft\InetStp') and FileExists(ExpandConstant('{sys}\inetsrv\appcmd.exe'))` to `src/AkmlSql.Installer/web-installer.iss` (FR-014; contract `iis-detection-contract.md` C1)
- [ ] T024 [US3] Surface a three-button dialog before the Hosting choice commits when the web component is selected, `HostMode == IIS`, and `IsIisInstalled()` is false, in `src/AkmlSql.Installer/web-installer.iss` (FR-015; contract C2)
- [ ] T025 [US3] Wire "Enable IIS now": show a "Enabling IIS… this can take up to a minute" notice (a `CreateOutputMsgPage` before the call, or a `WizardForm` status label + `Repaint` + wait cursor — no live progress bar; the call blocks the wizard thread) then `Exec('dism.exe', '/online /enable-feature /featurename:IIS-WebServerRole /All /Quiet /NoRestart', ..., ewWaitUntilTerminated, ...)`; re-check `IsIisInstalled()` on return, re-present the three buttons on non-zero, in `src/AkmlSql.Installer/web-installer.iss` (FR-016)
- [ ] T026 [US3] Wire "Switch to Don't host" → `WebHostPage.SelectedValueIndex := 1` and "Cancel install" → abort with exit code 0, in `src/AkmlSql.Installer/web-installer.iss` (FR-017, FR-018)
- [ ] T027 [US3] Silent-mode branch: under `/VERYSILENT` + `/WEB_HOST=IIS` + missing IIS, log "IIS not installed — pass /WEB_HOST=NONE to skip IIS provisioning" and exit non-zero with no dialog, in `src/AkmlSql.Installer/web-installer.iss` (FR-019; contract C3)
- [ ] T028 [US3] Don't-host success text: bundle path at `{app}\Web\` + a Python `http.server` example; confirm the bundle + `AkmlSqlWebEngine` service still install while `web-iis-setup.ps1` is skipped, in `src/AkmlSql.Installer/web-installer.iss` (FR-020; contract C4)

**Checkpoint**: the IIS-missing path is handled with clear remediation in both interactive and silent modes.

---

## Phase 6: User Story 4 - Unattended install supports the documented silent flags (Priority: P2)

**Goal**: `/WEB_HOST`, `/WEB_EXPOSURE`, `/WEB_PORT`, `/BRIDGE_PORT` drive a fully unattended install; invalid combinations fail cleanly.

**Independent Test**: the happy-path silent command exits 0 with a working install; the two invalid combos exit non-zero with the documented log line (SC-004, SC-005).

**Depends on**: Phase 2 (the `InitializeSetup` hook point reserved in T005).

- [ ] T029 [US4] Parse `/WEB_HOST=IIS|NONE`, `/WEB_EXPOSURE=LOCALHOST|LAN`, `/WEB_PORT=<int>`, `/BRIDGE_PORT=<int>` in `InitializeSetup` of `src/AkmlSql.Installer/AkmlSqlSetup.iss`; map to the wizard state, defaulting absent flags to wizard defaults (FR-021, FR-022; contract `installer-integration-contract.md` C3)
- [ ] T030 [US4] Enforce the two cross-validation rules in `InitializeSetup`: `WEB_HOST=NONE` + `WEB_EXPOSURE=LAN` → "LAN exposure requires a hosting mode (use /WEB_HOST=IIS)"; `WEB_PORT == BRIDGE_PORT` → "IIS port and Bridge port must differ" — each `Result := False` + log + abort before any state is created (FR-023, FR-024)
- [ ] T031 [US4] Make parsed flags drive component selection + `Web_Skip` page-skipping so `/VERYSILENT` never blocks on a wizard page, across `src/AkmlSql.Installer/AkmlSqlSetup.iss` + `web-installer.iss` (FR-022)
- [ ] T032 [US4] On any sub-step failure (IIS / cert / firewall / service / config), set a non-zero exit code, log the reason, and run the uninstall hooks to roll back provisioned state, in `src/AkmlSql.Installer/web-installer.iss` (FR-025)
- [ ] T033 [US4] Verify a re-run with changed `/WEB_EXPOSURE` (localhost → LAN) regenerates cert + firewall + binding while preserving `%CommonAppData%\AKML SQL Web\tokens.json` byte-for-byte (FR-026)

**Checkpoint**: unattended install + reconfigure works; invalid flag combos fail safely.

---

## Phase 7: User Story 5 - Installer smoke tests catch regressions on a real Windows host (Priority: P3)

**Goal**: An opt-in, host-gated suite asserts a real install's end state and the plugin-state preservation invariant.

**Independent Test**: `dotnet test tests/AkmlSql.Installer.Tests --filter Category=InstallerSmoke` on an IIS+admin host is green in < 5 min; Skipped on a non-admin/non-IIS host; not run by default `dotnet test` (SC-006, SC-007).

**Depends on**: US1 + US2 + US3 + US4 (the suite drives a silent install — US4 — and asserts the end states US1/US2/US3 produce).

- [X] T034 [US5] Create `tests/AkmlSql.Installer.Tests/AkmlSql.Installer.Tests.csproj` (`net10.0`, refs `xunit` + `Microsoft.NET.Test.Sdk` + `Xunit.SkippableFact`) and register it in `AKML-SQL.slnx` (FR-027; contract `installer-smoke-suite-contract.md` C1)
- [X] T035 [US5] Add `tests/AkmlSql.Installer.Tests/InstallerSmokeFixture.cs` (`IAsyncLifetime`): locate the prebuilt `Output/AKMLSQLSetup.exe` (fail clearly if absent), capture the pre-install plugin-config hash, run a silent LAN install on two free ports, read `INSTALL-SUMMARY.txt`, silent-uninstall on dispose; expose `IsAdministrator()` + `IsIisInstalled()` for the skip gate (FR-029, FR-032; contract C3)
- [X] T036 [P] [US5] Add `tests/AkmlSql.Installer.Tests/IisProvisioningTests.cs` ([Trait("Category","InstallerSmoke")], `Skip.IfNot` gated): assert the `AkmlSqlWeb` site is bound on the IIS port, the five MIME types are registered, and the CSP header is present on a `HEAD` to `http://localhost:<IisPort>/` (FR-030 a–c)
- [X] T037 [P] [US5] Add `tests/AkmlSql.Installer.Tests/LanTlsTests.cs` ([Trait], gated): assert `netsh http show sslcert ipport=0.0.0.0:<BridgePort>` thumbprint matches `INSTALL-SUMMARY.txt` and the firewall rule "AKML SQL Web Engine" exists (FR-030 d–e)
- [X] T038 [P] [US5] Add `tests/AkmlSql.Installer.Tests/ReRunAndUninstallTests.cs` ([Trait], gated): assert `INSTALL-SUMMARY.txt` is non-empty with a `URL:` line, run an install → uninstall → re-install cycle, and assert `Get-FileHash %AppData%\AKML SQL\config.json` is identical pre-install / post-install / post-uninstall (FR-030 f–g, FR-031, SC-007)
- [X] T039 [US5] Verify the trait gating: plain `dotnet test` runs none of the smoke tests; `--filter Category=InstallerSmoke` runs them; a non-admin/non-IIS host reports Skipped (not Failed) (FR-028, SC-006) — verified here: `dotnet test` on the project reports 7 Skipped (non-admin). SC-006 (assertions executing green) requires an admin+IIS host (T041-adjacent).

**Checkpoint**: a one-command pre-merge installer gate exists for IIS+admin hosts.

---

## Phase 8: User Story 6 - doc/deployment.md walks an operator through a Web-edition install (Priority: P3)

**Goal**: A canonical operator install doc + a baselined first-interactive-run record.

**Independent Test**: a maintainer follows `doc/deployment.md` §Web edition on a fresh host in < 5 min and every command/click matches the wizard (SC-008).

**Depends on**: US1–US5 (documents the shipped behaviour); FR-035 gates SC-001/SC-002/SC-003.

- [X] T040 [US6] Add a "Web edition" section to `doc/deployment.md` with subsections Prerequisites, Component selection (the four pages), Localhost mode, LAN mode, Don't host (host-it-yourself + Python `http.server`), Silent install (flag matrix + one happy + one failure example), Uninstall, Troubleshooting (IIS missing / port collision / admin-rights / service-fails-to-start) (FR-033)
- [ ] T041 [US6] Run the first interactive integration run on a real Windows host (Inno Setup 7 + IIS + admin); record `specs/026-m4-installer-closure/INSTALL-RUN-NOTES.md` with Windows/IIS/Inno versions, per-phase wall-clock, wizard screenshots, and observed deltas (FR-035; gates SC-001, SC-002, SC-003)
- [ ] T042 [US6] File the observed deltas from T041 as spec-026 follow-up tasks; once integration has landed and the run is recorded, remove the "ships as scaffolding" banner from `doc/WEB/quickstart-m4.md` (FR-034)

**Checkpoint**: operators have a canonical install doc; maintainers have a baselined first-run record.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: DoD closure, progress log, end-to-end validation.

- [ ] T043 [P] Add the spec-026 entry to `doc/progress.md` (per-spec table style): tasks complete/deferred, the enforced-LAN-auth scope expansion, the two-port fix
- [ ] T044 [P] DoD closure check in `specs/026-m4-installer-closure/INSTALL-RUN-NOTES.md`: map every M4 PRD §11 / spec 021 §11 Definition-of-Done checkbox to a shipped feature or an FR-001..FR-035 / FR-003a / FR-007a / FR-013a..e (SC-009)
- [ ] T045 Run `quickstart.md` end-to-end on a real host; confirm SC-001..SC-010 hold; file any residual friction as follow-ups

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: no dependencies; T001/T002/T003 all parallel.
- **Phase 2 (Foundational)**: T004–T006 depend on nothing; **T007 compile gate depends on T001** (the `[Files]` bundle wildcard). Blocks Phases 3, 5, 6, 7, 8 (installer-side).
- **Phase 3 (US1)**: depends on Phase 2.
- **Phase 4 (US2)**: engine-side (T015–T020) depends only on Phase 1 (independent of the installer — can run in parallel with US1); installer-side (T021–T022) depends on Phase 2.
- **Phase 5 (US3)**: depends on Phase 2 + US1 (Hosting page).
- **Phase 6 (US4)**: depends on Phase 2 (the `InitializeSetup` hook point).
- **Phase 7 (US5)**: depends on US1 + US2 + US3 + US4 (drives a silent install and asserts their end states).
- **Phase 8 (US6)**: depends on US1–US5.
- **Phase 9 (Polish)**: depends on the target stories being complete.

### User-story dependencies

- **US1 (P1)** → Foundational; the MVP.
- **US2 (P1)** → engine half independent of US1; installer half (PIN summary) needs Foundational + US1's `Web_PostInstall` shape.
- **US3 (P2)** → Foundational + US1.
- **US4 (P2)** → Foundational.
- **US5 (P3)** → US1 + US2 + US3 + US4.
- **US6 (P3)** → US1–US5.

### Within each user story

- US1: page split (T008) → validation (T009) → routing (T010) → URL/skip/paths (T011–T013) → compile+verify (T014).
- US2: auth chain T015→T016→T017→T018 (sequential, shared/dependent files); T019 (new file) and T021 (installer) parallel to the chain; T020 after T016+T019; T022 after T021.
- US5: csproj (T034) → fixture (T035) → the three test classes (T036/T037/T038 parallel) → gating check (T039).

### Parallel opportunities

- Phase 1: T001, T002, T003 all `[P]`.
- US2 engine-side (T015–T020) parallelises with US1 installer-side (T008–T014) — different files, different subsystems.
- US2: T019 `[P]` and T021 `[P]` run alongside the T015→T018 chain.
- US5: T036, T037, T038 `[P]` (different test files) once the fixture (T035) exists.
- Polish: T043, T044 `[P]`.

---

## Parallel Example: User Story 2

```bash
# Engine auth chain (sequential — dependent files):
T015 WebSocketTransport.cs (source-IP threading)
  → T016 EngineHost.cs (LAN composition)
    → T017 EngineHandlerRegistry.cs (optional handler param)
      → T018 EngineHostTests.cs (composition matrix)

# In parallel with the chain (different files):
T019 [P] PairingPinFile.cs (new writer)
T021 [P] web-installer.iss (ACL'd dir)   # installer-side, independent of engine

# Meanwhile US1 (installer happy path) can proceed entirely in parallel with US2 engine-side.
```

---

## Implementation Strategy

### MVP first (US1 only)

1. Phase 1 (Setup) → publish bundle + engine.
2. Phase 2 (Foundational) → `#include` + hooks + `ShouldSkipPage` + compile gate.
3. Phase 3 (US1) → two-port split + routing + URL + skip logic.
4. **Stop and validate**: localhost install → editor loads at `http://localhost/`. The Web edition is installable in one click at this point (localhost-only).

### Incremental delivery

1. Setup + Foundational → installer offers the Web component.
2. US1 → localhost install MVP.
3. US2 → enforced LAN pairing + usable PIN (the security boundary + two-machine story).
4. US3 → graceful IIS-missing handling.
5. US4 → unattended/scripted install.
6. US5 → regression gate on a real host.
7. US6 → operator docs + first interactive run record.

### Parallel team strategy

After Foundational closes: Developer A drives US1 → US3 → US4 (installer-side); Developer B drives US2 engine-side (auth + PIN writer) in parallel; they converge on US2's installer-side PIN summary, then US5 + US6.

---

## Notes

- `[P]` = different files, no dependency on incomplete tasks in the same phase.
- Tests are part of the deliverable (FR-013e `EngineHostTests`; US5 the smoke suite) — do not defer them.
- The four PowerShell helpers' internals are NOT modified; only the `-Port` value routed to each changes (T010).
- The IDE-plugin path stays byte-for-byte unchanged when the Web component is unticked (FR-006); SC-007 (T038) automates the proof.
- US2 auth (FR-013a..e) is sequenced before US2 PIN persistence (FR-008..013) — a persisted PIN is cosmetic until the handshake enforces it.
- The first interactive integration run (T041) is the gating evidence for SC-001/SC-002/SC-003; until it runs on a real Windows host, those three SCs are aspirational.
- Commit per task or per logical group; each phase checkpoint is a natural validation boundary.
