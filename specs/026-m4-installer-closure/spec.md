# Feature Specification: M4 — Installer (IIS Deployment Option) Closure

**Feature Branch**: `026-m4-installer-closure`
**Created**: 2026-05-28
**Status**: Draft
**Input**: User description: PRD `doc/WEB/M4-iis-installer.md` (Status: Draft; Estimated effort 1 week)

## Overview

The M4 PRD ("Installer offers a Web edition component") looks like greenfield work but is substantially **already merged inside spec 021 Phase 5**: `src/AkmlSql.Installer/` already contains `web-installer.iss` (Inno Setup component group, three wizard pages, post-install summary writer, uninstall hooks), `web-iis-setup.ps1` (IIS site + MIME + CSP), `web-tls-setup.ps1` (self-signed cert + `netsh http add sslcert`), `web-firewall.ps1` (inbound rule), and `web-config-bridge.ps1` (writes the WebSocket transport section into `%AppData%\AKML SQL\config.json`). Spec 025 (M3 bridge closure, merged 2026-05-27) made the engine consume the bridge config those scripts produce, including LAN-mode TLS via `HttpListener`.

What is **not** merged maps to six gaps the M4 PRD §11 Definition-of-Done cannot retire today:

1. **The scaffolding is dead code** — `web-installer.iss` is never `#include`'d from `AkmlSqlSetup.iss`, and the five hook procedures it exposes (`Web_Init`, `Web_NextButton`, `Web_Skip`, `Web_PostInstall`, `Web_Uninstall`) plus the `GetWebPort` helper are never called by the existing event handlers. A grep of `AkmlSqlSetup.iss` for `web-installer.iss` / `Web_Init` returns zero matches. The shipping `AKMLSQLSetup.exe` therefore deploys IDE plugins only — no Web component appears in the wizard. This is the dominant gap of M4.
2. **LAN pairing is unenforced AND the PIN never reaches the install summary** — two layered gaps surfaced during the plan-stage code audit. First, the bridge auth path is wired to a placeholder: `EngineHandlerRegistry.cs:258` registers `new HandshakeHandler()` (the **parameterless** constructor), whose callbacks (`HandshakeHandler.cs:37-45`) are all-permissive — `pairingRequired: () => false`, `pinValidator: _ => true`, `bearerValidator: _ => true`, `bearerMinter: _ => null`. So in production the LAN bridge auto-accepts **every** connection regardless of PIN; `PairingService` and `BearerTokenStore` are never instantiated (the registry comment says "NO_AUTH semantics intact"). Spec 025 closed the bridge transport + LAN TLS but left auth as a placeholder. Second — even once auth is wired — `PairingService` (spec 021 T063, merged) holds the minted 6-digit PIN only in memory (`CurrentPin` + `PinChanged`); nothing writes it to disk, and `Web_PostInstall` reads `%CommonAppData%\AKML SQL Web\pairing-pin.txt` to bake into `INSTALL-SUMMARY.txt`. **Both layers must close** for a second machine to pair: the engine must enforce the PIN at handshake in LAN mode (wire the full `HandshakeHandler` constructor to a live `PairingService` + `BearerTokenStore`) AND surface the PIN to the operator via the install summary. Without the first, the printed PIN is a security illusion; without the second, there is no PIN to print.
3. **IIS port and bridge port share the same value** — the current wizard has one port input (default 47291) that flows to both `web-iis-setup.ps1 -Port` (IIS site binding) and `web-config-bridge.ps1 -Port` (engine WebSocket transport binding). On Windows, IIS and the engine's `HttpListener` cannot both own the same TCP port via HTTP.SYS port sharing for the same URL prefix. The PRD §4.1 + §4.4 design assumes IIS on port 80 (`http://localhost/`) and bridge on a separate port (47291). The current wizard violates this.
4. **The IIS-not-installed dialog branch does not exist** — PRD §4.2 specifies a pre-install check (`IsIisInstalled()` = `RegKeyExists(HKLM, 'SOFTWARE\Microsoft\InetStp')` AND `FileExists({sys}\inetsrv\appcmd.exe)`) that offers three paths: "Enable IIS now" (runs `dism /online /enable-feature /featurename:IIS-WebServerRole`), "Switch to Don't host", "Cancel install". Today, `web-iis-setup.ps1` silently exits 0 when `WebAdministration` is missing; the user gets "install succeeded" but the URL never works. No Pascal-script gate exists.
5. **Silent-install flags are unparsed** — PRD §11 / spec 021 T096 specifies `/WEB_HOST=IIS|NONE`, `/WEB_EXPOSURE=LOCALHOST|LAN`, `/WEB_PORT=<N>`, plus the cross-validation rule "reject `/WEB_HOST=NONE` with `/WEB_EXPOSURE=LAN`". None are parsed today; an unattended install cannot opt into the Web component.
6. **No installer smoke coverage** — `tests/AkmlSql.Installer.Tests/` does not exist. Spec 021 tasks T086 (IIS provisioning), T090 (LAN TLS), T097 (re-run + uninstall plugin-state preservation) are all deferred against this. Without them, the PRD §8 success metrics ("clean Windows machine → 60 seconds", "two-machine LAN test", "uninstall leaves IDE-plugin state untouched") have no automated evidence.

This is a verification + plumbing closure, not a redesign. The six user stories below map 1:1 to these gaps in priority order; everything else the M4 PRD describes is already shipped (component group declared, file copy, MIME types, CSP header, cert generation, firewall rule, Windows service install, uninstall path) and is explicitly **not** rewritten by this spec.

**Open follow-ups acknowledged but deferred** (consistent with how spec 021 left T065 and how spec 025 left the TLS-fingerprint-pinning UI):

- **Tray-app design (PRD §5.1)** — The PRD proposed a `AkmlSql.Tray.exe` (~50 KB) as the default engine-lifecycle host. The shipping implementation chose a Windows service (`AkmlSqlWebEngine`) instead, and both spec 021 Phase 5 + spec 025's bridge composition assume the service. Out of scope here; documented in §Out of Scope so a future spec can revisit if telemetry shows users want tray-based control.
- **ARR / reverse-proxy single-port** — Out of scope per PRD §9.
- **Engine-side tray pairing pane** (spec 021 T065) — Same constraint as spec 025 deferred.
- **TLS fingerprint mismatch dialog** (spec 025 deferred) — Records to diagnostics today; no modal.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Install the Web edition end-to-end on a localhost-only host (Priority: P1)

A first-time AKML SQL user runs `AKMLSQLSetup.exe` on a Windows 11 workstation with IIS already installed. They tick "Web edition", leave defaults (Host on IIS, Localhost only, IIS port 80, bridge port 47291), and finish the wizard. The install-summary page shows `http://localhost/`. They click it, the browser opens, the AKML SQL Web editor loads, they format a SQL script. No more `dotnet publish`, no manual config edit.

