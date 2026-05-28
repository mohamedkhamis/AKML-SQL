# Quickstart: M4 — Installer (IIS Deployment Option) Closure

Developer walkthrough to land each user story, in dependency order. Each step ends with a concrete verification. Build prereqs: .NET 10 SDK, Inno Setup 7 (`ISCC.exe`), Windows with admin rights; IIS for the on-host verification steps.

## Prep — build the artefacts the installer bundles

```powershell
dotnet publish src/AkmlSql.Web    -c Release                # WASM bundle → bin/Release/net10.0/publish/wwwroot
dotnet publish src/AkmlSql.Engine -c Release -r win-x64     # engine → {app}\Engine\
```

## US1 — Integration wiring + two-port split (P1, headline)

1. In `src/AkmlSql.Installer/AkmlSqlSetup.iss`: add `#include "web-installer.iss"` at the end of `[Code]`; insert the five hook calls at the audited lines (InitializeWizard@345, NextButtonClick@469, CurStepChanged@579, CurUninstallStepChanged@674); **create a new `ShouldSkipPage` function** that calls `Web_Skip` (it does not exist today — see `contracts/installer-integration-contract.md` C1).
2. In `web-installer.iss`: split `WebPortPage` into `WebIisPortPage` (default 80) + `WebBridgePortPage` (default 47291); route IIS port → `web-iis-setup.ps1`, bridge port → the other three helpers; reject equal ports in `Web_NextButton` (contract C2).
3. Compile: `& "C:\Program Files\Inno Setup 7\ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss`.
4. **Verify**: run `Output/AKMLSQLSetup.exe`, tick "Web edition", confirm the four pages (Hosting / Network / IIS Port / Bridge Port) appear and Skip when the component is unticked; finish a localhost install; browse `http://localhost/` → the editor loads.

## US2 — Enforced LAN pairing, then PIN to disk (P1, security boundary)

> Auth first — a persisted PIN is cosmetic until the handshake enforces it.

1. In `src/AkmlSql.Engine/EngineHost.cs`: when `BridgeOptions.IsLoopback == false`, construct `PairingService` + `BearerTokenStore(bridge.TokenStorePath, …)` and build a `HandshakeHandler` via its full constructor wired to those (contract `lan-auth-composition-contract.md` C1).
2. In `src/AkmlSql.Engine/EngineHandlerRegistry.cs`: replace the hardcoded `new HandshakeHandler()` (line 258) with the host-supplied handler (optional param, default null ⇒ parameterless for loopback / named-pipe).
3. Extend `tests/AkmlSql.Engine.Tests/EngineHostTests.cs` with the composition matrix (C4): wrong PIN → `PinInvalid`; right PIN → `Ok` + bearer + single-use; loopback → auto-accept; shared `RpcRouter`.
4. Add `src/AkmlSql.Engine/Pairing/PairingPinFile.cs` (atomic temp+rename writer); in `EngineHost` subscribe to `PairingService.PinChanged` and publish `CurrentPin` once after subscribing (`pairing-pin-file-contract.md` C2–C3).
5. In `web-installer.iss`: create `%CommonAppData%\AKML SQL Web\` with the Administrators+SYSTEM ACL before starting the service (C4); `Web_PostInstall` polls `pairing-pin.txt` (30 s) and bakes the PIN into `INSTALL-SUMMARY.txt`, else the fallback text (C5).
6. **Verify**: `dotnet test tests/AkmlSql.Engine.Tests --filter FullyQualifiedName~EngineHostTests` green; a LAN install writes a 6-digit `pairing-pin.txt` with the right ACL and the summary shows it; a wrong PIN from a browser is refused, the right PIN mints a bearer and reaches `Open`.

## US3 — IIS-not-installed dialog (P2)

1. Add `function IsIisInstalled(): Boolean` (registry + `appcmd.exe`) to `web-installer.iss`; gate the three-path `MsgBox` before the Hosting page commits (`iis-detection-contract.md` C1–C2).
2. Wire "Enable IIS now" → `dism … IIS-WebServerRole`; "Switch to Don't host" → `WebHostPage.SelectedValueIndex := 1`; "Cancel" → abort. Silent-mode branch logs + exits non-zero (C3). Add the Don't-host success text (C4).
3. **Verify**: on a host with IIS removed (`dism /online /disable-feature /featurename:IIS-WebServerRole`), the dialog fires; each button behaves per contract; `/VERYSILENT /WEB_HOST=IIS` exits non-zero with the log line.

## US4 — Silent-install flags (P2)

1. In `AkmlSqlSetup.iss` `InitializeSetup`: parse `/WEB_HOST` `/WEB_EXPOSURE` `/WEB_PORT` `/BRIDGE_PORT`; enforce the two cross-validation rules (NONE+LAN invalid; equal ports invalid) with non-zero exit + log line (`installer-integration-contract.md` C3).
2. **Verify**: the happy-path command exits 0 with a working install; `/WEB_HOST=NONE /WEB_EXPOSURE=LAN` and `/WEB_PORT=80 /BRIDGE_PORT=80` each exit non-zero with the documented log line and leave no state.

## US5 — Installer smoke suite (P3)

1. Create `tests/AkmlSql.Installer.Tests` (net10.0 + `Xunit.SkippableFact`), register in `AKML-SQL.slnx`.
2. Write `InstallerSmokeFixture` (silent install → capture summary → silent uninstall, gated on admin+IIS) and the four test classes per `installer-smoke-suite-contract.md` C3–C4.
3. **Verify**: on an IIS+admin host, `dotnet test tests/AkmlSql.Installer.Tests --filter Category=InstallerSmoke` green in < 5 min; on a non-admin host every test Skipped; plain `dotnet test` runs none of them.

## US6 — Docs + first interactive run (P3)

1. Add the "Web edition" section to `doc/deployment.md` (prerequisites, the four wizard pages, localhost/LAN/Don't-host, silent flags, uninstall, troubleshooting) per FR-033.
2. Run the **first interactive integration run** on a real Windows host (Inno Setup 7 + IIS + admin); record version info, per-phase wall-clock, wizard screenshots, and observed deltas in `specs/026-m4-installer-closure/INSTALL-RUN-NOTES.md` (FR-035). File deltas as follow-up tasks.
3. Once integration lands + the run is recorded, remove the "ships as scaffolding" banner from `doc/WEB/quickstart-m4.md` (FR-034).
4. **Verify**: a fresh reader follows `doc/deployment.md` §Web edition end-to-end in < 5 min and every command/click matches the wizard.

## Done when

SC-001..SC-010 hold: localhost install ≤ 90 s; IIS-missing → enable → working ≤ 5 min; two-machine LAN pair ≤ 30 s; silent happy/failure paths correct; smoke suite green on an IIS host; plugin state byte-for-byte preserved (SC-007); LAN PIN enforced 100% (SC-010); every M4 DoD checkbox closable against a shipped feature or an FR.
