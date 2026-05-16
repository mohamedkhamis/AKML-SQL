# Implementation Plan: AKML SQL — Local Web Edition (M0–M6)

**Branch**: `021-web-edition` | **Date**: 2026-05-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/021-web-edition/spec.md`

## Summary

Deliver a browser-based "web edition" of AKML SQL that runs entirely in the user's own browser and pairs with a local engine process. The edition reaches the user via a single new optional component in the existing Inno Setup 7 installer (M4), is independent from the SSMS/VS plugins (separate `%AppData%/AKML SQL Web/` namespace, separate engine instance), and is sequenced over seven milestones M0–M6.

**Technical approach** (consolidated from `doc/WEB/M0–M6` and the five clarifications recorded in `spec.md`):

1. **M0** refactors the engine's monolithic `PipeRpcServer` dispatcher into an `IRpcTransport` + `IRpcRequestHandler<,>` model so the same handlers serve named pipes (today), in-process calls (Blazor WASM), and WebSocket (browser ↔ local engine). Wire format and message-type integer codes are unchanged.
2. **M1** is a one-week spike that confirms `ScriptDom`, `AkmlSql.Core`, `AkmlSql.Formatting`, and `AkmlSql.Analyzer` run inside Blazor WASM, and lands a thin Blazor project scaffold.
3. **M2** ships the first user-visible web surface: a Blazor WASM standalone app with a real editor component (Monaco vs CodeMirror chosen at M2.1), full formatter pipeline running in-browser via `InProcessTransport`, full analyser rule set rendered as a problems list, theme parity with the WPF surface via a shared `theme-tokens.json` source.
4. **M3** adds the `WebSocketTransport` on the engine (localhost + LAN modes) and a browser-side `EngineConnection` service. LAN mode is **WSS only**, with an installer-generated self-signed certificate (clarification 1). Pairing uses a one-time 6-digit PIN that mints a long-lived bearer token; the token is stored in IndexedDB.
5. **M4** wires the web edition into the installer as an optional component with sub-options "Host on local IIS" / "Don't host — I'll serve files myself" and "Localhost only" / "LAN exposed". IIS detection is registry- and `appcmd.exe`-based; a lightweight fallback host stays out of scope for M4 (per M4 PRD it's "out of scope until needed" — see Open Questions).
6. **M5** extracts `CompletionEngine`, `QuickInfoEngine`, `SignatureHelpEngine`, and `DatabaseCache` into a new `AkmlSql.IntelliSense` netstandard2.0 library shared between engine and web; adds IndexedDB schema cache keyed by `(server-canonical-identity, database-name)` (clarification 3); adds offline IntelliSense, snippets, lightweight/heavyweight refactoring.
7. **M6** extracts AI prompt/provider code into a new `AkmlSql.AI` netstandard2.0 library; adds Text-to-SQL, Explain, Fix, Optimize, Index, Chat, Ghost Text directly from the browser to the AI provider (no AKML server in the path); AI keys are wrapped at rest with Web Crypto using a non-extractable wrapping key (clarification 2).

**Cross-cutting** (from the clarification session): a bridge handshake exchanges version + capability metadata; features whose required engine version is unmet are hidden/disabled inline rather than blocking the whole bridge (clarification 5). A browser-side ring-buffer diagnostic log and a one-click "Export diagnostics" action are added in M2 and extended through later phases (clarification 4).

## Technical Context

**Language/Version**:

- Engine, web app: C# / .NET 10 (existing engine + new `AkmlSql.Web` Blazor WASM standalone)
- Shared logic libraries (`AkmlSql.Core`, `AkmlSql.Formatting`, `AkmlSql.Analyzer`, new `AkmlSql.IntelliSense`, new `AkmlSql.AI`): `netstandard2.0` so they remain reusable from both the .NET Framework 4.7.2 shells and the .NET 10 Blazor WASM runtime
- Editor JS interop: JavaScript (Monaco) or modular JS (CodeMirror 6) — choice deferred to M2.1 spike
- Installer: Inno Setup 7 Pascal Script

**Primary Dependencies**:

- New: `Microsoft.AspNetCore.Components.WebAssembly` (.NET 10), `Microsoft.AspNetCore.Components.WebAssembly.Server` (only for dev), Monaco Editor (~2 MB) OR `@codemirror/lang-sql` (~500 KB), `System.Net.WebSockets` for the new transport, browser-native Web Crypto API (no external lib)
- Reused: existing `AkmlSql.Core`, `AkmlSql.Formatting`, `AkmlSql.Analyzer`, MessagePack-CSharp, Serilog 4.x (engine), Microsoft.SqlServer.TransactSql.ScriptDom

**Storage**:

- **Browser** (IndexedDB): schema cache entries keyed by `(server-canonical-identity, database-name)`; user profile imports; per-rule analysis-settings overrides; last-edited document; theme preference; engine connection records (host, port, isLocalhost, lastConnected); bearer tokens; **wrapped AI provider keys**; ring-buffer diagnostic log
- **Browser** (memory only): non-wrapped AI key material during a session; current editor session (caret, undo stack)
- **Engine host** (filesystem): existing `%AppData%/AKML SQL/...` for the IDE plugin engine; **new** `%AppData%/AKML SQL Web/...` for the web edition's engine instance (config, logs, cache). The two engine processes never share state.
- **IIS** (or fallback): static files for the WASM bundle deployed under `%ProgramFiles%/AKML SQL/Web/` (M4)

**Testing**:

- `xunit` for .NET (existing) — new test projects `AkmlSql.Web.Tests` (bUnit for Blazor components), `AkmlSql.IntelliSense.Tests`, `AkmlSql.AI.Tests`
- `Playwright` (or Selenium) for end-to-end browser tests — new test project `AkmlSql.Web.E2E.Tests`
- Existing `AkmlSql.E2E.Tests` extended with bridge-handshake coverage
- Parity corpus: golden formatted-SQL files and golden analysis-finding sets reused from `tests/format-parity/`

**Target Platform**:

- Web edition runtime: any modern evergreen browser (current Chrome / Edge / Firefox / Safari) on any desktop OS
- Engine: Windows x64 (.NET 10 self-contained, trimmed) — unchanged
- Installer: Windows 10/11

**Project Type**: Web application (Blazor WASM frontend + .NET 10 engine backend on Windows) + desktop installer

**Performance Goals**:

- SC-002: 10 MB document formats without UI freeze; analysis renders results in seconds for typical-complexity scripts
- SC-005: completions appear within the same time budget the IDE plugin meets for the same DB and caret
- Cold-load budget for WASM bundle: target ≤ 5 s on a typical broadband connection (final target locked at M1)
- `CompletionRequest` p50/p99 within 5 % of pre-M0 baseline (M0 success metric)
- Schema Phase A < 500 ms (engine-side, unchanged); browser-side cache hit < 50 ms

**Constraints**:

- SC-003: byte-identical formatted output vs IDE plugin for the same input and profile
- SC-004: identical analysis findings (rule IDs, severities, messages, line/column) vs IDE plugin
- SC-007: installing/uninstalling the web component must not modify any IDE plugin state
- SC-008: offline IntelliSense survives engine unreachable for at least one working day
- SC-009: no AKML server in the AI request path
- 10 MB max document size (existing engine-side `MaxDocumentSizeChars`)
- Browser storage quota: respect browser-managed limit; LRU eviction; degrade gracefully when quota exhausted
- Wire format unchanged from existing named-pipe transport (`[length][CRC][MessagePack(RpcMessage)]`)

**Scale/Scope**:

- Single user per install; multiple browser tabs OK; single engine process per install
- 7 milestones (M0–M6), 16–20 weeks single-developer effort
- ~33 functional requirements, 10 success criteria, 5 user stories
- Net new projects: `AkmlSql.Web` (Blazor WASM), `AkmlSql.Web.Shared` (web utilities), `AkmlSql.IntelliSense` (extracted shared), `AkmlSql.AI` (extracted shared) plus matching test projects

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` does not exist in this repository. The gate is therefore advisory only.

