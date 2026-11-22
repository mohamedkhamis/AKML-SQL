# Contract: engine discovery and pairing (WEB-001, ENG-001)

## 1. `akml-engine.json` (installer → page)

```json
{ "manifestVersion": 1, "bridgePort": 59417, "scheme": "ws" }
```

- **Written by** `src/AkmlSql.Installer/web-engine-manifest.ps1`
  `-WebRoot <path> -Port <int> -Scheme ws|wss [-Remove]`. It writes UTF-8 without BOM, atomically
  (temp + rename). `-Remove` deletes the file. Exit 0 on success, 1 on any failure, with the reason on stderr for
  install.log.
  - **Refused (exit 1):** a `-WebRoot` that is not an existing, non-reparse directory, or a port outside 1024–65535.
- **Callers:**

  | Caller | When | Arguments |
  | --- | --- | --- |
  | `Web_PostInstall` (web-installer.iss) | `WebHost` is `IIS` or `None` | `-WebRoot {app}\Web -Port WebBridgePort -Scheme (ws for Localhost, wss for Lan)` |
  | `Web_PostInstall` | `WebHost` is `Service` | `-Remove` (a host switch leaves no stale file) |
  | `web-hosting-repair.ps1`, `web-iis-repair.ps1` | after a port or exposure change | the current values |

- **Served** as a static file by the page's host. The IIS site must serve `.json` (verify the bundle's `web.config`
  mime map; add `<mimeMap fileExtension=".json" mimeType="application/json" />` only if missing).

## 2. Page engine resolution (`PageEngine.GetAsync`)

1. `HEAD /` answers `X-AKML-Host: engine` → `Engine`, Source `ServedPage`, at the page's own origin. This is today's
   behaviour.
2. Otherwise `readManifest(new URL('/akml-engine.json', page))` (`js/akml-page-engine.js`, fetch `GET`, no-cache,
   5 s timeout, parsed JSON or `null`):
   - a valid manifest gives `Engine`, Source `InstallManifest`, `PageEngineInfo(pageHost, bridgePort, scheme)`;
   - an invalid, missing or unreachable one gives `Static`.
3. The existing retry and 60 s rules for an unreachable page still apply to step 1.

Consequences, which the code needs no change for:

- `IEngineAutoConnect.TryOnceAsync` auto-pairs a loopback page engine with no PIN;
- a LAN page engine answers `PairingRequired` with the prefill set;
- `StrictScheme` follows `scheme == "wss"`.

## 3. One-click failure UI (`StatusBar.razor`)

`UseLocalEngineAsync` awaits a `LocalPairResult` instead of a bool and no longer navigates on failure. The status bar
shows an inline notice (`data-testid="local-engine-failure"`):

| Failure | Text | Actions |
| --- | --- | --- |
| `NotReachable` | "No engine answered on {address}. This install may use another port." | **Add engine…** (`local-engine-add`), Dismiss (`local-engine-dismiss`) |
| `PinRequired` | "This engine needs its pairing PIN." | **Pair…** (`local-engine-pair`; opens Settings › Connections with the prefill, as today), Dismiss |
| `Rejected` | "The engine at {address} refused the connection. Check engine pairing." | **Settings…** (`local-engine-settings`), Dismiss |

**Add engine…** opens `settings/connections` with the Add form pre-filled: host = the page host, port = the tried
port. The button's tooltip names the address it will try: page engine `{host}:{port}`, else `localhost:47291`.

## 4. Browser label (handshake key 5)

The pure helper `browserLabel(nav, origin)` in `js/akml-bridge.js`, unit-tested in node, returns:

```
"{family} on {os} · {origin}"     e.g. "Edge on Windows · http://localhost:59416"
```

- **family:** Edge | Chrome | Firefox | Safari | Opera | "Browser"; **os:** Windows | macOS | Linux | Android | iOS |
  "unknown OS".
- **Length:** at most 80 characters, cut at a character boundary with "…".
- **Sources:** `navigator.userAgentData.brands` and `platform` when present, else the user-agent string.
- **Where it is used:** `IEngineBridge` sends it instead of `"Web edition"`. The engine stores it as minted and never
  trusts it for anything but display.

## 5. IPC: pairing list and revoke (capability `pairing.manage.v1`)

| Type | Number | Direction | Payload |
| --- | --- | --- | --- |
| `PairingListRequest` | 227 | Web → Engine | (empty) |
| `PairingListResponse` | 228 | Engine → Web | `PairingUsed`, `Entries[]` (data-model §3) |
| `PairingRevokeRequest` | 229 | Web → Engine | `Id` |
| `PairingRevokeResponse` | 230 | Engine → Web | `Ok`, `Message?`, `ClosedConnections` |

**Rules:**

- **Bridge only.** Both requests are served only on bridge connections; a named-pipe (SSMS) request gets an error
  reply.
- **Authentication.** Both require an authenticated bridge connection: one that passed the handshake, by PIN or
  bearer, or loopback where pairing is not required. Any such connection may list and revoke any entry, because a
  paired browser already holds the engine account's full power. The engine logs each revoke at Information as
  `Pairing revoked: {label} (by {caller label})`, without hashes or tokens.
- **Revoke behaviour.** It removes the entry (`BearerTokenStore.RevokeByHash`) and closes every open bridge socket
  whose `BridgeConnection.TokenHash` matches it, with WebSocket close status 1008 and reason "pairing revoked". It
  then replies. An unknown `Id` gives `Ok = false`, Message "That pairing no longer exists."
- **No pairing in use.** An engine where pairing is not required replies `PairingUsed = false` with an empty list,
  and Revoke `Ok = false`.
- **Documentation.** The messages are documented in `doc/ipc-api.md` with their numbers and the capability.

## 6. Web Settings › Connections: "Paired browsers"

- **When it shows:** for the active engine row, when the engine advertises `pairing.manage.v1` and `PairingUsed` is
  true (`data-testid="paired-browsers"`).
- **Each row** (`paired-browser-{id8}`, where `id8` is the first 8 hex characters of `Id`) shows Label, "This
  browser" (when `IsCurrent`), "Paired {MintedAt:d}", "Last used {LastUsedAt:g}" and "Expired" (when past
  `ExpiresAt`), plus **Revoke…** (`paired-browser-revoke-{id8}`).
- **Confirmation dialog:**
  - title "Revoke pairing"; message "{Label} will need the engine's PIN to connect again.";
  - when `IsCurrent`, the message is "This browser will be disconnected and will need the engine's PIN to connect
    again.";
  - confirm label "Revoke", destructive.
- **After a revoke** the list is refreshed. Revoking the current pairing leaves the engine record in place, in
  "needs pairing" state, because the next connect asks for the PIN.
