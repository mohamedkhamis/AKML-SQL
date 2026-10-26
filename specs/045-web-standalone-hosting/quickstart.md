# Quickstart: Web edition hosting without IIS

This is the acceptance gate for "done" (constitution, Development Workflow). Each scenario
names the requirement or success criterion it proves. The order is: automated gates, spikes,
installs on disposable machines, then the owner's two installs.

> **Never run the installer smoke classes on the owner's machines.** They run the real
> installer with the product's AppId, so they upgrade the real install and partly remove it
> during cleanup (codebase-map I21). Every smoke class now also requires
> `AKML_INSTALLER_SMOKE=1`.

## 0. Environment

1. **Builds.** The browser suite needs Debug builds. A Release-only build silently tests stale
   code (CLAUDE.md), and the E2E freshness guard stops on a stale Debug build.
   ```bash
   MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
   "$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet
   "$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal
   dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Debug
   dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug
   ```
2. **Installer.** Run `build.ps1`, which empties the publish folders first. Then run
   `"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss`. The
   probe-ISS tests need ISCC at `C:\Program Files\Inno Setup 7`.
3. **Disposable machines.** Snapshot each one before every install.

   | VM | Windows | IIS | Use |
   | --- | --- | --- | --- |
   | A | Windows 11 | never enabled | standalone; pre-045 upgrade; repair; uninstall; custom folders |
   | B | Windows 11 | enabled, Management Scripts and Tools **off** | readiness (S4), the IIS choice, switching, the clean-IIS reference for SC-006 |
   | C | Windows Server 2022 | snapshot 1: **no** Web-Server; snapshot 2: Web-Server **without** Web-Scripting-Tools; snapshot 3: with them | SC-001/SC-003 on Server, readiness on Server (S4), the SC-012 variant |
   | D | any Windows on the same LAN | — | the "other computer" for the LAN checks |

4. **Logs.**
   - Inno's setup log (`%TEMP%\Setup Log *.txt`, or `/LOG=<file>` when silent) holds the
     `IIS readiness:` and `Web hosting:` lines.
   - `%ProgramData%\AKML SQL Web\install.log` holds what the PowerShell helpers write.
   - Read both, and `INSTALL-SUMMARY.txt`, after every install.

## 1. Automated gates (no install)

| # | Command | Proves |
| --- | --- | --- |
| 1.1 | `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~StaticWebResponder\|FullyQualifiedName~BridgeAccess\|FullyQualifiedName~EngineHost\|FullyQualifiedName~WebSocketTransport"` | Engine contract §2 rows and 503; §4 headers, including 304 by ETag, by whole-second `If-Modified-Since`, and on the fallback; §5 content types; the §8 SC-013 table over raw TcpClient (S5); §1a web-root refusals; slot limits and no slot leak; the CSP drift test (AST); every bundle extension mapped; `webRoot` only in web mode; the bind-failure message; the plain page unchanged without a web root (FR-012, FR-019, FR-022, SC-006, SC-013) |
| 1.2 | `dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj --filter "FullyQualifiedName~Config"` | `bridge.webRoot` round-trips with its exact key |
| 1.3 | `dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj --filter "FullyQualifiedName~EngineEndpoint\|FullyQualifiedName~PageEngine\|FullyQualifiedName~EngineAutoConnect\|FullyQualifiedName~StatusBar\|FullyQualifiedName~StatusIndicator\|FullyQualifiedName~ConnectionPicker\|FullyQualifiedName~EngineConnectDiagnosis"` | The default engine and precedence (FR-014): the three detection answers and their caching; a dangling active id; the strict scheme; prefill and wording |
| 1.4 | `dotnet test tests/AkmlSql.Installer.Tests/AkmlSql.Installer.Tests.csproj --filter "Category!=InstallerSmoke"` | See the breakdown below. (FR-003, FR-004/005/007/008, FR-030/030a, FR-040–FR-044, FR-051, SC-010) |
| 1.5 | After the Debug builds: `dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj --filter "FullyQualifiedName~EngineServedWebTests\|FullyQualifiedName~SpikePageTests" -- xUnit.ParallelizeTestCollections=false` | The engine serves a composed Debug bundle (`src/AkmlSql.Web/wwwroot` + `bin/Debug/net10.0/wwwroot/_framework`). The **freshness guard** fails when `bin/Debug/net10.0/AkmlSql.Web.dll` is older than the newest `*.cs`/`*.razor` in `src/AkmlSql.Web`. A fresh context goes Live with no pairing (US1-4). Every route reloads (SC-005). No request fails, none gets ≥ 400, and the console shows no errors (SC-006). The headers match the contract. The §8 table returns no content (SC-013). **/spike**: the `.sql` files come back as `text/plain`, and the formatted input is SQL, not HTML. (`SpikePageTests` stays as the dev-server regression check.) |
| 1.6 | The full solution build, then the full engine, web and Core unit suites | No regressions; corpora untouched (constitution II, III) |
| 1.7 | `"/c/Program Files/Inno Setup 7/ISCC.exe" src/AkmlSql.Installer/AkmlSqlSetup.iss` | Setup compiles |

