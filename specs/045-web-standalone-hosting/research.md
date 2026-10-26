# Research: Web edition hosting without IIS — a standalone Windows service by default

**Branch**: `045-web-standalone-hosting` | **Date**: 2026-10-26 | **Spec**: [spec.md](./spec.md)

Phase 0 output, revised after an adversarial review of the plan by four reviewers (requirement
coverage, consistency, technical correctness, security), each with a verifier that tried to
refute the findings. Each decision names the alternatives it rejected. Codebase facts are cited
by their id in [codebase-map.md](./codebase-map.md) (E = engine, W = web app, I = installer,
P = platform). Items marked **SPIKE** depend on claims that the code reading could not settle;
§ Spikes says what proves each one.

## R1. The pages are served by the engine's existing bridge listener, on the engine port

**Decision**: Add a static responder to `WebSocketTransport`. In standalone mode, the
`HttpListener` that carries the bridge also serves the web edition's files from `{app}\Web`:
- localhost mode: `http://127.0.0.1:{port}/`, opened as `http://localhost:{port}/`;
- LAN mode: `https://+:{port}/`.

The responder is on only when the web-mode engine is given a web root (R8). Otherwise
everything behaves as today.

**Rationale**: The answer to Q1 was one address. The listener already registers the site root
on that port (E1). LAN mode already has the TLS binding and certificate for it (E17). The Origin
check already accepts same-origin pages (E6), and pairing happens inside the WebSocket
handshake, not in front of plain HTTP (E7). So pages and bridge share one port, one certificate
and one firewall rule, with no new HTTP.SYS registration and no URL ACL (the service is
LocalSystem, E15). This keeps the new capability in the engine (constitution I) and reuses the
existing listener (constitution V).

**Alternatives considered**:
- **Kestrel or ASP.NET Core in the engine.** It needs a framework reference the engine does not
  have (E20) and a second HTTP stack on the same port, and it grows the self-contained publish.
- **A second Windows service for the pages.** That means two services to start, repair and
  uninstall, plus either a second port, which contradicts Q1, or prefix sharing, which is
  fragile (I22).
- **Serving `..\Web` relative to the exe.** The SSMS-started engine also builds the bridge
  (E13), and the Debug exe used in tests has no sibling `Web` folder (W15).

## R2. Request routing, concurrency and limits

**Decision**: The accept loop keeps exactly one test inline: `IsWebSocketRequest`. If it is
true, the request takes today's upgrade path (any path, with the loopback-remote and Origin
checks unchanged). Every other request goes to `Task.Run`, so it never blocks the loop (E3),
and is handled in this order:

0. If `Request.Url` is null (an unparsable URL or Host), answer 400.
1. No responder configured (IIS or None hosting, or an SSMS-started engine): today's plain page,
   unchanged, on every path (FR-022, E4).
2. A method other than GET or HEAD gets 405 with `Allow: GET, HEAD`.
3. In localhost mode only, a Host that is not `localhost`, `127.0.0.1` or `[::1]` (any port)
   gets 400. This defends against DNS rebinding, because the IP-bound prefix does not check
   Host (E8). LAN mode is not restricted: its valid names are open-ended (SAN plus
   `-ExtraNames`, E17).
4. `/akmlsql` or `/akmlsql/` (case-insensitive) gets the plain page, which keeps the
   certificate-trust link working (E5).
5. The static lookup follows R3, R4 and R5.

**Limits**. Only responses that read a file take a slot: a file, or the `index.html`
fallback. Rows 0–4, 304, 400 and 404 do no file I/O and never wait.
- **Slots.** A `SemaphoreSlim(32)` bounds them. A slot is taken with
  `WaitAsync(TimeSpan.FromSeconds(10))`; on timeout the answer is `503` with `Retry-After: 5`.
- **Deadline.** Each file response has a deadline of 60 s plus `Length / 64 KiB` per second.
  When it passes, the response is aborted (`Response.Abort()`).
- **Release.** The slot is released in `finally`, and a catch-all aborts the response, so an
  exception can never leak a slot or leave a response open.
- `HttpListener.TimeoutManager` is not used, because it would also throttle upgraded WebSocket
  sends (E20).

A slow LAN reader can then hold a slot for a bounded time only, and queued requests fail fast
instead of piling up in memory.

**Alternatives considered**:
- **Routing upgrades by path (`/akmlsql` only).** It breaks the existing tests and clients that
  dial the root (E2), for no security gain.
- **A Host allowlist in LAN mode too.** It would reject legitimate NAT and DNS names.
- **A per-address concurrency cap.** Several browsers behind one NAT address would get 503 on a
  normal page load of more than 100 requests.

## R3. Path safety: a manifest of servable files, validation before any filesystem call

**Decision**: When the transport starts, the responder walks the web root once and builds a
**manifest**. Each entry holds:
- the relative path with `/` separators, as the key (OrdinalIgnoreCase);
- the full path, the length and the last-write time;
- the content type and the ETag.

The walk keeps only files whose extension is in the content-type table (R4). It leaves out any
name ending in `.config`, `.br` or `.gz`, any entry with the Hidden, System or ReparsePoint
attribute, and the contents of reparse-point directories. `.config` is excluded because IIS
writes `web.config` into this folder (I16). `.br` and `.gz` are excluded because precompressed
files are never negotiated and some are stale (I17).

A request is handled in this order, and **nothing touches the filesystem before step 6**:

1. Unescape `Request.Url.AbsolutePath` exactly once.
2. Reject (400) the path if it contains `\`, `:`, `%`, `~`, NUL or another control character
   (E10, P6). `%` is rejected because a valid name never has one after decoding, so its
   presence means double encoding.
3. If the path is exactly `/`, use the key `index.html`.
4. Otherwise strip the one leading `/`, then reject (400) the path when:
   - any segment is empty (`//`, or a trailing `/` such as `/js/` or `/_framework/`; a
     directory is never listed or served);
   - a segment is `.` or `..`;
   - a segment ends in `.` or a space.
5. Look the key up in the manifest. A miss goes to R5.
6. Only now touch the filesystem. `Path.GetFullPath(entry.FullPath)` must start with
   `GetFullPath(webRoot) + Path.DirectorySeparatorChar`, compared OrdinalIgnoreCase (the
   CLAUDE.md security rule). Then open the file with `FileShare.Read` and async sequential I/O.

**Validating the web root at start**. The responder refuses a web root when:
- it is a volume root;
- it has the ReparsePoint attribute;
- it equals, contains, or lies inside the folder of the config file or of `tokenStorePath`.

A refusal follows R8: one Warning, and the plain page is served.

**Rationale**: The service is LocalSystem (E15). Only enumerated bundle files can ever be
opened, so even a gate bug cannot reach other files. The PIN, tokens, certificates and config
live in `%ProgramData%\AKML SQL Web`, a separate tree, and the overlap refusal keeps them out
even if `webRoot` were mis-set. That containment relies on `config.json` being writable by
administrators only, which R22 enforces, including the folder's owner. Setup stops the service
before it copies files and starts it afterwards (I12, I15), so a manifest built once at start is
current.

