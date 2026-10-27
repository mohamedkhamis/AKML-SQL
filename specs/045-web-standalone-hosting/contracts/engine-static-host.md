# Contract: the engine's HTTP surface on the bridge port

**Branch**: `045-web-standalone-hosting` | **Date**: 2026-10-26

This change adds **no IPC message type, no MessagePack key and no capability**. The contract
defines what the web-mode engine answers on its bridge port, once a web root is configured, to
anything that is not a WebSocket upgrade. The same change updates `doc/ipc-api.md` (transport
table), `doc/architecture.md` §9d, `doc/m3-security.md` and `doc/configuration.md` (a new
`bridge` section).

## 1. Switch

| Engine | `bridge.webRoot` | Behaviour on non-upgrade requests |
| --- | --- | --- |
| web mode (`--web`, `RunWebAsync`) | non-empty and valid (§1a) | static responder (§2–§7) |
| web mode | empty | today's plain page on every path, byte for byte |
| web mode | non-empty but invalid | today's plain page, plus one Warning: `WebSocketTransport: not serving the web edition from {WebRoot}: {Reason}`. The engine does not exit. |
| IDE mode (SSMS-started, `RunAsync`) | any | today's plain page; `webRoot` is never read |

**§1a Valid web root**:
- the path is rooted, the folder exists, and `index.html` is in it;
- it is not a volume root;
- it does not have the ReparsePoint attribute;
- it does not equal, contain, or lie inside the folder of the config file or of `tokenStorePath`.

**Bind failure**: if `HttpListener.Start` fails with 183 or 32, the transport throws
`port {p} is held by another process`, and `RunWebAsync` logs that message.

## 2. Routing (in this order)

| # | Condition | Response | Slot |
| --- | --- | --- | --- |
| 1 | `IsWebSocketRequest`, any path | today's upgrade path, with the loopback-remote and localhost Origin checks unchanged. It stays inline on the accept loop. | — |
| — | *Everything below is dispatched with `Task.Run`; nothing else runs on the accept loop.* | | |
| 2 | `Request.Url` is null | `400 Bad request.` | no |
| 3 | method is not GET or HEAD | `405`, `Allow: GET, HEAD` | no |
| 4 | localhost mode, and Host is not `localhost`, `127.0.0.1` or `[::1]` (any port) | `400 Bad host.` | no |
| 5 | path is `/akmlsql` or `/akmlsql/` (OrdinalIgnoreCase) | today's plain page (200, its own CSP, `no-store`) plus `X-AKML-Host: engine` | no |
| 6 | path fails validation (§3 steps 2–4) | `400 Bad request.` | no |
| 7 | manifest hit, or fallback target, with a matching validator (§4) | `304`, no body | no |
| 8 | manifest hit | `200`, the file | yes |
| 9 | manifest miss, and the last segment has no `.` (query ignored) | `200`, `index.html` with its headers | yes |
| 10 | manifest miss otherwise | `404 Not found.` | no |
| — | rows 8–9 get no slot within 10 s | `503`, `Retry-After: 5`, `Busy.` | — |

- **HEAD** returns the headers GET would return, with no body.
- **Range requests** get the full `200`.
- **Limits**: there are 32 slots. A file response's deadline is 60 s plus `Length / 64 KiB` per
  second; when it passes, the response is aborted (`Response.Abort()`). A slot is released in
  `finally`. A catch-all aborts the response, so an exception never leaks a slot.
  `HttpListener.TimeoutManager` is not used.

## 3. Path validation and containment

The filesystem is touched **only at step 6**.

1. Unescape `Request.Url.AbsolutePath` exactly once (`Uri.UnescapeDataString`).
2. Reject the path if it contains `\`, `:`, `%`, `~`, `\0`, or any character below 0x20.
3. If the path is exactly `/`, the key is `index.html`. Go to step 5.
4. Strip the one leading `/`. Then reject the path when:
   - any segment is empty: `//`, or a trailing `/` such as `/js/` or `/_framework/`. Directories
     are never listed or served;
   - a segment is `.` or `..`;
   - a segment ends in `.` or a space.
