# Data model: Web edition hosting without IIS

**Branch**: `045-web-standalone-hosting` | **Date**: 2026-10-26 | **Spec**: [spec.md](./spec.md)

Phase 1 output, revised after the plan review. Decision ids (R…) refer to
[research.md](./research.md). The contracts treat §2–§5, §9 and §11 as binding.

## 1. Hosting choice (`WebHost` global; registry `Host`)

| Value | Meaning | Engine service | Engine serves the pages | IIS site |
| --- | --- | --- | --- | --- |
| `Service` | Standalone Windows service (default) | installed, `start= auto`, `DelayedAutostart = 0` | yes (`bridge.webRoot = {app}\Web`) | removed if one existed |
| `IIS` | IIS hosts the pages; the service runs the engine | installed, `delayed-auto` | no (`webRoot = ""`) | created or kept |
| `None` | Files only | installed, `delayed-auto` | no | removed if one existed |

- **Who writes `WebHost`**:
  - the hosting page (in `Web_NextButton`);
  - the silent resolution (§4, in `InitializeSetup`);
  - when the web edition is unticked, `InferPreviousHost` (§3).
- **Who reads it**: every consumer reads `WebHost` and never `WebHostPage.SelectedValueIndex`.
- **Finish link and repair shortcut**: only Service and IIS get them. For None an existing
  shortcut is deleted.
- **IIS** can be chosen only when readiness is `Ready` (§2).

## 2. IIS readiness

The pure function `IisReadinessFrom` (`web-hosting.iss`) decides from these signals:

| Signal | Source |
| --- | --- |
| `InetStpPresent`, `MajorVersion` | `HKLM64\SOFTWARE\Microsoft\InetStp` |
| `W3svc`, `StaticContent`, `DefaultDocument`, `HttpErrors` | `InetStp\Components\<name>` (DWORD 1 = installed) |
| `W3svcStart`, `WasStart` | `HKLM64\SYSTEM\CurrentControlSet\Services\{W3SVC,WAS}\Start` (4 = disabled) |
| `Probe` | `ok` / `failed` / `skipped`: exit code of the 64-bit `Import-Module WebAdministration; Get-ChildItem IIS:\Sites` probe |

| State (first match wins) | Condition | Reason shown under the disabled item |
| --- | --- | --- |
| `NotEnabled` | not `InetStpPresent`, or `MajorVersion < 7`, or `W3svc <> 1` | "IIS is not enabled on this computer. To use it, turn on Internet Information Services in Windows Features, then go Back and Next." |
| `PartMissing` | `StaticContent`, `DefaultDocument` or `HttpErrors` `<> 1` | "IIS is missing {Static Content \| Default Document \| HTTP Errors}. Turn it on under Internet Information Services › World Wide Web Services › Common HTTP Features in Windows Features, then go Back and Next." |
| `Disabled` | `W3svcStart = 4` or `WasStart = 4` | "The World Wide Web Publishing Service (or the Windows Process Activation Service it needs) is disabled. Set it to Manual or Automatic in Services, then go Back and Next." |
| `ToolsUnavailable` | `Probe = failed` | "Setup cannot use IIS's management tools. Turn on IIS Management Tools (under Internet Information Services) in Windows Features, then go Back and Next." (Spike S4 replaces this with the exact feature.) |
| `Ready` | otherwise | — |

**When readiness is evaluated**:
- **Interactive**: in `WebHostPage.OnActivate`, once per show. The probe runs only when the
  registry checks pass.
- **Silent** (`InitializeSetup`, before §4):
  - The candidate host is the `/WEB_HOST` value if one was given, otherwise `InferPreviousHost`.
  - The registry checks run for **every** silent web install.
  - The probe runs only when the candidate is IIS; otherwise `Probe = skipped`.

**Log line** (Inno setup log): `IIS readiness: {State} (InetStp={…}, W3SVC={…}, Parts={…},
W3svcStart={…}, WasStart={…}, Probe={ok|failed|skipped})`.

## 3. Web hosting settings (machine registry) and the previous state

`HKLM64\Software\AKML SQL\Web`. Reads fall back to `HKLM32`, as today.

