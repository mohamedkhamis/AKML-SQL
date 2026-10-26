# Contract: setup (Inno Setup 7) for the web edition's hosting

**Branch**: `045-web-standalone-hosting` | **Date**: 2026-10-26

Decision ids (R…) refer to [research.md](../research.md). The tables that the data model defines
are binding here and are not repeated: readiness §2, registry and previous state §3, silent
resolution §4, firewall plan §5, switching §9, address §11 and ACLs §12
([data-model.md](../data-model.md)).

## 1. Files and sections

| Item | Today | After 045 |
| --- | --- | --- |
| `[Components]` | `web`, `web\iis`, `web\service` | `web` only. If spike S1 fails, keep both sub-entries with `Check: False`. |
| `web-hosting.iss` (**new** include) | — | `[Code]` only, pure functions: `IisReadinessFrom`, `InferPreviousHost`, `ResolveSilentHost`, `HostingAddress`, `ReadConfigString`. `#include`d before `web-installer.iss`. |
| `web-iis-setup.ps1`, `web-https-binding.ps1`, `web-iis-repair.ps1` → `{app}\Support` | `Components: web\iis` | `Components: web`, every host, because repair and switching need them. `web-iis-setup.ps1` gains `.sql = text/plain` in `$mimes`. `web-iis-repair.ps1` gains a guard: it refuses unless the registry `Host` is absent or `IIS`. |
| `web-firewall.ps1`, `web-config-bridge.ps1` | `{tmp}`, `deleteafterinstall` | `{app}\Support`, kept. Each is restructured into pure dot-sourceable functions (`Get-FirewallPlan`, `Get-BridgeConfigJson`) plus an entry point guarded by `if ($MyInvocation.InvocationName -ne '.')`. `#Requires` is dropped, and the entry points check for administrator rights themselves. |
| `web-iis-remove.ps1` (**new**) → `{app}\Support` | — | IIS removal for switches and uninstall (§6) |
| `web-hosting-repair.ps1` (**new**) → `{app}\Support` | — | the repair shortcut's target (§7) |
| `web-hosting-check.ps1` (**new**) → `{app}\Support` | — | the answer check (§5) |
| `[Run]` firewall entries (`{tmp}\web-firewall.ps1`, engine rule and HTTPS-site rule) | present | **removed**; `Web_PostInstall` applies the firewall plan |
| `[InstallDelete]` | — | `Type: filesandordirs; Name: "{app}\Web\_framework"; Components: web` |
| `[UninstallDelete]` | `{app}\Support`, `{app}\Engine`, `dirifempty {app}` | adds `filesandordirs: {app}\Web`, which includes IIS's untracked `web.config` |
| `[UninstallRun]` | service stop and delete; two firewall removals via `{tmp}` | **removed**; `Web_Uninstall` does this work (§8). Entries recorded by older installs are dropped by the fresh log (§9). |
| `[Icons]` "Repair AKML SQL Web hosting" | `Components: web\iis` → `web-iis-repair.ps1` | `Check: WebHostHasHost` → `web-hosting-repair.ps1`. **The name is unchanged.** For None, `Web_PostInstall` deletes an existing `.lnk`. |

## 2. Wizard

The page order is unchanged: … wpSelectDir → wpSelectComponents → **WebHostPage** →
WebNetworkPage → **WebPortsPage** → … → wpReady (new memo) → wpPreparing (`PrepareToInstall`) →
… → InstallSummaryPage → wpFinished.

**WebHostPage** ("Web edition hosting"):

| Index constant | Caption | Enabled |
| --- | --- | --- |
| `WebHostServiceIdx` = 0 | `Standalone Windows service (recommended)` | always |
| `WebHostIisIdx` = 1 | `IIS` | only when readiness is `Ready` |
| `WebHostNoneIdx` = 2 | `Don't host (files only)` | always |

- **Sub-caption**: "The standalone service needs nothing else on this computer. Choose IIS if
  you manage your sites in IIS."
- **Notice label**: a word-wrapped label under a shortened list. It shows, in order:
  1. the IIS readiness reason, when IIS is not `Ready` (data-model §2);
  2. the FR-031 move line, when the previous host was IIS and IIS is no longer ready: "IIS is
     no longer available on this computer, so the web edition will move to the standalone
     service.";
  3. the FR-033 address-change text (data-model §11, R14), when
     `HostingAddress(previous) <> HostingAddress(selected)` and both are non-empty.
