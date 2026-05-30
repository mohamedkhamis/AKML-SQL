# Implementation Plan: M4 — Installer (IIS Deployment Option) Closure

**Branch**: `026-m4-installer-closure` | **Date**: 2026-05-28 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/026-m4-installer-closure/spec.md`

## Summary

Close the six genuinely-unmet items from the M4 PRD (`doc/WEB/M4-iis-installer.md`) so the M4 Definition of Done can be retired against shipped evidence. The M4 scaffolding (`web-installer.iss` + four PowerShell helpers) merged under spec 021 Phase 5 (tasks T081–T095) but is **dead code** — `AkmlSqlSetup.iss` never `#include`s it and never calls its five hook procedures, so the shipping installer offers no Web component at all. The closure: (1) wire the integration (`#include` + the five hook procedures: four called from existing event handlers, `Web_Skip` via a new `ShouldSkipPage`) and split the single port input into two (IIS port default 80 + bridge port default 47291) to clear the HTTP.SYS bind conflict; (2) make the engine **enforce** the LAN pairing PIN — a plan-stage code audit found `EngineHandlerRegistry.cs:258` registers `HandshakeHandler` with the all-permissive parameterless constructor, so the LAN bridge currently auto-accepts every connection; this closure constructs a live `PairingService` + `BearerTokenStore` in LAN mode and persists the minted PIN to `%CommonAppData%\AKML SQL Web\pairing-pin.txt` so the install summary can show it; (3) add the IIS-not-installed three-path dialog; (4) parse the silent-install flags `/WEB_HOST`, `/WEB_EXPOSURE`, `/WEB_PORT`, `/BRIDGE_PORT` with cross-validation; (5) add an opt-in `[Trait("Category","InstallerSmoke")]` test suite; (6) write the `doc/deployment.md` web-edition section and run the first interactive integration run.