**Alternatives considered**:
- **Look up the file on disk for every request.** A UNC-shaped path would then make an SMB
  connection as SYSTEM before the gate (E10).
- **Rely on HTTP.SYS canonicalization.** .NET 10 keeps `%5C`, `%3A`, trailing dots and drive
  letters (E10).
- **Strip a trailing `/` and fall back to `index.html`.** App routes never end in `/` (W9), so a
  400 is the simpler rule.

## R4. Response headers: IIS parity plus deliberate differences

**Decision**: File responses, the fallback, 304, 400, 404, 405 and 503 all carry:
- `Cache-Control: no-cache`, never `no-store`. The runtime fetches fingerprinted wasm with
  `cache: force-cache` and stable files with `no-cache` (W8); IIS sends `no-cache` (I18).
- `Content-Security-Policy`. It is byte-identical to the `$csp` that `web-iis-setup.ps1`
  builds, held in one engine constant, `StaticWebResponder.ContentSecurityPolicy`. A drift test
  pulls the `$bridgeConnectSrc` and `$csp` assignments out of the ps1 with the PowerShell AST
  (`[Parser]::ParseFile`), evaluates only those two statements, and compares.
- `X-Content-Type-Options: nosniff`;
- `X-AKML-Host: engine` (R6).

Files and the fallback also carry:
- the content type from the table below;
- the length, set **only with `Response.ContentLength64`**. Setting `Content-Length` as a header
  makes the first body write fail (E20).
- `ETag` = `"{length:x}-{lastWriteUtcTicks:x}"`;
- `Last-Modified` = the last write, **truncated to whole seconds**.

A request whose `If-None-Match` matches gets 304. Without `If-None-Match`, a request whose
`If-Modified-Since` is at or after that truncated time also gets 304. A deep link revalidates
against `index.html`'s ETag. A 304 has no body. HEAD gets the same headers with no body (E11).
Range requests get the full body with 200, and no `Accept-Ranges` header is sent.

Content types. Only these extensions are servable; the table grows when the bundle gains a type,
and a test fails if any bundle file's extension is missing.

| Extension | Content-Type |
| --- | --- |
| `.html` | `text/html; charset=utf-8` |
| `.js` | `text/javascript` |
| `.css` | `text/css` |
| `.wasm` | `application/wasm` |
| `.json`, `.map` | `application/json` |
| `.svg` | `image/svg+xml` |
| `.txt`, `.sql` | `text/plain; charset=utf-8` |
| `.pdb` | `application/octet-stream` (Debug bundles, E2E only) |

**Deliberate differences from a clean IIS install** (also listed in the contract, §7).
SC-006 compares the behaviours FR-012 names, not every header.
1. `nosniff` is sent; IIS sends none (I18).
2. `.js` is `text/javascript` (RFC 9239), where IIS sends `application/javascript` (P1).
3. `.sql` is served. IIS gains the same mapping in `web-iis-setup.ps1` `$mimes` (one line;
   FR-021 as amended), because today the `/spike` page silently formats HTML (W11).
4. No `Accept-Ranges` header, and ranges get a full 200, where IIS honours them. The runtime
   never sends range requests.
5. Text types carry `; charset=utf-8`. Confirm against the clean IIS install in quickstart 3.11.
6. The deep-link status (R5).

**Alternatives considered**:
- **`no-store`.** It defeats `force-cache`.
- **`immutable`.** It is not IIS behaviour, and ETag already gives the revalidation.
- **Serving `.br`.** IIS does not compress either (I18), and the folder holds stale `.br` files
  (I17).

## R5. Deep links: `index.html` with 200; a missing file with an extension gets 404

**Decision**: A GET or HEAD that passes R3 and misses the manifest is answered by its last path
segment, with the query string ignored (`/editor?autorun=1`, W9):
- **no `.`** (for example `/editor`, `/settings/connections`): `index.html` with status 200 and
  `index.html`'s headers;
- **a `.`**: 404 with a short `text/plain` body and the same CSP. A missing `.js` or `.wasm`
  therefore never returns HTML, and the runtime's integrity error stays clear (W8).

Directory forms ending in `/` are 400 (R3).

**Rationale**: No app route contains a dot (W9). Serving a 404 main document would log a failed
load in the console on every reload (SC-006).

**Deliberate difference from a clean IIS install**: there, `httpErrors` returns `index.html`
with status 404 for every unknown path, with or without an extension. The workstation's IIS
returns 200 only because of a hand-added rewrite rule (I16).

## R6. How a page knows the engine served it: the `X-AKML-Host` response header

**Decision**: Every responder response carries `X-AKML-Host: engine`. At startup the web app
sends one `HEAD /` (no-cache) through the HttpClient that is already registered (W5). It treats
the page as **engine-hosted** when that header is present, and as **static** when a response
arrives without it. IIS and the Blazor dev server never send the header, and an engine without
a web root never serves the page.

The same header doubles as setup's check that the bundle really is being served (R16). The
plain-page fallback used when the web root is invalid (R8) carries no header.

**Known limit**: on a computer where several people sign in at once (for example a
Remote Desktop host) in localhost mode, another user can take the port while the service is
stopped (an upgrade, a repair, a crash-restart gap). That user can then serve a page at the same
origin, including the header. The documentation says so (doc/deployment.md, doc/m3-security.md,
doc/topics/browser-setup.md): on such hosts, use LAN/HTTPS mode with the certificate trusted, or
IIS. When the engine cannot bind its port, it logs a distinct message (R8), and the answer check
names the program holding the port (R16).

**Alternatives considered**:
- **A marker file shipped in the bundle and shadowed by an engine route.** It is fragile.
- **Content negotiation on `/akmlsql`.** It relies on IIS answering 200 with HTML.
- **A handshake capability.** It arrives too late (E18).
- **Comparing `location.port` with 47291.** Installs move the port (I14).

## R7. The default engine: the page's own address when no engine is active (FR-014)

**Decision**:
- **New `IPageEngine` service** (`Services/PageEngine.cs`).
  - A pure helper, `EngineEndpoint.FromPage(Uri pageBase)`, gives the host (IPv6 brackets
    stripped), the port (explicit or the scheme default) and the scheme (`http` → `ws`,
    `https` → `wss`).
  - `PageEngine.GetAsync()` returns one of three answers: **Engine(info)**, **Static** or
    **Unknown**.
  - Only definitive answers are cached for the session: a response with the header, or a
    response without it.
  - An exception or timeout returns **Unknown** and is not cached.
  - After 3 consecutive failures, or 60 s after the first, the answer becomes **Static** and is
    cached. A static page whose `HEAD` keeps failing therefore ends at NotPaired instead of
    retrying forever.
