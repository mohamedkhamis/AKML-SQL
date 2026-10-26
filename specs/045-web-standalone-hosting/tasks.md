---
description: "Task list for 045-web-standalone-hosting"
---

# Tasks: Web edition hosting without IIS — a standalone Windows service by default

**Input**: Design documents from `/specs/045-web-standalone-hosting/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/engine-static-host.md](./contracts/engine-static-host.md), [contracts/web-default-engine.md](./contracts/web-default-engine.md), [contracts/installer.md](./contracts/installer.md), [quickstart.md](./quickstart.md), [codebase-map.md](./codebase-map.md)

**Tests are not optional here.** Constitution III requires every new behaviour to land with
automated checks, and the plan names the test classes (quickstart §1). In every phase, write the
test tasks first, see them fail, then implement.

**Owner continuation (2026-10-26).** The owner explicitly deferred T034 to Claude Code and
asked to continue all remaining tasks. Keep T034 unchecked and its strict test unchanged.
No disposable VMs are available: implement the remaining source/tests/docs locally, using
the documented S1 compatibility fallback (retain hidden legacy component names), a wrapped
readiness notice, the existing generic ToolsUnavailable guidance pending S4, and the strict
custom-folder owner check pending S7. These are provisional choices, not passed spikes.
VM experiments/acceptance and owner upgrades remain unchecked; do not run them on this host.

**Spikes and machine rows are recording tasks, not TDD.** A spike (S1–S7, research.md § Spikes)
ends when its outcome and the chosen branch are recorded in research.md. A disposable-machine row
(quickstart §3) ends when its result is recorded in `baseline.md`. The task is done when the
record exists, not when something "passes".

**Organization**: Phase 2 holds the setup hosting model that every installer story builds on: one
hosting choice, IIS readiness, the previous host, the restructured scripts in `{app}\Support`, and
the answer check. Then there is one phase per user story, in spec priority order:

- US1: standalone by default (P1, MVP);
- US2: IIS when it is ready (P2);
- US3: upgrade and switching (P3);
- US4: LAN on either host (P4);
- US5: silent installs (P5);
- US6: repair and uninstall (P6).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel. The task touches different files from every other unfinished task
  and does not depend on one.
- **[Story]**: `[US1]`…`[US6]`, mapping to the user stories in spec.md.
- `R12` and `S3` refer to research.md; `E…`, `W…`, `I…` and `P…` to codebase-map.md; "data-model §n"
  and "contract … §n" to those files. Read the referenced entry before you start a task: it names
  the alternatives that were rejected and the traps. Line numbers in codebase-map.md come from the
  043 tip and drift, so search for the member, not the line.

## Rules for every task

- **Paths.** Repository root: `D:\Repo\01-Khamis-Projects\AKML-SQL`.
  - Engine: `src/AkmlSql.Engine/`. Core: `src/AkmlSql.Core/`. Web app: `src/AkmlSql.Web/`.
    Setup: `src/AkmlSql.Installer/`. Tests: `tests/`.
  - The SSMS shell projects are **not** touched (FR-071).
  - No IPC message type, MessagePack key or capability is added.
- **Never run the installer smoke classes, or any quickstart §3 row, on the owner's machines**
  (I21). They run the real installer with the product's AppId.
  - Smoke classes need `AKML_INSTALLER_SMOKE=1` (T001).
  - They run on the disposable VMs A–D of quickstart §0 only, each snapshotted first.
  - The owner's installs (quickstart §4) are T108, and only with the owner's approval.
- **Setup `[Code]`.**
  - Decisions are pure functions in `web-hosting.iss`: no registry, file or `Exec` calls, so the
    probe-ISS tests can drive them. Thin readers in `web-installer.iss` collect the signals.
  - Every consumer reads the `WebHost` global, never `WebHostPage.SelectedValueIndex`.
  - Page handlers start with `if WizardSilent then Exit;` (I28).
  - Never compare page IDs with `>` (I3).
  - `Check:` takes only function calls combined with `not`, `and` and `or`.
  - `web-hosting.iss` is included before `web-installer.iss`, so it cannot call `ReadConfigBool`,
    which `AkmlSqlSetup.iss` defines later.
- **`web-installer.iss` and `AkmlSqlSetup.iss` are shared by almost every setup task.** A task that
  edits either one never carries [P].
- **PowerShell helpers.**
  - Windows PowerShell 5.1, 64-bit.
  - The logic is a pure function that can be dot-sourced. Side effects run only in an entry point
    guarded by `if ($MyInvocation.InvocationName -ne '.')`.
  - No `#Requires`; the entry point checks for administrator rights itself.
  - The helpers log to `%ProgramData%\AKML SQL Web\install.log`.
  - Tests dot-source them in `powershell.exe` 5.1, as `HttpsBindingDecisionTests` does.
- **Engine responder.**
  - Nothing touches the filesystem before path validation (R3).
  - Containment uses `Path.GetFullPath`, never a substring check.
  - The length is set with `Response.ContentLength64` only.
  - `HttpListener.TimeoutManager` is never set.
  - No request is logged except refusals, which are rate-limited and at Warning. Never use Error
    here: Error goes to the telemetry sink (E16).
  - Write failures (`HttpListenerException`, `IOException`, `ObjectDisposedException`) are
    swallowed.
  - The SSMS-started engine (`RunAsync`) never serves pages (R8).
- **Web app.** No IndexedDB version bump. Records are schemaless JSON, and
  `EngineConnection.StrictScheme` reads false when it is absent.
- **Builds and browser tests.**
  - Never use `dotnet build` on the shell projects.
  - E2E needs Debug builds of `src/AkmlSql.Web` and `src/AkmlSql.Engine`, and runs serially
    (`-- xUnit.ParallelizeTestCollections=false`).
  - Rebuilding the engine while a sandbox engine runs locks its DLLs.
  - The probe-ISS tests need ISCC at `C:\Program Files\Inno Setup 7`.
- **Git.** Deliver the changes uncommitted. At each checkpoint, ask "Ready to commit?"
  (constitution IV). Never commit, push or amend unless asked.

---

## Phase 1: Setup (safety gate and baseline)

**Purpose**: no installer test can touch a real install, and there is a recorded "before".

- [X] T001 Add the `AKML_INSTALLER_SMOKE=1` opt-in to `tests/AkmlSql.Installer.Tests/InstallerSmokeEnv.cs`.
  - Without the variable, `CanRun` is false, with the skip reason "installer smoke tests run the
    real installer; set AKML_INSTALLER_SMOKE=1 on a disposable VM".
  - Confirm that `IisProvisioningTests`, `LanTlsTests`, `ServiceHealthTests` and
    `ReRunAndUninstallTests` all gate on it.
  - Confirm that `dotnet test tests/AkmlSql.Installer.Tests --filter Category=InstallerSmoke` on
    this machine skips every test.
- [X] T002 Create `specs/045-web-standalone-hosting/baseline.md` with the results from before the feature.
  - Run:
    - the full Release solution build;
    - `dotnet test` for `tests/AkmlSql.Engine.Tests`, `tests/AkmlSql.Core.Tests`,
      `tests/AkmlSql.Web.Tests`, and `tests/AkmlSql.Installer.Tests --filter "Category!=InstallerSmoke"`;
    - the serial E2E class `SpikePageTests`, after the Debug builds;
    - an ISCC compile of `src/AkmlSql.Installer/AkmlSqlSetup.iss`.
  - Save the `.trx` files under `specs/045-web-standalone-hosting/baseline/before/`, for
    `scripts/compare-test-results.ps1`.
  - Add empty sections for later rows: "Spikes", "Disposable machines (quickstart §3)" and "Owner
    installs (quickstart §4)".

---

## Phase 2: Foundational (the setup hosting model)

**Purpose**: one hosting choice alone decides what setup sets up (FR-003). This phase also adds IIS
readiness, the previous host, the restructured scripts in `{app}\Support`, and the answer check.
Every installer story builds on it.

**⚠️ CRITICAL**: no installer task of any story starts before this phase is done. US1's engine and
web tasks (T022–T034, T036–T046) need only Phase 1, and can run in parallel with this phase.

### Tests for Phase 2 (write first; they must fail)

- [X] T003 [P] Create `tests/AkmlSql.Installer.Tests/WebHostingProbeTests.cs` in the `ProcessDetectionTests` style. ISCC compiles a probe `.iss` that `#include`s `src/AkmlSql.Installer/web-hosting.iss`, calls the functions in `InitializeSetup`, writes the results to a file and aborts. Cases:
  - **`ReadConfigString`**, over the real `config.json` that `web-config-bridge.ps1` writes under
    `powershell.exe` 5.1 (UTF-8 BOM, two spaces after each colon): `bindAddress` `127.0.0.1` and
    `0.0.0.0`; a missing key returns False.
  - **`HostingAddress`**, for every data-model §11 row: Service/Localhost; Service/Lan with the
    computer name; IIS on port 80 gives `http://localhost/`; IIS on 8083; None gives "".
  - **`IisReadinessFrom`**, for every data-model §2 state:
    - the first-match order (for example, NotEnabled wins over Disabled);
    - each reason text;
    - `Ready`.
- [X] T004 Extend `tests/AkmlSql.Installer.Tests/WebHostingProbeTests.cs` with `InferPreviousHost` and `InferPreviousExposure` (data-model §3).
  - **Previous host.** A present `Host` wins. With no `Host`, no `BridgePort` and no service, the
    result is none. An `applicationHost.config` text that holds `<site name="AkmlSqlWeb"` gives
    IIS; without it, Service.
  - **Previous exposure.**
    - When `Exposure` is present, it is used.
    - Otherwise the exposure is Lan when the real PS 5.1 `config.json` has `bindAddress`
      `0.0.0.0` or `::`, or when `IisMode = Lan`.
    - Otherwise it is Localhost.
  - **Named cases**: the pre-045 IIS + LAN case, Lan only in `IisMode`, and Lan only in
    `config.json`.