The new/changed application surfaces are: an `#include` + the five hook-procedure calls (four into existing handlers + `Web_Skip` via a new `ShouldSkipPage`) in `AkmlSqlSetup.iss`; a two-port-page split + IIS-missing dialog + silent-flag wiring in `web-installer.iss`; an engine-side LAN auth composition in `EngineHost`/`EngineHandlerRegistry` (the spec-025-left-undone auth wiring, surfaced during this plan's audit — same pattern as spec 025 absorbing its engine-host composition gap); a PIN-file writer hung off `PairingService.PinChanged`; one new test project (`tests/AkmlSql.Installer.Tests`); an `EngineHostTests` extension; and two docs. Everything else the M4 PRD describes (component group, file copy, MIME types, CSP header, cert generation, firewall rule, Windows service install, uninstall path) is already shipped and is **not** rewritten.

## Technical Context

**Language/Version**: Inno Setup 7 Pascal Script for the installer; Windows PowerShell 5.1 for the four `.ps1` helpers (already shipped); C# 12 on .NET 10 (`net10.0`, `win-x64`) for the engine + engine tests + the new installer-smoke test project.
**Primary Dependencies**: Inno Setup 7 (`ISCC.exe`); Windows BCL only on the engine side — `System.Security.AccessControl` / `FileSystemAclExtensions` for the PIN-file ACL, the existing `PairingService` + `BearerTokenStore` + `HandshakeHandler` (spec 021 T060/T063/T064, merged); `System.Net.HttpListener` LAN-TLS path (spec 025, merged). New test-only package: `Xunit.SkippableFact` for the host-mismatch skip path in `tests/AkmlSql.Installer.Tests`. **No new runtime package references** on the engine.
**Storage**: One new on-disk artefact — `%CommonAppData%\AKML SQL Web\pairing-pin.txt` (6-digit decimal, atomic temp+rename, Administrators+SYSTEM ACL). Bearer tokens already persist to `%CommonAppData%\AKML SQL Web\tokens.json` via `BearerTokenStore` (spec 021 T064). No new config schema — `web-config-bridge.ps1` already writes the `bridge` section; this plan only changes which port value it receives.
**Testing**: `dotnet test tests/AkmlSql.Engine.Tests` (xUnit) for the auth-composition matrix in `EngineHostTests`; `dotnet test tests/AkmlSql.Installer.Tests --filter Category=InstallerSmoke` (xUnit + SkippableFact) for the on-host install/uninstall assertions; Inno Setup compile via `ISCC.exe` as the build gate for the `.iss` changes. The installer-smoke suite is opt-in (excluded from default `dotnet test`), mirroring spec 024's `ParityBaseline` and spec 025's `BridgeE2E`.
**Target Platform**: Windows 10/11 + admin rights for the installer; IIS for the recommended hosting path; .NET 10 SDK for the engine + tests. The installer-smoke suite runs only on a host with IIS + admin (skips otherwise).
**Project Type**: Plumbing + verification + one engine-side security-wiring slice over the already-merged installer scaffolding and bridge stack. One new test csproj; no new IPC message types; the existing `MessageTypes.HandshakeRequest=200/201` envelope is unchanged; the four PowerShell helpers' internals are untouched except for the port value they receive.
**Performance Goals**: Localhost install → working editor ≤ 90 s (SC-001); IIS-missing → enable-IIS → working install ≤ 5 min (SC-002); two-machine LAN pair → `Open` ≤ 30 s (SC-003); installer-smoke suite ≤ 5 min (SC-006).
**Constraints**:

- The IDE-plugin install path MUST stay byte-for-byte unchanged when the Web component is unticked (FR-006) — `%AppData%\AKML SQL\` untouched across a Web install/uninstall cycle (SC-007).
- The engine's loopback / named-pipe path MUST keep the parameterless auto-accept `HandshakeHandler` (FR-013b) — no regression to the IDE-plugin-only deployment or to localhost web mode.
- LAN mode MUST refuse a wrong PIN 100% of the time (FR-013c, SC-010) — there is no configuration under which a LAN connection bypasses PIN/bearer validation.
- IIS port and bridge port MUST differ (FR-003, FR-024) — enforced both in the interactive wizard (`Web_NextButton`) and the silent-flag validator (`InitializeSetup`).
- The PIN-file write MUST NOT crash engine startup on failure (FR-013) — best-effort, Serilog-logged.
- Silent install MUST be fully unattended (FR-022) and roll back on any sub-step failure (FR-025).

**Scale/Scope**: Six user stories; 42 functional requirements (FR-001..FR-035 + FR-003a + FR-007a + FR-013a..FR-013e). Installer-side: ~1 `#include` + 4 hook calls + 1 new `ShouldSkipPage` (~40 LOC in `AkmlSqlSetup.iss`); split one port page into two + IIS-missing dialog + silent-flag parse (~120 LOC delta in `web-installer.iss`). Engine-side: ~60 LOC of LAN auth composition in `EngineHost`/`EngineHandlerRegistry` + a ~40 LOC `PairingPinFile` writer. Tests: one new csproj (~250 LOC across the smoke suite) + ~80 LOC `EngineHostTests` extension. Docs: ~200 lines (`doc/deployment.md` section + quickstart-m4 banner removal + `INSTALL-RUN-NOTES.md`).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

No `.specify/memory/constitution.md` exists for this repository, so no constitution gates apply (same as specs 022–025). The closure spec constrains itself with four self-imposed gates that serve the same purpose:

- **No new IPC message types.** The auth wiring uses the existing `HandshakeRequest`/`HandshakeResponse` (`MessageTypes` 200/201); the PIN flow is an on-disk file + the existing in-memory `PairingService` API.
- **No new runtime package on the engine.** The PIN-file ACL uses BCL `System.Security.AccessControl`; the auth composition uses already-shipped `PairingService` / `BearerTokenStore` / `HandshakeHandler`.
- **The four PowerShell helpers' internals are untouched.** Only the port value routed to `web-iis-setup.ps1` (IIS port) vs `web-config-bridge.ps1` / `web-tls-setup.ps1` / `web-firewall.ps1` (bridge port) changes — at the call site in `web-installer.iss`, not inside the scripts.
- **The IDE-plugin path is untouched.** When the Web component is unticked, the installer behaves byte-for-byte as today; the engine's named-pipe + loopback handshake path is unchanged.

These gates are re-checked in the Post-Design re-evaluation below.

## Project Structure

### Documentation (this feature)

```text
specs/026-m4-installer-closure/
├── plan.md                                          # This file (/speckit.plan command output)
├── spec.md                                          # Already written by /speckit.specify (updated for enforced-LAN-auth scope)
├── research.md                                      # Phase 0 output — six decisions
├── data-model.md                                    # Phase 1 output — six conceptual entities
├── quickstart.md                                    # Phase 1 output — how to land each user story
├── contracts/                                       # Phase 1 output — five artefact contracts
│   ├── installer-integration-contract.md
│   ├── lan-auth-composition-contract.md
│   ├── pairing-pin-file-contract.md
│   ├── iis-detection-contract.md
│   └── installer-smoke-suite-contract.md
├── checklists/
│   └── requirements.md                              # Created by /speckit.specify; all green
├── INSTALL-RUN-NOTES.md                             # FR-035 first-interactive-run record (written during US6)
└── tasks.md                                         # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Installer/
│   ├── AkmlSqlSetup.iss                             # ← #include web-installer.iss; 4 hook calls into existing handlers + a new ShouldSkipPage (5 procedures total); parse silent flags in InitializeSetup (US1/FR-001,FR-002 + US4/FR-021..FR-026)
│   └── web-installer.iss                            # ← split 1 port page → 2 (IIS 80 + bridge 47291); IsIisInstalled + 3-path dialog; route IIS port vs bridge port to the right helpers; Don't-host success text (US1/FR-003..FR-007 + US3/FR-014..FR-020)
│   #   web-iis-setup.ps1 / web-tls-setup.ps1 / web-firewall.ps1 / web-config-bridge.ps1 — UNCHANGED internals; only the -Port value they receive changes at the call site
│
└── AkmlSql.Engine/
    ├── EngineHost.cs                                # ← in LAN mode construct PairingService + BearerTokenStore, wire PairingPinFile to PinChanged, pass validators into handler registration (US2/FR-013a,FR-013d)
    ├── EngineHandlerRegistry.cs                     # ← register HandshakeHandler full ctor (LAN) vs parameterless (loopback) — replaces the hardcoded `new HandshakeHandler()` at line 258 (US2/FR-013a,FR-013b)
    └── Pairing/
        └── PairingPinFile.cs                        # ← NEW; atomic temp+rename writer for pairing-pin.txt, subscribed to PairingService.PinChanged, swallows write errors (US2/FR-008,FR-009,FR-013)

tests/
├── AkmlSql.Engine.Tests/
│   └── EngineHostTests.cs                           # ← extend with the LAN/loopback auth composition matrix (US2/FR-013e, SC-010)
└── AkmlSql.Installer.Tests/                         # ← NEW csproj (net10.0, Xunit.SkippableFact)
    ├── AkmlSql.Installer.Tests.csproj
    ├── InstallerSmokeFixture.cs                     # silent install → assert → silent uninstall → assert; IsAdministrator()+IsIisInstalled() gate
    ├── IisProvisioningTests.cs                      # site bound, MIME types, CSP header (US5/FR-030 a–c)
    ├── LanTlsTests.cs                               # sslcert binding, firewall rule (US5/FR-030 d–e)
    └── ReRunAndUninstallTests.cs                    # summary well-formed; plugin-state byte-for-byte hash (US5/FR-030 f–g, FR-031)

doc/
├── deployment.md                                    # ← add "Web edition" section (US6/FR-033)
└── WEB/
    └── quickstart-m4.md                             # ← remove the "ships as scaffolding" banner once integration lands (US6/FR-034)
```

**Structure Decision**: Plumbing + verification + one engine-side security-wiring slice. Installer-side touches are an `#include` + hook calls + a new `ShouldSkipPage` in `AkmlSqlSetup.iss` and a page-split + dialog + flag-parse in `web-installer.iss`; the four PowerShell helpers are reused unchanged. Engine-side touches are the LAN auth composition (`EngineHost` + `EngineHandlerRegistry`) plus one new ~40 LOC `PairingPinFile` writer. One new test csproj, one `EngineHostTests` extension, two docs, one first-run record. All other M4 artefacts (component group, file copy, MIME, CSP, cert, firewall, service install, uninstall) are already shipped and are not retouched.

## Complexity Tracking

No constitution gate violations to justify (no constitution). One scope expansion is tracked explicitly:

| Expansion | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Engine-side LAN auth composition (FR-013a..e) added to an "installer closure" | The plan-stage audit found the LAN bridge auto-accepts every connection (`HandshakeHandler` registered with the all-permissive parameterless ctor). The M4 PRD §8 two-machine-pairing metric and the printed PIN are meaningless without enforcement. The user confirmed LAN pairing is a real security boundary M4 must ship. | "Write the PIN to disk but leave auth unwired" ships a security illusion — a printed PIN that validates nothing. The advisor flagged the hybrid as the one incoherent option. Deferring auth to spec 027 (the other coherent path) was offered to the user and declined. |

## Phase 0: Research

Six technical decisions drive the plan, captured in `research.md`. Summary:

1. **Installer integration shape** — `#include "web-installer.iss"` near the bottom of `AkmlSqlSetup.iss`'s `[Code]` section, then add the four hook calls inside the existing event procedures (`InitializeWizard` @345, `NextButtonClick` @469, `CurStepChanged` @579, `CurUninstallStepChanged` @674) **and add a brand-new `ShouldSkipPage` function** (for the fifth procedure, `Web_Skip`) — the audit confirmed `AkmlSqlSetup.iss` has no `ShouldSkipPage` today, so `Web_Skip` has no caller until one is created. `InitializeSetup` @403 gains the silent-flag parse + cross-validation.
2. **Two-port split** — replace the single `WebPortPage` (default 47291) in `web-installer.iss` with two pages: `WebIisPortPage` (default 80) and `WebBridgePortPage` (default 47291). `web-iis-setup.ps1` receives the IIS port; `web-config-bridge.ps1`, `web-tls-setup.ps1`, `web-firewall.ps1` receive the bridge port. `Web_NextButton` rejects equal ports. Clears the HTTP.SYS conflict where IIS and the engine's `HttpListener` fought for one port.
3. **Engine-side LAN auth composition** — in `EngineHost`, when `BridgeOptions.IsLoopback == false`, construct `new PairingService()` + `new BearerTokenStore(bridge.TokenStorePath, ...)`, and register `HandshakeHandler` via the full constructor with `pairingRequired: () => true` and validator/minter delegates bound to those services. Loopback keeps the parameterless registration. The hardcoded `new HandshakeHandler()` at `EngineHandlerRegistry.cs:258` becomes a parameter the host supplies. `EngineHostTests` asserts the LAN/loopback matrix.
4. **PIN-file writer** — a new `PairingPinFile` class subscribes to `PairingService.PinChanged` and does an atomic temp+rename write (mirroring `ConfigManager.Save`). Because the initial PIN is minted inside the `PairingService` constructor (before any subscriber attaches), the host publishes `CurrentPin` once immediately after subscribing. Write failures are swallowed + Serilog-logged (FR-013). The installer (not the engine) sets the directory ACL before starting the service.
5. **IIS-not-installed dialog** — add `function IsIisInstalled(): Boolean` (`RegKeyExists(HKLM, 'SOFTWARE\Microsoft\InetStp')` AND `FileExists({sys}\inetsrv\appcmd.exe)`) to `web-installer.iss`; gate it in `Web_NextButton`/`Web_Skip` so the three-path `MsgBox` (Enable IIS now / Switch to Don't host / Cancel) fires before the Hosting page commits. Silent mode + `/WEB_HOST=IIS` + missing IIS → log + non-zero exit, no dialog.
6. **Installer-smoke harness** — a new `tests/AkmlSql.Installer.Tests` csproj using `Xunit.SkippableFact`; `InstallerSmokeFixture : IAsyncLifetime` runs the prebuilt `Output/AKMLSQLSetup.exe` silently, captures the install-summary, and tears down via silent uninstall. Every test is `[Trait("Category","InstallerSmoke")]` and `Skip.IfNot(IsAdministrator() && IsIisInstalled())`. Mirrors spec 024's opt-in pattern.

`research.md` records each decision with rationale + alternatives.

## Phase 1: Design & Contracts

### Data model (`data-model.md`)

Six conceptual entities — only one new persisted artefact (the PIN file); the rest are wizard state, parsed flags, or test scaffolding:

1. **WebEditionInstallChoice** — the wizard's collected state: host mode (IIS / Don't host), exposure (Localhost / LAN), IIS port, bridge port.
2. **PairingPinFile** — the on-disk PIN artefact: path, format, ACL, write timing, read/poll protocol.
3. **SilentInstallFlags** — the parsed CLI set (`/WEB_HOST`, `/WEB_EXPOSURE`, `/WEB_PORT`, `/BRIDGE_PORT`) + the two cross-validation rules.
4. **LanAuthComposition** — the engine-side wiring state: LAN (full handshake ctor + live services) vs loopback (parameterless auto-accept).
5. **InstallSummary** — the `INSTALL-SUMMARY.txt` structure: URL line, LAN block (PIN, thumbprint, trust steps), localhost block.
6. **InstallerSmokeAssertion** — one checkpoint in the smoke suite (site, MIME, CSP, sslcert, firewall, summary, plugin-state hash).

### Contracts (`contracts/`)

Five artefact contracts, one per surface that produces a non-trivial format, wiring, or harness:

1. **`installer-integration-contract.md`** — the `#include` placement; the four hook-call insertion points (with the existing line numbers) + the new `ShouldSkipPage` for `Web_Skip` (five procedures total); the new `ShouldSkipPage` function shape; the two-port page split; the silent-flag parse + cross-validation in `InitializeSetup`. Defines FR-001..FR-007 + FR-021..FR-026.
2. **`lan-auth-composition-contract.md`** — the LAN-vs-loopback handshake registration; the validator/minter delegate bindings; the bearer-mint + replay + revocation behaviour; the `EngineHostTests` composition matrix. Defines FR-013a..FR-013e + SC-010.
3. **`pairing-pin-file-contract.md`** — the `pairing-pin.txt` byte format, ACL, atomic-write method, the `PinChanged`-plus-one-shot-publish wiring, the `Web_PostInstall` 30-second poll + fallback text. Defines FR-008..FR-013.
4. **`iis-detection-contract.md`** — `IsIisInstalled()` exact predicate; the three-path dialog text + actions; the `dism` command; silent-mode behaviour; the Don't-host success text. Defines FR-014..FR-020.
5. **`installer-smoke-suite-contract.md`** — the csproj + `Xunit.SkippableFact` setup; the `[Trait("Category","InstallerSmoke")]` opt-in; the `Skip.IfNot` gate; the fixture lifecycle; the seven assertions; the install→uninstall→re-install cycle. Defines FR-027..FR-032.

### Quickstart (`quickstart.md`)

A walkthrough developers run to land each user story, in dependency order:

- **US1**: `#include` web-installer.iss → 4 hook calls into existing handlers + a new `ShouldSkipPage` for `Web_Skip` → split the port page in two → compile with `ISCC.exe` → run the wizard, tick Web edition, confirm the four pages appear and the editor loads at `http://localhost/`.
- **US2 (auth first, then PIN)**: wire `EngineHost` LAN composition (PairingService + BearerTokenStore + full handshake ctor) → add `EngineHostTests` matrix → write `PairingPinFile` + hook `PinChanged` → confirm wrong PIN refused, right PIN mints bearer, PIN file written, install summary shows it.
- **US3**: add `IsIisInstalled()` + the three-path dialog → test on a host with IIS removed → confirm Enable-IIS / Don't-host / Cancel branches.
- **US4**: parse `/WEB_HOST` `/WEB_EXPOSURE` `/WEB_PORT` `/BRIDGE_PORT` in `InitializeSetup` → add the two cross-validation rules → run the happy-path + the two failure-path silent commands.
- **US5**: create `tests/AkmlSql.Installer.Tests` → write the fixture + the four test classes → `dotnet test --filter Category=InstallerSmoke` on an IIS+admin host.
- **US6**: write `doc/deployment.md` §Web edition → run the first interactive integration run → record deltas in `INSTALL-RUN-NOTES.md` → remove the scaffolding banner from `quickstart-m4.md`.

### Agent context

Run `.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude` to refresh the agent context file with this closure's surfaces (the installer integration hooks; the engine-side LAN auth composition; the `PairingPinFile` writer; the `InstallerSmoke` test category).

## Phase 2 planning note

Tasks are generated by `/speckit.tasks`, not here. The tasks file will turn each user story into a sequence: in US1, `#include` + hook calls + `ShouldSkipPage` → two-port split → ISCC compile gate; in US2, the engine LAN auth composition + `EngineHostTests` matrix FIRST (the security boundary), then the `PairingPinFile` writer + `Web_PostInstall` poll; in US3, `IsIisInstalled()` + the three-path dialog + silent-mode branch; in US4, the silent-flag parse + two cross-validation rules; in US5, the test csproj + fixture + four test classes; in US6, the deployment doc + first interactive run + banner removal. US2's auth sub-tasks (FR-013a..e) are sequenced before its PIN-persistence sub-tasks (FR-008..013) because a persisted PIN is cosmetic until the handshake enforces it.

## Post-Design Constitution Re-Check

The four self-imposed gates from the Constitution Check section all hold post-design:

- **No new IPC message types** — the auth wiring reuses `HandshakeRequest`/`HandshakeResponse` (200/201); the PIN flow is an on-disk file plus the existing in-memory `PairingService` API.
- **No new engine runtime package** — PIN-file ACL via BCL `System.Security.AccessControl`; auth composition via already-shipped `PairingService` / `BearerTokenStore` / `HandshakeHandler`. The only new package is test-only (`Xunit.SkippableFact`).
- **PowerShell helper internals untouched** — only the `-Port` argument routed to each helper changes, at the `web-installer.iss` call site.
- **IDE-plugin path untouched** — Web component unticked → byte-for-byte identical install; loopback/named-pipe handshake keeps the parameterless auto-accept registration (FR-013b); `%AppData%\AKML SQL\` preserved across a Web install/uninstall cycle (SC-007, asserted by FR-030 g).

The one scope expansion (engine-side LAN auth) is tracked in Complexity Tracking above with the rationale and the rejected alternatives. Closure-spec discipline holds: every artefact is a test file, a checked-in doc, a one-class addition, or a targeted extension of an existing file — no new persistence layer beyond the single PIN text file, no new IPC message type, no new public service interface.