- **`EngineAutoConnect.TryOnceAsync`** takes an optional `IPageEngine`. It uses it **only**
  where it would otherwise return `NotPaired`.
  - **When**: no active connection, **or an active id whose record is missing**. In the second
    case the dangling id is cleared first (`SetActiveIdAsync(null)`), because today that is a
    second NotPaired branch (W1).
  - **Precedence**:
    1. An active saved connection always wins, even one to another engine that is down.
    2. Otherwise an engine-hosted page uses its own engine.
    3. Otherwise nothing is dialled, as today.
  - **Unknown**: return `Unreachable`, "Checking this page's engine…", so the existing backoff
    retries.
  - **Reuse**: a saved record with the page's host and port is made active.
  - **Probe**: otherwise build a record:

    | Field | Value |
    | --- | --- |
    | `Host`, `Port` | from the page |
    | `ResolvedScheme` | the page scheme |
    | `StrictScheme` | true when the page is https (new persisted field, default false) |
    | `IsLocalhost` | `IsLoopbackHost(host)` |
    | `Name` | "This computer" for a loopback host, otherwise the host |

    Dial it with no PIN and no bearer. `EngineEndpoint.CandidateUrls` gains a `strictScheme`
    argument that returns only the given scheme, and `EngineBridge` passes
    `connection.StrictScheme`. An https page's own engine is therefore never dialled with `ws`,
    even on loopback or on reconnect. Without that, the `ws` fallback that loopback hosts get
    would run after a failed `wss`, and the error shown would be the misleading one (W3).
  - **Outcomes**:

    | Outcome | Result |
    | --- | --- |
    | `Ok` | store the record and make it active |
    | `PinRequired` | `PairingRequired`: "This engine needs its pairing PIN. Pair it in Settings › Connections."; set `IPageEngine.PairingPrefill`; store nothing |
    | socket failure | `Unreachable` |

  - The PIN-less probe is safe on every load (E7). A user who removes every connection gets the
    page's engine back on the next load.
- **`PairLocalEngineAsync`** and the status-bar button use the page engine's address, with the
  title "This computer's engine ({host}:{port})".
- **`ConnectionPickerComponent`** starts its Add form from the page engine, and shows the PIN
  field at once when the prefill is set.
- **`SecureContextNotice`** is unchanged (W13).

**Alternatives considered**:
- **Changing only the `EngineEndpoint` defaults.** Nothing dials on first load (W1).
- **Dropping `ws` for every https page.** An https IIS page can legitimately reach a
  localhost-mode engine over `ws://localhost`.
- **Giving IIS-hosted pages a default engine.** It is not required.

## R8. Configuration: `bridge.webRoot`, honoured only by the web-mode engine

**Decision**:
- **Core**: `BridgeOptions` (in `AkmlSql.Core`) gains `[JsonPropertyName("webRoot")] public
  string WebRoot { get; set; } = string.Empty;`. It is netstandard2.0-safe, and empty means
  off.
- **Engine**: `WebSocketTransportOptions` gains `WebRoot` (`string?`).
  `EngineHost.BuildWebSocketTransport` gains `bool serveWebEdition = false`. Only `RunWebAsync`
  passes `true`, so existing callers and tests are unchanged (E13).
- **Validation at start** (R3): the path must be rooted, the folder must exist and hold
  `index.html`, and the root must not be refused. Otherwise the engine logs **one Warning**,
  `WebSocketTransport: not serving the web edition from {WebRoot}: {Reason}`, and runs with the
  plain page. It never exits with 2, which would start a 60-second restart loop (E14). The level
  is Warning because Error goes to the telemetry sink (E16).
- **Logging**:
  - When serving starts, one Information line: `WebSocketTransport: serving the web edition from
    {WebRoot} ({Count} files)`.
  - Files, fallbacks, 304s and 404s are never logged.
  - Refusals (rows 0, 2, 3 and validation) are logged at Warning:
    `WebSocketTransport: refused {Method} {Path} from {Remote} ({Reason})`. `{Path}` is cut to
    200 characters, which is safe on .NET 10 (P8). These lines are rate-limited **globally** to
    one a second, with a periodic `WebSocketTransport: {Count} refused requests not logged`
    line. There is no per-address state.
- **Bind failure**: when `HttpListener.Start` fails with 183 (`ERROR_ALREADY_EXISTS`)
  or 32 (`ERROR_SHARING_VIOLATION`), the transport throws with the message `port {p} is held by
  another process`, and `RunWebAsync` logs it as such.
- **Setup**: `web-config-bridge.ps1` is restructured (R21) and **always** writes `webRoot`:
  `{app}\Web` for the standalone host, an empty string otherwise. The script replaces the whole
  `bridge` object, so a re-run or a switch turns serving on or off by config alone (I13). It is
  installed to `{app}\Support`.

**Alternatives considered**:
- **A top-level `webHost` section.** It needs a second POCO and a second merge rule.
- **An exe-relative path.** It fails for the test exe (W15).

## R9. Setup's hosting model: one code-owned choice; the two sub-components go away

**Decision**:
- **Components**: remove `web\iis` and `web\service`, and keep `web` (**SPIKE S1**).
- **The `WebHost` global** (`Service` | `IIS` | `None`) is the only record of the choice. It
  is written by:
  - the hosting page, in `Web_NextButton`;
  - the silent resolution (R12), in `InitializeSetup`;
  - when the web edition is unticked (the hosting page is skipped), `InferPreviousHost`, so a
    previous standalone install keeps `start= auto` (R20). `Web_PostInstall` still exits early
    in that case.
- **Consumers read `WebHost`, never `WebHostPage.SelectedValueIndex`**: every `Check:`
  function (`WebHostIsIis`, `WebHostIsService`, `WebHostHasHost`), `IsWebSiteHosted`,
  `WebSiteUrl`, `UpdateReadyMemo`, `Web_PostInstall` and the summary.
- **Silent mode** (I28): Inno clicks through the pages in silent mode, so `WebHostPage.OnActivate`,
  the `CheckListBox.OnClickCheck` handler and `WebPortsPage.OnActivate` all start with
  `if WizardSilent then Exit;`.
- **The engine service** is installed and started for every host once the web edition is
  ticked. For None it runs the bridge only (FR-043 as amended).
- **The hosting page** has three radio items with named index constants, a shortened list and a
  word-wrapped notice label (I5). The page logic:
  - preselects the previous host the first time the page shows;
  - evaluates readiness in `OnActivate` (R10), sets `ItemEnabled[WebHostIisIdx]` and resets an
    IIS selection that is no longer ready to Standalone;
  - recomputes the notice in `OnClickCheck` (R14).
- **Delete** the dism "Enable IIS now" dialog and `IsIisInstalled` (FR-006).
- **Page logic** never compares page IDs with `>` (I3).