**What 1.4 covers.** The installer tests that install nothing:
- **`WebHostingProbeTests`** (probe-ISS over `web-hosting.iss`):
  - `IisReadinessFrom`: every state, including PartMissing;
  - `InferPreviousHost`: with the **real PS 5.1 `config.json`** from `web-config-bridge.ps1`,
    plus the cases Lan only in `IisMode` and Lan only in config;
  - `ResolveSilentHost`: data-model §4 rows 1–12, including the None + LAN flag stop and a
    persisted None + Lan install with no flags kept;
  - the readiness trigger: `/WEB_HOST=IIS`, no flag with previous host IIS, and `/WEB_HOST=SERVICE`
    with the readiness line logged and `Probe=skipped`;
  - `HostingAddress`, and `ReadConfigString`.
- **`WebInstallerSourceTests`**: source-text facts.
  - Absent: `Components: web\iis` and `Components: web\service`; `{tmp}\web-firewall.ps1` and
    `{tmp}\web-config-bridge.ps1`; `dism`.
  - Present: the scripts are installed to `{app}\Support`; three hosting items; the
    `WizardSilent` guards on the page handlers; `Components: web` on `[InstallDelete]`;
    `UninstallSilent` around both uninstall prompts.
- **`WebConfigBridgeTests`**: `Get-BridgeConfigJson` writes `webRoot` and keeps the other keys,
  in PS 5.1 format.
- **`FirewallPlanTests`**: `Get-FirewallPlan` against the data-model §5 table.
- **`IisRemovalPlanTests`**: with WebAdministration; with `appcmd` only; with the site still
  listed (`web.config` kept, failure marker).
- **`WebHostingCheckTests`**: the script runs as a process against an in-test listener.
  - header + HTML → 0;
  - the plain page, or a missing header → 2;
  - `-ExpectBundle` with and without `blazor.webassembly.js` in the body;
  - a closed port → 1 within `-TimeoutSeconds 3`;
  - the reason file is written.

## 2. Spikes (before the work that depends on them is trusted)

| Spike | Where | Pass |
| --- | --- | --- |
| S1 unknown component names | a probe setup; then VM A with an earlier release (default components) upgraded | no error; no stray component state |
| S2 disabled IIS item | VM A at 100%, 150% and 200% DPI, light and dark wizard styles | IIS is greyed and the reason is readable; arrow keys, mouse and accelerators cannot select it |
| S3 LAN standalone | VM A (LAN install) and VM D | `https://A:47291/` serves the app and `wss`; `https://localhost:47291/` works on A over `::1`; `http://A:47291/` returns no app; after trusting the certificate, a reload shows 304s |
| S4 readiness | VM B; VM C snapshots 2 and 3; toggle each Common HTTP Feature and the management tools | the states match data-model §2; record which feature turns `ToolsUnavailable` into `Ready` and put its name in the reason text; a cold probe takes under 5 s |
| S5 path decoding | gate 1.1 | the §8 table returns no content |
| S6 delayed flag | VM A, after each `sc config` | record whether `start= auto` clears `DelayedAutostart`. The explicit write makes the result `AUTO_START` without "(DELAYED)" either way. |
| S7 custom-folder owner | VM A: install an earlier release to `D:\AKML3` | record the owner SID; quickstart 3.12(d) passes, or R22 switches to its fallback |

