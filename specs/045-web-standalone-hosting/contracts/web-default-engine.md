# Contract: the web edition's default engine (FR-014)

**Branch**: `045-web-standalone-hosting` | **Date**: 2026-10-26

This is what the browser app does when the engine itself served the page. A page served by IIS
or by the dev server behaves exactly as it does today.

## 1. Detection (`IPageEngine.GetAsync()`)

At startup the app sends one `HEAD /`, using the registered `HttpClient`
(`BaseAddress = HostEnvironment.BaseAddress`) with `SetBrowserRequestCache(NoCache)`. The result
is one of three answers:

| Answer | When | Cached for the page session |
| --- | --- | --- |
| `Engine(info)` | a response with `X-AKML-Host: engine` | yes |
| `Static` | a response without the header; **or** 3 consecutive failed attempts; **or** 60 s since the first failure | yes |
| `Unknown` | an exception or a timeout, before the `Static` limits are reached | no; the next call tries again |

Nothing about the answer is stored in browser storage.

## 2. Address (`EngineEndpoint.FromPage(Uri pageBase)`, pure)

| Page address | Host | Port | Scheme |
| --- | --- | --- | --- |
| `http://localhost:47291/` | `localhost` | 47291 | `ws` |
| `http://127.0.0.1:59417/` | `127.0.0.1` | 59417 | `ws` |
| `https://sql-box:47291/` | `sql-box` | 47291 | `wss` |
| `https://sql-box/` | `sql-box` | 443 | `wss` |
| `http://[::1]:47291/` | `::1` | 47291 | `ws` |

The bridge URL is `{scheme}://{FormatHost(host)}:{port}/akmlsql`, as today.

`EngineEndpoint.CandidateUrls(host, port, rememberedScheme, strictScheme = false)` gains
`strictScheme`. When it is true, the function returns **only** the remembered scheme. Without
it, a loopback host also gets `ws` and `wss` added.

## 3. Precedence

**Nothing usable is active** means either of these:
- no active id;
- an active id whose record is missing. The dangling id is cleared first, with
  `SetActiveIdAsync(null)`.

The order is:
1. An active, existing saved engine connection always wins, even when it points at another
   engine that is down.
2. Otherwise, an `Engine` page uses its own engine (§4).
3. Otherwise, an `Unknown` page returns `Unreachable` with "Checking this page's engine…", and
   the existing backoff retries.
4. Otherwise, a `Static` page dials nothing, as today (`NotPaired` "No engine paired yet.").

Browser storage is per origin, so a page at a new address starts with nothing usable active.

## 4. Connecting to the page's engine

1. If a saved record has the same host (OrdinalIgnoreCase) and port, make it active and take
   the normal connect path.
2. Otherwise dial a new record with no PIN and no bearer:

   | Field | Value |
   | --- | --- |
   | `Host`, `Port` | from §2 |
   | `ResolvedScheme` | the scheme from §2 |
   | `StrictScheme` | true when the page is https. It is a new persisted field on `EngineConnection`, default false. `EngineBridge.ConnectAsync` passes it to `CandidateUrls`, so an https page's engine is never dialled with `ws`, on the first connect or on any reconnect. |
   | `IsLocalhost` | `IsLoopbackHost(Host)` |
   | `Name` | "This computer" for a loopback host, otherwise the host |

3. Act on the outcome:

   | Handshake outcome | Result | Stored |
   | --- | --- | --- |
   | `Ok` | connected; `LastKnownEngineVersion` and `LastKnownCapabilities` set as in `PairLocalEngineAsync` | yes, active |
   | `PinRequired` | `PairingRequired`, "This engine needs its pairing PIN. Pair it in Settings › Connections."; `IPageEngine.PairingPrefill` set | no |
   | socket or transport failure | `Unreachable` (existing backoff) | no |

The PIN-less probe never touches the engine's PIN rate limiter, so it is safe on every load.

## 5. UI changes

| Place | Change |
| --- | --- |
| Status bar, "Use this computer's engine" (`engine-use-local`) | On an `Engine` page, `PairLocalEngineAsync` uses the page engine's host, port and scheme, with `StrictScheme` set for https. The title reads `This computer's engine ({host}:{port})`. Otherwise unchanged. |
| Settings › Connections › Add (`ConnectionPickerComponent`) | On an `Engine` page, the form starts with the page engine's host and port. With `PairingPrefill` set, the PIN field shows at once; this covers `https://localhost:{port}/` in LAN mode, where `NeedsPin` would otherwise be false. |
| `SecureContextNotice` | No change. A standalone page is always a secure context. |
| Strings that cite the `INSTALL-SUMMARY.txt` "Bridge port" line | No change. The summary keeps that line for every host. |

## 6. Tests

| Test file | Cases |
| --- | --- |
| `tests/AkmlSql.Web.Tests/Services/EngineEndpointTests.cs` | the §2 table; `strictScheme` returns only the given scheme for a loopback host |
| `tests/AkmlSql.Web.Tests/Services/PageEngineTests.cs` (new) | header present → `Engine`, cached; header absent → `Static`, cached; request throws → `Unknown`, not cached, retried; a later header response → `Engine`; 3 failures, or 60 s, → `Static`, cached |
| `tests/AkmlSql.Web.Tests/Bridge/EngineAutoConnectPrecedenceTests.cs` (new) | an active record wins; a dangling active id is cleared and the page engine is used; `Engine` + `Ok` stores one active record with the page scheme and `StrictScheme`; `Engine` + `PinRequired` stores nothing and sets the prefill; `Static` → `NotPaired` with no dial; `Unknown` → `Unreachable`; a matching saved record is reused, not duplicated; a socket failure → `Unreachable`; an https loopback page engine whose `wss` fails never dials `ws` |
| `StatusBarSegmentsTests`, `StatusIndicatorTests` | the title uses the page engine's address |
| `ConnectionPickerPinTests` | the prefill fills the Add form and shows the PIN field |
| `tests/AkmlSql.Web.E2E.Tests/EngineServedWebTests.cs` (new) | a fresh browser context opened at the engine's address reaches "Live" with no pairing and no typing |