- [X] T005 [P] Create `tests/AkmlSql.Installer.Tests/WebConfigBridgeTests.cs`. It dot-sources `src/AkmlSql.Installer/web-config-bridge.ps1` in `powershell.exe` 5.1. `Get-BridgeConfigJson`:
  - writes `bridge.webRoot`, both as a `…\Web` path and as "";
  - rewrites the whole `bridge` object with the data-model §6 keys;
  - keeps the other top-level keys of an existing file;
  - writes `0.0.0.0` for Lan and `127.0.0.1` for Localhost.

  Dot-sourcing the script writes no file.
- [X] T006 [P] Create `tests/AkmlSql.Installer.Tests/FirewallPlanTests.cs`. It dot-sources `src/AkmlSql.Installer/web-firewall.ps1`. `Get-FirewallPlan -WebHost -Exposure -EnginePort -HttpsPort` returns every data-model §5 row, with the ports:

  | Host / exposure | `AKML SQL Web Engine` | `AKML SQL Web Site (HTTPS)` |
  | --- | --- | --- |
  | Service / Lan | add | remove |
  | IIS / Lan | add | add |
  | None / Lan | add | remove |
  | any / Localhost | remove | remove |

  Dot-sourcing the script runs no `netsh`.
- [X] T007 [P] Create `tests/AkmlSql.Installer.Tests/WebHostingCheckTests.cs`. It runs `src/AkmlSql.Installer/web-hosting-check.ps1` as a `powershell.exe` 5.1 process against an in-test `HttpListener` on a free port (contract installer §5):
  - 200 `text/html` with `X-AKML-Host: engine`, with `-ExpectEngine`: exit 0;
  - the plain page, or no header: exit 2;
  - `-ExpectBundle` with a body that contains `_framework/blazor.webassembly.js`: exit 0, and
    without it: exit 2;
  - a closed port: exit 1 within `-TimeoutSeconds 3`;
  - on a failure, the `-ReasonPath` file holds the reason.
- [X] T008 [P] Create `tests/AkmlSql.Installer.Tests/WebInstallerSourceTests.cs`, with source-text facts over `src/AkmlSql.Installer/*.iss`.
  - **Absent**:
    - `Components: web\iis` and `Components: web\service`;
    - `dism` and `IsIisInstalled`;
    - `{tmp}\web-firewall.ps1` and `{tmp}\web-config-bridge.ps1` in `[Files]` and `[Run]`.
  - **Present**:
    - `web-config-bridge.ps1`, `web-firewall.ps1`, `web-hosting-check.ps1`, `web-iis-setup.ps1`,
      `web-https-binding.ps1` and `web-iis-repair.ps1` installed to `{app}\Support`, with
      `Components: web`;
    - `Check: WebHostIsIis` on the `web-iis-setup.ps1` `[Run]` entry;
    - `Check: IsWebSelected` on `sc create`;
    - three items on the hosting page;
    - `if WizardSilent then Exit;` as the first statement of the hosting page's `OnActivate` and
      `OnClickCheck` handlers;
    - in `AkmlSqlSetup.iss`, `#include "web-hosting.iss"` before `#include "web-installer.iss"`.

### Implementation for Phase 2