- **`OnActivate`**:
  1. `if WizardSilent then Exit;`
  2. Evaluate readiness and log it.
  3. Set `ItemEnabled[WebHostIisIdx]`.
  4. The first time the page shows, preselect the previous host (`InferPreviousHost`), or
     Service on a new install.
  5. If IIS is selected but not `Ready`, set `SelectedValueIndex := WebHostServiceIdx`.
- **`CheckListBox.OnClickCheck`**: `if WizardSilent then Exit;`, then recompute the notice.
- **`Web_NextButton`** copies the selection into the `WebHost` global. Nothing else reads the
  page.
- The dism "Enable IIS now" dialog and `IsIisInstalled` are **removed**. Setup never changes
  Windows features.

**WebNetworkPage**: the captions are unchanged. It is preselected from the previous exposure
(data-model §3).

**WebPortsPage**. `OnActivate` starts with `if WizardSilent then Exit;`.

| Host | Fields | Validation on Next |
| --- | --- | --- |
| Service | `Web edition port -- 1024 to 65535. The web edition and its engine share this address:` | Range check. Then a **blocking** in-use check (connect probe):<br>• exempt only when `BridgePort` = p **and** `AkmlSqlWebEngine` is Running or StartPending;<br>• if the previous host is IIS, `IisPort` = p and the `AkmlSqlWeb` site exists: "Port {p} belongs to the AKML SQL site in IIS, which this change removes. Choose a different port for the standalone service.";<br>• any other listener: "Port {p} is already used by another program. Choose another port." |
| None | `Engine port -- 1024 to 65535:` | range; the in-use check is a warning (today) |
| IIS | today's three fields | today's checks |

**Ready memo** (`UpdateReadyMemo`, new). It returns the whole memo: `MemoUserInfoInfo`,
`MemoDirInfo`, `MemoTypeInfo`, `MemoComponentsInfo`, `MemoGroupInfo` and `MemoTasksInfo`, each
non-empty one followed by `NewLine + NewLine`. When `IsWebSelected`, it then appends:

```
Web edition:
      Standalone Windows service at http://localhost:47291/
      The web edition will move from http://localhost:8083/ to http://localhost:47291/. Connections, tabs and other data saved in the browser at http://localhost:8083/ will not appear at http://localhost:47291/. Query history is kept by the engine. To keep your settings, use Settings › Export… first.
```

The second line appears only when the address changes. The host line for None reads `Files only,
not hosted`.

## 3. Service

| Step | Today | After 045 |
| --- | --- | --- |
| Create | `sc create … start= auto` (`Components: web\service`) | the same command, `Check: IsWebSelected` |
| Start type (each run, when the service exists) | `delayed-auto` | Service: `sc config start= auto` (`Check: WebHostIsService`), then `DelayedAutostart = 0` in `Web_PostInstall`. IIS and None: `delayed-auto` (`Check: not WebHostIsService`). |
| Failure actions, `failureflag` | each run | unchanged |
| Start | `Web_PostInstall` (`web\service`) | `Web_PostInstall`, every host |

## 4. Order of work

**`PrepareToInstall`**. These steps come first, before SSMS is closed and before the service is
stopped:
1. When the web edition is ticked and `{app}` is not under Program Files, check and harden the
   install tree (data-model §12). Refuse a reparse-point folder, or a non-empty folder with a
   foreign owner, and return an error if the hardening fails.
2. When the web edition is ticked, lock down the state folder with its owner set (data-model
   §12). A failure becomes a summary warning.
3. Detect the old uninstall log and set it aside (§9).
4. Then run today's steps: close SSMS and devenv, then `TerminateAkmlBackgroundProcesses`.

**Non-postinstall `[Run]`**:
- `web-tls-setup.ps1` (LAN mode);
- `web-iis-setup.ps1` (`Check: WebHostIsIis`);
- `sc create`;
- the start type;
- the failure actions.

**`Web_PostInstall`**:
1. Write the registry values (data-model §3) and the hosting log line (data-model §4).
2. If the previous host was IIS and the new one is not, run `web-iis-remove.ps1` (§6). If
   `iis-remove.failed` exists afterwards, add a summary warning.