| Value | Type | Written | Read by |
| --- | --- | --- | --- |
| `Host` | string `Service`/`IIS`/`None` | **new**, every web install | `Web_Init`, the silent resolution, `web-hosting-repair.ps1`, `web-iis-repair.ps1` (guard), `Web_Uninstall` |
| `Exposure` | string `Localhost`/`Lan` | **new**, every web install | `Web_Init`, the silent resolution, repair |
| `BridgePort` | DWORD | every web install (unchanged) | ports page, the own-port exemption, uninstall, repair |
| `WebRoot` | string | **now every web install** | `web-iis-repair.ps1`, `web-hosting-repair.ps1` |
| `IisPort`, `HttpsPort` | DWORD | IIS only (unchanged) | `Web_Init`, the ports page (IIS-port block), `web-iis-repair.ps1` |
| `IisMode` | string | IIS only (unchanged) | `web-iis-repair.ps1`; the previous-exposure fallback |

**Previous state** (`InferPreviousHost`, FR-030 and FR-030a):

| `Host` present | Other signals | Previous host |
| --- | --- | --- |
| yes | — | `Host` |
| no | no `BridgePort` and no `AkmlSqlWebEngine` service | none (new install) |
| no | `applicationHost.config` (read as a file) holds `<site name="AkmlSqlWeb"` | `IIS` |
| no | no such site | `Service` (no choice recorded; SC-012) |

**Previous exposure**:
- If `Exposure` is present, use it.
- Otherwise it is `Lan` when **either** holds, and `Localhost` if neither does:
  - `ReadConfigString(config.json, 'bindAddress')` is `0.0.0.0` or `::`. The reader tolerates
    whitespace and a BOM (I27).
  - the registry `IisMode = Lan`.

## 4. Silent resolution

`ResolveSilentHost(FlagHost, FlagExposure, PreviousHost, IisReady, IisOnlyPortsGiven, var
LogLine)` returns `Service`, `IIS`, `None` or `Stop(reason)`. The rows are checked in order.

| # | `/WEB_HOST` | `/WEB_EXPOSURE` | Previous host | IIS ready | IIS-only ports given | Result |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | a value other than SERVICE/IIS/NONE | any | any | — | any | Stop: "Unknown /WEB_HOST value: {v}" |
| 2 | `NONE` | `LAN` (given) | any | — | any | Stop: "/WEB_HOST=NONE with /WEB_EXPOSURE=LAN is invalid" |
| 3 | `IIS` | any | any | no | any | Stop: the readiness reason |
| 4 | `IIS` | any | any | yes | any | IIS |
| 5 | `SERVICE` or `NONE` | any | any | — | yes | Stop: "/WEB_PORT and /WEB_HTTPS_PORT apply to IIS hosting only" |
| 6 | `SERVICE` | any | any | — | no | Service |
| 7 | `NONE` | not LAN | any | — | no | None |
| 8 | (none) | any | `IIS` | yes | any | IIS |
| 9 | (none) | any | `IIS` | no | any (ignored, logged) | Service; log "IIS is no longer ready ({reason}); the web edition moves to the standalone service at {address}" |
| 10 | (none) | any | none, `Service` or `None` | — | yes | Stop (as row 5) |
| 11 | (none) | any | none | — | no | Service |
| 12 | (none) | any | `Service` / `None` | — | no | the previous host, **including a persisted None + Lan** |

`IisReady` is evaluated only for rows 3, 4, 8 and 9 (§2). When the exposure and port flags are
absent, the values come from §3, falling back to Localhost, 47291, 80 and 443. The None + LAN
stop (row 2) looks at the command line only.

**Hosting log line** (every web install, interactive or silent): `Web hosting: {Service|IIS|None}
(from {page|/WEB_HOST|previous host|IIS fallback: <reason>})`.

## 5. Firewall plan (`Get-FirewallPlan` in `web-firewall.ps1`)

This is the only implementation. Setup and repair both call `web-firewall.ps1 -Apply …`. Rules
are netsh rules keyed by DisplayName (I9).

| Host / exposure | `AKML SQL Web Engine` (engine port) | `AKML SQL Web Site (HTTPS)` (IIS secure port) |
| --- | --- | --- |
| Service / Lan | add | remove |
| IIS / Lan | add | add |
| None / Lan | add | remove |
| any / Localhost | remove | remove |
| uninstall (direct netsh in `Web_Uninstall`) | remove | remove |

## 6. Engine configuration (`%ProgramData%\AKML SQL Web\config.json`)

`bridge` gains one key. The whole object is still rewritten by `web-config-bridge.ps1`
(`Get-BridgeConfigJson`):

```json
{
  "configVersion": 1,
  "bridge": {
    "enabled": true,
    "bindAddress": "127.0.0.1",
    "port": 47291,
    "tlsCertPath": "",
    "tlsCertPasswordRef": null,
    "tokenStorePath": "C:\\ProgramData\\AKML SQL Web\\tokens.json",
    "tokenTtlDays": 90,
    "webRoot": "C:\\Program Files\\AKML SQL\\Web"
  }
}
```