**Why this priority**: This is the dominant unmet checkbox in the M4 PRD §11 Definition-of-Done. Until `web-installer.iss` is `#include`'d from `AkmlSqlSetup.iss` and the five hook procedures are wired (four into existing event handlers, `Web_Skip` via a new `ShouldSkipPage`), the shipping installer offers no Web component at all — every other M4 user story is a no-op because the user never reaches the Web-edition wizard pages.

**Independent Test**: On a clean Windows 11 host with IIS pre-installed, run a freshly-compiled `AKMLSQLSetup.exe`, walk through the wizard accepting Web-edition defaults, complete install, click the URL on the success page, see the editor render in the browser, format a script. Total wall-clock under 90 seconds.

**Acceptance Scenarios**:

1. **Given** a clean Windows 11 host with IIS installed, **When** the user runs `AKMLSQLSetup.exe` and ticks "Web edition" without changing any defaults, **Then** the wizard shows the Hosting page (default "Host on local IIS"), the Network page (default "Localhost only"), the IIS Port page (default 80), and the Bridge Port page (default 47291) in that order — and Skips them when "Web edition" is unticked.
2. **Given** the user selected Web edition with IIS port 80 and bridge port 47291, **When** install completes, **Then** the install-summary page shows `URL: http://localhost/`, the WASM bundle is present at `{app}\Web\`, the IIS site `AkmlSqlWeb` is bound on port 80, and the engine service `AkmlSqlWebEngine` is running with `bridge.port = 47291` in its config.
3. **Given** the same install on the same port pair, **When** the user enters IIS port 47291 (collides with the bridge port) and clicks Next, **Then** the wizard refuses to advance with a clear error referencing the conflict ("IIS port and Bridge port must differ"), no install happens.
4. **Given** install completed, **When** the user opens `http://localhost/` in Chrome, **Then** the editor renders within 5 seconds, `Ctrl+K Ctrl+F` formats the document, the bridge status bar reads `Open` (the engine is on localhost), and no console errors appear.

---

### User Story 2 - Enforced LAN pairing: the engine validates the PIN and a second machine pairs (Priority: P1)

An operator installs AKML SQL Web on Machine A with LAN exposure. The engine, started in LAN mode, **enforces** the pairing PIN at handshake — a wrong PIN is refused, a correct PIN mints a bearer token, and subsequent reconnects replay the bearer. The install summary at the end of the wizard (and the persisted `%CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt`) shows the 6-digit pairing PIN, the LAN URL, and the TLS thumbprint. They copy the PIN, walk to Machine B, open the LAN URL in a browser, paste the PIN into the Add Connection dialog, and reach `BridgeState.Open` with live schema. A bystander on the LAN who does not know the PIN cannot connect.

**Why this priority**: This is the entire point of the M4 PRD's two-machine deployment story (PRD §8 success metric "Two-machine LAN test: pairs, sees live schema") **and** its security boundary. The plan-stage code audit found two layered gaps: (a) the engine registers `HandshakeHandler` with the all-permissive parameterless constructor, so LAN connections auto-accept regardless of PIN — `PairingService`/`BearerTokenStore` are never instantiated in production; (b) even once auth is wired, the minted PIN lives only in memory and never reaches the install summary. Until both close, LAN mode either has no enforced auth (a) or no operator-visible PIN to pair with (b). Enforcing the PIN is the difference between a real security boundary and a TLS-encrypted but open port.

**Independent Test**: On Machine A run a Web-edition install with `LAN exposed` selected. Read the install-summary screen; confirm the PIN line shows a 6-digit number; confirm persistence at `%CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt`. From Machine B on the same LAN, browse to the printed LAN URL, click Add Connection, enter a **wrong** PIN → handshake is refused with `PinInvalid`; enter the **correct** PIN → bridge state reaches `Open` within 10 seconds, a bearer token is minted and stored, and a reload reconnects without re-prompting. Kill + restart the engine → the stored bearer reconnects without a new PIN.

**Acceptance Scenarios**:

1. **Given** an engine started in LAN mode (`RequirePairingToken = true`), **When** Machine B sends a handshake with a wrong PIN, **Then** the engine returns `HandshakeStatus.PinInvalid` and no bearer is minted — the bridge does not auto-accept.
2. **Given** the same LAN engine, **When** Machine B sends a handshake with the correct PIN, **Then** the engine returns `HandshakeStatus.Ok` with a newly-minted bearer token, the PIN is consumed (single-use), and the token is persisted to `bridge.TokenStorePath` (`%CommonAppData%\AKML SQL Web\tokens.json`).
3. **Given** an engine bound to loopback (`RequirePairingToken = false`), **When** a localhost browser handshakes with no PIN, **Then** the engine auto-accepts (the parameterless / loopback-trust path is retained) — localhost mode requires no PIN.
4. **Given** a fresh Web-edition LAN install, **When** the engine service `AkmlSqlWebEngine` starts for the first time, **Then** `%CommonAppData%\AKML SQL Web\pairing-pin.txt` exists, contains exactly one 6-digit decimal line (no trailing newline), and the ACL grants read+write to Administrators and SYSTEM only (standard users denied).
5. **Given** the engine started and `pairing-pin.txt` was written, **When** the installer reaches `Web_PostInstall`, **Then** `INSTALL-SUMMARY.txt` includes a `Pairing PIN: <6-digit>` line and the success page displays it with a Copy button.
6. **Given** a paired Machine B, **When** the engine process is killed and restarted, **Then** the stored bearer token reconnects without re-prompting for the PIN (bearer replay), and a revoked bearer instead returns `PinRequired`.
7. **Given** the engine has not yet started (rare — installer races the service), **When** `Web_PostInstall` reads `pairing-pin.txt` and finds it absent, **Then** `INSTALL-SUMMARY.txt` and the success page show the fallback text "Pairing PIN not yet generated — start the AkmlSqlWebEngine service, then re-read %CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt".

---

### User Story 3 - IIS-not-installed branch presents a clear remediation dialog (Priority: P2)

A user on a clean Windows 11 host without IIS runs the installer, ticks "Web edition → Host on local IIS", and is met by a dialog explaining IIS is required, with three actions: "Enable IIS now", "Switch to Don't host", "Cancel install". They click Enable IIS now, the installer runs `dism` under their existing elevated session, IIS comes up, the wizard continues with the IIS provisioning step, and the install completes in one run.

**Why this priority**: The PRD §4.2 §"IIS detection logic" is explicit about the three-path dialog, and PRD §8 lists "Clean Windows machine with no IIS: install with web edition → user sees clear 'enable IIS' guidance" as a success metric. Today the script silently skips IIS setup if `WebAdministration` is missing — the user sees "install succeeded" but the URL doesn't work, and they have no idea why. This is the second-most-common new-user failure mode after the integration gap itself.

