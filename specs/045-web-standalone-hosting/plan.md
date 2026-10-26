# Implementation Plan: Web edition hosting without IIS — a standalone Windows service by default

**Branch**: `045-web-standalone-hosting` | **Date**: 2026-10-26 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/045-web-standalone-hosting/spec.md`; plan-phase
codebase map in [codebase-map.md](./codebase-map.md)

## Summary

Make the web edition work as soon as setup finishes, on any Windows machine. By default the
existing **AKML SQL Web Engine** Windows service serves the web edition's pages at its own
address. One port carries both the pages and the engine:
- `http://localhost:47291/` in localhost mode;
- `https://<computer>:47291/` in LAN mode, with the existing certificate and one firewall rule.

**IIS** stays a choice, but only when it is ready. Ready means the web server and the parts the
site uses are enabled, its services are not disabled, and IIS's management tools load in 64-bit
PowerShell. When IIS is not ready, the choice is shown disabled with the reason. Setup never
turns IIS on.

**Upgrades** keep their host and address. Installs from before this feature that never had a
working IIS site get the standalone host. A page served by the engine connects to that engine
without anyone typing an address.

**Approach** ([research.md](./research.md) R1–R23, revised after an adversarial review):

- **Engine.**
  - A static responder inside `WebSocketTransport`, behind a new `bridge.webRoot` key that only
    the web-mode engine honours. It answers non-upgrade requests off the accept loop, with
    bounded slots, deadlines and no slot leaks.
  - It serves only a manifest of bundle files built at start, and validates paths before any
    filesystem call. The web root itself must not overlap the state folder.
  - Headers: the IIS site's CSP (checked against the ps1 by a drift test), `no-cache` with
    ETag/Last-Modified and 304, `nosniff`, and `X-AKML-Host: engine`.
  - Deep links fall back to `index.html` with 200. `/akmlsql` keeps the certificate-trust page.
- **Web.**
  - `IPageEngine` reads that header from a `HEAD /`, with three answers and only definitive ones
    cached.
  - When nothing usable is active, `EngineAutoConnect` dials the page's own origin, keeping the
    page's scheme strictly. On `PinRequired` it prefills pairing.
- **Setup.**
  - One code-owned hosting choice (Service / IIS / None) replaces the `web\iis` and
    `web\service` sub-components.
  - Pure functions for readiness, previous host and exposure, silent resolution and the
    address, with probe tests.
  - The choice and the exposure are persisted.
  - `PrepareToInstall` hardens a custom install folder and the state folder, and starts a fresh
    uninstall log when the old one holds the temp-folder script entries.
  - Restructured Support scripts: config, firewall plan, IIS removal, answer check, and a repair
    that serves both hosts.
  - Uninstall work moves into code: stop the service first, delete the rules and the
    certificate directly, never block when silent.

**Spec amendments made at plan time** (2026-10-26, in [spec.md](./spec.md)):
1. **The owner's installs.** The workstation is on IIS `:8083` (localhost); the server is on IIS
   `:59416` (LAN, HTTPS 59418, engine 59417). SC-007 checks both.
2. **The cause of the old IIS failures.** It was the fixed 32-bit PowerShell trap. Readiness is
   a functional check (Assumptions).
3. **What a change of address loses.** Query history is kept, because it is engine-side
   (Today, US3, FR-033).
4. **Confirmed defects**, recorded in Today:
   - "Don't host" and `/WEB_HOST=NONE` still provision IIS;
   - firewall rules leak on uninstall;
   - LAN mode is forgotten on upgrade;
   - IIS's untracked `web.config`;
   - stale files in the web folder.
5. **FR-043.** None still installs the engine service.
6. **SC-006.** It is compared against a clean IIS install, with deliberate differences recorded.
7. **FR-021.** IIS also serves `.sql` as plain text, for parity.
8. **FR-035.** A standalone switch between localhost and LAN is an address change too.
9. **FR-071.** The desktop-settings question at uninstall is skipped, and the settings kept, in a
   silent uninstall.
10. **SC-009 and US4-4.** The IIS site's plain-HTTP port stays as it is today: setup opens no
    rule for it. IIS's all-interface HTTP binding is recorded as found, not fixed.
11. **The readiness Assumption.** It now names the IIS parts the site uses: Static Content,
    Default Document and HTTP Errors.

## Technical Context

**Language/Version**:
- C# 14 / .NET 10 (engine, web, tests);
- `AkmlSql.Core` netstandard2.0 + net10.0 (one property);
- Blazor WebAssembly .NET 10;
- Inno Setup 7.1 Pascal Script;
- Windows PowerShell 5.1 (setup scripts).