**Alternatives considered**:
- **Keeping the components and adding Checks.** That is two controls for one choice, which is
  the FR-003 defect.
- **Hidden components (`Check: False`).** Kept only as the S1 fallback.

## R10. IIS readiness

**Decision**: A pure Pascal function, `IisReadinessFrom` in the new `[Code]` include
`web-hosting.iss`, decides readiness from signals that thin readers collect (I8). The checks
run in this order, and the first failure wins:

| State | Condition | Reason under the disabled item |
| --- | --- | --- |
| `NotEnabled` | The `HKLM64\SOFTWARE\Microsoft\InetStp` key is missing, `MajorVersion` < 7, or `Components\W3SVC` ≠ 1 | "IIS is not enabled on this computer. To use it, turn on Internet Information Services in Windows Features, then go Back and Next." |
| `PartMissing` | `Components\StaticContent`, `DefaultDocument` or `HttpErrors` ≠ 1. These are the parts the AKML SQL site uses: static files, `index.html` at `/`, and deep links. | "IIS is missing {Static Content \| Default Document \| HTTP Errors}. Turn it on under Internet Information Services › World Wide Web Services › Common HTTP Features in Windows Features, then go Back and Next." |
| `Disabled` | `Services\W3SVC\Start` = 4 or `Services\WAS\Start` = 4 | "The World Wide Web Publishing Service (or the Windows Process Activation Service it needs) is disabled. Set it to Manual or Automatic in Services, then go Back and Next." |
| `ToolsUnavailable` | The functional probe fails (`{sys}\WindowsPowerShell\v1.0\powershell.exe -NoProfile -NonInteractive -Command "Import-Module WebAdministration -ErrorAction Stop; $null = Get-ChildItem IIS:\Sites -ErrorAction Stop"` exits non-zero) | "Setup cannot use IIS's management tools. Turn on IIS Management Tools (under Internet Information Services) in Windows Features, then go Back and Next." This text is generic until S4 names the exact feature. |
| `Ready` | Otherwise | — |

**When it runs**:
- **Interactive**: in `WebHostPage.OnActivate`, once per show. The registry checks always run;
  the probe (about 1 s warm, I8) runs only when they all pass.
- **Silent** (in `InitializeSetup`, before resolution):
  - The **candidate host** is the `/WEB_HOST` value if one was given, otherwise
    `InferPreviousHost`.
  - The registry checks run, and their result is logged, for **every** silent web install
    (FR-008), with `Probe=skipped` when the probe does not run.
  - The probe runs only when the candidate is IIS.
  - The result goes to `ResolveSilentHost` as `IisReady` (R12).
- **Log line**: `IIS readiness: {State} (InetStp={…}, W3SVC={…}, Parts={…}, W3svcStart={…},
  WasStart={…}, Probe={ok|failed|skipped})`.

**Rationale**:
- The Windows Features state disagrees with what works (I8). On the owner's workstation the
  scripting tools are off, yet provisioning works. Gating on that feature would move the owner
  off IIS (SC-007).
- The probe tests the exact capability `web-iis-setup.ps1` uses.
- The component values make FR-005's "a named part of IIS is missing" concrete.
- **SPIKE S4** confirms the values on a clean Windows 11 and on Windows Server, and records
  which feature turns `ToolsUnavailable` into `Ready`, so the reason can name it.

**Alternatives considered**:
- **`ManagementScriptingTools`, DISM or CIM.** All three are wrong on the owner's machine.
- **Checking only that the module file exists.** It can exist while the COM classes it needs are
  missing.

## R11. Persisting the choice, and inferring it for installs from before this feature

**Decision**: `Web_PostInstall` always writes `HKLM64\Software\AKML SQL\Web`:
- `Host` (`Service` | `IIS` | `None`);
- `Exposure` (`Localhost` | `Lan`);
- `BridgePort`;
- `WebRoot`.

`IisPort`, `HttpsPort` and `IisMode` are still written for IIS only.

When `Host` is absent but a previous web edition exists (`BridgePort` or the service is
present), `InferPreviousHost` (FR-030a) decides:
- **The host** is IIS when `%windir%\System32\inetsrv\config\applicationHost.config` (read as
  a file) holds `<site name="AkmlSqlWeb"`. This is the decisive signal (I7). Otherwise it is
  Service.
- **The exposure** is Lan when **either** of these holds; otherwise it is Localhost:
  - `bridge.bindAddress` in `%ProgramData%\AKML SQL Web\config.json` is `0.0.0.0` or `::`. The
    value is read with a whitespace-tolerant `ReadConfigString(Json, 'bindAddress', Value)` in
    `web-hosting.iss`, modelled on `ReadConfigBool` (I27): find `"bindAddress"`, skip
    whitespace and `:`, then read the quoted value. The file has a BOM and two spaces after each
    colon (I27), so a literal match would never hit.
  - the registry `IisMode` = `Lan`. It is written only when `web\iis` was ticked, and its value
    reflects the exposure chosen at the time.

**Rationale**: Without a recorded `Exposure`, every upgrade of a non-IIS LAN install, and the
owner's server, came back as localhost (I14), which breaks FR-030, FR-041 and SC-007.

**Tests**: the probe-ISS fixtures (R21) use **the real output of `web-config-bridge.ps1`** under
Windows PowerShell 5.1, not hand-written JSON. They include:
- a pre-045 IIS + LAN case;
- Lan only in `IisMode`;
- Lan only in `config.json`.

**Alternatives considered**:
- **`GetPreviousData`.** The scripts cannot read it.
- **`IisPort` as the host signal.** It is written on every default install (I7).

## R12. Silent flags

**Decision**: `Web_ValidateSilentFlags` always resolves the host, even with no web flag.
`/WEB_HOST` must be `SERVICE`, `IIS` or `NONE`, in any case; any other value stops setup with
"Unknown /WEB_HOST value: {v}". The resolution table is data-model §4, and it is binding.
Points that table makes:
- With no flag, the exposure and ports come from the persisted or inferred values (R11), not
  from 80/47291/443 (I14).
- `/WEB_PORT` and `/WEB_HTTPS_PORT` apply to IIS only. With a resolved host other than IIS they
  stop setup ("/WEB_PORT and /WEB_HTTPS_PORT apply to IIS hosting only"), **except** in the
  FR-041 fallback, where they are ignored and logged.
- The **None + LAN stop applies only to flags passed on the command line**: `/WEB_HOST=NONE`
  with `/WEB_EXPOSURE=LAN` stops. A persisted None + Lan install (possible interactively) still
  updates silently with no flags.
- The standalone port check (R13) runs here for silent installs, before `PrepareToInstall`
  stops anything (I15).
- **Every web install** logs `Web hosting: {Service|IIS|None} (from {page|/WEB_HOST|previous
  host|IIS fallback: <reason>})` (FR-008). The line goes to the Inno setup log (`%TEMP%\Setup
  Log *.txt`, or `/LOG=`), next to the readiness line.