## 3. Installs on disposable machines

| # | Machine | Steps | Expect | Proves |
| --- | --- | --- | --- | --- |
| 3.1 | A, and C snapshot 1 | Interactive install with every default; time it from launch to a working editor. | Standalone is selected and IIS is greyed with "IIS is not enabled…". Only the engine port is asked. The Ready memo shows "Standalone Windows service at http://localhost:47291/". The Finish link opens `http://localhost:47291/`, which goes Live with no engine address typed. The summary reads "Hosting: Standalone Windows service" with no IIS warning. Under 3 minutes. | US1, SC-001, SC-002 |
| 3.2 | A, and C snapshot 1 | Check the machine after 3.1. | No AkmlSqlWeb site and no app pool; the IIS feature still off. The setup log has `IIS readiness: NotEnabled …` and `Web hosting: Service (from page)`. No firewall rule. Registry `Host=Service`, `Exposure=Localhost`. `sc qc AkmlSqlWebEngine` shows AUTO_START without "(DELAYED)". `icacls` on `%ProgramData%\AKML SQL Web` shows owner Administrators, with Administrators and SYSTEM only. | SC-003, FR-008, FR-011, FR-018, FR-020 |
| 3.3 | A | Restart without signing in. Time it to the first 200 on `http://127.0.0.1:47291/`. Then kill `AkmlSql.Engine.exe`. | It answers within 3 minutes of boot. The service restarts by itself. | SC-004, FR-011 |
| 3.4 | A | Reload `/`, `/editor`, `/history`, `/settings`, `/settings/connections`, `/settings/ai`, `/snippets`, `/styles`, `/diagnostics`, `/spike` with DevTools ▸ Network open. | Every page loads. No failed request and none ≥ 400. The second load shows 304s. | SC-005, SC-006 |
| 3.5 | A | Send the contract §8 table with `curl --path-as-is`. | No file content and no listing for any row. | SC-013, FR-019 |
| 3.6 | A; and C snapshot 2 | Restore the snapshot. Install the previous release with defaults, so the site is never created. Upgrade interactively; run the C variant through `AkmlSql.Updater.exe --install` with a staged update. | Standalone is preselected; IIS is greyed (NotEnabled on A, ToolsUnavailable or PartMissing on C). After the upgrade the web edition answers at `http://localhost:47291/`. | SC-012, FR-030a, US3-7 |
| 3.7 | A + D | LAN install (Standalone + LAN). On D, open `https://A:47291/`, trust `bridge.cer`, pair with the PIN and run a query. From D, also try `http://A:47291/`. | One `AKML SQL Web Engine` rule and no HTTPS-site rule. Plain http from D gets no app. `https://A:47291/` works on A. The summary includes the profile=any line. | US4, SC-009, FR-016–FR-018, S3 |
| 3.8 | A + D | On the localhost install of 3.1: from D, try `http://A:47291/` and `ws://A:47291/akmlsql`. Then re-run setup switching LAN ↔ localhost, and once changing the port to 47300. | D gets nothing. Each re-run shows the move text on the Ready page. The rule moves to 47300 in LAN mode and is removed in localhost mode. | US4-4, FR-016, FR-018, FR-035 |
| 3.9 | A | Re-run setup after 3.1. Then install with None and re-run. | Standalone is preselected (and kept) on the first; None is preselected (and kept) on the second. After the None install the Start-menu repair shortcut is gone. | FR-030, FR-034, FR-060 |
| 3.10 | A | Repair, three runs. **(a)** Stop and disable the service, delete the firewall rule (LAN install), then run repair. **(b)** Rename `{app}\Web\index.html`, restart the service, restore the file, then run repair. **(c)** Re-run choosing None, then click an old-version shortcut if one exists, and the new one. | **(a)** The service is back (AUTO_START, not delayed); the address answers; the rule is back. **(b)** Repair restarts the service; the check passes; the page carries `X-AKML-Host: engine`. **(c)** No AkmlSqlWeb site is created ("does not host the web edition on IIS" / "Nothing to repair"). | US6-1, FR-060, FR-043 |
| 3.11 | A | Uninstall interactively. Then reinstall LAN and uninstall with `/VERYSILENT` only. Then reinstall and uninstall with `/VERYSILENT /SUPPRESSMSGBOXES`. | The service is gone, no `AKML SQL Web*` rule remains, and no `CN=AKML SQL Web Engine` certificate is left in LocalMachine\My (LAN). `{app}\Web` is gone. Neither silent uninstall stops on a dialog, and both keep the data. | FR-061, FR-062, FR-071, SC-008 |
| 3.12 | A | Custom folders and squatting. **(a)** Install to `C:\AKML` and to `D:\AKML`. **(b)** As a standard user, pre-create `C:\ProgramData\AKML SQL Web`, then install. **(c)** Pre-create `D:\AKML2` as a standard user with a file in it, then install there. **(d)** Install an earlier release to `D:\AKML3`, then upgrade with 045 setup (S7). | **(a)** `icacls` on `{app}`, Web, Support and Engine shows no write ACE for Users or Authenticated Users, and writing as a standard user is denied. Repair still runs. **(b)** The folder's owner becomes Administrators, and the standard user cannot read `config.json`. **(c)** Setup refuses the folder before any change. **(d)** The upgrade is **not** refused, and the folder ends up hardened as in (a). | FR-020, R22, S7 |
| 3.13 | A | Failure paths. **(a)** After the ports page, start a PowerShell `HttpListener` on `http://+:47291/`, an HTTP.SYS registration, then finish. **(a′)** Repeat with a plain TCP listener (`TcpListener`) on 47291. **(b)** Make the service fail to start (corrupt `config.json` while setup is on the Ready page). **(c)** Interactive install with 47291 held. | **(a)** The summary says the address did not answer or did not serve the pages, gives the reason, names the holder's image path (from `netsh http show servicestate`), and points to repair. **(a′)** The same warning without a holder name: plain sockets are not listed by HTTP.SYS. **(b)** The summary says the service is not running and how to start it. **(c)** The ports page blocks with "Port 47291 is already used by another program". | FR-051, SC-011, FR-015 |
| 3.14 | A | Re-run with the web edition unticked, after a Service install. | `http://localhost:47291/` still loads with no failed requests (the runtime is not deleted); the service stays AUTO_START. | edge case, R9 |
| 3.15 | B | Interactive install: first Standalone, then a re-run with IIS (default site port 80 while Default Web Site runs), then a re-run with IIS on port 8090, then a re-run back to Standalone. | IIS is enabled and Standalone preselected. With port 80, the check reports "answered but not serving the pages" (IIS welcome page), not a working address. On 8090, IIS serves the app and `http://localhost:47291/` shows the plain page (FR-022). Each switch shows the move text first. After the switch back, the AkmlSqlWeb site, its app pool and `{app}\Web\web.config` are gone, and other sites are untouched. | US2, US3-3/4, FR-021, FR-022, FR-032, FR-033, FR-051, SC-008 |
| 3.16 | B | Compare a clean IIS install (8090) with Standalone for the same files: the headers of `/`, `_framework/dotnet.js`, a `.wasm`, a `.css`; reload every route listed in 3.4 on IIS. | Same CSP, `no-cache`, and validators with 304. The differences are exactly those in the engine contract §7. Every route loads on IIS too. | SC-005, SC-006 |
| 3.17 | B + D | IIS LAN install; on D, pair over `https://B:{HttpsPort}/`. Then: **(a)** delete the site and both rules, stop and disable the service, run repair; **(b)** switch to Standalone; **(c)** uninstall a fresh IIS install. | **(a)** D loads and pairs again. **(b)** Only `AKML SQL Web Engine` remains, and the old HTTPS port answers nothing for D. **(c)** The site, app pool, both rules and the owned HTTPS binding are gone, and other sites are untouched. | US6-2, FR-060, SC-008, US6-3 |
| 3.18 | B; and C snapshot 3 | After an IIS install: **(a)** disable W3SVC, then upgrade interactively and silently (no flags); **(b, C)** remove Web-Scripting-Tools, then switch to Standalone. | **(a)** Interactive: IIS greyed with the reason, Standalone preselected, the move line shown. Silent: it moves to Standalone, and the log shows the reason and the new address. **(b)** No AkmlSqlWeb site remains (`appcmd` fallback), or the summary warns with the removal command. | FR-031, FR-041, FR-032, FR-061 |
| 3.19 | B; C | Toggle each Common HTTP Feature, the management tools and IIS itself; open the hosting page each time. | The readiness state and reason match data-model §2 (S4). | FR-004, FR-005, FR-007 |
| 3.20 | A, B | Silent matrix: `/WEB_HOST=SERVICE`, `IIS`, `NONE`, `bogus`; `/WEB_HOST=SERVICE /WEB_PORT=8080`; `/WEB_HOST=NONE /WEB_EXPOSURE=LAN`; `/WEB_HOST=IIS` on A; no flags on a new install; no flags on an upgrade of Service / IIS / None+Lan; a port held by another program. | Each result matches data-model §4. Stops happen before any change (the service is not stopped) and give the reason in `/LOG`. The `Web hosting:` and `IIS readiness:` lines are present. | US5, SC-010, FR-015, FR-008 |
| 3.21 | A | After an upgrade from a pre-045 release that has the stale `[UninstallRun]` entries, inspect the new `unins*.dat`. | No `\is-*.tmp\` path in it. Uninstall still removes everything. | R23 |

The installer smoke classes automate 3.1/3.2, 3.7 and 3.15's switch, and part of 3.20, through
`StandaloneSmokeFixture` (cases `Standalone`, `LanStandalone`, `IisToStandaloneSwitch`) on VMs A
and B. They require `AKML_INSTALLER_SMOKE=1`. `CanRunService` requires an administrator;
`CanRunIis` requires an administrator with IIS `Ready`. The uninstaller path comes from the
uninstall key.

## 4. The owner's installs (with the owner, after §1–§3 pass)

These are interactive upgrades only, run when the owner approves, as with earlier deployments.
Note the browser's saved connections first.

| # | Install | Expect | Proves |
| --- | --- | --- | --- |
| 4.1 | Workstation: IIS `:8083`, localhost, `C:\Program Files (x86)\AKML SQL` | IIS preselected and enabled (the site exists). After the upgrade, `http://localhost:8083/` still opens with its saved connections, and the bridge answers on 47291. | SC-007, FR-030 |
| 4.2 | Server: IIS `:59416` LAN, HTTPS `:59418`, bridge `:59417` | IIS preselected; LAN kept, not reset to localhost. Other computers still open `https://server:59418/`, and their pairing still works. Two firewall rules, as before. | SC-007, FR-030, I14 fix |