3. Run `web-config-bridge.ps1 -Port {enginePort} -Mode {Localhost|Lan} -WebRoot {app}\Web | ""`.
4. Run `web-firewall.ps1 -Apply -WebHost {h} -Exposure {e} -EnginePort {p} -HttpsPort {q}`
   (data-model §5).
5. Re-apply the state-folder ACL (idempotent).
6. For Service, set `DelayedAutostart = 0`.
7. Run `sc start AkmlSqlWebEngine`.
8. Poll for the LAN PIN and read the thumbprint.
9. Run the service-Running check (every host), then the answer check (§5).
10. For None, delete `{group}\Repair AKML SQL Web hosting.lnk`.
11. Write the summary file and the wizard page (data-model §10).

## 5. `web-hosting-check.ps1`

```
web-hosting-check.ps1 -Url <address> [-ExpectEngine | -ExpectBundle] [-TimeoutSeconds 30] [-ReasonPath <file>]
```

- **Requests**: GET once a second until the timeout. TLS validation is off for this script's
  own requests only, with a callback set and then reset.
- **Exit codes**:

  | Code | Meaning |
  | --- | --- |
  | 0 | 200 and `text/html`. With `-ExpectEngine`, also `X-AKML-Host: engine`. With `-ExpectBundle`, the body also contains `_framework/blazor.webassembly.js`. |
  | 2 | answered, but not serving the pages: the plain page, IIS's welcome page, or another program |
  | 1 | no answer before the timeout |

- **On failure**, the script names the program registered on the port when `netsh http show
  servicestate view=requestq` lists one. It parses URLs and image paths only, never the
  localized labels. The reason goes to `-ReasonPath`.

| Host / exposure | Probed |
| --- | --- |
| Service / Localhost | `http://127.0.0.1:{port}/ -ExpectEngine` |
| Service / Lan | `https://localhost:{port}/ -ExpectEngine` |
| IIS | `http://localhost:{IisPort}/ -ExpectBundle` |
| None | not run (the service-Running check covers it) |

## 6. `web-iis-remove.ps1`

```
web-iis-remove.ps1 [-AppDir <path>]   # logs to %ProgramData%\AKML SQL Web\install.log; always exits 0
```

The pure function `Get-IisRemovalPlan` decides the steps. Its inputs are:
- whether WebAdministration loads, and whether `appcmd.exe` exists;
- whether the site is listed in `applicationHost.config`;
- the app pool's other users;
- the owned-binding record.

The steps are:

1. **Remove the site.** Use WebAdministration when it loads. Otherwise use `appcmd delete site
   "AkmlSqlWeb"` when `appcmd.exe` exists.
2. **Remove the app pool** `AkmlSqlWeb`, the same two ways, but only when no application uses
   it.
3. **`Remove-OwnedHttpsBinding`**, from `web-https-binding.ps1`. It uses netsh only, so it
   always runs.
4. **Delete `{AppDir}\Web\web.config`**, but only when `applicationHost.config` no longer lists
   the site.
5. **Delete `iis-site.ok`, `https-site.ok` and `https-site.failed`.**

If the site is still listed afterwards, the script writes `iis-remove.failed` with the site name
and the manual command. It never touches other sites or IIS settings.

## 7. `web-hosting-repair.ps1` (the "Repair AKML SQL Web hosting" shortcut)

- **Elevation**: it self-elevates.
- **Location**: `{app}` comes from the uninstall key's `InstallLocation`, not from
  `$PSScriptRoot`.
- **Refusal**: both before and after elevating, it refuses to run when either holds:
  - `$PSScriptRoot` is not `{app}\Support`;
  - `{app}` has a write ACE for a non-admin SID.

  For the second check:
  - **A write ACE** is an Allow ACE that grants WriteData, AppendData, WriteAttributes,
    WriteExtendedAttributes, Delete, DeleteSubdirectoriesAndFiles, ChangePermissions or
    TakeOwnership.
  - **The allowed holders** are Administrators, SYSTEM and TrustedInstaller.
  - **Ignored**: inherit-only ACEs (for example CREATOR OWNER (IO)) and read or execute ACEs
    (for example Users RX and ALL APPLICATION PACKAGES RX). A default Program Files install
    passes.