**Found, not fixed** (out of scope): a bad web flag in an *interactive* run exits with only a log
line (I14).

## R13. Ports page and the port check

**Decision**:
- **Fields shown**:
  - Service: one field, captioned "Web edition port -- 1024 to 65535. The web edition and its
    engine share this address:";
  - None: one field, captioned "Engine port -- 1024 to 65535:";
  - IIS: the three fields as today.

  `OnActivate` sets the visibility of `Edits[]` and `PromptLabels[]`, and starts with the
  silent guard.
- **Blocking check for Service** (FR-015). A connect probe (`Test-NetConnection 127.0.0.1
  -Port`) finds HTTP.SYS listeners, which `netstat` does not show (I22).
  - **Exempt** only when the port is the previous install's own: the registry `BridgePort` =
    p, **and** `AkmlSqlWebEngine`'s status is Running or StartPending (the locale-safe
    `Get-Service` probe, `AkmlSqlSetup.iss:1310-1313`). A stopped AKML service never holds the
    port, so a listener then is foreign.
  - **The previous IIS site's port**: when the previous host is IIS, the registry `IisPort` = p,
    and the `AkmlSqlWeb` site exists, block with "Port {p} belongs to the AKML SQL site in IIS,
    which this change removes. Choose a different port for the standalone service." Reusing it
    would keep the origin, but the browser's saved engine connection would then point at the old
    engine port. The spec's "switching changes the address" stands.
  - **Any other listener**: "Port {p} is already used by another program. Choose another
    port." An interactive setup stays on the page; a silent setup stops in `InitializeSetup`
    (R12).
- **IIS and None**: the engine-port check stays a warning, as today.
- **The "site and engine ports must differ" check** applies to IIS only.

**Alternatives considered**:
- **Parsing `netsh http show servicestate` for the yes/no.** The labels are localized.
  `web-hosting-check.ps1` uses that output only to *name* the holder after a failure (R16).
- **Checking in `PrepareToInstall`.** The user's service is already stopped there (I15).

## R14. Telling the user that the address changes (FR-033, FR-035)

**Decision**:
- **One pure function**, `HostingAddress(Host, Exposure, EnginePort, IisPort)` in
  `web-hosting.iss`, gives the address for every host (data-model §11), and an empty string for
  None.
- **Trigger**: a warning shows when `HostingAddress(previous)` ≠ `HostingAddress(new)` and both
  are non-empty. That covers a host switch, a port change, and a standalone switch between
  localhost and LAN (FR-035 as amended).
- **The text** is the same everywhere:

  "The web edition will move from {old} to {new}. Connections, tabs and other data saved in the
  browser at {old} will not appear at {new}. Query history is kept by the engine. To keep your
  settings, use Settings › Export… first."

- **Hosting page**: the notice shows this text when the selected host alone already changes the
  address. `{new}` uses the persisted or default port.
- **Ready page**: `UpdateReadyMemo` is the final word. It returns the whole memo (I28):
  `MemoUserInfoInfo`, `MemoDirInfo`, `MemoTypeInfo`, `MemoComponentsInfo`, `MemoGroupInfo` and
  `MemoTasksInfo`, in order, each non-empty one followed by `NewLine + NewLine`. When
  `IsWebSelected`, it then adds:

  ```
  Web edition:
        Standalone Windows service at http://localhost:47291/      (or: IIS at …, or: files only, not hosted)
        <the move text above, only when the address changes>
  ```

- **The install summary** repeats the new address.

## R15. Order of work: `PrepareToInstall`, `[Run]`, `Web_PostInstall`; the firewall plan

**Decision**:

1. **`PrepareToInstall`**. These steps run first, before SSMS is closed and before
   `TerminateAkmlBackgroundProcesses` stops the service:
   - the install-tree hardening and state-folder lockdown (R22). If the tree hardening fails,
     return an error, so setup stops before any change.
2. **Non-postinstall `[Run]`** (runs before `ssPostInstall`, I12):
   - `web-tls-setup.ps1` (LAN mode, any host);
   - `web-iis-setup.ps1` (`Check: WebHostIsIis`);
   - `sc create` (`Check: IsWebSelected`);
   - the start type: `sc config start= auto` (`Check: WebHostIsService`) or `start= delayed-auto`
     (`Check: not WebHostIsService`), both also requiring that the service exists;
   - the failure actions.

   The two `{tmp}\web-firewall.ps1` firewall entries are **removed** (I10).
3. **`Web_PostInstall`**:
   1. Write the registry values (R11) and the hosting log line (R12).
   2. If the previous host was IIS and the new one is not, run `{app}\Support\web-iis-remove.ps1`
      (R18). If it leaves `iis-remove.failed`, the summary warns.
   3. Run `{app}\Support\web-config-bridge.ps1 -Port -Mode -WebRoot` (R8).
   4. Run `{app}\Support\web-firewall.ps1 -Apply -WebHost {h} -Exposure {e} -EnginePort {p}
      -HttpsPort {q}`. `Get-FirewallPlan` is a pure, dot-sourceable function in that script and
      the only implementation of the data-model §5 table, shared with repair (R17).
   5. For the standalone host, write `DelayedAutostart = 0` under
      `HKLM64\SYSTEM\CurrentControlSet\Services\AkmlSqlWebEngine` (R20).
   6. Run `sc start`.
   7. Poll for the LAN PIN, and read the thumbprint.
   8. Run the answer check (R16).
   9. For None, delete `{group}\Repair AKML SQL Web hosting.lnk` (R17).
   10. Write the summary (R16).

The ACL lockdown that used to sit between the config write and `sc start` moves to
`PrepareToInstall` (R22). `Web_PostInstall` re-applies it, idempotently, before `sc start`.

## R16. Answer check, install summary and Finish link

**Decision**:
- **`{app}\Support\web-hosting-check.ps1`**: `-Url <u> [-ExpectEngine | -ExpectBundle]
  [-TimeoutSeconds 30] [-ReasonPath <file>]`.
  - It sends GET once a second until the timeout, with TLS validation off **for its own requests
    only** (a per-request `ServicePointManager` callback, reset on exit).
  - **Exit 0**: 200 and `text/html`. With `-ExpectEngine` the response must also carry
    `X-AKML-Host: engine`. With `-ExpectBundle` (IIS) the body must contain
    `_framework/blazor.webassembly.js` (W6). IIS must not send the engine header, because the
    page reads it (R6).
  - **Exit 2**: answered, but not serving the pages. Examples are the plain page, IIS's welcome
    page on port 80, or another program.
  - **Exit 1**: no answer.
  - On failure it also names the program registered on that port when `netsh http show
    servicestate view=requestq` lists one. Only URLs and image paths are parsed, never the
    localized labels. The reason goes to `-ReasonPath`.

  | Host / exposure | Probed |
  | --- | --- |
  | Service / Localhost | `http://127.0.0.1:{port}/ -ExpectEngine` |
  | Service / Lan | `https://localhost:{port}/ -ExpectEngine` |
  | IIS | `http://localhost:{IisPort}/ -ExpectBundle` |
  | None | not run |

  The **service-Running check stays for every host**, with today's warning.