**Independent Test**: On a fresh Windows 11 host with IIS uninstalled, run `AKMLSQLSetup.exe`, tick Web edition, leave default "Host on IIS". The wizard shows the IIS-missing dialog before reaching the Hosting page. Click "Enable IIS now"; observe a "Enabling IIS…" notice while `dism` runs (the wizard is necessarily busy during the synchronous call); the wizard then proceeds normally. After install, `http://localhost/` works in the browser without a separate user action.

**Acceptance Scenarios**:

1. **Given** Windows 11 with IIS uninstalled, **When** the user ticks "Web edition" and reaches the Hosting page selection, **Then** before the page is shown the installer runs `IsIisInstalled()` (`RegKeyExists(HKLM, 'SOFTWARE\Microsoft\InetStp')` AND `FileExists({sys}\inetsrv\appcmd.exe)`) and surfaces a dialog with three buttons because the result is false.
2. **Given** the IIS-missing dialog, **When** the user clicks "Enable IIS now", **Then** the installer shows a "Enabling IIS…" notice and runs `dism /online /enable-feature /featurename:IIS-WebServerRole /All /Quiet /NoRestart` (which can take 30–60 seconds, during which the wizard is busy), and continues to the Hosting page on success.
3. **Given** the IIS-missing dialog, **When** the user clicks "Switch to Don't host", **Then** the install proceeds with the bundle copied to `{app}\Web\` but no IIS site is provisioned; the success page shows a "host it yourself" path with a Python `http.server` example and an absolute filesystem path; the engine service still installs and runs.
4. **Given** the IIS-missing dialog, **When** the user clicks "Cancel install", **Then** the installer exits with exit code 0 (user-cancelled, not error) and no partial install remains.
5. **Given** `/VERYSILENT` mode with `/WEB_HOST=IIS` and IIS missing, **When** the installer reaches the IIS check, **Then** it exits non-zero with a log line "IIS not installed — pass /WEB_HOST=NONE to skip IIS provisioning"; no dialog is shown (silent mode contract).

---

### User Story 4 - Unattended install supports the documented silent flags (Priority: P2)

A devops engineer scripts an unattended deployment of AKML SQL Web across many machines: `AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /WEB_HOST=IIS /WEB_EXPOSURE=LOCALHOST /WEB_PORT=80 /BRIDGE_PORT=47291`. The installer runs without showing a single dialog, applies the choices, writes the install summary to the documented path, and exits 0. The reverse case (`/WEB_HOST=NONE /WEB_EXPOSURE=LAN` — nonsensical combination) exits non-zero with a clear log line.

**Why this priority**: The PRD §11 / spec 021 T096 specifies these flags. Without them, automated rollouts have to drive the GUI wizard. PRD §8 success metric "Re-running installer to change from localhost to LAN mode (or back) works without uninstalling first" assumes the flags exist.

**Independent Test**: On a clean host with IIS, run the silent command above. Confirm exit code 0, no dialog appearance, IIS site bound on port 80, engine config at bridge port 47291, install summary file written. Re-run with `/WEB_EXPOSURE=LAN` to verify state transition works without uninstall. Run the invalid combination and confirm non-zero exit + log entry.

**Acceptance Scenarios**:

1. **Given** the silent command in the test description, **When** the installer runs, **Then** exit code is 0, no UI appears, `Get-Website -Name AkmlSqlWeb` returns a site bound on port 80, `Get-Content "%AppData%\AKML SQL\config.json"` shows `bridge.port = 47291` and `bridge.bindAddress = 127.0.0.1`, `Get-Service AkmlSqlWebEngine` shows Running, and `%CommonAppData%\AKML SQL Web\INSTALL-SUMMARY.txt` is written.
2. **Given** an existing localhost install, **When** the user runs `AKMLSQLSetup.exe /VERYSILENT /WEB_EXPOSURE=LAN` (every other flag preserved from the prior run via `UsePreviousAppDir=yes`), **Then** the installer regenerates the TLS cert, adds the firewall rule, switches `bridge.bindAddress` to `0.0.0.0`, and **preserves the existing `tokens.json`** so previously-paired browsers stay paired.
3. **Given** the invalid combination `/WEB_HOST=NONE /WEB_EXPOSURE=LAN`, **When** the installer runs, **Then** exit code is non-zero, no install happens, and the install log contains "LAN exposure requires a hosting mode (use /WEB_HOST=IIS)".
4. **Given** `/WEB_PORT=80 /BRIDGE_PORT=80` (same port), **When** the installer runs, **Then** exit code is non-zero, no install happens, and the install log contains "IIS port and Bridge port must differ".
5. **Given** any silent run, **When** any sub-step (IIS provisioning, cert generation, firewall rule, service install) fails, **Then** the exit code is non-zero, the failure reason is in the install log, and no half-installed state remains (uninstall hooks run to roll back what was provisioned).

---

### User Story 5 - Installer smoke tests catch regressions on a real Windows host (Priority: P3)

A maintainer pushes a change to one of the `.ps1` helpers or to `web-installer.iss`. Before merging, they run `dotnet test tests/AkmlSql.Installer.Tests --filter Category=InstallerSmoke` on a Windows 11 host with IIS + admin rights. The suite installs the wizard-built EXE silently, asserts the IIS site is bound, MIME types are present, CSP header is served, the cert is bound, the firewall rule is in place, the install summary file is well-formed, and the IDE-plugin state at `%AppData%\AKML SQL\` is byte-for-byte unchanged across install/uninstall/re-install.

**Why this priority**: The PRD §8 success metrics are unbacked by evidence until these tests exist. Spec 021 T086, T090, T097 are all deferred against this. A pre-merge sanity gate is the cheapest defense against regression in code that only runs on a fully-configured Windows host with admin rights.

**Independent Test**: From a clean clone on a Windows host with IIS + admin: run `dotnet test tests/AkmlSql.Installer.Tests --filter Category=InstallerSmoke`. The suite builds the installer (or uses the prebuilt `Output/AKMLSQLSetup.exe`), drives a silent install, asserts every checkpoint, drives a silent uninstall, asserts cleanup, and ends green. Total runtime under 5 minutes.

**Acceptance Scenarios**:

1. **Given** a fresh checkout on a Windows host with IIS + admin, **When** the developer runs `dotnet test tests/AkmlSql.Installer.Tests --filter Category=InstallerSmoke`, **Then** the suite compiles, performs a silent install, asserts the documented end state (IIS site, MIME types, CSP header, cert binding, firewall rule, summary file, plugin state hash), uninstalls, asserts cleanup, and exits green.
2. **Given** the same suite on a host **without** IIS or admin rights, **When** the developer runs the command, **Then** every test is skipped (not failed) via `Skip.IfNot(IsAdministrator && IsIisInstalled, ...)` from `Xunit.SkippableFact`.
3. **Given** the standard `dotnet test` command (no filter), **When** developers run it locally or CI runs it, **Then** the installer suite does NOT run (excluded by `[Trait("Category","InstallerSmoke")]`) — same opt-in pattern as spec 024's `ParityBaseline` and spec 025's `BridgeE2E`.
4. **Given** a Web-edition install that completes, **When** the suite runs the byte-for-byte plugin-state preservation test, **Then** `Get-FileHash %AppData%\AKML SQL\config.json` before install equals the same hash after install AND after uninstall — proving SC-007 holds.
5. **Given** a regression that breaks the MIME type registration (or the CSP header, or the cert binding, or any other documented end state), **When** the suite runs, **Then** the corresponding test fails with a message naming the missing artefact (e.g., "Expected .wasm MIME type registered on AkmlSqlWeb; got 0 matches").

---

### User Story 6 - doc/deployment.md walks an operator through a Web-edition install (Priority: P3)

An operator deploying AKML SQL Web for the first time reads `doc/deployment.md`, finds a "Web edition" section, and follows it end-to-end from "what you need" through "the wizard" through "verifying the install" to "uninstall". A separate first-interactive-integration-run record captures the deltas the maintainer observed running the installer on a real Windows host.

**Why this priority**: PRD §11 lists "Documentation written: install guide, troubleshooting, 'host it yourself' path" as a Definition-of-Done item. Today, `doc/deployment.md` covers IDE plugin install only; `doc/WEB/quickstart-m4.md` walks through the design-level scaffolding but openly admits it has never been run interactively. After this user story lands, an operator has a single canonical install doc and a maintainer has a baselined first-run record.

**Independent Test**: A reviewer reads `doc/deployment.md`'s new Web-edition section end-to-end and answers: "What do I need pre-installed?", "Which ports does it use?", "How do I install silently?", "How do I uninstall cleanly?", "What does 'Don't host' mean?". A maintainer following the new section on a fresh Windows host completes the install in under 5 minutes and the section's command-by-command instructions match what actually appears on screen.

**Acceptance Scenarios**:

1. **Given** the operator opens `doc/deployment.md`, **When** they search for "Web edition", **Then** a section exists with subsections "Prerequisites", "Component selection", "Localhost mode", "LAN mode", "Don't host", "Silent install", "Uninstall", and "Troubleshooting".
2. **Given** the operator follows the LAN-mode walkthrough on a real Windows host, **When** they reach the install summary, **Then** the PIN they see matches the format the doc describes (6 decimal digits) and the TLS thumbprint matches the format the doc describes (SHA-1 hex, last 12 highlighted).
3. **Given** the maintainer completes the first interactive integration run on a Windows host with Inno Setup 7 + IIS + admin rights, **When** they file the observed deltas (compile warnings, wizard-text adjustments, port-conflict cases, etc.), **Then** they land as follow-up tasks under `specs/026-m4-installer-closure/` rather than as silent commits.
4. **Given** the section's "Troubleshooting" subsection, **When** a user hits one of the four most common install failures (IIS missing, port collision, admin-rights missing, service fails to start), **Then** the user finds a clear diagnostic command and a remediation path in the doc.

---

### Edge Cases

- **IIS port 80 already taken by the Default Web Site** — The wizard's IIS port default is 80. If IIS is installed but Default Web Site is bound to a non-standard port, the installer should fall back to that port and show it in the success URL; alternately the user picks a different port and the wizard validates no conflict.
- **Bridge port 47291 already taken by another service** — Per FR-003a, the Bridge Port wizard page probes the port (`Test-NetConnection` via Pascal Script `Exec`, or equivalent) and warns (does not block) if it is in use; the user dismisses to proceed or picks another port. The probe degrades to "no warning" if PowerShell is unavailable.
- **IIS port == Bridge port** — The wizard MUST refuse to advance with a clear error. This is the structural conflict that motivates the two-port redesign.
- **User runs the installer twice with different IIS port** — Old `AkmlSqlWeb` site removed cleanly, new site created on the new port; existing `tokens.json` preserved so paired browsers stay paired.
- **Engine service fails to start** — Per FR-007a, `Web_PostInstall` detects `AkmlSqlWebEngine` not `Running` within 10 s and surfaces a "service did not start — see Event Log + install.log" message on the success page and in `INSTALL-SUMMARY.txt`. The install is not failed (files are in place; the user can start the service manually).
- **`pairing-pin.txt` write fails (disk full, ACL denied)** — Engine logs the error to Serilog; install summary shows the "not yet generated" fallback; service is still considered started for the rest of the flow.
- **Race: installer reads `pairing-pin.txt` before service starts** — `Web_PostInstall` should poll for the file's appearance with a 30-second timeout before falling back to the "not yet generated" text.
- **`/VERYSILENT` install with missing IIS** — Installer logs the missing-IIS failure and exits non-zero; no dialog (silent mode contract). The user knows install failed because the exit code is non-zero.
- **User uninstalls the Web edition but keeps plugins installed** — The uninstall path removes only Web-edition state; `%AppData%\AKML SQL\` (IDE plugin) is byte-for-byte untouched. SC-007 verifies.
- **Tab order on wizard pages** — Default focus on the IIS Port page must be the port input, not the Back button; same for Bridge Port.
- **Re-run with `/WEB_HOST=NONE` after a previous `/WEB_HOST=IIS` install** — Old IIS site is removed; bundle stays at `{app}\Web\`; the user is responsible for hosting it themselves.

## Requirements *(mandatory)*

### Functional Requirements

#### Integration wiring (US1)

- **FR-001**: `src/AkmlSql.Installer/AkmlSqlSetup.iss` MUST `#include "web-installer.iss"` so the `[Components]`, `[Files]`, `[Run]`, `[UninstallRun]`, and `[Code]` sections are compiled into the shipping installer.
- **FR-002**: `AkmlSqlSetup.iss` MUST wire the five hook procedures — `Web_Init` in `InitializeWizard`, `Web_NextButton` in `NextButtonClick`, `Web_PostInstall` in `CurStepChanged(ssPostInstall)`, `Web_Uninstall` in `CurUninstallStepChanged(usUninstall)` (four calls into existing event handlers), and `Web_Skip` via a **new** `ShouldSkipPage` function (which does not exist in `AkmlSqlSetup.iss` today) — plus the port-accessor helpers (`GetIisPort`/`GetBridgePort`, split from the original `GetWebPort` per FR-004) made accessible to `[Run]` parameter substitution.
- **FR-003**: The wizard MUST expose two distinct port inputs: an **IIS port** input (default 80) and a **Bridge port** input (default 47291). Both validated against `[1024..65535]` (with IIS port additionally accepting 80). `Web_NextButton` MUST refuse to advance when the two are equal.
- **FR-003a**: On the Bridge Port page, `Web_NextButton` MUST detect whether the chosen bridge port is already in use (e.g. `Exec` a `Test-NetConnection -ComputerName 127.0.0.1 -Port <port>` probe, or a `netstat`-based check) and, if so, MUST **warn** the user (a non-blocking `MsgBox` they can dismiss to proceed or go back and pick another port) — it MUST NOT hard-block, since a transient listener or the engine's own prior install can occupy the port harmlessly. This closes spec 021 T083. The probe failure (e.g. PowerShell unavailable) MUST degrade to "no warning" rather than blocking the wizard.
- **FR-004**: `web-iis-setup.ps1` MUST receive the IIS port (not the bridge port); `web-config-bridge.ps1` MUST receive the bridge port (not the IIS port); `web-tls-setup.ps1` MUST receive the bridge port (the cert is bound to the bridge listener, not the IIS site).
- **FR-005**: The success page URL line MUST show the IIS site over **HTTP** in both modes — `http://localhost:<IISPort>/` for localhost and `http://<hostname>:<IISPort>/` for LAN — with `:<IISPort>` omitted when the port is 80. The IIS-served WASM bundle is plain HTTP; only the engine **bridge** uses TLS (`wss` on the bridge port), per §Out of Scope item 3 (static-bundle HTTPS is a future spec). The summary's separate bridge line records the bridge port + its `wss`/TLS status; it MUST NOT present the browse URL as `https://`.
- **FR-006**: When the user does NOT tick the "web" component, `Web_Skip` MUST return true for all four web-edition pages (Hosting / Network / IIS Port / Bridge Port) and `Web_PostInstall` MUST return early without writing `INSTALL-SUMMARY.txt`.
- **FR-007**: The web bundle MUST land at `{app}\Web\` recursively, with the `_framework\` subtree intact; the engine binary MUST land at `{app}\Engine\` (its existing path from the IDE-plugin install).
- **FR-007a**: After the `[Run]` step starts the `AkmlSqlWebEngine` service, `Web_PostInstall` MUST check the service reached `Running` (poll `Get-Service`/`sc query` for ≤ 10 s). If it has not, the success page and `INSTALL-SUMMARY.txt` MUST show a clear "AkmlSqlWebEngine did not start — see Windows Event Log and `%CommonAppData%\AKML SQL Web\install.log`" message. This MUST NOT fail the install (the files are in place; the user can start the service manually) and applies in BOTH localhost and LAN modes. (Complements FR-011: in LAN mode a stalled service also produces the PIN "not yet generated" fallback; FR-007a is the mode-independent service-health signal, including localhost where no PIN poll runs.)

#### Pairing PIN persistence (US2)

- **FR-008**: The engine MUST persist the current pairing PIN — minted by the live `PairingService` constructed in LAN mode per FR-013a — to `%CommonAppData%\AKML SQL Web\pairing-pin.txt` whenever the in-memory PIN changes (at initial mint during construction, and at every `RegeneratePin()` call). The write MUST be wired via the `PairingService.PinChanged` event plus a one-shot publish of `CurrentPin` immediately after subscription (the initial mint happens inside the constructor, before any external subscriber attaches). `PairingService` itself stays free of file I/O — a separate `PairingPinFile` writer (owned by `EngineHost`) performs the disk write.
- **FR-009**: The file MUST contain exactly the 6-digit decimal PIN as UTF-8 bytes, no trailing newline, no BOM. Atomic write: temp file + `File.Replace` (or `File.Move(overwrite:true)` on .NET 10) — never a partial write visible to readers.
- **FR-010**: The file's ACL MUST grant read+write to Administrators and SYSTEM only. The installer (running elevated) and the engine service (running as LocalSystem) are the only legitimate readers; standard users on the host MUST NOT be able to read the PIN, since leaking it lets any local process pair as the operator and unwrap the LAN bearer-mint flow. The installer creates the parent directory with this ACL (via `New-Item ... -Force` then `Set-Acl`) if the directory does not already exist; subsequent re-runs leave the ACL intact.
- **FR-011**: `Web_PostInstall` MUST poll for `pairing-pin.txt` appearance after starting the engine service with a 30-second timeout. If the file appears, read it and bake the value into `INSTALL-SUMMARY.txt`. If the timeout expires, write the fallback text "Pairing PIN not yet generated — start the AkmlSqlWebEngine service, then re-read this file." and continue (do not fail the install).
- **FR-012**: The success page MUST mirror the same value: real PIN in the LAN-mode summary block when available, fallback text otherwise. Localhost-mode installs MUST NOT mention pairing (no PIN needed; the engine auto-accepts loopback).
- **FR-013**: Engine startup MUST NOT fail if writing `pairing-pin.txt` fails (e.g., disk full, ACL denied); a Serilog `Error` entry is sufficient. The PIN is still served from memory via the in-process API; only the installer-readable file is lost.

#### Engine-side LAN auth composition (US2)

> These FRs are the **prerequisite** for the PIN-persistence block above to be meaningful: until the handshake actually enforces the PIN, a persisted PIN is cosmetic. The plan-stage audit found `EngineHandlerRegistry.cs:258` registers `new HandshakeHandler()` (parameterless ctor) whose callbacks (`HandshakeHandler.cs:37-45`) are all-permissive (`pairingRequired: () => false`, `pinValidator: _ => true`), so the LAN bridge auto-accepts every connection. Spec 025 closed the bridge transport + TLS but left auth as a placeholder; this closure wires it. The user confirmed LAN pairing is a real security boundary M4 must ship.

- **FR-013a**: When the engine composes a `WebSocketTransport` in LAN mode (`BridgeOptions.IsLoopback == false`, i.e. `RequirePairingToken == true`), the engine MUST construct a live `PairingService` and a live `BearerTokenStore` (pointed at `BridgeOptions.TokenStorePath`) and register the `HandshakeHandler` via its **full** constructor — wiring `pairingRequired: () => true`, `pinValidator` → `PairingService.ValidatePin`, `bearerValidator`/`bearerMinter` → `BearerTokenStore`, and `serverCanonicalIdentityProvider` → the existing identity resolver.
- **FR-013b**: When the engine composes a loopback-only bridge (`IsLoopback == true`) or no bridge at all, the `HandshakeHandler` MUST keep the parameterless / auto-accept registration — localhost browsers pair with no PIN, exactly as today. The auth wiring is strictly additive to the LAN path; the IDE-plugin named-pipe path is untouched.
- **FR-013c**: The wired `pinValidator` MUST enforce single-use + 24h TTL + the per-source 5-attempts/minute rate limit already implemented in `PairingService` (no reimplementation); a wrong/expired/rate-limited PIN MUST surface as `HandshakeStatus.PinInvalid` and MUST NOT mint a bearer.
- **FR-013d**: A correct PIN MUST mint a bearer token via `BearerTokenStore`, persist its SHA-256 hash to `BridgeOptions.TokenStorePath` (`%CommonAppData%\AKML SQL Web\tokens.json`), and return it in `HandshakeResponse.NewBearerToken`. A stored, unrevoked, unexpired bearer presented on reconnect MUST return `HandshakeStatus.Ok` without consuming a PIN; a revoked/unknown bearer MUST return `HandshakeStatus.PinRequired`.
- **FR-013e**: `EngineHostTests` MUST assert the composition matrix: (a) a LAN `BridgeOptions` produces a `HandshakeHandler` that refuses a wrong PIN (`PinInvalid`) and accepts the right PIN (`Ok` + non-null bearer); (b) a loopback `BridgeOptions` produces a `HandshakeHandler` that auto-accepts a no-PIN handshake; (c) both LAN and loopback share the same `RpcRouter` instance for all non-handshake handlers (no regression to spec 025's dual-transport composition).

#### IIS-not-installed handling (US3)

- **FR-014**: `web-installer.iss` MUST define `function IsIisInstalled(): Boolean` that returns `RegKeyExists(HKLM, 'SOFTWARE\Microsoft\InetStp') and FileExists(ExpandConstant('{sys}\inetsrv\appcmd.exe'))`. The current PowerShell-side check (`Get-Module -ListAvailable -Name WebAdministration`) is INSUFFICIENT — modules can ship without IIS itself.
- **FR-015**: When the user selects "Host on local IIS" but `IsIisInstalled()` returns false, the wizard MUST surface a modal dialog (`MsgBox` with three custom-labelled buttons) BEFORE the Hosting page is committed: "Enable IIS now" / "Switch to Don't host" / "Cancel install".
- **FR-016**: "Enable IIS now" MUST run `dism.exe /online /enable-feature /featurename:IIS-WebServerRole /All /Quiet /NoRestart` via `Exec(..., ewWaitUntilTerminated)`. Because that call synchronously blocks the wizard thread, the implementation MUST first surface a "Enabling IIS… this can take up to a minute" notice (a `CreateOutputMsgPage` shown before the call, or a `WizardForm` status label + `WizardForm.Repaint` + a wait cursor) so the frozen wizard is explained rather than appearing hung — a live/determinate progress bar is NOT required (DISM gives no progress to Pascal Script). On return, re-check `IsIisInstalled()`; on a non-zero exit, surface the error code and re-present the same three buttons.
- **FR-017**: "Switch to Don't host" MUST programmatically set `WebHostPage.SelectedValueIndex := 1` (the "Don't host" option) and resume the wizard.
- **FR-018**: "Cancel install" MUST exit Inno Setup with code 0 (user-cancelled) and remove any partial state.
- **FR-019**: Under `/VERYSILENT` mode with `/WEB_HOST=IIS` and IIS missing, the installer MUST NOT show a dialog. It MUST log the missing-IIS state and exit non-zero with the message "IIS not installed — pass /WEB_HOST=NONE to skip IIS provisioning".
- **FR-020**: "Don't host" mode MUST still copy the bundle to `{app}\Web\` and install the engine service; only `web-iis-setup.ps1` is skipped. The success page MUST show a "host it yourself" subsection with an absolute filesystem path and a Python `http.server` example command.

#### Silent install flags (US4)

- **FR-021**: `AkmlSqlSetup.iss` MUST parse `/WEB_HOST=IIS|NONE`, `/WEB_EXPOSURE=LOCALHOST|LAN`, `/WEB_PORT=<N>` (the IIS port), and `/BRIDGE_PORT=<N>` (the bridge port) from the command line in `InitializeSetup` (so the values are available before any wizard page or install step runs).
- **FR-022**: Parsed flag values MUST drive the wizard's component selection, page selections, and port values without GUI input. `/VERYSILENT` MUST therefore complete unattended.
- **FR-023**: The validation rule "`/WEB_HOST=NONE` combined with `/WEB_EXPOSURE=LAN` is invalid" MUST be enforced in `InitializeSetup`; the installer exits non-zero with a clear log line before any state is created.
- **FR-024**: The validation rule "`/WEB_PORT` MUST NOT equal `/BRIDGE_PORT`" MUST be enforced in the same place; same failure mode.
- **FR-025**: Any sub-step failure (IIS provisioning, cert generation, firewall rule, service install, config write) MUST result in a non-zero exit AND a clear log message AND a partial-state rollback (uninstall hooks run, regardless of `/VERYSILENT`).
- **FR-026**: `UsePreviousAppDir=yes` plus Inno Setup's existing repair/upgrade logic MUST allow re-running the installer with `/VERYSILENT` and changed flags (`/WEB_EXPOSURE=LAN` to add LAN cert + firewall to an existing localhost install) without uninstall — preserving `%CommonAppData%\AKML SQL Web\tokens.json` byte-for-byte so paired browsers stay paired.

#### Installer smoke tests (US5)

- **FR-027**: A new xunit project `tests/AkmlSql.Installer.Tests/AkmlSql.Installer.Tests.csproj` MUST exist, targeting `net10.0`, referencing `Xunit.SkippableFact` for the IIS-missing skip path.
- **FR-028**: Every test class MUST carry `[Trait("Category","InstallerSmoke")]` so the default `dotnet test` run excludes them; an opt-in run uses `dotnet test --filter Category=InstallerSmoke`. Mirror of spec 024's `ParityBaseline` and spec 025's `BridgeE2E`.
- **FR-029**: Tests MUST `Skip.IfNot(IsAdministrator() && IsIisInstalled(), "Requires admin + IIS")` so the suite degrades to skipped (not failed) on hosts that cannot run it.
- **FR-030**: The suite MUST cover (at minimum): (a) `Get-Website -Name AkmlSqlWeb` returns a site bound on the IIS port from `INSTALL-SUMMARY.txt`; (b) MIME types `.wasm`, `.dat`, `.blat`, `.br`, `.dll` are registered on the site; (c) `Invoke-WebRequest -Method HEAD http://localhost:<IISPort>/` returns a response with `Content-Security-Policy` header present; (d) `netsh http show sslcert ipport=0.0.0.0:<BridgePort>` returns a binding whose thumbprint matches `INSTALL-SUMMARY.txt` (LAN mode only); (e) `Get-NetFirewallRule -DisplayName "AKML SQL Web Engine"` exists with the expected port (LAN mode only); (f) `INSTALL-SUMMARY.txt` is non-empty and contains a `URL:` line; (g) `Get-FileHash %AppData%\AKML SQL\config.json` is identical before-install and after-install-then-uninstall.
- **FR-031**: The suite MUST run an install → uninstall → re-install cycle to exercise the re-run path; the byte-for-byte hash check (FR-030 (g)) runs at the bracketing points.
- **FR-032**: The suite MUST use the prebuilt `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe` rather than compiling Inno Setup as part of the test run; the test must fail clearly if the EXE is absent ("Build the installer first via `ISCC.exe AkmlSqlSetup.iss`").

