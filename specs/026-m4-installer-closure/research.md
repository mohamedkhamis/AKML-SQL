# Research: M4 — Installer (IIS Deployment Option) Closure

Six decisions drive the plan, grounded in a plan-stage audit of the actual installer + engine code. Each records the decision, rationale, and alternatives considered.

---

## Decision 1 — Installer integration shape (`#include` + hook calls + new `ShouldSkipPage`)

**Decision**: `#include "web-installer.iss"` near the end of `AkmlSqlSetup.iss`'s `[Code]` section, then add four hook calls inside the existing event procedures, and **add a brand-new `ShouldSkipPage` function** to host the `Web_Skip` call (five hook procedures wired in total).

The audit (`grep` for the procedure signatures) confirmed `AkmlSqlSetup.iss` already defines:

| Procedure | Line | Hook to add |
|-----------|------|-------------|
| `InitializeWizard` | 345 | `Web_Init();` |
| `InitializeSetup` | 403 | silent-flag parse + cross-validation (Decision 6 / US4) |
| `NextButtonClick` | 469 | `if not Web_NextButton(CurPageID) then Result := False;` |
| `CurStepChanged` | 579 | `if CurStep = ssPostInstall then Web_PostInstall();` |
| `CurUninstallStepChanged` | 674 | `if CurUninstallStep = usUninstall then Web_Uninstall();` |
| `ShouldSkipPage` | **absent** | **create the function**, body `Result := Web_Skip(PageID);` |

**Rationale**: `web-installer.iss`'s own header comment lists exactly this integration recipe but it was never applied — the file is dead code. The critical non-obvious finding is that `ShouldSkipPage` does **not exist** in `AkmlSqlSetup.iss`; `web-installer.iss` assumes a caller that isn't there, so `Web_Skip` (which hides the web pages when the component is unticked) never runs. The integration must create the function, not just add a line to it.

**Alternatives considered**:

- *Move all web logic directly into `AkmlSqlSetup.iss`* — rejected; the hook-procedure indirection is already written and tested-by-construction in `web-installer.iss`, and Pascal Script forbids declaring a procedure twice, so keeping the web logic in its own file with named hooks is the only clean composition.
- *Auto-skip via `Check:` flags instead of `ShouldSkipPage`* — rejected; the wizard pages are created with `CreateInputOptionPage`, which is governed by `ShouldSkipPage`, not `[Components] Check:`.

---

## Decision 2 — Two-port split (IIS port default 80 + bridge port default 47291)

**Decision**: Replace the single `WebPortPage` (default 47291) with two wizard pages — `WebIisPortPage` (default 80) and `WebBridgePortPage` (default 47291). Route the IIS port to `web-iis-setup.ps1`; route the bridge port to `web-config-bridge.ps1`, `web-tls-setup.ps1`, `web-firewall.ps1`. `Web_NextButton` rejects equal ports.

**Rationale**: The audit found `web-installer.iss` passes one `GetWebPort` value to *both* `web-iis-setup.ps1 -Port` (IIS site binding) and `web-config-bridge.ps1 -Port` (engine WebSocket binding). On Windows these are two separate HTTP.SYS listeners; IIS owning port *X* means the engine's `HttpListener` cannot also bind *X* for `http://+:X/`. The PRD §4.1 (`http://localhost/akmlsql/`, i.e. port 80) + §4.4 (bridge 47291) already assumed two ports; the merged code conflated them. The user confirmed the two-port model.

**Alternatives considered**:

- *Single port + IIS ARR reverse-proxy to the bridge* — rejected; out of scope per PRD §9, adds an ARR install dependency.
- *Single port, bridge on port+1 implicitly* — rejected; surprising, undocumented, and still collides if port+1 is taken.
- *Fixed bridge port 47291 (no wizard input)* — considered (it was the runner-up the user was offered); rejected in favour of an explicit input so an operator with 47291 already taken can change it without editing config.json.

---

## Decision 3 — Engine-side LAN auth composition (the security boundary)