5. Look the key up in the manifest (OrdinalIgnoreCase). A miss goes to rows 9–10.
6. Check that `Path.GetFullPath(entry.FullPath)` starts with
   `Path.GetFullPath(webRoot) + Path.DirectorySeparatorChar` (OrdinalIgnoreCase). Then open the
   file (`FileMode.Open`, `FileAccess.Read`, `FileShare.Read`, async, sequential).

**Manifest**: built once at transport start by walking the web root.
- It includes only files whose extension is in §5.
- It excludes names ending in `.config`, `.br` or `.gz`, and Hidden, System and ReparsePoint
  entries.
- It does not descend into reparse-point directories.

## 4. Headers

**Rows 2–4 and 6–10, and 503**:

| Header | Value |
| --- | --- |
| `Content-Type` | rows 8–9: from §5. Every other row with a body: `text/plain; charset=utf-8` |
| length | set with `Response.ContentLength64` **only**, never as a header. A HEAD sets it and writes no body. 304 has no body. |
| `Cache-Control` | `no-cache` |
| `ETag` | rows 7–9: `"{length:x}-{lastWriteUtcTicks:x}"`. Row 9 uses `index.html`'s values. |
| `Last-Modified` | rows 7–9: RFC 1123, the last write **truncated to whole seconds** |
| `Content-Security-Policy` | `StaticWebResponder.ContentSecurityPolicy`. It equals the `$csp` that `src/AkmlSql.Installer/web-iis-setup.ps1` builds. A drift test parses the ps1 with the PowerShell AST, evaluates only the `$bridgeConnectSrc` and `$csp` assignments, and compares. |
| `X-Content-Type-Options` | `nosniff` |
| `X-AKML-Host` | `engine`. This is the page's and setup's signal that the engine serves the bundle. It is never present when the engine does not. |
| `Allow` | row 3: `GET, HEAD` |
| `Retry-After` | 503: `5` |

The CSP today:

```
default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline';
img-src 'self' data:; connect-src 'self' ws://127.0.0.1:* wss://127.0.0.1:* ws://localhost:*
wss://localhost:* wss: https://api.openai.com https://api.anthropic.com
https://generativelanguage.googleapis.com https://*.openai.azure.com http://localhost:11434
http://127.0.0.1:11434 http://localhost:1234 http://127.0.0.1:1234;
```

**Validators (row 7)**:
- If `If-None-Match` is present, it alone decides: a match gives 304.
- If it is absent, an `If-Modified-Since` at or after the truncated `Last-Modified` gives 304.

## 5. Content types (the only servable extensions)

| Extension | Content-Type |
| --- | --- |
| `.html` | `text/html; charset=utf-8` |
| `.js` | `text/javascript` |
| `.css` | `text/css` |
| `.wasm` | `application/wasm` |
| `.json`, `.map` | `application/json` |
| `.svg` | `image/svg+xml` |
| `.txt`, `.sql` | `text/plain; charset=utf-8` |
| `.pdb` | `application/octet-stream` |

A test enumerates the composed bundle, in both the Release publish layout and the Debug E2E
layout, and fails if any file's extension is missing from this table.

## 6. Logging

| Event | Level | Message |
| --- | --- | --- |
| Serving starts | Information, once | `WebSocketTransport: serving the web edition from {WebRoot} ({Count} files)` |
| Rows 2, 3, 4, 6 | Warning | `WebSocketTransport: refused {Method} {Path} from {Remote} ({Reason})` |
| Refusals over the limit | Warning, periodic | `WebSocketTransport: {Count} refused requests not logged` |
| 503 | Warning, at most once a minute | — |

- Refusal lines are capped **globally** at one a second. `{Path}` is cut to 200 characters,
  which is safe on .NET 10 with default HTTP.SYS settings (P8). No line ever contains file
  contents or any state.
- Rows 5 and 7–10 are never logged. The service log defaults to Debug, and a page load is more
  than 100 requests.
- Write failures (the browser went away) are swallowed, as today: `HttpListenerException`,
  `IOException`, `ObjectDisposedException`.

## 7. Differences from a clean IIS install, on purpose