#### Documentation (US6)

- **FR-033**: `doc/deployment.md` MUST gain a "Web edition" section covering: Prerequisites (admin rights, Windows 10/11, IIS for the recommended path); Component selection (the four wizard pages); Localhost vs LAN mode; "Don't host" — host-it-yourself with a Python `http.server` example; Silent install with full flag matrix and one happy-path example + one failure-mode example; Uninstall behaviour (what's removed, what's preserved); Troubleshooting for the four most common failures (IIS missing, port collision, admin-rights missing, service fails to start).
- **FR-034**: `doc/WEB/quickstart-m4.md` MUST be updated to remove the "ships as scaffolding" banner once FR-001..FR-032 land and the first interactive integration run completes.
- **FR-035**: A first-interactive-integration-run record MUST land at `specs/026-m4-installer-closure/INSTALL-RUN-NOTES.md` capturing: Windows version, IIS version, Inno Setup version, wall-clock for each phase, screenshots of each wizard page, observed deltas (compile warnings, text adjustments, port-conflict cases), and a list of follow-up tasks. This record is the gating evidence for SC-001 + SC-002 + SC-003.

### Key Entities

- **Integration hook procedures**: The five Pascal Script hook procedures (`Web_Init`, `Web_NextButton`, `Web_Skip`, `Web_PostInstall`, `Web_Uninstall`) plus the `GetIisPort`/`GetBridgePort` port-accessor helpers that `AkmlSqlSetup.iss` invokes after `#include "web-installer.iss"` — four wired into existing event handlers, `Web_Skip` via a new `ShouldSkipPage`. The hookup is the headline FR of this spec.
- **IIS port vs Bridge port**: Two distinct TCP ports modelled as separate wizard inputs. IIS port serves the WASM static bundle (default 80, range 80 or 1024..65535). Bridge port serves the engine's WebSocket transport (default 47291, range 1024..65535). The two MUST differ — enforced in `Web_NextButton` and in the silent-install validation.
- **Pairing PIN file**: `%CommonAppData%\AKML SQL Web\pairing-pin.txt` — 6-digit decimal, UTF-8, no trailing newline. Written by `PairingService` on every PIN change (initial mint + regenerate). Read by `Web_PostInstall` to populate the install summary. ACL: Administrators + SYSTEM read+write only (no standard-user read; leaked PIN allows local impersonation of the operator).
- **Engine-side LAN auth composition**: The wiring in `EngineHost` / `EngineHandlerRegistry` that, in LAN mode, constructs a live `PairingService` + `BearerTokenStore` and registers `HandshakeHandler` via its full constructor so the PIN is actually enforced. In loopback mode the parameterless auto-accept registration is retained. This is the security boundary that makes the printed PIN meaningful — without it, the LAN bridge auto-accepts every connection (the placeholder state spec 025 left behind).
- **Silent-install flag matrix**: `/WEB_HOST=IIS|NONE`, `/WEB_EXPOSURE=LOCALHOST|LAN`, `/WEB_PORT=<N>`, `/BRIDGE_PORT=<N>`. Validated in `InitializeSetup`; cross-checks: (a) NONE + LAN is invalid; (b) `/WEB_PORT == /BRIDGE_PORT` is invalid; (c) every port in `[80, 1024..65535]`.
- **InstallerSmoke test category**: `[Trait("Category","InstallerSmoke")]` — opt-in label on every test in `tests/AkmlSql.Installer.Tests/`. Excluded from default `dotnet test`. Opt-in via `--filter Category=InstallerSmoke`. Skipped (not failed) on non-admin / non-IIS hosts via `Xunit.SkippableFact`.
- **First interactive integration run**: The acceptance-test event on a real Windows host with Inno Setup 7 + IIS + admin rights. Produces `specs/026-m4-installer-closure/INSTALL-RUN-NOTES.md` and a list of follow-up tasks. Gates SC-001, SC-002, SC-003.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a clean Windows 11 host with IIS pre-installed, a user runs `AKMLSQLSetup.exe`, picks Web edition with default values (IIS 80, bridge 47291, localhost), and reaches a working editor at `http://localhost/` within 90 seconds of starting the installer.
- **SC-002**: On a clean Windows 11 host without IIS, a user runs the installer, picks Web edition → Host on IIS, sees the IIS-missing dialog, picks "Enable IIS now", and ends with a working install on the same run (total ≤ 5 minutes including IIS feature install).
- **SC-003**: On two Windows machines (A + B) on the same LAN: A's install summary shows a non-empty 6-digit PIN within 30 seconds of install completion; B pairs from a browser using the printed PIN, hostname, and bridge port; the bridge status bar reaches `Open` within 30 seconds of clicking Add Connection.
- **SC-004**: `AKMLSQLSetup.exe /VERYSILENT /ACCEPTEULA /WEB_HOST=IIS /WEB_EXPOSURE=LOCALHOST /WEB_PORT=80 /BRIDGE_PORT=47291` exits 0 with no UI; `Get-Website -Name AkmlSqlWeb` shows a bound site; `Get-Service AkmlSqlWebEngine` shows Running; `INSTALL-SUMMARY.txt` exists and is well-formed.
- **SC-005**: `AKMLSQLSetup.exe /VERYSILENT /WEB_HOST=NONE /WEB_EXPOSURE=LAN` exits non-zero; the install log contains "LAN exposure requires a hosting mode"; no state is created on disk.
- **SC-006**: `dotnet test tests/AkmlSql.Installer.Tests --filter Category=InstallerSmoke` runs on an interactive Windows host with IIS + admin rights, completes in under 5 minutes, and reports pass on the closure-spec landing commit. The same command on a non-admin / non-IIS host shows every test as Skipped, not Failed.
- **SC-007**: After install → uninstall → re-install cycle, `Get-FileHash %AppData%\AKML SQL\config.json` (IDE-plugin state) returns the same hash before-install, after-install, and after-uninstall — proving the SC-007 invariant from spec 021 holds.
- **SC-008**: A maintainer following `doc/deployment.md` §"Web edition" on a fresh Windows host completes the install in under 5 minutes; every command and click in the doc corresponds to what the wizard actually shows.
- **SC-009**: Every M4 PRD §11 / spec 021 §11 Definition-of-Done checkbox can be marked closed against either a shipped feature (spec 021 Phase 5 T081–T095 already merged) or one of FR-001..FR-035 (incl. FR-003a, FR-007a, FR-013a..FR-013e) in this spec.
- **SC-010**: In LAN mode, the engine refuses a wrong/expired/rate-limited pairing PIN 100% of the time (`HandshakeStatus.PinInvalid`, no bearer minted) and accepts a correct PIN exactly once (single-use) — verified by `EngineHostTests`. In loopback mode, a no-PIN handshake is auto-accepted. There is no configuration under which a LAN connection bypasses PIN/bearer validation.