- **Summary**: the first lines are `Hosting: Standalone Windows service | IIS | Not hosted` and
  the address (FR-052).
  - **Service + LAN**: `https://{COMPUTERNAME}:{port}/`, "for every computer, this one
    included". Then the PIN, the thumbprint and the `bridge.cer` trust steps, presented as the
    recommended step: until the certificate is trusted, the browser warns and re-downloads 26 MB
    on every load (P5). One line notes that the firewall rule applies to every network profile,
    including Public, and recommends a Private or Domain profile.
  - **`Bridge port:`** stays for every host (W12). For Service it reads `Bridge port: {port}
    (the same address serves the pages)`.
  - **Failures**: a failed check says that the address did not answer, gives the reason, and
    points to the repair shortcut.
  - The IIS-failure text no longer blames the scripting tools (I8).
- **Finish link**: `IsWebSiteHosted` is true for Service or IIS, and `WebSiteUrl =
  HostingAddress(...)`.

## R17. Repair: one shortcut, both hosts, and no stale shortcut

**Decision**:
- **The shortcut**: "Repair AKML SQL Web hosting", with the name unchanged (W12). It is created
  with `Check: WebHostHasHost` (Service or IIS) and runs `{app}\Support\web-hosting-repair.ps1`.
  For None, `Web_PostInstall` deletes an existing `.lnk` (I28: an `[Icons]` Check never removes
  an old shortcut).
- **`web-iis-repair.ps1` gains a guard**: when the registry `Host` value exists and is not
  `IIS`, it prints "This computer does not host the web edition on IIS." and stops. Shortcuts
  made by older versions therefore cannot re-create a removed site.
- **`web-hosting-repair.ps1`** self-elevates. It reads `{app}` from the uninstall key's
  `InstallLocation`, not from `$PSScriptRoot`. It refuses to run, both before and after
  elevating, when either of these is true:
  - `$PSScriptRoot` is not `{app}\Support`;
  - `{app}` has a write ACE for a non-admin SID (R22).

  For that second check:
  - **Counts as a write ACE**: an Allow ACE that grants any of WriteData, AppendData,
    WriteAttributes, WriteExtendedAttributes, Delete, DeleteSubdirectoriesAndFiles,
    ChangePermissions or TakeOwnership.
  - **Allowed holders**: Administrators (`S-1-5-32-544`), SYSTEM (`S-1-5-18`) and
    TrustedInstaller (`S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464`).
  - **Ignored**: inherit-only ACEs (`InheritanceFlags` with `PropagationFlags.InheritOnly`),
    such as CREATOR OWNER (IO) on Program Files, and read or execute ACEs such as Users RX and
    ALL APPLICATION PACKAGES RX.

  A default Program Files install therefore passes.

  It then reads `Host`, `Exposure`, `BridgePort`, `IisPort` and `HttpsPort` and dispatches:
  - **IIS**:
    1. In LAN mode, run `web-tls-setup.ps1`. It reuses a healthy certificate, and the site's
       HTTPS binding needs it.
    2. Run the `web-iis-repair.ps1` flow.
    3. Apply the firewall plan (both rules in LAN mode, none in localhost mode).
    4. Re-apply `delayed-auto` and the failure actions.
    5. Start the service.
    6. Run `web-hosting-check.ps1 -ExpectBundle`.
  - **Service**:
    1. Create the service if it is missing.
    2. Apply `start= auto`, `DelayedAutostart = 0` and the failure actions.
    3. Lock down the state folder, owner included (R22).
    4. Run `web-config-bridge.ps1 -WebRoot {app}\Web`.
    5. In LAN mode, run `web-tls-setup.ps1` and the firewall plan; in localhost mode, remove the
       rules.
    6. **Restart** the service if it is running (`Restart-Service -Force`, then wait for
       Running); otherwise start it. The engine reads its config only at start.
    7. Run `web-hosting-check.ps1 -ExpectEngine`.
    8. In LAN mode, print the PIN read *after* the restart.
  - **None, or nothing installed**: "Nothing to repair: the web edition is not hosted on this
    computer."
- `web-firewall.ps1` and `web-config-bridge.ps1` move to `{app}\Support` (I13, I10).

## R18. Switching away from IIS; stale files

**Decision**: `{app}\Support\web-iis-remove.ps1` runs for IIS → Service and IIS → None, and at
uninstall. A pure `Get-IisRemovalPlan`, which can be dot-sourced, decides the steps, and it is
tested in the `HttpsBindingDecisionTests` style:

1. Remove the `AkmlSqlWeb` site:
   - with WebAdministration when it loads;
   - otherwise, when `%windir%\System32\inetsrv\appcmd.exe` exists, with `appcmd delete site
     "AkmlSqlWeb"`.
2. Remove the `AkmlSqlWeb` app pool when no application uses it, the same two ways.
3. `Remove-OwnedHttpsBinding`, by dot-sourcing `web-https-binding.ps1`. It uses only netsh, so
   it always runs.
4. Delete `{app}\Web\web.config` **only when the site is confirmed gone**:
   `applicationHost.config`, read as a file, no longer lists it (I7). A site that still serves
   keeps its MIME map and CSP.
5. Delete `iis-site.ok` and `https-site.*`.

If the site is still listed afterwards, the script writes `iis-remove.failed`. The summary turns
that into a warning that names the site and how to remove it. The script never touches other
sites or IIS settings, and it always exits 0 (FR-032, FR-061). The firewall plan removes the
HTTPS rule. Switching from Service to IIS needs no removal step: the empty `webRoot` turns the
responder off.

**Stale files**:
- `[InstallDelete] Type: filesandordirs; Name: "{app}\Web\_framework"; Components: web`. The
  gate matters: without it, an upgrade that leaves the web edition out would delete the runtime
  while nothing is copied back (I17, spec edge case).
- `[UninstallDelete] Type: filesandordirs; Name: "{app}\Web"`.

## R19. Uninstall

**Decision**: `Web_Uninstall` runs at `usUninstall`, before `[UninstallRun]` (I11), and does
these steps in order:

1. `sc stop AkmlSqlWebEngine`, wait up to 10 s, then `sc delete`.
2. Delete both firewall rules with direct `netsh advfirewall firewall delete rule name="…"`
   Execs.
3. Remove the bridge sslcert binding and run `-RemoveOwned` for the site binding (both as today).
4. Run `web-iis-remove.ps1` if it is present; otherwise today's inline `Remove-WebSite`.
5. Delete the AKML certificate (I26). The thumbprint comes from `certs\thumbprint.txt`, or
   failing that every `CN=AKML SQL Web Engine` certificate in `Cert:\LocalMachine\My` is a
   candidate. A certificate is deleted only while no `netsh http show sslcert` binding still uses
   its hash.