- **`webRoot`**: string, default `""`. It is honoured only by the web-mode engine
  (`RunWebAsync`).
- **Valid** means:
  - the path is rooted, the folder exists and holds `index.html`;
  - it is not a volume root and not a reparse point;
  - it does not equal, contain or sit inside the folder of the config file or of
    `tokenStorePath`.
- **Invalid** produces one Warning, and the engine falls back to the plain page with no exit
  (R8).
- **Where the property lives**: `BridgeOptions.WebRoot` is in `AkmlSql.Core` (netstandard2.0
  and net10.0); `WebSocketTransportOptions.WebRoot` is in the engine.

## 7. Static responder

**Manifest entry**, built once when the transport starts (R3):

| Field | Meaning |
| --- | --- |
| `Key` | relative path with `/`, OrdinalIgnoreCase (e.g. `_framework/dotnet.js`) |
| `FullPath` | canonical absolute path under the web root |
| `Length`, `LastWriteUtc` | from the walk |
| `LastModified` | `LastWriteUtc` truncated to whole seconds. It is sent as `Last-Modified` and compared with `If-Modified-Since`. |
| `ContentType` | from the extension table (R4) |
| `ETag` | `"{Length:x}-{LastWriteUtc.Ticks:x}"` |

What the walk excludes: extensions not in the table, `*.config`, `*.br`, `*.gz`, Hidden,
System and ReparsePoint entries, and anything inside a reparse-point directory.

**Request outcome**. Each request has exactly one outcome, in this order:

| Outcome | When | Status | Body | Takes a slot |
| --- | --- | --- | --- | --- |
| `Upgrade` | `IsWebSocketRequest` (any path) | 101 / 403 (unchanged) | — | no |
| `BadUrl` | `Request.Url` is null | 400 | `Bad request.` | no |
| `PlainPage` | no responder; or the path is `/akmlsql` or `/akmlsql/` | 200 | the plain page | no |
| `MethodNotAllowed` | not GET/HEAD | 405, `Allow: GET, HEAD` | — | no |
| `BadHost` | localhost mode; the Host is not `localhost`/`127.0.0.1`/`[::1]` | 400 | `Bad host.` | no |
| `Rejected` | fails R3 steps 2–4 | 400 | `Bad request.` | no |
| `NotModified` | a manifest hit or the fallback, with `If-None-Match` = ETag (or, with no `If-None-Match`, `If-Modified-Since` ≥ `LastModified`) | 304 | — | no |
| `File` | a manifest hit | 200 | the file | yes |
| `Fallback` | a miss with no `.` in the last segment | 200 | `index.html` | yes |
| `NotFound` | a miss with a `.` in the last segment | 404 | `Not found.` | no |
| `Busy` | no slot within 10 s | 503, `Retry-After: 5` | `Busy.` | — |

**Headers**:
- Every outcome except `Upgrade` and `PlainPage` carries `X-AKML-Host: engine`, the CSP,
  `nosniff` and `Cache-Control: no-cache`. Error and empty bodies are `text/plain; charset=utf-8`.
- `PlainPage` keeps today's headers. It adds `X-AKML-Host: engine` only when the responder is
  configured.
- The length is always set with `ContentLength64`.

**Limits**:
- 32 slots, each taken with a 10 s wait.
- The response deadline is 60 s plus `Length / 64 KiB` per second; past it, the response is
  aborted.
- A slot is released in `finally`, and a catch-all aborts the response.

## 8. Page engine (web app)

**`PageEngine.GetAsync()`** returns one of:
- `Engine(PageEngineInfo)`: the `HEAD /` response had `X-AKML-Host: engine`. Cached.
- `Static`: a response without the header, or 3 failures, or 60 s since the first failure.
  Cached.
- `Unknown`: an exception or timeout. Not cached.

**`PageEngineInfo`**:

| Field | From | Example |
| --- | --- | --- |
| `Host` | `pageBase.Host`, IPv6 brackets stripped | `localhost`, `sql-box` |
| `Port` | `pageBase.Port` (the scheme default when implicit) | `47291`, `59417` |
| `Scheme` | `http` → `ws`, `https` → `wss` | `wss` |

**`IPageEngine.PairingPrefill`** is set when auto-connect got `PinRequired` from the page's
engine; the connection picker reads it.

**Connection created automatically** (an `EngineConnection` record, stored only after `Ok`):