## Dependencies and Assumptions

### Dependencies

- Spec 021 Phase 5 tasks T081–T095 (already merged) provide the scaffolding: `web-installer.iss`, `web-iis-setup.ps1`, `web-tls-setup.ps1`, `web-firewall.ps1`, `web-config-bridge.ps1`. This spec only wires them in and fixes the port architecture; the scripts themselves are not re-touched except where FR-004 requires routing different ports to different scripts.
- Spec 025 (M3 bridge closure, merged 2026-05-27) provides the LAN-mode TLS code path in `WebSocketTransport.StartAsync` that consumes the cert this installer generates, and the dual-transport `EngineHost` composition (`BuildWebSocketTransport`, FR-027 there). Spec 025 wired the transport + TLS but registered `HandshakeHandler` with the placeholder parameterless ctor — the auth path (`PairingService` + `BearerTokenStore` from spec 021 T063+T064) was never instantiated in production. FR-013a..FR-013e of this spec absorb that left-undone auth wiring (surfaced during the M4 plan-stage audit — the same way spec 025 absorbed the engine-host composition gap mid-plan). FR-008..FR-013 (PIN persistence) build on the live `PairingService` that FR-013a constructs.
- Inno Setup 7 installed at `C:\Program Files\Inno Setup 7\ISCC.exe` for compiling the installer.
- Administrative privileges (already required by `[Setup] PrivilegesRequired=admin`).
- Windows host with PowerShell 5.1+ (already a hard dependency for the IDE-plugin install).
- For US5 (smoke tests): a Windows 11 host with IIS pre-installed and admin rights; `Xunit.SkippableFact` NuGet for the host-mismatch skip path.