Applying common engineering gates by inspection:

| Gate | Result | Notes |
|------|--------|-------|
| New tech only where the spec requires it | **PASS** | Blazor WASM is the only major new runtime; chosen because it lets the formatter/analyser libraries (already `netstandard2.0`) run in the browser unchanged. |
| Shared logic in libraries, transport adapters at the edges | **PASS** | M0 establishes the pattern (`IRpcTransport`); M5/M6 repeat it (`AkmlSql.IntelliSense`, `AkmlSql.AI`). The web edition does not duplicate logic. |
| Wire-compatible with the existing IDE plugin engine | **PASS** | Frame format and message-type integer codes are unchanged; only new transports are added. |
| Independence from IDE plugins | **PASS** | Separate `%AppData%/AKML SQL Web/`, separate engine instance, no shared lock files, installer component is optional. |
| No new telemetry surface | **PASS** | Spec explicitly forbids it; diagnostics export is local and user-initiated only. |
| Tests cover the new surface | **GATED ON IMPLEMENTATION** | New test projects must land alongside source; parity corpus drives SC-003/SC-004. |

No violations to track in **Complexity Tracking**.

## Project Structure

### Documentation (this feature)

```text
specs/021-web-edition/
├── plan.md                                 # this file
├── spec.md                                 # produced by /speckit.specify (clarified)
├── checklists/
│   └── requirements.md                     # produced by /speckit.specify
├── research.md                             # Phase 0 output (this command)
├── data-model.md                           # Phase 1 output (this command)
├── quickstart.md                           # Phase 1 output (this command)
├── contracts/
│   ├── rpc-handshake.md                    # bridge handshake message contract
│   ├── rpc-transport-abstraction.md        # IRpcTransport / IRpcRequestHandler<,>
│   ├── pairing-flow.md                     # PIN → bearer token protocol (M3)
│   ├── schema-cache-shape.md               # IndexedDB layout + cache key (M5)
│   ├── ai-key-wrapping.md                  # Web Crypto wrap/unwrap contract (M6)
│   └── installer-component.md              # installer component + Inno Setup pages (M4)
└── tasks.md                                # produced by /speckit.tasks (next command)
```