| Field | Value |
| --- | --- |
| `Name` | "This computer" for a loopback host; otherwise the host |
| `Host`, `Port` | from `PageEngineInfo` |
| `IsLocalhost` | `EngineEndpoint.IsLoopbackHost(Host)` |
| `ResolvedScheme` | `PageEngineInfo.Scheme` |
| `StrictScheme` | **new field**, default false. True when the page is https; `CandidateUrls` then returns only `wss`. |
| active | yes |

**Auto-connect when nothing usable is active**. This is `TryOnceAsync`. "Nothing usable" means
no active id, or an active id whose record is missing; a dangling id is cleared first.

```
nothing usable is active
 ├─ page Static ─────────────────────────▶ NotPaired "No engine paired yet."   (unchanged)
 ├─ page Unknown ────────────────────────▶ Unreachable "Checking this page's engine…" (backoff retries)
 └─ page Engine
     ├─ saved record with same host:port ─▶ set active ─▶ normal connect path
     └─ probe (no PIN, no bearer; strict scheme when https)
         ├─ Ok ───────────────────────────▶ store + set active ─▶ Connected
         ├─ PinRequired ──────────────────▶ PairingRequired "This engine needs its pairing PIN.
         │                                   Pair it in Settings › Connections." + PairingPrefill
         └─ socket failure ───────────────▶ Unreachable (existing backoff)
```

## 9. Switching hosts and addresses

| From → To | Setup actions (in addition to the normal install) | Warning (§11 addresses differ) |
| --- | --- | --- |
| IIS → Service | `web-iis-remove.ps1` (site, unused app pool, owned binding, `web.config` once the site is gone, markers; failure marker → summary warning); the firewall plan removes the HTTPS rule; `webRoot` set; `start= auto` + `DelayedAutostart = 0` | yes |
| IIS → None | `web-iis-remove.ps1`; firewall plan; repair shortcut deleted | no (no new address) |
| Service → IIS | `webRoot = ""`; `web-iis-setup.ps1`; `delayed-auto` | yes |
| Service → None | `webRoot = ""`; `delayed-auto`; repair shortcut deleted | no |
| None → Service / IIS | as a new install of that host | no (no old address) |
| same host, new port | the firewall rule moves (delete then add) | yes (FR-035) |
| Service, localhost ↔ LAN | firewall plan; TLS in LAN mode | yes (FR-035 as amended) |
| web edition unticked | nothing in the web part; `WebHost` = previous (keeps the start type) | — |

## 10. Install summary (`INSTALL-SUMMARY.txt` and the wizard page)

```
AKML SQL Web -- install summary
=================================

Hosting:     Standalone Windows service            (or: IIS | Not hosted)
URL:         http://localhost:47291/               (Service + Localhost)
             https://MYPC:47291/  (every computer, this one included)   (Service + LAN)
Bridge port: 47291 (the same address serves the pages)   (Service)
...PIN / thumbprint / bridge.cer trust steps -- recommended (LAN)...
...LAN: "The firewall rule applies to every network profile, including Public. Use a Private or Domain network."...
...warnings: address did not answer (reason, holder, repair) / service not running / ACL / IIS site not removed / address moved...
```

`URL:` stays the first address line, because `InstalledEngine.SiteUrl` in the E2E harness
parses it.

## 11. Hosting address (`HostingAddress`, pure, `web-hosting.iss`)

| Host / exposure | Address |
| --- | --- |
| Service / Localhost | `http://localhost:{enginePort}/` |
| Service / Lan | `https://{COMPUTERNAME}:{enginePort}/` |
| IIS / either | `http://localhost/` when `IisPort = 80`, otherwise `http://localhost:{IisPort}/` (LAN computers use the HTTPS site address, as today) |
| None | `""` (no address) |

## 12. ACLs that setup enforces (R22)

| Path | When | Owner | Grants (inheritance removed) |
| --- | --- | --- | --- |
| `{app}` (recursive) | the web edition is ticked and `{app}` is not under Program Files | Administrators | Administrators F, SYSTEM F, Users RX, IIS_IUSRS RX |
| `%ProgramData%\AKML SQL Web` (recursive) | every web install, in `PrepareToInstall`; re-applied in `Web_PostInstall` and repair | Administrators | Administrators F, SYSTEM F |

`PrepareToInstall` refuses an existing custom `{app}` that is a reparse point, or that is
non-empty and owned by a SID other than Administrators, SYSTEM or TrustedInstaller. The
ownership part depends on spike S7. If an older elevated setup left the installing user as
owner, the refusal narrows to reparse points, and ownership is taken and reset instead.