**Primary Dependencies**: existing only. These are `System.Net.HttpListener` (HTTP.SYS),
System.Text.Json, Serilog, xunit 2.x, bUnit and Playwright 1.*. No package is added, and there
is no ASP.NET Core or Kestrel.

**Storage**:
- `%ProgramData%\AKML SQL Web\config.json`: `bridge.webRoot` added.
- `HKLM64\Software\AKML SQL\Web`: `Host` and `Exposure` added, and `WebRoot` is now always
  written.
- Browser IndexedDB: the `EngineConnection` record gains `StrictScheme` (default false).

**Testing**:
- engine and Core unit tests, including raw `TcpClient` cases for the SC-013 table;
- web unit tests (xunit and bUnit);
- a web E2E suite that serves a composed Debug bundle from `SandboxEngine`, run serially on
  Debug builds behind a freshness guard;
- installer tests that install nothing: probe-ISS, PowerShell dot-source and process tests, and
  source-text facts;
- installer smoke tests on disposable VMs only, opted in with `AKML_INSTALLER_SMOKE=1`.

**Target Platform**: the setup's existing range (Windows 10 and 11, Windows Server 2016 and
later), x64. Browsers: Chromium, Edge and Firefox.

**Project Type**: an out-of-process engine (Windows service), a Blazor WASM web app and an Inno
Setup installer.

**Performance Goals**:
- A second load revalidates with 304 instead of downloading 26 MB again.
- 32 file slots, each taken with a 10 s wait.
- The readiness probe takes about 1 s warm and under 5 s cold (S4).
- The answer check has a 30 s budget.
- The page answers within 3 minutes of boot (SC-004): `start= auto` with
  `DelayedAutostart = 0`.

**Constraints**:
- No new port, listener or HTTP.SYS registration; no IPC message, key or capability.
- The SSMS extension is unchanged, and the SSMS-started engine never serves pages.
- `AkmlSql.Core` stays netstandard2.0-safe.
- No filesystem access before path validation; the service is LocalSystem.
- No per-request logging, because the service log defaults to Debug.
- The smoke tests never run on the owner's machines.
- Constitution V: reuse the existing listener, scripts, ownership record and test patterns.

**Scale/Scope**:
- 1 Core file.
- 4 engine files, 1 of them new.
- 7 web files, 1 of them new.
- 10 installer files, 4 of them new: `web-hosting.iss`, `web-iis-remove.ps1`,
  `web-hosting-repair.ps1` and `web-hosting-check.ps1`. Two are restructured:
  `web-config-bridge.ps1` and `web-firewall.ps1`.
- About 17 test classes, 12 of them new, plus harness changes.
- 14 documents.
- 6 user stories.

## Constitution Check

*GATE: passed before Phase 0, and re-checked after Phase 1 and after the plan review (see the
end of this file).*

| Principle | Status | Evidence |
| --- | --- | --- |
| I. Process isolation & host safety | PASS | The new capability is engine-side (`Transports/StaticWebResponder.cs`), and only the web-mode host passes it (`RunWebAsync`). The shell is untouched. The SSMS-started engine never serves pages (R8). |
| II. Build integrity | PASS | No SDK or package change. The solution builds in one pass, and ISCC compiles setup (quickstart 1.7). No theme tokens are touched. |
| III. Tests & corpora non-regressible | PASS | Every touched project has tests in its existing test project (quickstart §1). The corpora and goldens are untouched, and the full suites are re-run (1.6). |
| IV. Git consent | PASS | The plan makes no git mutation. Implementation delivers uncommitted changes and asks before committing. |
| V. Simplicity & convention fidelity | PASS with justification | Reused: the existing listener, port, certificate binding and firewall rule names; the `web-https-binding.ps1` ownership record; `web-firewall.ps1`; the `MigrateLegacy32BitInstall` pattern; the `ReadConfigBool` pattern; the `HttpsBindingDecisionTests` and `ProcessDetectionTests` test patterns; the OptionsNote UI pattern. Three items are justified under Complexity Tracking. |

**Additional constraints**:
- **Security & robustness**:
  - `Path.GetFullPath` containment, never a substring check (R3).
  - **FR-020** is now enforced: a custom install folder and the state folder are re-owned and
    re-ACL'd before anything runs (R22).