### Source Code (repository root)

The web edition extends the existing single-tree layout rather than introducing a new top-level structure. New projects sit alongside the existing ones under `src/` and `tests/`; the engine, shell-shared project, and installer are extended in place.

```text
src/
├── AkmlSql.Core/                         # netstandard2.0 + net10.0 (unchanged)
├── AkmlSql.Formatting/                   # netstandard2.0 (unchanged; runs in WASM)
├── AkmlSql.Analyzer/                     # netstandard2.0 (unchanged; runs in WASM)
├── AkmlSql.IntelliSense/                 # NEW (M5) — netstandard2.0
│   ├── CompletionEngine.cs                       # moved from AkmlSql.Engine
│   ├── QuickInfoEngine.cs
│   ├── SignatureHelpEngine.cs
│   ├── DatabaseCache.cs                          # shape-compatible with engine version
│   └── AkmlSql.IntelliSense.csproj
├── AkmlSql.AI/                           # NEW (M6) — netstandard2.0
│   ├── Prompts/                                  # template files
│   ├── Providers/                                # Claude / OpenAI / Gemini / Azure / Ollama / LM Studio
│   ├── PromptBuilder.cs
│   ├── PrivacyMode.cs
│   └── AkmlSql.AI.csproj
├── AkmlSql.Engine/                       # extended (M0)
│   ├── Transports/                                       # NEW folder
│   │   ├── IRpcTransport.cs
│   │   ├── NamedPipeTransport.cs                          # renamed from PipeRpcServer
│   │   ├── InProcessTransport.cs                          # NEW (M0)
│   │   └── WebSocketTransport.cs                          # NEW (M3) — localhost + LAN binding
│   ├── Handlers/                                          # NEW folder; one class per message type
│   │   ├── Completion/ Formatting/ Analysis/ ...
│   │   └── Ai/  (AiHandlerBase + concrete handlers, M0.5)
│   ├── RpcRouter.cs                                       # NEW (M0)
│   ├── RpcContext.cs                                      # NEW (M0)
│   ├── Pairing/                                           # NEW (M3)
│   │   ├── PairingPin.cs
│   │   ├── BearerTokenStore.cs                            # %AppData%/AKML SQL Web/tokens.json
│   │   └── PairingService.cs
│   └── HandshakeMetadata.cs                               # NEW — version + capability advertisement
├── AkmlSql.Web/                          # NEW (M1 skeleton, M2 fills in)
│   ├── Pages/
│   │   ├── Editor.razor
│   │   ├── Settings.razor
│   │   └── About.razor
│   ├── Shared/
│   │   ├── MainLayout.razor
│   │   ├── EditorComponent.razor              # Monaco/CodeMirror wrapper
│   │   ├── ProblemsListComponent.razor
│   │   └── ProfilePickerComponent.razor
│   ├── Services/
│   │   ├── FormatterService.cs                # InProcessTransport adapter
│   │   ├── AnalyserService.cs
│   │   ├── ConnectionStore.cs                 # M3 — engine connections
│   │   ├── EngineConnection.cs                # M3 — WebSocket client + handshake
│   │   ├── SchemaCacheStore.cs                # M5 — IndexedDB-backed
│   │   ├── DiagnosticsRingBuffer.cs           # M2 — ring-buffer log
│   │   ├── AiKeyVault.cs                      # M6 — Web Crypto wrap/unwrap
│   │   └── AiClientFactory.cs                 # M6 — provider client routing
│   ├── wwwroot/
│   │   ├── js/editor-interop.js
│   │   └── css/themes/{light,dark,high-contrast}.css   # generated from theme-tokens.json
│   ├── Program.cs
│   └── AkmlSql.Web.csproj
├── AkmlSql.Web.Shared/                   # NEW (M2) — Blazor-side DTOs + IndexedDB abstractions
├── AkmlSql.Shell.Shared/                 # unchanged
├── AkmlSql.Ssms20/ AkmlSql.Ssms21/ AkmlSql.Ssms22/
├── AkmlSql.VS2019/ AkmlSql.VS2022/ AkmlSql.VS2026/    # all unchanged
├── AkmlSql.Updater/                      # unchanged
└── AkmlSql.Installer/                    # extended (M4)
    ├── AkmlSqlSetup.iss                          # add "Web edition" component
    ├── web-installer.iss                         # NEW (M4) — IIS detection, MIME types, firewall, cert
    └── environment-scanner.iss                   # unchanged

tests/
├── AkmlSql.Core.Tests/                   # unchanged
├── AkmlSql.Engine.Tests/                 # extended (M0) — in-process handler tests + handshake
├── AkmlSql.Formatting.Tests/             # unchanged
├── AkmlSql.IntelliSense.Tests/           # NEW (M5)
├── AkmlSql.AI.Tests/                     # NEW (M6) — prompt-builder + provider client unit tests
├── AkmlSql.Web.Tests/                    # NEW (M1/M2) — bUnit component tests
├── AkmlSql.Web.E2E.Tests/                # NEW (M2 onward) — Playwright/Selenium
├── AkmlSql.Installer.Tests/              # extended (M4)
├── AkmlSql.Shell.Shared.Tests/           # unchanged
├── AkmlSql.E2E.Tests/                    # extended (M3) — bridge handshake coverage
└── format-parity/                        # parity corpus — extended with web-edition runner

doc/
├── architecture.md                       # updated by M0 (transports) and M5/M6 (shared libs)
├── ipc-api.md                            # updated by M0 (transport plurality), M3 (handshake), M5
└── WEB/                                  # PRDs that drive this plan (unchanged content)
    └── M0..M6 ...
```