| Behaviour | IIS (`web-iis-setup.ps1`) | Engine | Why |
| --- | --- | --- | --- |
| Deep-link status | `index.html` with **404** (httpErrors ExecuteURL) | `index.html` with **200** | A 404 main document logs a failed load on every reload (SC-006) |
| Missing file with an extension | `index.html` with 404 | plain `404` | A missing `.js` or `.wasm` must never return HTML (integrity) |
| Directory form (`/js/`) | 403 / listing disabled | `400` | No directory is ever listed |
| `nosniff` | not sent | sent | Stricter; the content-type table is tested |
| `.js` type | `application/javascript` | `text/javascript` | RFC 9239 |
| `.sql` | now mapped (`text/plain`) | `text/plain; charset=utf-8` | Parity (FR-021 as amended) |
| Charset on text types | to confirm on the clean install | `; charset=utf-8` | Explicit encoding |
| Range requests | honoured, `Accept-Ranges: bytes` | full `200`, no `Accept-Ranges` | The runtime never sends ranges |
| Compression | none | none | Parity |

## 8. The SC-013 request table

Each request is sent as a raw `TcpClient` request line (an `HttpClient` would normalize it).
The assertion for every row: **no bytes of any file other than `index.html` in the response, and
no directory listing**. The status is recorded but not asserted, because HTTP.SYS answers some
rows itself with 400 or 403.

| # | Request line | Expected |
| --- | --- | --- |
| 1 | `GET /..%2F..%2FProgramData/AKML%20SQL%20Web/pairing-pin.txt` | 400 / 403, no content |
| 2 | `GET /%2e%2e/%2e%2e/Windows/win.ini` | 400 / 403, no content |
| 3 | `GET /%5C..%5Cweb.config` | 400, no content |
| 4 | `GET /C:/Windows/win.ini` | 400, no content |
| 5 | `GET /C%3A/Windows/win.ini` | 400, no content |
| 6 | `GET //localhost/c$/Windows/win.ini` | 400, no content (no SMB connection is made) |
| 7 | `GET /index.html::$DATA` | 400, no content |
| 8 | `GET /index.html.` | 400 |
| 9 | `GET /index.html%20` | 400 |
| 10 | `GET /WEB~1.CON` | 400 |
| 11 | `GET /web.config` (a `web.config` placed in the root) | 404, no content |
| 12 | `GET /CON` and `GET /con.txt` | `index.html` / 404, never a device read |
| 13 | `GET /_framework/` | 400, no listing |
| 14 | `GET /_framework/dotnet.js/` | 400 |
| 15 | `GET /certs/bridge.cer`, `GET /pairing-pin.txt`, `GET /tokens.json`, `GET /config.json`, `GET /INSTALL-SUMMARY.txt` | 404, no content |
| 16 | `GET /index.html.br` (a stale `.br` placed in the root) | 404, no content |
| 17 | `GET /%252e%252e/%252e%252e/Windows/win.ini` | 400 (`%` after decoding), no content |
| 18 | `GET /a//b` | 400 |

`StaticWebResponderTests` and quickstart 1.1 and 3.5 use this table.

## 9. Tests that pin this contract

| Test | Covers |
| --- | --- |
| `tests/AkmlSql.Engine.Tests/Transports/StaticWebResponderTests.cs` (new) | §2 rows 2–10 and 503; the §4 headers; the §5 types; HEAD; 304 by ETag and by whole-second `If-Modified-Since` (a file whose write time has a sub-second part); fallback 304; `/` → `index.html`; the §8 table; the §1a refusals; the slot cap (32 slow readers do not stop a 33rd request finishing or getting 503 within the wait); an exception does not leak a slot |
| `StaticWebResponderCspTests` (new) | the constant equals the AST-extracted ps1 `$csp`; every bundle extension is mapped |
| `BridgeAccessTests` | with no web root, the plain page on every path (today's assertions); `/akmlsql` keeps the page with a web root; an upgrade on `/` and on `/akmlsql` with a same-origin `Origin` works; a foreign `Origin` still gets 403; a foreign Host gets 400 in localhost mode |
| `tests/AkmlSql.Engine.Tests/EngineHostTests.cs` | `webRoot` is mapped only when `serveWebEdition` is true; the bind-failure message |
| `tests/AkmlSql.Core.Tests` › config round-trip | `bridge.webRoot` with its exact camelCase key |