### Assumptions

- **IIS port default 80** is acceptable to the operator. If port 80 is already taken by the Default Web Site (the common case), the wizard either replaces it with confirmation or offers a clear alternative — implementation-detail decision during build.
- **Bridge port default 47291** is acceptable. The engine has no special claim on this port; any free `[1024..65535]` value works. Port-collision detection at install time warns (does not block) per FR-003a / PRD §4.5.
- **The pairing PIN file** is single-line decimal, no newline, no BOM. ACL is Administrators + SYSTEM only — the installer (elevated) and the engine service (LocalSystem) are the only legitimate accessors; standard-user read is denied so a local non-admin process can't impersonate the operator's pairing flow.
- **Silent-install errors** are written to Inno Setup's native `/LOG` flag output (default `%TEMP%\Setup Log YYYY-MM-DD #NNN.txt`). No separate logging layer is introduced.
- **The first interactive integration run** is expected to produce 5–15 deltas (compile warnings, text adjustments, port-conflict cases); they are filed as spec 026 follow-up tasks in `specs/026-m4-installer-closure/INSTALL-RUN-NOTES.md` rather than blocking the spec close.
- **Plugin state preservation** at `%AppData%\AKML SQL\` is byte-for-byte identical across Web-edition install/uninstall cycles; SC-007 from spec 021 covers this informally and US5 / FR-030 (g) automates the assertion.
- **Tray-app vs Windows service**: Windows service is the final choice (already shipped, used by spec 025). PRD §5.1's tray-app design is documented as a deferred follow-up only; this spec does not introduce `AkmlSql.Tray.exe`.
- **The IIS deployment model**: the current `web-iis-setup.ps1` creates a dedicated `AkmlSqlWeb` site at the user's IIS port. PRD §4.3's alternative ("application under Default Web Site at `/akmlsql`") is recognised but not adopted — the dedicated-site path is already merged and working, and the rewrite cost is not justified by the user-visible URL difference.
- **IIS detection method**: Pascal Script `RegKeyExists` + `FileExists` (FR-014) is canonical per PRD §4.2. The PowerShell-side `Get-Module -ListAvailable -Name WebAdministration` check remains in `web-iis-setup.ps1` as a defence-in-depth fallback for the rare case where IIS is partially installed.

## Out of Scope (deferred follow-ups)

The M4 PRD §9 and the M4 PRD §11 open questions, plus the "deferred follow-up" notes in spec 021 Phase 5, leave the following items un-addressed by this closure spec. They are listed so the next M4-touching session can find them:

1. **Tray-app design (PRD §5.1)** — `AkmlSql.Tray.exe` with tray icon, "Pair a device" menu, "Restart engine" action. Windows service shipped instead. Future spec can introduce the tray app as an opt-in advanced mode if telemetry shows users want tray-based engine control.
2. **ARR / reverse-proxy single-port** — Out of scope per PRD §9. IIS port and bridge port stay separate.
3. **HTTPS for the IIS-served WASM bundle** — Only the bridge port uses TLS today (per spec 025). Static-bundle HTTPS via a separate cert is a future spec; mixed-content concerns are noted in the threat model.
4. **macOS / Linux deployment** — PRD §9, Phase 16 in the product roadmap.
5. **Domain-joined GPO install** — PRD §9.
6. **TLS fingerprint mismatch dialog** (spec 025 deferred follow-up) — Records to diagnostics today; the user-facing modal that lets a user re-trust a regenerated cert remains unbuilt.
7. **Engine-side tray pairing pane** (spec 021 T065, spec 025 deferred follow-up) — Revoke / Revoke all / Regenerate PIN actions in a Windows WPF tray context. Independent of this installer closure.
8. **In-flight WebSocket revocation** (spec 021 T066-partial, spec 025 deferred follow-up) — Drop open sockets the moment `BearerTokenStore.RevokeByHash` runs.
9. **Multi-engine installs on a single host** — Out of scope; one engine service per machine.
10. **Desktop shortcut creation** (PRD §1 step 7) — Recognised but deferred; the install summary's URL line is sufficient until telemetry shows users want a shortcut.
11. **IIS Express as a fallback** (PRD §10 open question) — Rejected; "Don't host" is the canonical fallback for users without IIS.
12. **Auto-update for the WASM bundle** — Already handled by `AkmlSql.Updater`; no installer changes needed.

Each of these can land as a one-task addition to a future closure or as a standalone follow-up spec if telemetry or user demand surfaces them.