- **"IPC stays local-only … no network listener"**: the bridge listener predates 045. This
  change adds a read-only surface to it (Complexity Tracking #1).
- **Secrets**: never served. They live in a separate tree, and a web root that overlaps it is
  refused.
- **Technology pins**: unchanged.
- **Documentation currency**: `CLAUDE.md` and every document in quickstart §5 are updated in
  the same change. Deferred items go to `doc/progress.md`.

## Project Structure

### Documentation (this feature)

```text
specs/045-web-standalone-hosting/
├── plan.md              # this file
├── spec.md              # feature spec (amended at plan time, see Summary)
├── codebase-map.md      # verified facts from 4 mappers + 4 verifiers, plus review findings (I23–I28, E20, P7–P8)
├── research.md          # Phase 0: decisions R1–R23, spikes S1–S7, found-not-fixed (revised after review)
├── data-model.md        # Phase 1: choice, readiness, registry, silent resolution, firewall plan, config, responder, page engine, switching, summary, address, ACLs
├── quickstart.md        # Phase 1: acceptance gate (automated, spikes, 21 VM rows, owner installs, docs)
├── contracts/
│   ├── engine-static-host.md   # switch, routing + limits, path rules, headers, types, logging, IIS differences, the SC-013 request table
│   ├── web-default-engine.md   # detection (three answers), address + strict scheme, precedence, connect flow, UI
│   └── installer.md            # files, wizard, service, order of work, check, removal, repair, uninstall, fresh log, silent, Finish
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit.tasks), not created here
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Core/Config/AppSettings.cs                # BridgeOptions.WebRoot ("webRoot", default "")
├── AkmlSql.Engine/
│   ├── EngineHost.cs                                 # BuildWebSocketTransport(bridge, serveWebEdition = false); RunWebAsync passes true; bind-failure message
│   └── Transports/
│       ├── WebSocketTransportOptions.cs              # + WebRoot
│       ├── WebSocketTransport.cs                     # non-upgrade requests off the loop; responder routing; localhost Host check; port-held message
│       └── StaticWebResponder.cs                     # NEW: root validation, manifest, path rules, containment, ETag/304, types, CSP constant, X-AKML-Host, fallback, slots/deadline, rate-limited refusal log
├── AkmlSql.Web/
│   ├── Program.cs                                    # register IPageEngine
│   ├── Services/
│   │   ├── EngineEndpoint.cs                         # + FromPage(Uri); CandidateUrls(..., strictScheme)
│   │   ├── IConnectionStore.cs                       # EngineConnection.StrictScheme
│   │   ├── IEngineBridge.cs                          # pass StrictScheme to CandidateUrls
│   │   ├── PageEngine.cs                             # NEW: IPageEngine (Engine/Static/Unknown), PageEngineInfo, PairingPrefill
│   │   └── IEngineAutoConnect.cs                     # page-engine branch (incl. dangling id); Unknown → Unreachable; PairLocalEngineAsync uses the page engine
│   └── Shared/
│       ├── StatusBar.razor                           # button title from the page engine
│       └── ConnectionPickerComponent.razor           # Add-form prefill; PIN shown at once on PairingPrefill
└── AkmlSql.Installer/
    ├── web-hosting.iss                               # NEW: IisReadinessFrom, InferPreviousHost, ResolveSilentHost, HostingAddress, ReadConfigString
    ├── web-installer.iss                             # components; WebHostPage (3 items, reason, OnActivate + silent guards); ports page per host + exemptions; Check gates; Web_PostInstall order; Web_Uninstall; removes dism, IsIisInstalled, the {tmp} firewall [Run] and [UninstallRun] entries
    ├── AkmlSqlSetup.iss                              # include; PrepareToInstall hardening + fresh uninstall log; IsWebSiteHosted/WebSiteUrl; UpdateReadyMemo; UninstallSilent at usPostUninstall; [InstallDelete]/[UninstallDelete]; header flags
    ├── web-config-bridge.ps1                         # restructured: Get-BridgeConfigJson + guarded entry; always writes webRoot
    ├── web-firewall.ps1                              # restructured: Get-FirewallPlan + -Apply; guarded entry
    ├── web-iis-setup.ps1                             # + .sql MIME; drop stale 32-bit comments
    ├── web-iis-repair.ps1                            # guard: refuse unless Host absent or IIS
    ├── web-iis-remove.ps1                            # NEW: Get-IisRemovalPlan (WebAdministration or appcmd; web.config only once the site is gone; failure marker)
    ├── web-hosting-repair.ps1                        # NEW: repair dispatcher (location checks; IIS and Service branches; restart; check)
    └── web-hosting-check.ps1                         # NEW: answer check (-ExpectEngine / -ExpectBundle; names the port holder)

tests/
├── AkmlSql.Core.Tests/                               # bridge.webRoot round-trip
├── AkmlSql.Engine.Tests/
│   ├── Transports/StaticWebResponderTests.cs         # NEW
│   ├── Transports/StaticWebResponderCspTests.cs      # NEW: AST-extracted ps1 CSP; bundle extension coverage
│   ├── Transports/BridgeAccessTests.cs               # no web root → plain page everywhere; /akmlsql kept; upgrade any path; Host check
│   └── EngineHostTests.cs                            # webRoot only for web mode; bind-failure message
├── AkmlSql.Web.Tests/
│   ├── Services/EngineEndpointTests.cs               # FromPage; strictScheme
│   ├── Services/PageEngineTests.cs                   # NEW
│   ├── Bridge/EngineAutoConnectPrecedenceTests.cs    # NEW
│   ├── Shell/StatusBarSegmentsTests.cs, Bridge/StatusIndicatorTests.cs  # title
│   └── Bridge/ConnectionPickerPinTests.cs            # prefill
├── AkmlSql.Web.E2E.Tests/
│   ├── EngineServedWebTests.cs                       # NEW (incl. /spike .sql case)
│   └── Harness/SandboxEngine.cs (+ webRoot), Harness/EngineServedBundle.cs (NEW: compose + freshness guard), Harness/InstalledEngine.cs (URL: line kept)
└── AkmlSql.Installer.Tests/
    ├── WebHostingProbeTests.cs                       # NEW: probe-ISS over web-hosting.iss (real PS 5.1 config fixtures)
    ├── WebInstallerSourceTests.cs                    # NEW: source-text facts
    ├── WebConfigBridgeTests.cs, FirewallPlanTests.cs, IisRemovalPlanTests.cs  # NEW: ps1 dot-source
    ├── WebHostingCheckTests.cs                       # NEW: ps1 process vs in-test listener
    ├── StandaloneSmokeFixture.cs + StandaloneSmokeTests.cs  # NEW: cases Standalone, LanStandalone, IisToStandaloneSwitch (VM only)
    └── InstallerSmokeEnv.cs, InstallerSmokeFixture.cs, ReRunAndUninstallTests.cs  # CanRunService/CanRunIis, AKML_INSTALLER_SMOKE opt-in, uninstaller from the uninstall key, fuller cleanup

doc/deployment.md, doc/configuration.md, doc/architecture.md, doc/ipc-api.md, doc/m3-security.md,
doc/WEB/{00-INDEX,M3-websocket-transport,M4-iis-installer,quickstart-m3,quickstart-m4,ai-local-provider-cors}.md,
doc/topics/browser-setup.md, doc/progress.md, CLAUDE.md
```

**Structure Decision**: the existing engine, web and installer layout; no new project. The new
code is:
- one engine class next to the transport it serves;
- one web service next to `EngineEndpoint`;
- one `[Code]` include;
- three PowerShell helpers next to the scripts they mirror.

Two existing scripts are restructured into a pure function plus a guarded entry point so they
can be tested. Everything else is an edit in place.

## Design notes (how the pieces fit)

### Fresh install on a machine without IIS (US1)

1. The hosting page shows IIS as `NotEnabled` and preselects Standalone. The ports page asks
   only for 47291 and checks it is free. The Ready memo shows the address.
2. `PrepareToInstall`:
   - Program Files is safe, so the install tree is left as it is;
   - lock the state folder and set its owner;
   - set aside an old uninstall log that holds temp-folder script paths;
   - stop the running processes.
3. `[Run]`: `sc create`, then `start= auto`.
4. `Web_PostInstall`:
   1. Write the registry values (`Host=Service`, `Exposure=Localhost`) and the log lines.
   2. `web-config-bridge.ps1 -WebRoot {app}\Web`.
   3. `web-firewall.ps1 -Apply` removes both rules.
   4. Set `DelayedAutostart = 0`.
   5. `sc start`. The engine's `RunWebAsync` builds the transport with
      `serveWebEdition: true`; the responder validates the root, builds the manifest and logs
      one line.
   6. `web-hosting-check.ps1 http://127.0.0.1:47291/ -ExpectEngine` returns 0.
   7. The summary reads "Hosting: Standalone Windows service".
5. The Finish link opens `http://localhost:47291/`.
6. In the browser: `HEAD /` returns `X-AKML-Host: engine` (`Engine`). Nothing usable is active,
   so the app probes `ws://localhost:47291/akmlsql`, gets `Ok`, stores the connection, and
   shows Live.

### Upgrade of the workstation's IIS install (SC-007)

1. There is no `Host` value, and `applicationHost.config` lists the `AkmlSqlWeb` site, so the
   previous host is IIS.
2. The exposure comes from `ReadConfigString(config.json, 'bindAddress')` or `IisMode`.
3. The hosting page preselects IIS (`Ready`). The ports page shows 8083, 47291 and 443 from the
   registry.
4. Setup writes `webRoot` as `""` and re-provisions IIS as it does today. The address and the
   browser data do not change.

### Switch from IIS to Standalone (US3)

1. The hosting page notice and the Ready memo show `http://localhost:8083/` →
   `http://localhost:47291/`, with the browser-data text.
2. `[Run]` has already applied `start= auto` (`Check: WebHostIsService`).
3. `Web_PostInstall`:
   1. `web-iis-remove.ps1` removes the site, the unused app pool and the owned binding, then
      `{app}\Web\web.config` once the site is confirmed gone, then the markers.
   2. `web-config-bridge.ps1` writes `webRoot`.
   3. The firewall plan runs.
   4. `DelayedAutostart = 0`.
   5. `sc start`.
   6. The answer check runs.

### LAN standalone (US4)

1. `[Run] web-tls-setup.ps1` binds the certificate to `0.0.0.0:47291` (unchanged). The transport
   registers `https://+:47291/`.
2. The firewall plan adds `AKML SQL Web Engine` and removes the HTTPS-site rule.
3. The answer check probes `https://localhost:47291/` with validation off for its own requests.
4. The summary gives `https://{COMPUTERNAME}:47291/` for every computer. The trust steps are
   presented as the recommended step, and a line notes that the rule applies to every network
   profile.
5. On another computer:
   1. `HEAD /` returns the header.
   2. The probe dials `wss://…/akmlsql` (strict scheme) and gets `PinRequired`.
   3. The picker opens prefilled, and the user pairs.
   4. The record is saved with its bearer and `StrictScheme`.

## Complexity Tracking

| Violation / tension | Why needed | Simpler alternative rejected because |
| --- | --- | --- |
| 1. A read-only, unauthenticated HTTP surface on the existing bridge listener (the "no network listener" constraint) | The user's choice of one address (Q1 = B); the pages must load before pairing | IIS is what the feature replaces. A second listener or service adds a port and a lifecycle. Serving only after pairing is impossible, because the page itself does the pairing. Mitigations: only the public bundle, through a manifest; the state lives in a separate tree; a web root that overlaps the state is refused; containment before any file is opened; the localhost Host allowlist; bounded slots with deadlines; the WebSocket Origin and PIN gates unchanged; the multi-session limit documented. |
| 2. Removing the `web\iis` and `web\service` sub-components | FR-003: the hosting choice alone decides, and the component ticks are the defect (I2) | Keeping them with Checks on top leaves two controls for one choice. The S1 fallback (hidden entries with `Check: False`) is kept in case Inno rejects the unknown names. |
| 3. Fixing installer defects inside this feature: <br>(a) the uninstall firewall leak; <br>(b) uninstall ordering; <br>(c) silent-uninstall prompts (web and desktop); <br>(d) LAN forgotten on upgrade; <br>(e) a user-writable custom install folder and a squattable state folder; <br>(f) the old `[UninstallRun]` temp-script entries | Each one blocks a requirement or is a security hole in code this feature rewrites: <br>(a) FR-061 and FR-018; <br>(c) FR-062, FR-071 as amended, and SC-010 (quickstart 3.11 and the smoke cleanup); <br>(d) FR-030 and FR-041; <br>(e) FR-020; <br>(f) an elevation path in the same uninstall flow | Deferring them leaves those requirements unmet, or ships a LocalSystem web host over a writable tree. Defects unrelated to hosting are recorded, not fixed (research › Found, not fixed). |

## Constitution Check (post-design re-check)

The gate was re-run after Phase 1 and after the plan review, against
[data-model.md](./data-model.md), [contracts/](./contracts/) and [quickstart.md](./quickstart.md).
**It passes.**

- **I.** All serving logic is in the engine, and the shell is untouched.
- **II.** No pin changes. ISCC and the solution build in the gate.
- **III.** Every new behaviour has a named test (quickstart §1). The installer smoke tests are
  opt-in and run on VMs only.
- **IV.** No git actions.
- **V.** The three justified items above. No other item adds a parallel mechanism: one firewall
  plan (PowerShell), one address function, one readiness function.

No NEEDS CLARIFICATION remains in the Technical Context.