**Decision**: In `EngineHost`, when `BridgeOptions.IsLoopback == false`, construct `new PairingService()` + `new BearerTokenStore(bridge.TokenStorePath, TimeSpan.FromDays(bridge.TokenTtlDays))` and register `HandshakeHandler` via its **full** constructor with `pairingRequired: () => true`, `pinValidator: pin => pairingService.ValidatePin(sourceId, pin) == PinAttemptResult.Valid`, `bearerValidator`/`bearerMinter` bound to `BearerTokenStore`, and `serverCanonicalIdentityProvider` from the existing resolver. Loopback / no-bridge keeps the parameterless registration. The hardcoded `new HandshakeHandler()` at `EngineHandlerRegistry.cs:258` becomes a handler the host supplies.

**`sourceId` plumbing (resolved during the plan audit)**: `RpcContext` is a per-process **shared singleton** (`RpcContext.cs`: *"Per-process shared state passed to every IRpcRequestHandler invocation"*), so it cannot carry the per-connection source IP — `WebSocketTransport` instead sets an `AsyncLocal<IPAddress?>` per connection that the `pinValidator` closure reads. A constant or client-supplied `sourceId` is forbidden (it would collapse the per-source rate limit into a global one and let an attacker lock out the operator). See `contracts/lan-auth-composition-contract.md` C1.

**Rationale**: The audit found the live engine registers `new HandshakeHandler()` — the parameterless constructor whose callbacks (`HandshakeHandler.cs:37-45`) are `pairingRequired: () => false`, `pinValidator: _ => true`, `bearerValidator: _ => true`, `bearerMinter: _ => null`. So **the LAN bridge auto-accepts every connection regardless of PIN**, and `PairingService`/`BearerTokenStore` are never instantiated in production (`grep` for `new PairingService` across `src/` returns zero hits outside tests). Spec 025 closed the bridge transport + TLS but left auth as a placeholder (the registry comment says "NO_AUTH semantics intact"). The `HandshakeHandler.HandleAsync` body already implements the full PIN→bearer→reconnect flow (lines 87–148) — it just needs live delegates instead of the all-permissive stubs. The user confirmed enforced LAN pairing is a real security boundary M4 must ship.

**Alternatives considered**:

- *Write the PIN to disk but leave auth unwired* — rejected as incoherent (the advisor flagged it as the one wrong option): a printed PIN that validates nothing is a security illusion.
- *Defer auth to a separate spec 027* — offered to the user (the coherent "deploy now, harden later" path); declined.
- *Have `WebSocketTransport` construct its own `PairingService` internally* — rejected; the transport should stay transport-only, and `EngineHostTests` needs to inject the services to assert the composition matrix. Composition belongs in the host.

---

## Decision 4 — PIN-file writer (`PairingPinFile` on `PinChanged`)

**Decision**: A new ~40 LOC `PairingPinFile` class with `Publish(string pin)` doing an atomic temp+rename write to `%CommonAppData%\AKML SQL Web\pairing-pin.txt`. `EngineHost` subscribes `pairingService.PinChanged += (_, pin) => pinFile.Publish(pin)` and then calls `pinFile.Publish(pairingService.CurrentPin)` once to capture the initial mint. Write failures are caught + Serilog-logged; engine startup never fails on a PIN-file error.

**Rationale**: The audit confirmed `PairingService` mints the initial PIN *inside its constructor* (`PairingService.cs:56` calls `RegeneratePin()`), firing `PinChanged` *before* any external subscriber can attach. So a subscribe-only approach misses the first PIN — hence the one-shot `CurrentPin` publish after subscription. The atomic write mirrors `ConfigManager.Save` (the established temp+`File.Replace` pattern referenced in CLAUDE.md). The directory and its Administrators+SYSTEM ACL are created by the **installer** before the service starts (so the LocalSystem engine writes into an already-locked-down dir); the engine just writes the file.

**Alternatives considered**:

