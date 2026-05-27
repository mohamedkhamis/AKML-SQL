# AKML SQL — Web Edition Plan (M0–M6)

**Author**: Mohamed Khamis
**Status**: Draft
**Scope**: Local web edition only (SaaS deferred to a separate planning cycle)
**Total estimated effort**: ~16–20 weeks of focused single-developer work

---

## At a glance

Seven milestones take AKML SQL from "SSMS/VS plugin only" to "browser-based local web edition installable to IIS, usable on localhost or LAN, fully independent from the IDE plugins."

| # | Milestone | Effort | Ship value |
|---|-----------|--------|------------|
| **M0** | Engine dispatcher + transport abstraction | 2 wk | Precondition — no user-visible change |
| **M1** | ScriptDom-in-WASM spike + Blazor project skeleton | 1 wk | Decision gate; thin scaffold lands |
| **M2** | Blazor WASM standalone — formatter + analyser | 3–4 wk | First usable web surface (offline, paste SQL) |
| **M3** | WebSocket transport + local-agent bridge + LAN auth | 2–3 wk | Browser talks to local engine for live schema |
| **M4** | Installer: IIS deployment option | 1 wk | One-click "host on local IIS" |
| **M5** | Schema cache in IndexedDB + offline IntelliSense | 2–3 wk | IntelliSense for cached DBs without the agent |
| **M6** | AI assistance in browser (BYO key) | 2 wk | Text-to-SQL, Explain, Fix, Optimize from browser |

SaaS / multi-tenant hosting / admin portal / staff portal are **out of scope** for this plan and will be planned separately once M0–M6 ship.

---

## Architecture at the end of M6

```
                                ┌─────────────────────────────────────────────┐
                                │  Browser (any modern, no install)            │
                                │                                              │
                                │   AkmlSql.Web (Blazor WASM)                  │
                                │   ├─ Monaco-style editor                     │
                                │   ├─ AkmlSql.Core         (in-process)       │
                                │   ├─ AkmlSql.Formatting   (in-process)       │
                                │   ├─ AkmlSql.Analyzer     (in-process)       │
                                │   ├─ IndexedDB schema cache                  │
                                │   └─ AI provider clients  (BYO key)          │
                                │                                              │
                                │   IRpcTransport implementations:             │
                                │   ├─ InProcessTransport (formatter/analyser) │
                                │   └─ WebSocketTransport (live schema)        │
                                └────────────────┬─────────────────────────────┘
                                                 │ HTTPS (static files)
                                                 ▼
                                ┌─────────────────────────────────────────────┐
                                │  Local IIS  (or Kestrel fallback)            │
                                │  Serves the WASM bundle as static content    │
                                │  Binding: localhost-only OR LAN              │
                                └─────────────────────────────────────────────┘
                                                 ▲
                                                 │ WebSocket on configurable port
                                                 │ pairing-token auth (LAN mode)
                                ┌────────────────┴─────────────────────────────┐
                                │  AkmlSql.Engine (.NET 10, win-x64, trimmed)  │
                                │  Same binary as today; transports added:     │
                                │  ├─ NamedPipeTransport  (SSMS/VS plugins)    │
                                │  └─ WebSocketTransport  (browser)            │
                                └──────────────────────────────────────────────┘
```

The browser and the engine are independent of the SSMS/VS plugins — separate `%AppData%` subdirectory, separate engine instance, no shared config or history. A user with both installed runs two engine processes; this was a deliberate choice to keep complexity bounded.

---

## Key decisions already baked in

- **Blazor WebAssembly, not Blazor Server, not MAUI.** Reuses .NET codebase; runs entirely client-side; no per-user backend compute.
- **IIS as static-file host.** Cleanest possible deployment; no .NET runtime needed in IIS; no app pool / Blazor Server SignalR concerns.
- **Localhost or LAN, install-time choice.** LAN mode adds a pairing-token authentication step on the WebSocket transport.
- **Web edition is independent of IDE plugins.** Separate `%AppData%/AKML SQL Web/` config; separate engine process; no shared state.
- **Engine binary is unchanged.** Same `AkmlSql.Engine` executable; new transports added alongside the existing named pipe.

---

## Phase files

| File | Phase | Topic |
|------|-------|-------|
| `M0-dispatcher-transport.md` | M0 | Engine dispatcher refactor + transport abstraction |
| `M1-wasm-spike-skeleton.md` | M1 | ScriptDom WASM viability + Blazor project skeleton |
| `M2-formatter-analyser-mvp.md` | M2 | First usable web surface — formatter + analyser only |
| `M3-websocket-transport.md` | M3 | Live schema via WebSocket + LAN pairing-token auth |
| `M4-iis-installer.md` | M4 | Installer option to deploy WASM bundle to local IIS |
| `M5-indexeddb-schema.md` | M5 | Schema cache in IndexedDB + offline IntelliSense |
| `M6-ai-browser.md` | M6 | AI assistance from the browser (BYO key) |

Each PRD follows the same template: executive summary, why now, current state, proposed architecture, milestones, risks, success metrics, out of scope, open questions, definition of done.

## Operator quickstarts

Hands-on walkthroughs covering the engineer-or-operator flows for each shipped milestone:

| File | Covers |
|------|--------|
| [`quickstart-m2.md`](quickstart-m2.md) | M2 — in-browser format + analyse (no engine; offline) |
| [`quickstart-m3.md`](quickstart-m3.md) | M3 — pair from a second LAN machine over `wss://`; localhost demo also covered |
| [`quickstart-m4.md`](quickstart-m4.md) | M4 — installer details: IIS site, TLS cert, firewall rule |
| [`quickstart-m5.md`](quickstart-m5.md) | M5 — IndexedDB schema cache + offline IntelliSense |
| [`quickstart-m6.md`](quickstart-m6.md) | M6 — AI assistance from the browser |

Security reference: [`doc/m3-security.md`](../m3-security.md) — threat model, on-disk artefacts, plaintext-on-LAN refusal contract for the M3 bridge.

---

## Plugin install timing — answer to "when should the user install the plugin?"

This is a deliberate non-coupling: **the IDE plugins and the web edition install independently and either can be installed first.** The installer's component selection page presents both as optional checkboxes:

| Component | When to choose |
|-----------|----------------|
| **SSMS / VS plugins** | User writes SQL primarily inside SSMS or Visual Studio |
| **Web edition (local)** | User wants browser access, or works on a machine without SSMS/VS, or uses a non-Windows machine to browse to a Windows host |
| **Both** | Different contexts on different days; the two surfaces don't share state but they don't conflict either |

The installer doesn't recommend an order. The component dependency tree is:

- Plugins → require the engine binary (already installed by installer)
- Web edition → require the engine binary + IIS-or-Kestrel-host choice
- Engine binary → always installed if anything else is selected

If the user later adds the web edition, re-running the installer with the existing components preserved adds only the new component without disturbing the plugins.