6. The data prompt: `if UninstallSilent then Log('keeping AKML SQL Web data') else if MsgBox(…)
   = IDYES then DelTree(…)`. `SuppressibleMsgBox` is not enough, because a plain `/VERYSILENT`
   uninstall would still show it (P7).
7. Delete the registry key.

The desktop settings prompt at `usPostUninstall` follows the same `UninstallSilent` pattern
(FR-071 as amended). The `[UninstallRun]` service and firewall entries are removed from the
script; R23 handles the entries older installs recorded.

## R20. Start type

**Decision**: The standalone host gets `sc config AkmlSqlWebEngine start= auto`, **and**
`DelayedAutostart = 0` is written explicitly. sc's documentation does not say whether
`start= auto` clears the delayed flag, which the existing service has set (I12) (**SPIKE S6**).
IIS and None keep `delayed-auto`, unchanged. Repair re-applies the start type for its host.
The quickstart checks `sc qc` (AUTO_START without "(DELAYED)") after install and after repair.

**Alternatives considered**:
- **`delayed-auto` everywhere.** It gives no time guarantee (P3).
- **`auto` everywhere.** That is an unrequested change for IIS installs.

## R21. Tests and their gates

**Decision** (the full list is in [quickstart.md](./quickstart.md)):

**Engine and Core unit tests**
- `StaticWebResponderTests` covers:
  - the routing rows, headers, content types, HEAD, and 304 by ETag and by a whole-second
    `If-Modified-Since` (a file whose write time has a sub-second part);
  - the fallback and its 304;
  - `/` → `index.html`; `/js/`, `/_framework/` and `/a//b` → 400; `/_framework/dotnet.js/` →
    400;
  - **the SC-013 table** (contract §8) over raw `TcpClient` request lines, asserting no file
    bytes;
  - web-root refusals;
  - limits: 32 slow readers do not stop a 33rd request from completing or getting 503 within
    the wait; an exception does not leak a slot; a null `Url` gives 400.
- `StaticWebResponderCspTests`: the AST-extracted ps1 CSP, plus coverage of the bundle's
  extensions.
- `BridgeAccessTests` (extended): with no web root, the plain page on every path; `/akmlsql`
  keeps it; an upgrade on any path; the Host allowlist.
- `EngineHostTests` (at `tests/AkmlSql.Engine.Tests/EngineHostTests.cs`): `webRoot` is mapped
  only for web mode; the bind-failure message.
- A Core round-trip test for `bridge.webRoot`.

**Web unit tests**
- `EngineEndpointTests`: `FromPage`, and `strictScheme`.
- `PageEngineTests`: the three answers; only definitive answers cached; 3 failures or 60 s →
  Static.
- `EngineAutoConnectPrecedenceTests` (new), covering the precedence, the dangling active id,
  Unknown → Unreachable, and an https loopback page engine that never dials `ws`.
- The status-bar title tests and the picker prefill test.

**Web E2E** (`EngineServedWebTests`)
- `SandboxEngine` with a web root composed from `src/AkmlSql.Web/wwwroot` and
  `bin/Debug/net10.0/wwwroot/_framework` (W15), built by `Harness/EngineServedBundle.cs`.
- **Freshness guard**: it fails, with "run `dotnet build src/AkmlSql.Web -c Debug`", when
  `bin/Debug/net10.0/AkmlSql.Web.dll` is older than the newest `*.cs` or `*.razor` under
  `src/AkmlSql.Web`, bin and obj excluded.
- It checks:
  - the status goes Live with no pairing;
  - every route reloads;
  - no failed loads;
  - the headers;
  - the SC-013 table;
  - a `/spike` case: `.sql` is served as `text/plain`, and the formatted input is SQL, not
    HTML.
- `SpikePageTests` stays as a regression check on the dev server.

**Installer tests that install nothing**
- `WebHostingProbeTests`: probe-ISS tests in the `ProcessDetectionTests` style over
  `web-hosting.iss`. They cover `IisReadinessFrom` (every state), `InferPreviousHost` (with the
  real PS 5.1 `config.json`), `ResolveSilentHost` (data-model §4, including the candidate
  trigger and the None + LAN cases), `HostingAddress` and `ReadConfigString`.
- `WebInstallerSourceTests`: source-text facts.
  - absent: `Components: web\iis` and `Components: web\service`; `{tmp}\web-firewall.ps1` and
    `{tmp}\web-config-bridge.ps1` anywhere; `dism`;
  - present: the scripts are installed to `{app}\Support`; three hosting items; `if WizardSilent
    then Exit` at the top of the page handlers; `Components: web` on the `[InstallDelete]`;
    `UninstallSilent` around both uninstall prompts.
- **PowerShell dot-source tests**. Each script is restructured so that its logic is a pure
  function, its side-effecting entry point runs only when the script is invoked (`if
  ($MyInvocation.InvocationName -ne '.')`), and it has no `#Requires`.

  | Test class | Script and function |
  | --- | --- |
  | `WebConfigBridgeTests` | `web-config-bridge.ps1` → `Get-BridgeConfigJson`: `webRoot` written, other keys kept, PS 5.1 format |
  | `FirewallPlanTests` | `web-firewall.ps1` → `Get-FirewallPlan` (the data-model §5 table) |
  | `IisRemovalPlanTests` | `web-iis-remove.ps1` → `Get-IisRemovalPlan` (with and without WebAdministration; the site still listed) |

- `WebHostingCheckTests` runs `web-hosting-check.ps1` as a process against an in-test
  `HttpListener`:
  - header + HTML → 0;
  - the plain page, or a missing header → 2;
  - an `-ExpectBundle` body check;
  - a closed port → 1 within `-TimeoutSeconds 3`;
  - the reason file written.

**Installer smoke tests (they run the real installer; disposable VMs only)**
- Every smoke class requires `AKML_INSTALLER_SMOKE=1`.
- `InstallerSmokeEnv.CanRun` is split into `CanRunService` (admin) and `CanRunIis` (admin with
  IIS `Ready`).
- The uninstaller path comes from the uninstall key.
- Cleanup removes both firewall rules, the app pool, the test sslcert, the AKML certificate and
  the registry key.
- One parameterized `StandaloneSmokeFixture` has three cases:
  - `Standalone`: `/WEB_HOST=SERVICE`;
  - `LanStandalone`: plus `/WEB_EXPOSURE=LAN`;
  - `IisToStandaloneSwitch`: `/WEB_HOST=IIS`, then `/WEB_HOST=SERVICE`, gated on `CanRunIis`.