- *Add a `pinFilePath` constructor parameter to `PairingService`* — rejected; couples the pure auth-logic class to file I/O and to a path it shouldn't know. The event-based writer keeps `PairingService` pure (it currently has zero file I/O).
- *Have the engine ACL the file itself* — rejected; the engine runs as LocalSystem and the installer runs elevated with the operator's context — the installer is the right place to set the ACL (FR-010), and it must exist before the service first writes.
- *Plain (non-atomic) write* — rejected; `Web_PostInstall` polls and reads concurrently with the engine's first write; a partial read would corrupt the summary.

---

## Decision 5 — IIS-not-installed detection + three-path dialog

**Decision**: Add `function IsIisInstalled(): Boolean` to `web-installer.iss` returning `RegKeyExists(HKLM, 'SOFTWARE\Microsoft\InetStp') and FileExists(ExpandConstant('{sys}\inetsrv\appcmd.exe'))`. Gate it so that, when the user has the web component + "Host on IIS" selected and IIS is absent, a three-button `MsgBox` (Enable IIS now / Switch to Don't host / Cancel install) fires before the Hosting page commits. "Enable IIS now" runs `dism /online /enable-feature /featurename:IIS-WebServerRole /All /Quiet /NoRestart`. Silent mode + `/WEB_HOST=IIS` + missing IIS → log + non-zero exit, no dialog.

**Rationale**: PRD §4.2 specifies this exact predicate and the three paths. The current `web-iis-setup.ps1` only checks `Get-Module -ListAvailable -Name WebAdministration` and silently `exit 0` when absent — so the install "succeeds" but the URL never works, with no guidance. The Pascal-script predicate is stronger (the registry key + `appcmd.exe` presence is the canonical IIS-installed signal; the PS module can exist without the IIS role). The existing PS check stays as defence-in-depth.

**Alternatives considered**:

- *Keep only the PowerShell check* — rejected; it's too weak (module-present ≠ IIS-installed) and gives the user no remediation path.
- *Auto-run `dism` without asking* — rejected; enabling a Windows Server Role is a material system change that deserves explicit consent (and can take 30–60 s + sometimes a reboot).
- *Block the install entirely when IIS is missing* — rejected; "Don't host" is a legitimate path (lay down the bundle, user serves it themselves).

---

## Decision 6 — Installer-smoke harness (opt-in, host-gated)

**Decision**: A new `tests/AkmlSql.Installer.Tests` csproj (`net10.0`) using `Xunit.SkippableFact`. `InstallerSmokeFixture : IAsyncLifetime` runs the prebuilt `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe` silently, captures `INSTALL-SUMMARY.txt`, and tears down via a silent uninstall. Every test carries `[Trait("Category","InstallerSmoke")]` (excluded from default `dotnet test`) and `Skip.IfNot(IsAdministrator() && IsIisInstalled(), ...)`. The silent-flag parse (Decision 1's `InitializeSetup` work) is the precondition that makes the fixture's unattended install possible.

**Rationale**: Spec 021 T086/T090/T097 are all deferred for want of a test host with IIS + admin. The opt-in trait + skip-gate is the established pattern in this repo (spec 024 `ParityBaseline`, spec 025 `BridgeE2E`) — it keeps CI / normal `dotnet test` green on hosts that can't run installer smoke, while giving maintainers a one-command pre-merge gate on a real host. Using the prebuilt EXE (not compiling Inno Setup in-test) keeps the suite fast and avoids an `ISCC.exe` dependency in the test runner.

**Alternatives considered**:

- *Compile the installer inside the test* — rejected; adds an `ISCC.exe` dependency to the test host and slows the suite; the contract instead fails clearly if `Output/AKMLSQLSetup.exe` is absent.
- *Mock IIS / netsh / firewall* — rejected; the entire value of these tests is asserting the real provisioning happened. A mocked installer smoke test asserts nothing.
- *Run in CI* — rejected for now; CI runners lack IIS + admin. Developer-side, same constraint as the parity + bridge E2E suites.