- [ ] T009 Spike S1, on VM A only (never the owner's machines); record it in `specs/045-web-standalone-hosting/research.md`.
  - Compile setup with `[Components]` reduced to `web`.
  - Upgrade an install of the last release that was made with the default components
    (`web,web\iis,web\service`).
  - Record in `specs/045-web-standalone-hosting/research.md` § Spikes whether Inno accepts the
    previous names it no longer knows, with no error and no stray component state.
  - Record the form chosen for T017: the entries removed, or kept with `Check: False`.
- [X] T010 [P] Create `src/AkmlSql.Installer/web-hosting.iss`. It is `[Code]` only and pure:
  - `ReadConfigString(const Json, Key: String; var Value: String): Boolean`, modelled on
    `ReadConfigBool`: find `"Key"`, skip whitespace and `:`, then read the quoted value. It
    tolerates a BOM (I27).
  - `HostingAddress(Host, Exposure, ComputerName: String; EnginePort, IisPort: Integer): String`,
    per data-model §11. The computer name is a parameter, which keeps the function pure.
  - `IisReadinessFrom`, which takes the data-model §2 signals and a `var Reason`. It returns the
    state constants `IisNotEnabled`, `IisPartMissing`, `IisDisabled`, `IisToolsUnavailable` and
    `IisReady`, with the reason texts of data-model §2. PartMissing names the first part that is
    missing.

  Make T003 pass.
- [X] T011 Add `InferPreviousHost` and `InferPreviousExposure` to `src/AkmlSql.Installer/web-hosting.iss`, per data-model §3. Their inputs are the values the readers collect, including the texts of `applicationHost.config` and `config.json`. Make T004 pass.
- [X] T012 [P] Restructure `src/AkmlSql.Installer/web-config-bridge.ps1` (R8, R21).
  - Add a pure `Get-BridgeConfigJson`: it takes the existing JSON text, the port, the mode and the
    web root, and returns the new JSON text.
  - Add a `-WebRoot` parameter (default ""). `webRoot` is always written.
  - The entry point is guarded by `if ($MyInvocation.InvocationName -ne '.')`. It checks for
    administrator rights and writes the file as today.
  - Drop `#Requires`.

  Make T005 pass.
- [X] T013 [P] Restructure `src/AkmlSql.Installer/web-firewall.ps1` (R15, R17).
  - Add a pure `Get-FirewallPlan` (data-model §5).
  - Add an entry point, `-Apply -WebHost -Exposure -EnginePort -HttpsPort`, that runs the plan
    with today's netsh delete-then-add by name. `profile=any` is unchanged.
  - The entry point is guarded and checks for administrator rights. No `#Requires`.

  Make T006 pass.
- [X] T014 [P] Create `src/AkmlSql.Installer/web-hosting-check.ps1` (contract installer §5, R16).
  - **Parameters**: `-Url`, `-ExpectEngine` | `-ExpectBundle`, `-TimeoutSeconds 30` and
    `-ReasonPath`.
  - **Requests**: GET once a second until the timeout. TLS validation is off for this script's own
    requests only: the callback is set, then reset in `finally`.
  - **Exit codes**: 0, 2 and 1, as the contract defines them.
  - **On a failure**:
    - Name the program registered on the port, from
      `netsh http show servicestate view=requestq`. Parse only URLs and image paths, never the
      localized labels.
    - Write the reason to `-ReasonPath`.

  Make T007 pass.
- [X] T015 In `src/AkmlSql.Installer/AkmlSqlSetup.iss`, add `#include "web-hosting.iss"` immediately before `#include "web-installer.iss"`.
- [X] T016 Add the thin readers to `src/AkmlSql.Installer/web-installer.iss`.
  - **`ReadIisReadiness(RunProbe: Boolean; var Reason: String): Integer`**:
    1. Read HKLM64:
       - `SOFTWARE\Microsoft\InetStp` `MajorVersion`;
       - `InetStp\Components\{W3SVC,StaticContent,DefaultDocument,HttpErrors}`;
       - `SYSTEM\CurrentControlSet\Services\{W3SVC,WAS}\Start`.
    2. When every registry check passes and `RunProbe` is true, run the 64-bit probe:
       `{sys}\WindowsPowerShell\v1.0\powershell.exe -NoProfile -NonInteractive -Command "Import-Module WebAdministration -ErrorAction Stop; $null = Get-ChildItem IIS:\Sites -ErrorAction Stop"`.
    3. Pass the signals to `IisReadinessFrom`.
    4. Log `IIS readiness: {State} (InetStp=…, W3SVC=…, Parts=…, W3svcStart=…, WasStart=…, Probe=ok|failed|skipped)`.
  - **`ReadPreviousHosting`**, called from `Web_Init`:
    1. Read the registry values `Host`, `Exposure`, `BridgePort` and `IisMode`, 64-bit view first,
       then 32-bit.
    2. Read `AkmlWebServiceExists`.
    3. Read `{sys}\inetsrv\config\applicationHost.config` and
       `{commonappdata}\AKML SQL Web\config.json` as files.
    4. Set the globals `PreviousHost` and `PreviousExposure` with T011's functions.
- [X] T017 Replace the two sub-components with one hosting choice in `src/AkmlSql.Installer/web-installer.iss` and `src/AkmlSql.Installer/AkmlSqlSetup.iss` (R9; contract installer §1 and §3).
  - **Components.** `[Components]` keeps only `web`, or uses T009's fallback.
  - **The choice.** Add the `WebHost` global (`Service`/`IIS`/`None`) and the Check functions
    `WebHostIsService`, `WebHostIsIis` and `WebHostHasHost`.
  - **Every `Components: web\iis` and `Components: web\service` gate changes**:
    - the `web-iis-setup.ps1` `[Run]` entry gets `Check: WebHostIsIis`;
    - `sc create` gets `Check: IsWebSelected`;
    - the start type becomes `sc config AkmlSqlWebEngine start= auto` with
      `Check: WebHostIsService`, and `start= delayed-auto` with `Check: not WebHostIsService`.
      Both run only when the service exists.
    - the Support scripts in `[Files]` go to `{app}\Support` with `Components: web`. Drop `{tmp}`
      and `deleteafterinstall`. Add the new `web-hosting-check.ps1` (T014) there too, with
      `Components: web`.
    - the repair shortcut gets `Check: WebHostIsIis` for now; US6 retargets it.
    - `IsWebSiteHosted` becomes `WebHostIsIis` for now; US1 widens it.
  - **Firewall.** Delete the two `{tmp}\web-firewall.ps1` `[Run]` entries; T019 applies the plan
    instead. Keep `[UninstallRun]` (US6).
  - **An interim silent value**, until US5:
    - `/WEB_HOST=NONE` gives None;
    - `/WEB_HOST=IIS` gives IIS. Setup stops when `ReadIisReadiness(True)` is not Ready; this
      replaces `IsIisInstalled`.
    - otherwise `PreviousHost`, or Service on a new install.
- [X] T018 Rebuild `WebHostPage` in `src/AkmlSql.Installer/web-installer.iss` (contract installer §2).
  - **Items**:

    | Constant | Index | Caption |
    | --- | --- | --- |
    | `WebHostServiceIdx` | 0 | "Standalone Windows service (recommended)" |
    | `WebHostIisIdx` | 1 | "IIS" |
    | `WebHostNoneIdx` | 2 | "Don't host (files only)" |

  - **Layout**: the sub-caption; a shortened list with a word-wrapped `TNewStaticText` notice under
    it, following the OptionsNote precedent in `AkmlSqlSetup.iss`.
  - **`OnActivate`**:
    1. The silent guard.
    2. `ReadIisReadiness(True)` on every show.
    3. `ItemEnabled[WebHostIisIdx]` only when Ready.
    4. On the first show, preselect `PreviousHost`, or Service when there is none.
    5. If IIS is selected but not Ready, reset the selection to Service.
    6. The notice shows the readiness reason.
  - **`CheckListBox.OnClickCheck`**: the silent guard, then recompute the notice.
  - **`Web_NextButton`** copies the selection into `WebHost`.
  - **Removed**: the dism "Enable IIS now" dialog and `IsIisInstalled` (FR-006).

  Make T008 pass.
- [X] T019 Rework the start of `Web_PostInstall` in `src/AkmlSql.Installer/web-installer.iss` (contract installer §4, steps 1, 3 and 4).
  1. **Registry** (data-model §3): write `Host`, `Exposure`, `BridgePort` and `WebRoot` on every
     web install. Write `IisPort`, `HttpsPort` and `IisMode` for IIS only.
  2. **Log** `Web hosting: {h} (from page|/WEB_HOST|previous host)`.
  3. **Config**: run `{app}\Support\web-config-bridge.ps1 -Port -Mode -WebRoot`, passing `{app}\Web`
     for Service and "" otherwise.
  4. **Firewall**: run
     `{app}\Support\web-firewall.ps1 -Apply -WebHost -Exposure -EnginePort -HttpsPort`.

  Keep today's service start and the Running check.
- [ ] T020 Spike S2 on VM A: check the disabled IIS item and its wrapped reason; record it in `specs/045-web-standalone-hosting/research.md`.
  - Look at it at 100%, 150% and 200% DPI, in the light and dark wizard styles.
  - Try to select it with the keyboard, the mouse and accelerators.
  - Record the outcome in `specs/045-web-standalone-hosting/research.md` § Spikes.
  - If the check fails, move the reason into an `AddGroup` row after the options, in
    `src/AkmlSql.Installer/web-installer.iss`.
- [X] T021 Run `dotnet test tests/AkmlSql.Installer.Tests --filter "Category!=InstallerSmoke"` and an ISCC compile of `src/AkmlSql.Installer/AkmlSqlSetup.iss`. Record the results in `specs/045-web-standalone-hosting/baseline.md`.

**Checkpoint**: what works at the end of Phase 2.

- Setup compiles.
- The hosting choice alone decides IIS provisioning (FR-003; I2 fixed).
- The hosting page shows three choices with live readiness.
- The registry `Host` and `Exposure` values are written.
- The firewall follows the plan.

**Not yet**:

- The engine does not serve the pages. A Service install writes a `webRoot` that the engine
  ignores.
- Silent installs use the interim value.
- An interactive re-run with the web edition unticked leaves `WebHost` unset until T067 sets it
  from `PreviousHost`, so a standalone install gets `delayed-auto` on such a run until US3.
- Repair and uninstall are unchanged.

---

## Phase 3: User Story 1 — Install on a machine without IIS and use the web edition right away (Priority: P1) 🎯 MVP

**Goal**: with the defaults, the AKML SQL Web Engine service serves the web edition at
`http://localhost:47291/`. It starts with Windows, and the page connects to its own engine without
anyone typing an address. Setup makes no IIS change.

**Independent Test**: on VM A (no IIS), install with the defaults. The Finish link opens the
address, and the page goes Live with nothing typed. After a restart without signing in, the address
answers again. Quickstart rows 3.1–3.5, 3.12 and 3.13, plus gates 1.1, 1.2, 1.3 and 1.5.

### Tests for User Story 1 (write first; they must fail)

- [X] T022 [P] [US1] Add a `bridge.webRoot` round-trip to `tests/AkmlSql.Core.Tests/Config/ConfigManagerTests.cs`: the exact camelCase key `"webRoot"`; the default `""`; a file without the key reads `""`.
- [X] T023 [P] [US1] Create `tests/AkmlSql.Engine.Tests/Transports/StaticWebResponderTests.cs`.
  - **Fixture**: a temp web root holding `index.html`, `_framework/dotnet.js`, a `.wasm`, a `.css`
    and a `.sql`, behind a real transport started as `BridgeAccessTests.StartLocalhostAsync` does.
  - **Routing**: contract engine-static-host §2, rows 2–10 and the 503.
  - **Headers on each row** (§4): the CSP, `nosniff`, `Cache-Control: no-cache`,
    `X-AKML-Host: engine`, `ETag`, `Last-Modified`, `Allow`, `Retry-After`, and
    `text/plain; charset=utf-8` bodies.
  - **Content types**: the §5 table.
  - **HEAD**: GET's headers, no body.
  - **304**: by `If-None-Match`; by a whole-second `If-Modified-Since` against a file whose write
    time has a sub-second part; and with `If-None-Match` deciding alone when both are sent.
  - **Paths**:
    - `/` gives `index.html`;
    - `/editor?autorun=1` gives `index.html` with 200, then its 304;
    - `/missing.js` gives a `text/plain` 404;
    - a Range request gives the full 200, with no `Accept-Ranges`.
  - **The localhost Host allowlist**: `localhost`, `127.0.0.1` and `[::1]` pass on any port; the
    machine name and `evil.example` get 400.
- [X] T024 [US1] Extend `tests/AkmlSql.Engine.Tests/Transports/StaticWebResponderTests.cs`.
  - **The SC-013 table** (contract §8, rows 1–18). This is also spike S5.
    - Send each row as a raw `TcpClient` request line.
    - Place a `web.config`, an `index.html.br` and a secret file where the rows need them, and
      another secret file outside the root.
    - Assert that no bytes of any file other than `index.html` come back, and no listing. Record
      the status, but do not assert it.
  - **The §1a refusals**, each of which gives the plain page and one Warning, with no exit:
    - a relative path, a missing folder, a folder without `index.html`;
    - a volume root, a junction made with `mklink /J`;
    - a root that equals, contains or lies inside the config file's folder or `TokenStorePath`'s
      folder.
  - **Limits**:
    - 32 slow readers do not stop a 33rd request from completing, or from getting 503 within the
      wait;
    - a failing response does not leak a slot: after 33 failures, a request still succeeds.
- [X] T025 [P] [US1] Create `tests/AkmlSql.Engine.Tests/Transports/StaticWebResponderCspTests.cs`.
  - **CSP drift**: a `powershell.exe` 5.1 process parses `src/AkmlSql.Installer/web-iis-setup.ps1`
    with `[System.Management.Automation.Language.Parser]::ParseFile`, evaluates only the
    `$bridgeConnectSrc` and `$csp` assignments, and prints `$csp`. It must equal
    `StaticWebResponder.ContentSecurityPolicy`.
  - **Extension coverage**: every file extension is in the §5 table, except `.br`, `.gz` and
    `.config`. Check the composed Debug layout (`src/AkmlSql.Web/wwwroot` plus
    `src/AkmlSql.Web/bin/Debug/net10.0/wwwroot/_framework`) and, when it exists, the Release publish
    `wwwroot`.
- [X] T026 [P] [US1] Extend `tests/AkmlSql.Engine.Tests/Transports/BridgeAccessTests.cs`.
  - With no web root, the plain page comes back byte for byte on `/`, `/editor`, `/index.html` and
    `/_framework/dotnet.js`.
  - With a web root, `/akmlsql` and `/akmlsql/` keep the plain page, plus `X-AKML-Host: engine`.
  - An upgrade on `/` and on `/akmlsql` with a same-origin `Origin` works.
  - A foreign `Origin` still gets 403.
  - A foreign Host gets 400 in localhost mode.
- [X] T027 [P] [US1] Extend `tests/AkmlSql.Engine.Tests/EngineHostTests.cs`.
  - `BuildWebSocketTransport` maps `webRoot` only when `serveWebEdition` is true, and never on the
    `RunAsync` path.
  - A second listener holding the port makes the transport throw
    `port {p} is held by another process`, and `RunWebAsync` logs that message.
- [X] T028 [P] [US1] Extend `tests/AkmlSql.Web.Tests/Services/EngineEndpointTests.cs` with `EngineEndpoint.FromPage`. Cover every row of the contract web-default-engine §2 table, and the bridge URL each gives:
  - http on localhost;
  - `127.0.0.1:59417`;
  - https on a host with a port;
  - https on a host on 443;
  - `[::1]`.
- [X] T029 [P] [US1] Create `tests/AkmlSql.Web.Tests/Services/PageEngineTests.cs`, with a fake `HttpMessageHandler` and an injected clock.
  - The header present gives `Engine`, cached.
  - The header absent gives `Static`, cached.
  - A request that throws gives `Unknown`, not cached, and the next call tries again.
  - A later response with the header gives `Engine`.
  - 3 failures, or 60 s after the first failure, give `Static`, cached.
  - The request is a `HEAD /` with no-cache.
- [X] T030 [P] [US1] Create `tests/AkmlSql.Web.Tests/Bridge/EngineAutoConnectPrecedenceTests.cs`.
  - **Fakes**: `FakeBridgeWebSocket`, `FakeConnectionStore` and `FakePairingTokenVault` (see
    `ReconnectLoopTests.cs`), and a fake `IPageEngine`.
  - **Cases**:
    - An active record wins, even when its engine is down.
    - A dangling active id is cleared (`SetActiveIdAsync(null)`), and the page engine is used.
    - `Engine` + `Ok` stores one active record: `Name` "This computer" for loopback, the page's
      host, port and scheme, and `IsLocalhost`, with `LastKnownEngineVersion` and the capabilities.
    - A saved record with the page's host and port is reused, not duplicated.
    - `Static` gives `NotPaired` "No engine paired yet." and dials nothing.
    - `Unknown` gives `Unreachable` "Checking this page's engine…".
    - A socket failure gives `Unreachable`, and nothing is stored.
- [X] T031 [P] [US1] Extend `tests/AkmlSql.Web.Tests/Shell/StatusBarSegmentsTests.cs` and `tests/AkmlSql.Web.Tests/Bridge/StatusIndicatorTests.cs`.
  - On an `Engine` page, the "Use this computer's engine" title reads
    `This computer's engine ({host}:{port})`.
  - On an `Engine` page, `PairLocalEngineAsync` dials the page engine's host, port and scheme.
  - On any other page, both stay as today.
- [X] T032 [P] [US1] Extend `tests/AkmlSql.Web.Tests/Bridge/ConnectionStoreTests.cs`: `EngineConnection.StrictScheme` round-trips, and a stored record without it reads false.
- [X] T033 [P] [US1] Add the E2E harness in `tests/AkmlSql.Web.E2E.Tests/Harness/`.
  - **`tests/AkmlSql.Web.E2E.Tests/Harness/EngineServedBundle.cs`**:
    - It composes a temp folder from `src/AkmlSql.Web/wwwroot` and
      `src/AkmlSql.Web/bin/Debug/net10.0/wwwroot/_framework` (W15).
    - Its freshness guard fails with "run `dotnet build src/AkmlSql.Web -c Debug`" when
      `bin/Debug/net10.0/AkmlSql.Web.dll` is older than the newest `*.cs` or `*.razor` under
      `src/AkmlSql.Web`, bin and obj excluded.
  - **`tests/AkmlSql.Web.E2E.Tests/Harness/SandboxEngine.cs`** gains a `webRoot` option, written
    into its `config.json`.
- [ ] T034 [US1] Create `tests/AkmlSql.Web.E2E.Tests/EngineServedWebTests.cs`. It runs serially, on Debug engine and web builds.
  - A fresh browser context at the sandbox engine's address goes Live with no pairing and no
    typing (US1-4).
  - Every route of quickstart 3.4 reloads (SC-005).
  - No request fails or gets 400 or more, and the console shows no errors (SC-006).
  - The responses carry the contract §4 headers.
  - The §8 rows return no content (SC-013).
  - `/spike` gets its `.sql` files as `text/plain`, and formats SQL, not HTML.
- [X] T035 [P] [US1] Extend `tests/AkmlSql.Installer.Tests/WebInstallerSourceTests.cs`.
  - `if WizardSilent then Exit;` is the first statement of the ports page's `OnActivate`.
  - `Web_PostInstall` writes `DelayedAutostart` for Service.
  - `web-hosting-check.ps1` is called with `-ExpectEngine`.
  - `IsWebSiteHosted` uses `WebHostHasHost`, and `WebSiteUrl` uses `HostingAddress`.
  - `UpdateReadyMemo` exists and returns all six memo parts.
  - `PrepareToInstall` runs `icacls` with `/setowner *S-1-5-32-544` on the state folder.

### Implementation for User Story 1 — engine

- [X] T036 [P] [US1] Add `[JsonPropertyName("webRoot")] public string WebRoot { get; set; } = string.Empty;` to `BridgeOptions` in `src/AkmlSql.Core/Config/AppSettings.cs`. It is netstandard2.0-safe, and empty means off. Make T022 pass.
- [X] T037 [P] [US1] Add two properties to `src/AkmlSql.Engine/Transports/WebSocketTransportOptions.cs`:
  - `WebRoot` (`string?`);
  - `ConfigFilePath` (`string?`), used only for the §1a overlap refusal. The plan names only
    `WebRoot`, but the check needs the config file's folder.
- [X] T038 [US1] Create `src/AkmlSql.Engine/Transports/StaticWebResponder.cs`, part 1 (R3, R4, R8, data-model §7).
  - **Web-root validation** (§1a). A refusal logs one Warning:
    `WebSocketTransport: not serving the web edition from {WebRoot}: {Reason}`.
  - **The manifest walk**:
    - only extensions in §5;
    - no `*.config`, `*.br` or `*.gz`;
    - skip Hidden, System and ReparsePoint entries, and do not descend into reparse-point
      directories.
  - **Manifest entries**: `Key`, `FullPath`, `Length`, `LastWriteUtc`, `LastModified` (whole
    seconds), `ContentType`, and `ETag` = `"{Length:x}-{LastWriteUtc.Ticks:x}"`.
  - **The content-type table.**
  - **`public const string ContentSecurityPolicy`**, equal to the ps1's `$csp`.
  - **One Information line** when serving starts:
    `WebSocketTransport: serving the web edition from {WebRoot} ({Count} files)`.
- [X] T039 [US1] Complete `src/AkmlSql.Engine/Transports/StaticWebResponder.cs`, part 2 (contract §2–§4 and §6).
  - **Outcomes**: the request outcomes, in data-model §7 order.
  - **Path rules**:
    1. Unescape once.
    2. Reject `\`, `:`, `%`, `~`, NUL and control characters.
    3. `/` gives `index.html`.
    4. Empty, `.` and `..` segments, and segments that end in `.` or a space, get 400.
  - **Opening a file**: the manifest lookup first, then the `GetFullPath` containment check, then
    open it with `FileShare.Read`, async, sequential.
  - **Responses**:
    - validators and 304;
    - the fallback when the last segment has no `.`, and 404 otherwise;
    - HEAD with no body;
    - the length set with `ContentLength64` only.
  - **Limits**:
    - a 32-slot `SemaphoreSlim`, taken with a 10 s wait;
    - a deadline of 60 s plus `Length / 64 KiB` seconds, enforced with `Response.Abort()`;
    - the slot released in `finally`, and a catch-all that aborts the response.
  - **Logging**:
    - refusal Warnings, rate-limited globally to one a second, with the periodic
      `{Count} refused requests not logged` line;
    - `{Path}` cut to 200 characters;
    - a 503 Warning at most once a minute.

  Make T023, T024 and T025 pass.
- [X] T040 [US1] Route requests in `src/AkmlSql.Engine/Transports/WebSocketTransport.cs` (R2, contract §2).
  - Keep only the `IsWebSocketRequest` test inline on the accept loop.
  - Dispatch every other request with `Task.Run`, in this order:
    1. A null `Url` gets 400.
    2. With no responder, today's plain page, on every path.
    3. A method other than GET or HEAD gets 405 with `Allow: GET, HEAD`.
    4. In loopback mode only, a Host outside the localhost allowlist gets 400 `Bad host.`.
    5. `/akmlsql` and `/akmlsql/` get the plain page, plus `X-AKML-Host: engine`.
    6. Everything else goes to the responder.
  - When `HttpListener.Start` fails with 183 or 32, throw `port {p} is held by another process`.

  Make T026 pass.
- [X] T041 [US1] In `src/AkmlSql.Engine/EngineHost.cs`:
  - Give `BuildWebSocketTransport` `bool serveWebEdition = false`, plus the config path for the
    overlap check.
  - Map `WebRoot` only when `serveWebEdition` is true. Only `RunWebAsync` passes true.
  - Log the bind-failure message as such.

  Make T027 pass.

### Implementation for User Story 1 — web

- [X] T042 [P] [US1] Add `EngineEndpoint.FromPage(Uri pageBase)` to `src/AkmlSql.Web/Services/EngineEndpoint.cs`. It gives the host without IPv6 brackets, the explicit or scheme-default port, and the scheme (`http` gives `ws`, `https` gives `wss`). Make T028 pass.
- [X] T043 [P] [US1] Add `StrictScheme` (bool, default false) to `EngineConnection` in `src/AkmlSql.Web/Services/IConnectionStore.cs`. Make T032 pass.
- [X] T044 [P] [US1] Create `src/AkmlSql.Web/Services/PageEngine.cs`, and register it in `src/AkmlSql.Web/Program.cs`.
  - The types: `IPageEngine`, `PageEngine`, `PageEngineInfo`, and the `Engine`/`Static`/`Unknown`
    answer.
  - The request: `HEAD /` through the registered `HttpClient`, with
    `SetBrowserRequestCache(NoCache)`.
  - Caching follows contract web-default-engine §1. Nothing goes into browser storage.

  Make T029 pass.
- [X] T045 [US1] Add the page-engine branch to `EngineAutoConnect.TryOnceAsync` in `src/AkmlSql.Web/Services/IEngineAutoConnect.cs` (contract web-default-engine §3–§4).
  - `IPageEngine` is optional. It is used only where `TryOnceAsync` would otherwise return
    `NotPaired`.
  - Clear a dangling active id first.
  - Reuse a matching record.
  - Otherwise probe a new record with no PIN and no bearer, and store it only on `Ok`.
  - `Unknown` gives `Unreachable` "Checking this page's engine…".
  - On an `Engine` page, `PairLocalEngineAsync` uses the page engine's address.

  Make T030 pass.
- [X] T046 [US1] In `src/AkmlSql.Web/Shared/StatusBar.razor`, take the "Use this computer's engine" title from the page engine: `This computer's engine ({host}:{port})` on an `Engine` page, unchanged otherwise. Make T031 pass.

### Implementation for User Story 1 — setup

- [ ] T047 [US1] Spike S7 on VM A; record it in `specs/045-web-standalone-hosting/research.md`.
  - Install the last release to `D:\AKML3`.
  - Read the folder's owner with
    `(Get-Acl).GetOwner([System.Security.Principal.SecurityIdentifier])`.
  - Record the owner in `specs/045-web-standalone-hosting/research.md` § Spikes.
  - Choose R22's ownership refusal or its fallback for T051.
- [X] T048 [US1] Rework `WebPortsPage` in `src/AkmlSql.Installer/web-installer.iss` (R13, contract installer §2).
  - **`OnActivate`** starts with the silent guard, then shows the fields for the host:

    | Host | Fields |
    | --- | --- |
    | Service | one field: "Web edition port -- 1024 to 65535. The web edition and its engine share this address:" |
    | None | one field: "Engine port -- 1024 to 65535:" |
    | IIS | today's three fields |

  - **On Next, for Service**: a blocking connect-probe check.
    - The port is exempt only when the registry `BridgePort` = p **and** `AkmlSqlWebEngine` is
      Running or StartPending. Use the locale-safe `Get-Service` pattern from
      `TerminateAkmlBackgroundProcesses`.
    - Otherwise a listener blocks with "Port {p} is already used by another program. Choose
      another port."
  - **None and IIS** keep today's warning.
  - **"The site and engine ports must differ"** applies to IIS only.
  - Put the check in a function that T084 can reuse for silent installs.
- [X] T049 [US1] Finish `Web_PostInstall` for the standalone host in `src/AkmlSql.Installer/web-installer.iss` (contract installer §4, steps 5–9 and 11; R16, R20).
  1. **ACL.** Replace today's lockdown step, which sets no owner and runs after the config write,
     with a re-apply that sets the owner, before `sc start`.
  2. **Start type.** For Service, write `DelayedAutostart = 0` under
     `HKLM64\SYSTEM\CurrentControlSet\Services\AkmlSqlWebEngine`.
  3. **Start.** Start the service for every host, and keep the Running check for every host.
  4. **Answer check.** For Service + Localhost, run
     `{app}\Support\web-hosting-check.ps1 -Url http://127.0.0.1:{port}/ -ExpectEngine -ReasonPath …`.
  5. **Summary**, both the file and the wizard page (data-model §10):
     - `Hosting:` first;
     - `URL:` as the first address line;
     - for Service, `Bridge port: {port} (the same address serves the pages)`;
     - the warning "the address did not answer", with the reason, the program holding the port,
       and the repair shortcut;
     - the warning "the service is not running", with how to start it;
     - no IIS text for Service.
- [X] T050 [US1] In `src/AkmlSql.Installer/AkmlSqlSetup.iss`:
  - `IsWebSiteHosted` = `WebHostHasHost`.
  - `WebSiteUrl` = `HostingAddress(WebHost, Exposure, computer name, BridgePort, IisPort)`.
  - Add `UpdateReadyMemo`, which returns the whole memo: the six parts in order, each non-empty one
    followed by two new lines. When `IsWebSelected`, it adds "Web edition:" and the host line:
    "Standalone Windows service at …", "IIS at …" or "Files only, not hosted".
- [X] T051 [US1] Harden the folders at the start of `PrepareToInstall` in `src/AkmlSql.Installer/AkmlSqlSetup.iss` (R22, data-model §12). This runs before SSMS is closed and before `TerminateAkmlBackgroundProcesses`.
  - **The state folder**, when the web edition is ticked:
    1. `ForceDirectories`.
    2. `icacls … /setowner *S-1-5-32-544 /T /C`.
    3. `/inheritance:r`, granting Administrators F and SYSTEM F.

    A failure becomes a summary warning.
  - **The install folder**, when `{app}` is not under `{commonpf64}` or `{commonpf32}`:
    - refuse a reparse point and, depending on T047, a non-empty folder with a foreign owner;
    - otherwise set the owner and grant Administrators F, SYSTEM F, Users RX and IIS_IUSRS RX;
    - if that fails, return an error.
- [X] T052 [P] [US1] In `tests/AkmlSql.Web.E2E.Tests/Harness/InstalledEngine.cs`, make `InstalledEngine.SiteUrl` take the first whitespace-delimited token after `URL:`. The LAN summary line (US4), `https://PC:47291/  (every computer, this one included)`, then parses.
- [ ] T053 [US1] Spike S6 on VM A: run `sc qc AkmlSqlWebEngine` after each `sc config` and after T049's write. Record whether `start= auto` clears `DelayedAutostart` in `specs/045-web-standalone-hosting/research.md` § Spikes.
- [ ] T054 [US1] Run quickstart gates 1.1, 1.2, 1.3 and 1.5 (Debug builds first, E2E serially), plus T035 and an ISCC compile. Record the results in `specs/045-web-standalone-hosting/baseline.md`.
- [ ] T055 [US1] On VM A and VM C snapshot 1, run quickstart rows 3.1, 3.2, 3.3, 3.4, 3.5, 3.12 and 3.13. Record each result in `specs/045-web-standalone-hosting/baseline.md`; S2's visual result goes with 3.1.

**Checkpoint**: the MVP works.

- A default install on a machine without IIS serves the web edition at the engine's address.
- The page goes Live with nothing typed.
- It answers after a restart, before anyone signs in.
- SC-013 returns nothing.
- Ask "Ready to commit?".

---

## Phase 4: User Story 2 — Choose IIS on a machine where IIS is ready (Priority: P2)

**Goal**: IIS can be chosen only when it is ready. Then it hosts the web edition as today, with
`.sql` parity, and the answer check proves the bundle is served. The service runs the engine only.

**Independent Test**:

- On VM B, choose IIS: the site exists and loads.
- On VM C snapshot 2, the IIS choice is greyed and names the missing part.
- Quickstart rows 3.16 and 3.19, and the IIS part of 3.15.

### Tests for User Story 2 (write first; they must fail)

- [X] T056 [P] [US2] Extend `tests/AkmlSql.Installer.Tests/WebInstallerSourceTests.cs`.
  - `$mimes` in `web-iis-setup.ps1` maps `.sql` to `text/plain`.
  - No "32-bit" comment is left in `web-iis-setup.ps1`.
  - `Web_PostInstall` calls `web-hosting-check.ps1` with `-ExpectBundle` for IIS.
  - No summary text mentions "management scripting tools".

### Implementation for User Story 2

- [ ] T057 [US2] Spike S4 on VM B and on VM C snapshots 2 and 3; record it in `specs/045-web-standalone-hosting/research.md`.
  - Toggle each Common HTTP Feature, the management tools, and IIS itself.
  - Record in `specs/045-web-standalone-hosting/research.md` § Spikes:
    - the registry values and the probe result for each state;
    - which feature turns `ToolsUnavailable` into `Ready`;
    - the cold probe time, which must be under 5 s.
  - Put that feature's name in the `ToolsUnavailable` reason in
    `src/AkmlSql.Installer/web-hosting.iss`, in data-model.md §2, and in T003's expectations.
- [X] T058 [P] [US2] In `src/AkmlSql.Installer/web-iis-setup.ps1`, add `.sql` = `text/plain` to `$mimes` (FR-021 as amended), and drop the stale 32-bit comments.
- [X] T059 [US2] Finish the IIS branch of `Web_PostInstall` in `src/AkmlSql.Installer/web-installer.iss`.
  - Run `web-hosting-check.ps1 -Url http://localhost:{IisPort}/ -ExpectBundle`. Exit 2 becomes
    "answered but is not serving the web edition"; one example is IIS's welcome page on port 80.
  - The IIS failure text gives the reason and the repair shortcut, and no longer blames the
    scripting tools (R16).
  - `URL:` gives the site address.

  Make T056 pass.
- [ ] T060 [US2] On VM B and VM C, run quickstart rows 3.16 and 3.19, and the IIS part of 3.15, recording them in `specs/045-web-standalone-hosting/baseline.md`:
  - IIS on port 80 while Default Web Site runs;
  - then IIS on 8090, where `http://localhost:47291/` shows the plain page (FR-022).

  Record the results in `specs/045-web-standalone-hosting/baseline.md`.

**Checkpoint**: IIS works where it is ready and stays out of the way where it is not. Ask "Ready to
commit?".

---

## Phase 5: User Story 3 — Upgrade or re-run setup without losing a working web edition (Priority: P3)

**Goal**: an upgrade keeps the host, the address and the exposure. A switch shows the old and new
addresses before anything is installed, moves the web edition, and removes what the old host left
behind.

**Independent Test**:

- An IIS install on port X upgrades with IIS preselected, and the address and the saved connection
  still work.
- Re-running setup with Standalone shows the move text first. Afterwards only the service serves
  the web edition, and the AkmlSqlWeb site is gone.
- Quickstart rows 3.6, 3.9, 3.14, 3.15 (switching) and 3.18.

### Tests for User Story 3 (write first; they must fail)

- [X] T061 [P] [US3] Create `tests/AkmlSql.Installer.Tests/IisRemovalPlanTests.cs`. It dot-sources `src/AkmlSql.Installer/web-iis-remove.ps1`. Cases for `Get-IisRemovalPlan`:
  - with WebAdministration: the site, the unused app pool, the owned binding, `web.config` and the
    markers;
  - with `appcmd.exe` only;
  - with neither: the binding and the markers only;
  - an app pool that another application uses is kept;
  - a site still listed afterwards keeps `web.config` and writes `iis-remove.failed` with the
    manual command.

  Dot-sourcing the script changes nothing.
- [X] T062 [P] [US3] Extend `tests/AkmlSql.Installer.Tests/WebHostingProbeTests.cs` with `AddressChangeNotice(Old, New)` (R14).
  - A host switch, a port change, and a Service switch between localhost and LAN each give the
    move text exactly: "The web edition will move from {old} to {new}. …".
  - The same address, or an empty one on either side (None), gives "".
- [X] T063 [P] [US3] Extend `tests/AkmlSql.Installer.Tests/WebInstallerSourceTests.cs`.
  - `[InstallDelete]` holds `Type: filesandordirs; Name: "{app}\Web\_framework"; Components: web`.
  - `web-iis-remove.ps1` is installed to `{app}\Support`.
  - The hosting page and `UpdateReadyMemo` both call `AddressChangeNotice`.

### Implementation for User Story 3

- [X] T064 [P] [US3] Create `src/AkmlSql.Installer/web-iis-remove.ps1` (R18, contract installer §6).
  - **Parameter**: `-AppDir`.
  - **The plan**: a pure `Get-IisRemovalPlan`.
  - **The entry point**, in order:
    1. Remove the `AkmlSqlWeb` site, with WebAdministration, otherwise with `appcmd delete site`.
    2. Remove the app pool, only when no application uses it.
    3. Run `Remove-OwnedHttpsBinding`, by dot-sourcing `web-https-binding.ps1`.
    4. Delete `{AppDir}\Web\web.config`, but only once `applicationHost.config` no longer lists the
       site.
    5. Delete `iis-site.ok`, `https-site.ok` and `https-site.failed`.
  - **On failure**: when the site is still listed, write `iis-remove.failed`.
  - **Always**: log to `install.log`, exit 0, and never touch other sites or IIS settings.

  Make T061 pass.
- [X] T065 [P] [US3] Add `AddressChangeNotice(Old, New: String): String` to `src/AkmlSql.Installer/web-hosting.iss`. Make T062 pass.
- [X] T066 [US3] Finish the upgrade path of the wizard in `src/AkmlSql.Installer/web-installer.iss` (FR-030, FR-031, FR-033, FR-035).
  - **The hosting page notice** adds:
    - the FR-031 line, when `PreviousHost` = IIS and IIS is not Ready;
    - `AddressChangeNotice(HostingAddress(previous), HostingAddress(selected))`.
  - **`WebNetworkPage`** preselects `PreviousExposure`. This fixes I14 for interactive upgrades.
  - **The Service port check** blocks with "Port {p} belongs to the AKML SQL site in IIS, which
    this change removes. Choose a different port for the standalone service." when
    `PreviousHost` = IIS, `IisPort` = p and the site exists.
- [X] T067 [US3] Add the switching steps to `src/AkmlSql.Installer/web-installer.iss` (data-model §9; contract installer §4, step 2).
  - **`[Files]`**: `web-iis-remove.ps1` goes to `{app}\Support`, with `Components: web`.
  - **`Web_PostInstall`**:
    - when `PreviousHost` = IIS and `WebHost` is not IIS, run
      `{app}\Support\web-iis-remove.ps1 -AppDir "{app}"` before the config write;
    - turn `iis-remove.failed` into a summary warning that names the site and the removal command;
    - add the "address moved" summary line, with the new address.
  - **Web edition unticked**: set `WebHost` := `PreviousHost`, so the start-type checks keep a
    standalone install on `auto`.
  - **`[InstallDelete]`**: add `Type: filesandordirs; Name: "{app}\Web\_framework"; Components: web`.

  Make T063 pass.
- [X] T068 [US3] Add the move text to `UpdateReadyMemo` in `src/AkmlSql.Installer/AkmlSqlSetup.iss`, as the second line under "Web edition:", only when `AddressChangeNotice` is non-empty.
- [ ] T069 [US3] On VMs A, B and C, run quickstart rows 3.6, 3.9 (the host preselection is kept; the shortcut part waits for US6), 3.14, the switching part of 3.15, and 3.18. Record the results in `specs/045-web-standalone-hosting/baseline.md`.

**Checkpoint**: upgrades keep the host, and switches are announced and clean. Ask "Ready to
commit?".

---

## Phase 6: User Story 4 — Reach the web edition from other computers in LAN mode, on either host (Priority: P4)

**Goal**: in Standalone + LAN, every computer opens `https://<computer>:<port>/`, and its page dials
only `wss` to its own engine. On `PinRequired`, the picker opens prefilled. Setup adds one firewall
rule, and the summary gives the trust steps.

**Independent Test**: install Standalone + LAN on VM A. There is one `AKML SQL Web Engine` rule. On
VM D, open the summary's HTTPS address, trust the certificate, pair and run a query. Plain http from
VM D gets no app. Quickstart rows 3.7 and 3.8, plus spike S3.

### Tests for User Story 4 (write first; they must fail)

- [X] T070 [P] [US4] Extend `tests/AkmlSql.Web.Tests/Services/EngineEndpointTests.cs`.
  - `CandidateUrls(host, port, remembered, strictScheme: true)` returns only the remembered scheme
    for a loopback host.
  - Without `strictScheme`, a loopback host still gets `ws` and `wss`.
- [X] T071 [P] [US4] Extend `tests/AkmlSql.Web.Tests/Bridge/EngineAutoConnectPrecedenceTests.cs`.
  - On an https page, `Ok` stores the record with `StrictScheme` true and `wss`.
  - `Engine` + `PinRequired` stores nothing, returns `PairingRequired` with "This engine needs its
    pairing PIN. Pair it in Settings › Connections.", and sets `IPageEngine.PairingPrefill`.
  - An https loopback page engine whose `wss` fails is never dialled with `ws`.
- [X] T072 [P] [US4] Extend `tests/AkmlSql.Web.Tests/Bridge/ConnectionPickerPinTests.cs`.
  - On an `Engine` page, the Add form starts with the page engine's host and port.
  - With `PairingPrefill` set, the PIN field shows at once, including for
    `https://localhost:{port}/`.

### Implementation for User Story 4

- [X] T073 [P] [US4] Add `strictScheme` to `EngineEndpoint.CandidateUrls` in `src/AkmlSql.Web/Services/EngineEndpoint.cs`, and pass `connection.StrictScheme` from `EngineBridge.ConnectAsync` in `src/AkmlSql.Web/Services/IEngineBridge.cs`. Make T070 pass.
- [X] T074 [P] [US4] Add `PairingPrefill` to `IPageEngine` and `PageEngine` in `src/AkmlSql.Web/Services/PageEngine.cs`.
- [X] T075 [US4] In `src/AkmlSql.Web/Services/IEngineAutoConnect.cs`:
  - set `StrictScheme` on an https page's record, both in auto-connect and in
    `PairLocalEngineAsync`;
  - map `PinRequired` from the page engine to `PairingRequired`, with the message and
    `PairingPrefill`.

  Make T071 pass.
- [X] T076 [US4] In `src/AkmlSql.Web/Shared/ConnectionPickerComponent.razor`, start the Add form from the page engine, and show the PIN field at once when `PairingPrefill` is set. Make T072 pass.
- [X] T077 [US4] Finish Service + LAN in `Web_PostInstall` in `src/AkmlSql.Installer/web-installer.iss` (R16, data-model §10).
  - The answer check probes `https://localhost:{port}/ -ExpectEngine`.
  - The summary reads `URL: https://{COMPUTERNAME}:{port}/  (every computer, this one included)`.
  - The PIN, thumbprint and `bridge.cer` trust steps are presented as the recommended step.
  - Add the line "The firewall rule applies to every network profile, including Public. Use a
    Private or Domain network."
- [ ] T078 [US4] On VM A + VM D, run spike S3 and quickstart rows 3.7 and 3.8. Record S3 in `specs/045-web-standalone-hosting/research.md` § Spikes, and the rows in `specs/045-web-standalone-hosting/baseline.md`.

**Checkpoint**: LAN standalone pairs from another computer over HTTPS, with one rule and one
certificate. Ask "Ready to commit?".

---

## Phase 7: User Story 5 — Silent installs choose the host explicitly (Priority: P5)

**Goal**: `/WEB_HOST=SERVICE|IIS|NONE`.

- With no flag, a new install gets Service and an upgrade keeps its host.
- An invalid command line stops before any change, with the reason in the log.
- The smoke tests cover the matrix on the VMs.

**Independent Test**: on VMs A and B, run quickstart row 3.20, and the smoke classes with
`AKML_INSTALLER_SMOKE=1`.

### Tests for User Story 5 (write first; they must fail)

- [X] T079 [P] [US5] Extend `tests/AkmlSql.Installer.Tests/WebHostingProbeTests.cs` with `ResolveSilentHost` and `SilentNeedsIisProbe`.
  - **`ResolveSilentHost`**, data-model §4 rows 1–12, including:
    - an unknown value;
    - `NONE` with `LAN` on the command line stops;
    - a persisted None + Lan with no flags is kept;
    - IIS-only ports with Service or None stop;
    - in the row-9 fallback, IIS-only ports are ignored, and the log line names the reason and the
      new address.
  - **`SilentNeedsIisProbe`** is true for `/WEB_HOST=IIS`, and for no flag with previous host IIS.
    It is false for `SERVICE`, for `NONE`, and for no flag otherwise.
- [X] T080 [P] [US5] Extend `tests/AkmlSql.Installer.Tests/WebInstallerSourceTests.cs`.
  - `Web_ValidateSilentFlags` has no early exit when no web flag is given, and it calls
    `ResolveSilentHost`.
  - The Phase 2 interim silent mapping and `WebSilentDontHost` are gone.
  - In silent mode, `IsLanExposed` reads the resolved exposure.
- [X] T081 [P] [US5] Update the smoke harness in `tests/AkmlSql.Installer.Tests/InstallerSmokeEnv.cs`, `tests/AkmlSql.Installer.Tests/InstallerSmokeFixture.cs`, `tests/AkmlSql.Installer.Tests/IisProvisioningTests.cs`, `tests/AkmlSql.Installer.Tests/LanTlsTests.cs` and `tests/AkmlSql.Installer.Tests/ServiceHealthTests.cs`.
  - **Gates.** Split `CanRun` into:
    - `CanRunService`: an administrator;
    - `CanRunIis`: an administrator with IIS Ready, by the data-model §2 registry checks plus the
      64-bit probe.

    Both stay behind `AKML_INSTALLER_SMOKE=1`. The IIS classes gate on `CanRunIis`.
  - **The uninstaller path** comes from the uninstall key.
  - **Cleanup** removes both firewall rules, the `AkmlSqlWeb` app pool, the test sslcert, the
    `CN=AKML SQL Web Engine` certificate, and `Software\AKML SQL\Web`.
- [X] T082 [US5] Create `tests/AkmlSql.Installer.Tests/StandaloneSmokeFixture.cs` and `tests/AkmlSql.Installer.Tests/StandaloneSmokeTests.cs`, with the trait `InstallerSmoke` (VM only).
  - **Cases**:

    | Case | Command line | Gate |
    | --- | --- | --- |
    | `Standalone` | `/WEB_HOST=SERVICE` | `CanRunService` |
    | `LanStandalone` | `/WEB_HOST=SERVICE /WEB_EXPOSURE=LAN` | `CanRunService` |
    | `IisToStandaloneSwitch` | `/WEB_HOST=IIS`, then `/WEB_HOST=SERVICE` | `CanRunIis` |

  - **Assertions**:
    - the address answers with `X-AKML-Host: engine`;
    - `sc qc` shows AUTO_START without "(DELAYED)";
    - the state folder has the data-model §12 ACL;
    - the firewall rules match data-model §5.

### Implementation for User Story 5

- [X] T083 [P] [US5] Add `ResolveSilentHost` and `SilentNeedsIisProbe` to `src/AkmlSql.Installer/web-hosting.iss` (data-model §4, rows checked in order).
  - The signature: `ResolveSilentHost(FlagHost, FlagExposure, PreviousHost: String; IisReady, IisOnlyPortsGiven: Boolean; IisReason, NewAddress: String; var LogLine: String): String`.
  - A stop returns `Stop:` followed by the reason.

  Make T079 pass.
- [X] T084 [US5] Rewrite `Web_ValidateSilentFlags` in `src/AkmlSql.Installer/web-installer.iss` (R12, contract installer §10).
  - **Parsing.** Always resolve. Accept `SERVICE`, `IIS` and `NONE` in any case.
  - **The candidate host** is the flag's value, or `PreviousHost`.
  - **Readiness.** Run `ReadIisReadiness(SilentNeedsIisProbe(…))` and log it for every silent web
    install.
  - **Exposure and ports.** When the flags are absent, use the previous values. This fixes I14 for
    silent upgrades. `IsLanExposed` reads the resolved exposure.
  - **Stops.**
    - A `Stop:` result fails `InitializeSetup`, with the reason in the log.
    - For Service, T048's port check stops with "Port {p} is already used by another program."
  - **Log** the line `Web hosting: … (from /WEB_HOST|previous host|IIS fallback: <reason>)`.
  - Remove the Phase 2 interim mapping.

  Make T080 pass.
- [ ] T085 [US5] On VM A and VM B, run quickstart row 3.20 and the smoke classes, with `AKML_INSTALLER_SMOKE=1`. Record the results in `specs/045-web-standalone-hosting/baseline.md`.

**Checkpoint**: every documented `/WEB_HOST` value gives its documented result, and every invalid
command line stops before any change (SC-010). Ask "Ready to commit?".

---

## Phase 8: User Story 6 — Repair and uninstall work for either host (Priority: P6)

**Goal**: one repair shortcut restores whichever host is installed. Uninstall removes everything
setup set up, firewall rules and certificate included. It never waits on a dialog when silent, and
it no longer runs the elevated temp-folder scripts that older installs recorded.

**Independent Test**: for each host, break the hosting, run "Repair AKML SQL Web hosting", and check
that the address answers. Then uninstall, interactively and silently, and check that nothing
remains. Quickstart rows 3.10, 3.11, 3.17 and 3.21, and the shortcut part of 3.9.

### Tests for User Story 6 (write first; they must fail)

- [X] T086 [P] [US6] Extend `tests/AkmlSql.Installer.Tests/WebInstallerSourceTests.cs`.
  - The "Repair AKML SQL Web hosting" `[Icons]` entry keeps its name, has `Check: WebHostHasHost`,
    and runs `{app}\Support\web-hosting-repair.ps1`.
  - No `[UninstallRun]` entry is left for `AkmlSqlWebEngine` or for the firewall.
  - No `{tmp}\web-firewall.ps1` appears anywhere.
  - `UninstallSilent` guards both the web data prompt and the `usPostUninstall` desktop-settings
    prompt.
  - `[UninstallDelete]` has `{app}\Web`.
  - `web-iis-repair.ps1` holds the `Host` guard text.
- [X] T087 [P] [US6] Create `tests/AkmlSql.Installer.Tests/HostingRepairGuardTests.cs`. It dot-sources `src/AkmlSql.Installer/web-hosting-repair.ps1`. This class is an addition to the plan's list, for the security check in contract installer §7. The pure write-ACE classifier:
  - flags Authenticated Users Modify and Users Write;
  - allows Administrators, SYSTEM and TrustedInstaller;
  - ignores inherit-only CREATOR OWNER, and read or execute ACEs (Users RX, ALL APPLICATION
    PACKAGES RX).

  A default Program Files ACL passes.
- [X] T088 [P] [US6] Extend `tests/AkmlSql.Installer.Tests/ReRunAndUninstallTests.cs` (smoke, VM only).
  - **After an upgrade**, the current `unins*.dat` contains no `\is-`…`.tmp\` path (R23).
  - **After uninstall**, none of these remain:
    - any `AKML SQL Web*` firewall rule;
    - the `AkmlSqlWebEngine` service;
    - `{app}\Web`;
    - after a LAN install, the `CN=AKML SQL Web Engine` certificate;
    - `Software\AKML SQL\Web`.
  - **Silent uninstalls**: `/VERYSILENT` and `/VERYSILENT /SUPPRESSMSGBOXES` each finish within a
    timeout and keep the data folder.

### Implementation for User Story 6

- [X] T089 [P] [US6] Create `src/AkmlSql.Installer/web-hosting-repair.ps1` (contract installer §7, R17).
  - **Elevation and location.** It self-elevates. It reads `{app}` from the uninstall key's
    `InstallLocation`.
  - **Refusal.** Both before and after elevating, it refuses to run when either holds:
    - `$PSScriptRoot` is not `{app}\Support`;
    - `{app}` has a write ACE for a non-admin SID (the pure classifier of T087).
  - **Settings.** It reads `Host`, `Exposure`, `BridgePort`, `IisPort` and `HttpsPort`.
  - **The IIS branch**:
    1. In LAN mode, run `web-tls-setup.ps1`.
    2. Run the `web-iis-repair.ps1` flow.
    3. Apply the firewall plan.
    4. Apply `delayed-auto` and the failure actions.
    5. Start the service.
    6. Run the `-ExpectBundle` check.
  - **The Service branch**:
    1. If the service is missing, create it with setup's binPath.
    2. Apply `start= auto`, `DelayedAutostart = 0`, the failure actions and `failureflag`.
    3. Lock down the state folder, owner included.
    4. Run `web-config-bridge.ps1 -WebRoot {app}\Web`.
    5. In LAN mode, run `web-tls-setup.ps1`. In either mode, apply the firewall plan.
    6. Restart the service if it is running, otherwise start it, and wait for Running.
    7. Run the `-ExpectEngine` check.
    8. In LAN mode, print the PIN read after the restart.
  - **None, or nothing installed**: print "Nothing to repair: the web edition is not hosted on this
    computer."
  - **At the end**, print the result and wait for Enter.

  Make T087 pass.
- [X] T090 [P] [US6] Add a guard to `src/AkmlSql.Installer/web-iis-repair.ps1`: when the registry `Host` value exists and is not `IIS`, print "This computer does not host the web edition on IIS." and stop.
- [X] T091 [US6] In `src/AkmlSql.Installer/web-installer.iss`:
  - install `web-hosting-repair.ps1` to `{app}\Support`;
  - point the "Repair AKML SQL Web hosting" shortcut at it, with `Check: WebHostHasHost`;
  - for None, delete `{group}\Repair AKML SQL Web hosting.lnk` in `Web_PostInstall` (contract
    installer §4, step 10).
- [X] T092 [US6] Rewrite `Web_Uninstall` in `src/AkmlSql.Installer/web-installer.iss` (R19, contract installer §8). It runs at `usUninstall`.
  1. Stop `AkmlSqlWebEngine`, wait up to 10 s, then delete it.
  2. Delete both firewall rules with direct `netsh advfirewall firewall delete rule name=…` Execs.
  3. Remove the bridge sslcert, and run `web-https-binding.ps1 -RemoveOwned`.
  4. Run `web-iis-remove.ps1` when it is present; otherwise today's inline removal. Skip quietly
     when IIS is gone.
  5. Delete the AKML certificate, found by `certs\thumbprint.txt` or by `CN=AKML SQL Web Engine`,
     but only while no `netsh http show sslcert` binding uses its hash.
  6. Ask about the data:
     `if UninstallSilent then Log('keeping AKML SQL Web data') else if MsgBox(…) = IDYES then DelTree(…)`.
  7. Delete `Software\AKML SQL\Web` in both registry views.

  Also remove the web `[UninstallRun]` entries, and add
  `[UninstallDelete] Type: filesandordirs; Name: "{app}\Web"`.
- [X] T093 [US6] In `src/AkmlSql.Installer/AkmlSqlSetup.iss`, guard the `usPostUninstall` desktop-settings prompt with `UninstallSilent`: a silent uninstall keeps the settings and logs that (FR-071 as amended).
- [X] T094 [US6] Add the fresh uninstall log to `src/AkmlSql.Installer/AkmlSqlSetup.iss` (R23, contract installer §9), following the `MigrateLegacy32BitInstall` pattern.
  1. In `PrepareToInstall`, find `unins???.dat` from the uninstall key's `UninstallString`.
  2. When that file holds the UTF-16 bytes of `\is-` followed by `.tmp\`, rename the `.dat` and the
     `.exe` aside.
  3. `DeinitializeSetup` restores them after a failed install, and deletes them after a successful
     one.
- [ ] T095 [US6] On VMs A, B and D, run quickstart rows 3.10, 3.11, 3.17 and 3.21, and the shortcut part of 3.9. Record the results in `specs/045-web-standalone-hosting/baseline.md`.

**Checkpoint**: repair restores either host. Uninstall leaves nothing and never blocks when silent.
Ask "Ready to commit?".

---

## Phase 9: Polish & cross-cutting concerns

**Purpose**: the documents (FR-070, quickstart §5), the full gate, and the owner's installs.

Each document covers these points:

- the three hosting choices, and when IIS counts as ready;
- the standalone address and its single firewall rule;
- the silent flags: `/WEB_HOST=SERVICE|IIS|NONE`, and `/WEB_PORT` and `/WEB_HTTPS_PORT` as IIS only;
- that switching host changes the address;
- the multi-session limit, wherever the page lists the security notes.

- [X] T096 [P] Update `doc/deployment.md` with the points above.
- [X] T097 [P] Add a `bridge` section to `doc/configuration.md`, including `webRoot`, and the registry values `Host`, `Exposure` and `WebRoot`.
- [X] T098 [P] Describe the static responder on the bridge listener in `doc/architecture.md` §9d.
- [X] T099 [P] Update the transport table in `doc/ipc-api.md`: what is served on non-upgrade requests, and that no message type changed.
- [X] T100 [P] Update `doc/m3-security.md`: the static surface; the manifest and containment rule; the refusal of a web root that overlaps the state folder; the localhost Host allowlist; the multi-session note.
- [X] T101 [P] Update `doc/WEB/00-INDEX.md` and `doc/WEB/M3-websocket-transport.md` with the points above.
- [X] T102 [P] Update `doc/WEB/quickstart-m3.md` and `doc/WEB/quickstart-m4.md` with the points above.
- [X] T103 [P] Add a note to `doc/WEB/M4-iis-installer.md` that this feature supersedes it. Add the standalone origin to `doc/WEB/ai-local-provider-cors.md`.
- [ ] T104 [P] Update `doc/topics/browser-setup.md` (public docs) with the points above, then run `dotnet test tests/AkmlSql.Site.Tests` and keep it green.
- [X] T105 [P] Update `CLAUDE.md`.
  - **Installer Details**: the hosting choices and the silent flags.
  - **Key Paths**: the registry values.
  - **The latest-work list**: a spec 045 entry.
- [X] T106 [P] Add the 045 entry to `doc/progress.md`: the per-phase table and the spike outcomes, and the 045 deferred list taken from research.md § Found, not fixed.
- [ ] T107 Run the full gate, quickstart 1.6 and 1.7, and record it in `specs/045-web-standalone-hosting/baseline.md`.
  - Builds: the solution restore and Release build, then the Debug builds.
  - Suites: the full engine, web, Core and installer (`Category!=InstallerSmoke`) suites; the E2E
    suite, serially; and ISCC.
  - Compare against `baseline/before` with `scripts/compare-test-results.ps1`.
  - Record the results in `specs/045-web-standalone-hosting/baseline.md`.
- [ ] T108 **Only with the owner's approval**: run quickstart 4.1 and 4.2, interactive upgrades of the workstation (IIS `:8083`, localhost) and the server (IIS `:59416`, LAN). Note the browser's saved connections first. Record the results in `specs/045-web-standalone-hosting/baseline.md`.

---

## Deferred (recorded with reasons; constitution Development Workflow)

These come from research.md § Found, not fixed. They are not fixed in this feature, and T106 copies
them into `doc/progress.md`.

- `CurPageChanged` compares page IDs with `>` (I3).
- The engine's LAN start parses netsh's English "Certificate Hash" label.
- `web-tls-setup.ps1` deletes any sslcert binding on the engine port without an ownership check.
  Leaving LAN mode does not remove the bridge sslcert (I9).
- A bad web flag in an interactive run exits with only a log line (I14).
- The web app checks `refactoring.heavy`, which the engine never advertises.
- Nothing checks for a disabled HTTP driver (`Services\HTTP\Start=4`).
- The IIS site's plain-HTTP binding listens on every interface (I25). This is the SC-009 exception
  for IIS, as amended.
- The LAN firewall rules use `profile=any`. The summary says so (T077); making the profile a
  parameter is deferred.
- The page origin can be squatted on a multi-session host in localhost mode (R6). This is
  documented in T096, T100 and T104, not fixed.

---

## Dependencies & Execution Order

### Phase dependencies

| Phase | Depends on |
| --- | --- |
| **Setup** (Phase 1) | nothing. T001 comes first, so no installer test can touch a real install. |
| **Foundational** (Phase 2) | Phase 1. It blocks every installer task of every story. US1's engine and web tasks (T022–T034, T036–T046) need only Phase 1, and can run in parallel with Phase 2. |
| **US1** (Phase 3, MVP) | Phase 2, for its setup tasks T035 and T047–T055 |
| **US2** (Phase 4) | Phase 2. It is independent of US1, apart from VM row 3.15 comparing against Standalone. |
| **US3** (Phase 5) | Phase 2 and US1. Switching to Standalone needs the engine to serve the pages. Its Service → IIS rows need US2's T059. |
| **US4** (Phase 6) | US1: the web tasks extend T042–T045, and T077 extends T049. |
| **US5** (Phase 7) | Phase 2, and US1's T048, whose port-check function T084 reuses |
| **US6** (Phase 8) | Phase 2 and US3's T064: `Web_Uninstall` step 4 and the IIS → None path run `web-iis-remove.ps1`. The smoke assertions in T088 use US5's harness (T081). |
| **Polish** (Phase 9) | every story in the release. T108 also needs the owner's approval. |

### Spikes gate the work that trusts them

| Spike | Gates |
| --- | --- |
| S1 (T009) | T017's component form |
| S2 (T020) | the reason layout in T018 |
| S4 (T057) | the `ToolsUnavailable` reason text |
| S7 (T047) | T051's ownership rule |

S6 (T053) only records a result: T049's explicit write covers both outcomes. S3 (T078) proves the
LAN design on real machines. S5 is gate 1.1 (T024).

### Cross-story touch points (soft dependencies)

- **`src/AkmlSql.Installer/web-installer.iss`** is edited by Phase 2 (T016–T019), US1 (T048,
  T049), US2 (T059), US3 (T066, T067), US4 (T077), US5 (T084) and US6 (T091, T092). Land them in
  phase order. Never run two of them at once.
- **`src/AkmlSql.Installer/AkmlSqlSetup.iss`** is edited by T015, T017, T050, T051, T068, T093 and
  T094. `PrepareToInstall` is touched by T051 (hardening) and T094 (fresh log): keep the hardening
  first.
- **`src/AkmlSql.Installer/web-hosting.iss`** grows in T010, T011, T057, T065 and T083. All of them
  are additive.
- **`tests/AkmlSql.Installer.Tests/WebInstallerSourceTests.cs`** grows in T008, T035, T056, T063,
  T080 and T086. **`WebHostingProbeTests.cs`** grows in T003, T004, T062 and T079.
- **`src/AkmlSql.Web/Services/IEngineAutoConnect.cs`** is edited by T045 (US1) and T075 (US4).
  **`PageEngine.cs`** is edited by T044 and T074.
- **`tests/AkmlSql.Web.Tests/Bridge/EngineAutoConnectPrecedenceTests.cs`** is created in T030 and
  extended in T071.

### Within each phase

- Tests come first, and must fail. A spike is recorded before the work it gates is trusted.
- Pure functions and scripts come before the `[Code]` that calls them.
- The engine comes before the E2E checks. The setup code comes before the VM rows.

### Parallel opportunities

- **Phase 2**:
  - the tests T003 and T005–T008 run in parallel;
  - then T010, T012, T013 and T014 run in parallel;
  - T015–T019 run in sequence, because they share the `.iss` files.
- **US1**:
  - the tests T022, T023 and T025–T033 run in parallel, then T024 and T034;
  - the engine tasks T036 and T037 run in parallel, then T038 → T039 → T040 → T041;
  - the web tasks T042, T043 and T044 run in parallel, then T045 and T046;
  - the engine and web chains run in parallel with each other, and with Phase 2;
  - T052 can run any time.
- **US2**: T056 and T058 run in parallel.
- **US3**: T061–T065 run in parallel; then T066 → T067 → T068.
- **US4**: T070–T074 run in parallel; then T075 → T076; T077 is independent of the web tasks.
- **US5**: T079, T080, T081 and T083 run in parallel; then T082 and T084.
- **US6**: T086–T090 run in parallel; then T091 → T092; T093 → T094.
- **Polish**: T096–T106 run in parallel.

---

## Parallel Example: User Story 1 (tests)

```bash
# After Phase 1 (Phase 2 may still be in progress), launch the US1 engine and web tests together:
Task: "ConfigManagerTests.cs webRoot round-trip"        # tests/AkmlSql.Core.Tests/Config/
Task: "StaticWebResponderTests.cs routing + headers"    # tests/AkmlSql.Engine.Tests/Transports/
Task: "StaticWebResponderCspTests.cs"                   # tests/AkmlSql.Engine.Tests/Transports/
Task: "BridgeAccessTests.cs"                            # tests/AkmlSql.Engine.Tests/Transports/
Task: "EngineHostTests.cs"                              # tests/AkmlSql.Engine.Tests/
Task: "EngineEndpointTests.cs FromPage"                 # tests/AkmlSql.Web.Tests/Services/
Task: "PageEngineTests.cs"                              # tests/AkmlSql.Web.Tests/Services/
Task: "EngineAutoConnectPrecedenceTests.cs"             # tests/AkmlSql.Web.Tests/Bridge/
Task: "StatusBarSegmentsTests.cs + StatusIndicatorTests.cs"
Task: "ConnectionStoreTests.cs StrictScheme"            # tests/AkmlSql.Web.Tests/Bridge/
Task: "EngineServedBundle.cs + SandboxEngine webRoot"   # tests/AkmlSql.Web.E2E.Tests/Harness/
```

## Parallel Example: Phase 2 (scripts and pure functions)

```bash
# After T003–T008 are written:
Task: "web-hosting.iss: ReadConfigString, HostingAddress, IisReadinessFrom"
Task: "web-config-bridge.ps1 restructure"
Task: "web-firewall.ps1 restructure"
Task: "web-hosting-check.ps1"
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1: T001 (the safety gate), then the baseline.
2. Phase 2, the setup hosting model. In parallel, US1's engine and web chains.
3. US1's setup tasks and spikes S6 and S7.
4. **Stop and validate**: gates 1.1, 1.2, 1.3 and 1.5, then VM rows 3.1–3.5, 3.12 and 3.13.
5. Demo: a machine without IIS opens the web edition from the Finish link, with nothing typed.

### Incremental delivery

1. **US1**: the failure the user hit is fixed (MVP).
2. **US2**: IIS stays available where it can work.
3. **US3**: existing installs upgrade safely, and switches are clean. The owner's installs depend
   on this, so ship it before any release reaches them.
4. **US4**: LAN standalone.
5. **US5**: the silent matrix and smoke coverage. The update notification runs silent setup, so
   US5 must land before a release goes out through the updater.
6. **US6**: repair and uninstall.

Each story stops at its checkpoint with its quickstart rows recorded.

### Parallel team strategy

- One person on the engine and web chain of US1, then US4's web tasks.
- A second person on Phase 2 and every story's setup tasks, in phase order, because they share
  `web-installer.iss` and `AkmlSqlSetup.iss`.
- VM rows run when each story's code is in: one session per VM, with a snapshot before each
  install.

---

## Notes

- [P] tasks touch different files and have no unfinished dependencies.
- The SSMS extension, the IPC contract and the web edition's features stay unchanged, except where
  FR-013, FR-014 and FR-071 say otherwise.
- Verify that the tests fail before you implement. Record each spike in research.md before you
  build on it.
- Never run the smoke classes or the quickstart §3 rows on the owner's machines.
- Deliver uncommitted, and ask "Ready to commit?" at each checkpoint.