**Structure Decision**: Extend the existing single-tree layout. Reasoning: (1) the web edition shares the engine, formatter, and analyser libraries with the IDE plugins, so a separate repository or top-level split would force cross-repo coordination for every shared change; (2) the existing solution already mixes .NET Framework 4.7.2 shell projects with .NET 10 engine and updater projects, so a Blazor WASM `.NET 10` project fits the precedent; (3) the installer is a single Inno Setup project that gains a component, not a second installer.

## Complexity Tracking

No constitution violations to justify. Notable complexity choices (recorded for visibility, not as deviations):

| Choice | Reason | Simpler alternative rejected because |
|--------|--------|---------------------------------------|
| Two engine processes (plugin + web) on the same host | Spec-mandated independence (FR-003, SC-007) | One shared engine would couple plugin and web release cycles and re-open the `%AppData%` config-sharing failure mode that the spec explicitly avoids |
| Extracting `AkmlSql.IntelliSense` and `AkmlSql.AI` as new `netstandard2.0` libraries (M5/M6) | Same logic must run in browser WASM **and** the .NET 10 engine | Keeping the code in `AkmlSql.Engine` would force a duplicate WASM-only port; long-term maintenance cost grows with every rule/prompt change |
| LAN mode WSS with installer-generated self-signed cert | Clarification 1 — browser mixed-content blocks would refuse plaintext WebSocket from an HTTPS-loaded page; pairing token leaks in plaintext are unacceptable | Plaintext ws:// rejected at clarification time |
| Web Crypto wrapping of AI keys (non-extractable wrapping key) | Clarification 2 — meaningful protection against malicious extensions / co-resident origins with zero ergonomic cost | Plain storage rejected; per-session passphrase added friction users would work around |