## 5. Documentation check

Each document below must describe these points (FR-070):
- the three hosting choices, and when IIS counts as ready;
- the standalone address and its single firewall rule;
- the silent flags: `/WEB_HOST=SERVICE|IIS|NONE`, and `/WEB_PORT` and `/WEB_HTTPS_PORT` as
  IIS-only;
- that switching host changes the address;
- the multi-session limit, wherever the page lists the security notes.

| Document | What it gains |
| --- | --- |
| `doc/deployment.md` | the points above |
| `doc/configuration.md` | a new `bridge` section, plus the registry values |
| `doc/architecture.md` | §9d |
| `doc/ipc-api.md` | the transport table |
| `doc/m3-security.md` | the static surface, the containment rule, the multi-session note |
| `doc/WEB/00-INDEX.md` | the points above |
| `doc/WEB/M3-websocket-transport.md` | the points above |
| `doc/WEB/quickstart-m3.md` | the points above |
| `doc/WEB/quickstart-m4.md` | the points above |
| `doc/WEB/M4-iis-installer.md` | a superseded note |
| `doc/WEB/ai-local-provider-cors.md` | the new origin |
| `doc/topics/browser-setup.md` | the points above (public docs) |
| `CLAUDE.md` | Installer Details, the latest-work list |
| `doc/progress.md` | the 045 entry and the deferred list |

The site's docs pipeline tests (`tests/AkmlSql.Site.Tests`) stay green after the `doc/topics`
edit.