- The cases assert:
  - `sc qc` shows AUTO_START without "(DELAYED)";
  - the ACLs;
  - the fresh uninstall log has no `\is-*.tmp\` path (R23).

## R22. Install tree and state folder hardening (FR-020)

**Decision**: In `PrepareToInstall`, when the web edition is ticked, before anything is stopped
or copied:

1. **The install tree.** When `{app}` is not under `{commonpf64}` or `{commonpf32}` (a custom
   folder, I23):
   - **Refuse** with a message, stopping before any change, when the existing folder is a
     reparse point, or is non-empty and owned by an SID other than Administrators, SYSTEM or
     TrustedInstaller. Resetting an ACL would not remove a planted DLL.
     **SPIKE S7**: it is not yet confirmed that a folder created by an older (elevated) setup
     is owned by Administrators rather than by the installing user's SID. If S7 shows the
     user's SID, the ownership refusal would block every upgrade of a pre-045 custom-folder
     install. In that case the fallback applies: refuse only reparse points, and otherwise take
     ownership and reset the ACL.
   - Otherwise `ForceDirectories({app})`, then run `icacls "{app}" /setowner *S-1-5-32-544 /T
     /C`, then `icacls "{app}" /inheritance:r /grant:r *S-1-5-32-544:(OI)(CI)F /grant:r
     *S-1-5-18:(OI)(CI)F /grant:r *S-1-5-32-545:(OI)(CI)RX /grant:r *S-1-5-32-568:(OI)(CI)RX /T
     /C`. Users and IIS_IUSRS get read, so an IIS site in that tree still works.
   - If this fails, return an error. A LocalSystem service is never created over a tree that
     standard users can write.

   Program Files folders are already safe by inheritance and are not touched.
2. **The state folder** `%ProgramData%\AKML SQL Web` (I24):
   - `ForceDirectories`;
   - `icacls … /setowner *S-1-5-32-544 /T /C`;
   - `icacls … /inheritance:r /grant:r *S-1-5-32-544:(OI)(CI)F /grant:r *S-1-5-18:(OI)(CI)F /T
     /C`.

   This now happens **before** the `[Run]` scripts and `web-config-bridge.ps1` write into the
   folder. A failure stays a summary warning, as today. `Web_PostInstall` and repair re-apply it.

`/DIR=` silent installs get the same checks, because `PrepareToInstall` runs in silent mode too.

## R23. A fresh uninstall log when the old one holds temp-folder script paths

**Decision**: Older installs recorded `[UninstallRun]` entries that run
`powershell -ExecutionPolicy Bypass -File "<%TEMP%>\is-…tmp\web-firewall.ps1"` elevated at
uninstall (I10). Unelevated code can plant a script at one of those paths. The fix reuses the
`MigrateLegacy32BitInstall` pattern (`AkmlSqlSetup.iss:548-585`):

1. In `PrepareToInstall`, find the current log from the uninstall key's `UninstallString`
   (`unins???.exe` → `unins???.dat`).
2. When that log contains the UTF-16 bytes of `\is-` followed by `.tmp\`, rename `unins???.dat`
   and `unins???.exe` aside.
3. Inno then writes a fresh log for this install. On a failed install, `DeinitializeSetup`
   restores the old files; on success, it deletes them.

Nothing is lost. `[UninstallDelete]` already covers `{app}\Engine`, `{app}\Support`, the SSMS
extension folder and `dirifempty {app}`, and this feature adds `{app}\Web`. The current install
records every file it copies. A smoke assertion checks that the produced log has no
`\is-*.tmp\` path.

**Alternatives considered**: `UninstallLogMode=overwrite` changes behaviour globally, and the
Inno docs advise against it.

## Spikes

| Id | Claim to prove | Proof | Fallback if it fails |
| --- | --- | --- | --- |
| S1 | Inno 7.1 accepts a previous selection naming the removed `web\iis` / `web\service` (`UsePreviousComponents` and `RestoreLegacyComponents`' `WizardSelectComponents`). | Probe setup; then upgrade VM A from a release that had `web,web\iis,web\service`. | Keep both entries with `Check: False`. |
| S2 | `ItemEnabled[1] := False`, with an explicit reset and a wrapped reason label, renders correctly in the light and dark wizard styles at 100–200% DPI, and the keyboard cannot select the disabled item. | VM screenshots; a keyboard walk-through. | Show the reason in an `AddGroup` row appended after the options. |
| S3 | Elevated LAN standalone: `https://+:{port}/` serves `index.html` and `wss` on the same port; `https://localhost:{port}/` works over `::1`; plain `http://{host}:{port}/` returns no app; reloads give 304 once the certificate is trusted. | A LAN VM pair. | An owned `[::]:port` sslcert binding (not expected, E17). |
| S4 | The readiness values (R10) match reality on a clean Windows 11 and on Windows Server 2022, for each Common HTTP Feature and with and without the management tools. Record **which feature turns `ToolsUnavailable` into `Ready`**, and set the reason text to name it. The cold probe takes under 5 s. | VM matrix. | Add the AppHostAdminManager CLSID as a pre-check, and run the probe asynchronously. |
| S5 | .NET 10 HttpListener decodes as E10 says, and the manifest and gate serve nothing in the SC-013 table. | `StaticWebResponderTests`. | n/a (the tests are the gate). |
| S6 | Whether `sc config start= auto` clears `DelayedAutostart`. The plan writes 0 explicitly either way. | `sc qc` on VM A after each step. | n/a (the explicit write covers it). |
| S7 | A custom `{app}` created by an older, elevated setup is owned by Administrators, so R22's ownership refusal does not block its upgrade. | Install an earlier release to `D:\AKML` on VM A, check the owner with `(Get-Acl).GetOwner([System.Security.Principal.SecurityIdentifier])`, then upgrade with 045 setup. | Refuse only reparse points; otherwise take ownership and reset the ACL (R22). |

## Found, not fixed (recorded in `doc/progress.md` › 045 deferred)

- `CurPageChanged` compares page IDs with `>`, so the Options-page capture fires on the wrong
  pages (I3).
- The engine's LAN start parses netsh's English "Certificate Hash" label (fails on non-English
  Windows).
- `web-tls-setup.ps1` deletes any sslcert binding on the engine port without an ownership check.
  Leaving LAN mode does not remove the bridge sslcert (I9).
- A bad web flag in an interactive run exits with a log line only (I14).
- The web app checks `refactoring.heavy`, which the engine never advertises.
- The HTTP driver being disabled (`Services\HTTP\Start=4`) breaks both hosts, and nothing checks
  for it.
- The IIS site's plain-HTTP binding listens on every interface in both modes (I25). This is an
  explicit exception to SC-009 for IIS, as amended. A later change could bind it to this
  computer only.
- The LAN firewall rules use `profile=any`. The summary says so in one line (R16); making the
  profile a parameter is deferred.
- On a multi-session host in localhost mode, the page origin can be squatted while the service
  is stopped (R6). This is documented, not fixed.