- **Registry**: it reads `Host`, `Exposure`, `BridgePort`, `IisPort` and `HttpsPort`.
- **IIS**:
  1. LAN mode: run `web-tls-setup.ps1`.
  2. Run the `web-iis-repair.ps1` flow.
  3. Run `web-firewall.ps1 -Apply`.
  4. Apply `delayed-auto` and the failure actions.
  5. Start the service.
  6. Run `web-hosting-check.ps1 -ExpectBundle`.
- **Service**:
  1. If the service is missing, create it (the same binPath as setup).
  2. Apply `start= auto`, `DelayedAutostart = 0`, the failure actions and `failureflag`.
  3. Lock down the state folder, owner included.
  4. Run `web-config-bridge.ps1 -WebRoot {app}\Web` with the persisted port and exposure.
  5. LAN mode: run `web-tls-setup.ps1`. Then, in either mode, run `web-firewall.ps1 -Apply`.
  6. If the service is running, `Restart-Service -Force` and wait for Running; otherwise start
     it.
  7. Run `web-hosting-check.ps1 -ExpectEngine`.
  8. LAN mode: print the PIN, read after the restart.
  9. Print the result and wait for Enter.
- **None or missing**: "Nothing to repair: the web edition is not hosted on this computer."

## 8. `Web_Uninstall` (at `usUninstall`, before any `[UninstallRun]`)

1. `sc stop AkmlSqlWebEngine`, wait up to 10 s for Stopped, then `sc delete AkmlSqlWebEngine`.
2. Delete both firewall rules with direct `Exec` calls:
   `netsh advfirewall firewall delete rule name="AKML SQL Web Engine"` and
   `name="AKML SQL Web Site (HTTPS)"`.
3. Remove the bridge sslcert binding, and run `web-https-binding.ps1 -RemoveOwned` (both
   unchanged).
4. Run `web-iis-remove.ps1` when it is present; otherwise today's inline `Remove-WebSite`.
5. Delete the AKML certificate from `Cert:\LocalMachine\My`, found by the thumbprint in
   `certs\thumbprint.txt` or by `CN=AKML SQL Web Engine`. Delete it only while no `netsh http
   show sslcert` binding still uses its hash.
6. Ask about the data:
   `if UninstallSilent then Log('keeping AKML SQL Web data') else if MsgBox('Delete AKML SQL Web
   data at …?', mbConfirmation, MB_YESNO) = IDYES then DelTree(…)`.
7. Delete `Software\AKML SQL\Web` in both registry views.

The `usPostUninstall` desktop-settings prompt in `AkmlSqlSetup.iss` follows the same
`UninstallSilent` pattern (FR-071 as amended).

## 9. Fresh uninstall log (R23)

1. In `PrepareToInstall`, take the current log name from the uninstall key's
   `UninstallString` (`unins???.exe` → `unins???.dat`).
2. When that log contains the UTF-16 bytes of `\is-` followed by `.tmp\`, rename
   `unins???.dat` and `unins???.exe` aside.
3. `DeinitializeSetup` restores the renamed files if the install failed, and deletes them if it
   succeeded.

The new uninstall log records every file this install copies. The `[UninstallDelete]` folders
cover the rest.

## 10. Silent installs

```
/VERYSILENT /ACCEPTEULA [/WEB_HOST=SERVICE|IIS|NONE] [/WEB_EXPOSURE=LOCALHOST|LAN]
            [/BRIDGE_PORT=<p>] [/WEB_PORT=<p>] [/WEB_HTTPS_PORT=<p>] [/DIR=<path>]   # /WEB_PORT, /WEB_HTTPS_PORT: IIS only
```

- **Resolution**: data-model §4. The readiness trigger is data-model §2.
- **Standalone port check**: it runs in `InitializeSetup` with the §2 exemptions. When another
  program holds the port, setup stops with "Port {p} is already used by another program." in
  the log.
- **`/DIR=`**: gets the §4 `PrepareToInstall` checks.
- **Smoke fixtures** (`StandaloneSmokeFixture`):
  - `Standalone` and `LanStandalone` pass `/WEB_HOST=SERVICE`;
  - `IisToStandaloneSwitch` passes `/WEB_HOST=IIS` and then `/WEB_HOST=SERVICE`, on IIS-ready
    VMs only.

## 11. Finish page

- `IsWebSiteHosted` = `WebHostHasHost` (Service or IIS).
- `WebSiteUrl` = `HostingAddress(WebHost, Exposure, EnginePort, IisPort)` (data-model §11).
