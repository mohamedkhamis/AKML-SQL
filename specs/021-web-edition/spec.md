# Feature Specification: AKML SQL — Local Web Edition (M0–M6)

**Feature Branch**: `021-web-edition`
**Created**: 2026-05-16
**Status**: Draft
**Input**: User description: "based on @doc/WEB/00-INDEX.md — AKML SQL Web Edition M0 through M6: browser-based local web edition installable to IIS, usable on localhost or LAN, fully independent from the IDE plugins. Covers engine transport abstraction, Blazor WASM formatter and analyser, WebSocket bridge for live schema, IIS installer option, IndexedDB schema cache for offline IntelliSense, and BYO-key AI assistance in the browser."

---

## Overview

AKML SQL is currently only reachable from inside SSMS 20/21/22 or Visual Studio 2019/2022/2026. This feature delivers a browser-based "web edition" that runs on the user's own machine (or a LAN-reachable host) and exposes the same formatting, analysis, IntelliSense and AI capabilities without requiring the IDE plugins. It is explicitly **a local web edition** — multi-tenant SaaS is out of scope for this spec.

The work is sequenced as seven milestones (M0–M6), staged so the first usable browser surface ships at M2 and each subsequent milestone adds independently shippable user value. M0 and M1 are foundational (no user-visible change); M2–M6 each deliver a slice of value that a real user would notice.

---

## Clarifications

### Session 2026-05-16

- Q: For LAN-mode installs, what is the required transport security between browser and engine? → A: LAN mode requires TLS; installer generates and installs a self-signed cert and prints trust-it instructions. Localhost mode stays plaintext.
- Q: How should AI provider keys be persisted in the browser? → A: Wrapped at rest using browser-native Web Crypto with a non-extractable wrapping key; transparent to the user.
- Q: What is the cache key for a "cached database" entry? → A: Server's canonical reported identity + database name; survives DNS/alias/IP variation; same SQL Server = same cache. Browser-profile isolation provides per-user separation.
- Q: What diagnostic / log surface does the web edition expose to the user? → A: Settings page has "Export diagnostics" — downloads a JSON bundle of the browser-side ring-buffer log and (if reachable) the engine's recent log file.
- Q: How should the web edition behave when its engine peer reports an incompatible version? → A: Handshake exchanges versions; features whose required engine version is unmet are hidden/disabled with an inline notice. Core editor, format, analyse continue to work.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Format and lint SQL in a browser, no engine required (Priority: P1)

A SQL developer opens the web edition in their browser, pastes (or opens) a `.sql` file, and uses formatting and static analysis exactly as they would inside SSMS — without needing to launch SSMS or VS, and without needing a database connection. This is the first usable web surface and lands at the end of M2.

**Why this priority**: This is the minimum slice that produces a usable web product. It validates the architecture end-to-end (browser runtime, editor component, formatter pipeline, analysis rules, theme system) without depending on a live database, the engine process, or installer plumbing. If only this slice ships, the web edition is already independently useful as an offline SQL formatter and linter.

**Independent Test**: Open the web edition URL in a modern browser on any OS, paste a SQL script, click Format and click Analyse. Verify the formatted SQL matches what SSMS would produce with the same profile, and the problems list shows the same diagnostics the IDE plugin would show. No engine process needs to be running.

**Acceptance Scenarios**:

1. **Given** the web edition is loaded in a browser and no engine is running, **When** the user pastes a SQL script and clicks Format, **Then** the script is formatted using the active profile and the result is displayed in the editor with no error.
2. **Given** the web edition is loaded in a browser, **When** the user clicks Analyse on a script that violates several analysis rules, **Then** a problems list appears with one entry per finding, each entry shows rule ID, severity, message, and line/column, and clicking an entry jumps the editor caret to that location.
3. **Given** the user has an existing `.akmlstyle` or `.sqlpromptstylev2` file from their IDE plugin, **When** the user imports it via the settings page, **Then** the web edition formats using that profile and produces output equivalent to the plugin output for the same input SQL.
4. **Given** the browser tab is closed and reopened, **When** the user returns to the web edition, **Then** the most-recent profile selection and the last edited document are restored from browser storage.

---

### User Story 2 - Live, schema-aware IntelliSense via a local engine (Priority: P2)

A SQL developer working in the web edition wants completions, signature help, and goto-definition that reflect real schema from their SQL Server instance — not a static keyword list. The web edition pairs with a local AKML SQL engine running on the same machine (or another machine on the LAN) and uses it as the source of truth for schema. This lands at the end of M3.

**Why this priority**: P1 already delivers value standalone, but live IntelliSense is the single biggest differentiator for SQL Server developers. M3 brings the web edition to feature parity with the IDE plugin for schema-aware editing.

**Independent Test**: With the engine running locally, configure the browser to pair with it using the pairing token shown by the engine. Open the web edition, type a query against a real database, and verify completions list real schema names, signature help shows real parameters, and goto-definition opens the object definition.

**Acceptance Scenarios**:

1. **Given** a running local engine and a paired browser session, **When** the user starts typing a table name in the editor, **Then** the completion list shows real tables/views/functions from the connected database, ordered the same way as in SSMS.
2. **Given** a running local engine and a paired browser session, **When** the user invokes signature help on a stored procedure call, **Then** the parameter list, types, defaults, and descriptions appear.
3. **Given** an engine is **not** running, **When** the user opens the web edition, **Then** the editor still works for formatting and analysis (P1 functionality) and the schema-dependent features show a clear, dismissable banner explaining that the engine bridge is offline and how to start it.
4. **Given** the install was configured for LAN access, **When** an un-paired browser on the same LAN attempts to connect, **Then** the engine rejects the connection until the user enters the pairing token shown by the installer / engine UI.

---

### User Story 3 - One-click deploy to local IIS (or fallback host) (Priority: P3)

A user running the AKML SQL installer wants to add the web edition as a component, choose whether it should be reachable on localhost only or on the LAN, and have the installer take care of hosting. After install, the web edition is reachable at a known URL with no further setup. This lands at M4.

**Why this priority**: The web edition is usable without IIS — the user could open the bundle from any static host or even `file://` — but the install experience is what turns "demo" into "product" for a Windows-host audience.

**Independent Test**: Run the installer, check the "Web edition" component, choose localhost vs LAN binding, and confirm. After install, browse to the printed URL on the local machine and (if LAN mode) on a second machine on the same network. Verify the web edition loads, P1 functionality works, and (if the engine is running) P2 functionality works.

**Acceptance Scenarios**:

1. **Given** IIS is installed on the machine, **When** the user selects the web edition component and confirms, **Then** the installer deploys the web edition to an IIS site bound to the user's chosen URL and prints that URL on the success page.
2. **Given** IIS is **not** installed, **When** the user selects the web edition component, **Then** the installer offers a lightweight fallback host that runs as a Windows service and prints the URL.
3. **Given** the user chose LAN mode at install time, **When** the install completes, **Then** the installer prints both the LAN URL and the pairing token, and writes them to a copyable summary file under the install directory.
4. **Given** the user is re-running the installer to add the web edition to an existing install of the IDE plugins, **When** the user checks the web edition component, **Then** the plugins remain installed and the new component is added without disturbing existing configuration or engine state.

---

### User Story 4 - Offline IntelliSense from a cached schema (Priority: P4)

A user who previously connected the web edition to a database wants completions to keep working even when the engine is not currently reachable — e.g. when away from the network where the SQL Server lives, or when the engine process is not running. The web edition caches schema in browser storage and serves IntelliSense from the cache when the live bridge is offline. This lands at M5.

**Why this priority**: This adds resilience and a "works on the train" experience. It is strictly an enhancement on top of P2 and is not blocking for the core web edition.

**Independent Test**: Connect the browser to a real engine, browse a database so its schema is cached, then stop the engine. Continue typing queries in the editor and verify completions still suggest objects from that database, with a visible indicator that the data is from cache.

**Acceptance Scenarios**:

1. **Given** the user has previously paired with a live engine and worked against a database, **When** the engine becomes unreachable and the user keeps typing, **Then** completions continue to be served from the cached schema and a "Cached schema" indicator is visible in the status area.
2. **Given** a cached database, **When** the engine becomes reachable again, **Then** the web edition refreshes the cache in the background and silently switches indicators back to "Live schema" once the refresh completes.
3. **Given** the user has cached multiple databases, **When** browser storage approaches its quota, **Then** the least-recently-used cached database is evicted and the user is informed via a non-blocking notice the next time it would have been used.
4. **Given** the user wants to clear cached schema for privacy, **When** the user opens settings and clicks "Clear schema cache", **Then** all cached databases are removed and a confirmation is shown.

---

### User Story 5 - AI assistance in the browser with the user's own provider key (Priority: P5)

A user wants to use Text-to-SQL, Explain, Fix, and Optimize features in the browser the same way they would in the IDE plugin, paying for usage with their own AI provider API key. The key is stored locally; no AKML-operated AI proxy is in scope. This lands at M6.

**Why this priority**: This is the final user-facing milestone in the roadmap, valuable but explicitly optional — many users will rely entirely on formatting/analysis/IntelliSense.

**Independent Test**: In the web edition's settings, enter an AI provider API key. Select some SQL in the editor and invoke Explain, Fix, and Optimize. Verify each returns a useful response, that requests go directly from the browser to the provider (not via an AKML server), and that the key is not transmitted to AKML.

**Acceptance Scenarios**:

1. **Given** the user has entered a valid provider key in settings, **When** the user selects SQL and invokes Explain, **Then** the explanation is rendered next to the editor within the provider's typical response window for that prompt size.
2. **Given** the user has entered a valid provider key, **When** the user invokes Text-to-SQL with a natural-language prompt, **Then** a SQL suggestion is returned and the user can accept it into the editor with one click.
3. **Given** the user has **not** entered a provider key, **When** the user invokes any AI feature, **Then** the UI prompts them to enter a key and links to the relevant provider documentation, with no request sent.
4. **Given** the user wants to remove their key, **When** the user clicks "Remove key" in settings, **Then** the key is erased from browser storage and any further AI invocation reprompts for a key.

---

### Edge Cases

- **Two engines coexist** — A user has both the IDE plugins and the web edition installed. Each runs its own engine process under a separate config directory; the two surfaces do not share state but also must not conflict (port collisions, pipe-name collisions, lock files).
- **No SQL Server reachable** — User opens the web edition and the engine has no database connection. Schema-dependent UI must show a clear empty state, not a stack trace or spinner that runs forever.
- **Large SQL document** — User pastes a multi-megabyte script. The web edition must surface a clear error or warning at the existing document size limit (10 MB per session) rather than freezing the browser tab.
- **Browser storage quota exceeded** — Schema cache fills available browser storage quota. The system must evict gracefully, not throw, and must keep formatter/analyser functionality working even when the schema cache is fully evicted.
- **LAN browser disconnects mid-session** — Network drops between a paired browser and a remote engine. The browser must transition cleanly into "engine offline" state, preserve in-flight editor state, and reconnect automatically when the network returns.
- **Pairing token leaks on a LAN** — A user shares the pairing token by mistake. The user must be able to regenerate the token and invalidate previously-paired browsers from the engine side.
- **AI provider rate-limit / 401** — AI request fails with a 401 (bad key) or 429 (rate limit). The UI shows a friendly, actionable error with the relevant provider documentation link.
- **Re-install over existing install** — User re-runs the installer to add or remove the web edition component. Existing config, profiles, and the engine state for the IDE plugins must survive untouched.
- **Mixed origin** — Browser on a Mac connects to a Windows host running the engine. The web edition must work without requiring any non-evergreen browser feature and without requiring the browser's host OS to share an authentication realm with the engine host.

---

## Requirements *(mandatory)*

### Functional Requirements

#### General

- **FR-001**: The web edition MUST run in any modern evergreen browser (current Chrome, Edge, Firefox, Safari) on any desktop operating system.
- **FR-002**: The web edition MUST be installable as a component of the existing AKML SQL installer, alongside (and independently from) the SSMS and VS plugins.
- **FR-003**: The web edition MUST be runnable without the IDE plugins, and the IDE plugins MUST remain runnable without the web edition. The two surfaces MUST NOT share configuration, history, or engine state.
- **FR-004**: The web edition MUST present the same product identity (name, version, licence, branding) as the IDE plugin surfaces.
- **FR-005**: The web edition MUST honour user OS theme preference (light / dark) by default and offer an explicit override, matching the established AKML SQL theme tokens.
- **FR-005a**: The web edition MUST maintain an in-browser ring-buffer diagnostic log capturing format/analyse/IntelliSense/AI events and bridge-connection state changes, and MUST expose an "Export diagnostics" action from the settings screen. The exported artefact MUST be a single downloadable bundle containing the browser ring-buffer log and — when the engine bridge is reachable — the engine's most recent log file. No diagnostic content MAY be transmitted off the user's machine without an explicit user action.

#### Formatter & analyser (P1, M2)

- **FR-006**: Users MUST be able to paste, type, or open a SQL file in the editor without an engine running.
- **FR-007**: The web edition MUST format the editor content using the active profile and produce output equivalent to the IDE plugin's output for the same input and profile.
- **FR-008**: The web edition MUST run the full AKML SQL analysis rule set against the editor content and render results as a problems list with rule ID, severity, message, file/line/column, and click-to-jump-to-location.
- **FR-009**: Users MUST be able to import and export `.akmlstyle` and `.sqlpromptstylev2` profile files from/to their local machine via the browser's file dialog.
- **FR-010**: The web edition MUST persist the user's profile selection, analysis settings, and most-recently-edited document content in browser storage across reload.
- **FR-011**: The web edition MUST display a clear error message and stop processing when the editor content exceeds the established 10 MB per-document size limit.

#### Live engine bridge (P2, M3)

- **FR-012**: The web edition MUST be able to connect to a local AKML SQL engine via a defined network transport on a configurable port.
- **FR-013**: When configured for LAN access, the engine MUST require a pairing-token authentication step before serving any browser request, and the pairing token MUST be visible to the user from both the installer success page and a re-display action in the local engine UI.
- **FR-013a**: When configured for LAN access, all browser ↔ engine traffic MUST be TLS-encrypted. The installer MUST generate a self-signed certificate for the chosen LAN binding, install it as the bridge's server certificate, and present the user with copyable instructions to trust the certificate on each browser host. Localhost-only installs MAY use plaintext transport.
- **FR-014**: Users MUST be able to regenerate the pairing token at any time, and regenerating it MUST invalidate previously-paired browsers immediately.
- **FR-015**: When the live engine bridge is connected, completions, signature help, goto-definition, and other schema-aware features MUST return data from the live database that matches what the IDE plugin would return.
- **FR-016**: When the live engine bridge is disconnected, the web edition MUST continue to provide P1 (formatter/analyser) functionality and show a clear, dismissable status indicator that the bridge is offline with instructions to start the engine.
- **FR-017**: A dropped network connection between browser and engine MUST be detected within a few seconds and trigger automatic reconnection attempts with exponential backoff; the editor state MUST be preserved across the disconnect.
- **FR-017a**: On every bridge handshake the browser and engine MUST exchange version and capability metadata. When the engine's reported version does not meet the minimum required for a feature, that feature MUST be hidden or disabled with an inline, dismissable notice naming the affected feature and the action the user must take; all unaffected features (including the entire P1 editor / format / analyse surface) MUST continue to work. A version mismatch MUST NOT produce a full-page blocker, and MUST NOT prevent the browser from talking to the engine for features whose required version is met.

#### Installer integration (P3, M4)

- **FR-018**: The installer MUST present "Web edition" as a selectable optional component with no required order relative to the IDE plugin components.
- **FR-019**: At install time, the user MUST be asked whether the web edition should be reachable only on localhost or also on the LAN, and the default MUST be localhost-only.
- **FR-020**: When IIS is installed on the machine, the installer MUST be able to deploy the web edition as an IIS site at a user-chosen binding.
- **FR-021**: When IIS is not installed, the installer MUST offer a lightweight built-in host as a fallback that runs as a Windows service and serves the web edition at the same URL the IIS deployment would have used.
- **FR-022**: The installer MUST print the final web edition URL and (if LAN mode) the pairing token on its success page, and MUST also write a copyable summary file in a known location under the install directory.
- **FR-023**: Re-running the installer to add or remove the web edition component MUST NOT modify, delete, or reset configuration owned by the IDE plugins or by an already-installed web edition.

#### Schema cache & offline IntelliSense (P4, M5)

- **FR-024**: The web edition MUST cache schema metadata for visited databases in browser storage so it survives reload.
- **FR-025**: When the live engine bridge is offline, the web edition MUST serve IntelliSense from cached schema and display an indicator showing the data is from cache and the timestamp of the cache.
- **FR-026**: When the live engine bridge comes back online, the web edition MUST refresh the cache in the background and switch the indicator back to "Live" once the refresh succeeds.
- **FR-027**: When browser storage approaches its quota, the web edition MUST evict the least-recently-used cached database and inform the user once, non-modally.
- **FR-028**: Users MUST be able to view a list of cached databases and clear the cache (one database, or all) from a settings screen.

#### AI assistance (P5, M6)

- **FR-029**: Users MUST be able to enter, view (masked), and remove AI provider keys in settings, with keys stored only in browser storage on the user's own machine. Keys MUST be wrapped at rest using browser-native cryptography backed by a non-extractable wrapping key (no plaintext on disk, no extra passphrase prompt to the user), and MUST be unrecoverable to anyone who has not paired the same browser session.
- **FR-030**: AI requests MUST go directly from the browser to the chosen AI provider; no AKML-operated server may receive or proxy these requests or see the user's key.
- **FR-031**: Users MUST be able to invoke Text-to-SQL, Explain, Fix, and Optimize from the editor on either the entire document or a selection.
- **FR-032**: When no provider key is configured, invoking an AI feature MUST prompt the user to add a key with a clear pointer to the relevant provider's docs, rather than failing silently or sending an unauthenticated request.
- **FR-033**: AI provider errors (invalid key, rate limit, network failure, content policy) MUST be surfaced to the user with provider-specific, actionable text and a link to the provider's documentation.

### Key Entities *(include if feature involves data)*

- **Web edition install** — A self-contained deployment of the browser app + (optional) static host configuration + (optional) lightweight fallback service. Keyed by its install directory; carries its own configuration namespace separate from the IDE plugin install.
- **Formatting profile** — A user-owned style preset (e.g. `.akmlstyle`, `.sqlpromptstylev2`) imported into the browser and persisted in browser storage. May be exported back as a file.
- **Analysis settings** — Per-rule severity overrides and toggles. Equivalent semantics to the existing `.casettings` file used by the IDE plugin.
- **Editor session** — The currently open SQL document text, caret/selection, undo stack, and most-recent analysis/format result. Persisted to browser storage for restore-on-reload.
- **Engine pairing** — The relationship between a browser tab and a running local engine: pairing token, engine endpoint, capability flags, last-known-online timestamp. Created at first connect, retired when the user "unpairs" or regenerates the token.
- **Schema cache entry** — Per-database snapshot of objects, columns, foreign keys, parameters, and modified-time markers used to drive offline IntelliSense. Identified by the tuple *(server's canonical reported identity, database name)* so that DNS aliasing, IP-vs-FQDN access, and re-pairing with a different engine that points at the same SQL Server all resolve to a single cache entry. Carries an LRU access timestamp. Per-user separation is provided by browser-profile isolation, not by the cache key.
- **AI provider configuration** — Provider identifier, API key (stored locally), optional model selection and request defaults. One per provider; user may configure multiple providers but only one is "active" at a time per AI feature.
- **Install summary file** — A plain-text file written by the installer on the user's disk listing the final URL, mode (localhost / LAN), and (if LAN) the pairing token. Reproducible from the engine UI at any time.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with no prior AKML SQL experience can install the web edition and reach the editor in their browser, ready to type SQL, in under 5 minutes from launching the installer.
- **SC-002**: A `.sql` document up to 10 MB pasted into the editor formats with no perceptible UI freeze; an analysis run on the same document completes and renders the problems list within seconds for a typical-complexity script.
- **SC-003**: For an existing IDE plugin user, formatting the same SQL with the same profile in the IDE plugin and in the web edition produces identical output (verified by a parity test corpus).
- **SC-004**: For an existing IDE plugin user, running analysis on the same SQL with the same settings in the IDE plugin and in the web edition produces the same set of findings, with the same rule IDs, severities, messages, and line/column locations.
- **SC-005**: With a paired live engine, completions appear within the same time budget the IDE plugin meets for the same database and the same caret position.
- **SC-006**: When the engine is unreachable, the web edition's P1 functionality remains fully usable with no failed requests or error states surfacing to the user other than the expected "bridge offline" indicator.
- **SC-007**: Installing the web edition alongside an existing IDE plugin install leaves the plugin install bit-for-bit unchanged (same files, same configuration, same engine state).
- **SC-008**: A user can switch off their machine, take a laptop somewhere with no network access to their SQL Server, and continue to get IntelliSense suggestions for previously-cached databases for the duration of a working day.
- **SC-009**: An AI feature invoked with a valid user-provided key returns a response within the AI provider's documented latency budget for the request size; no AKML-operated server participates in the request path.
- **SC-010**: Removing the web edition component via the installer cleanly removes its files, its host configuration (IIS site or fallback service), its persisted runtime data, and only those — no IDE plugin state is touched.

---

## Out of Scope

- **Multi-tenant SaaS / hosted offering** — Explicitly deferred to a separate planning cycle once M0–M6 ship. Web edition is local-only.
- **Admin / staff portal** — No central admin UI, no organisation accounts, no role-based access control. A single installed copy serves a single user (or a small LAN trust group).
- **AI proxy / shared AI service** — AI requests go directly from browser to provider with the user's own key. No AKML-operated AI inference, no AKML-operated key vault.
- **Mobile-first UI** — The web edition targets desktop browsers; tablet / phone form factors are not optimised for in this scope.
- **Authentication beyond pairing token** — No SSO, no LDAP, no Azure AD integration for the web edition in this scope. LAN security is established via the pairing-token model.
- **New SQL Server features outside parity** — The web edition aims for parity with the IDE plugin surface; net-new SQL language features (e.g. new dialect support) are not part of this scope.

## Assumptions

The PRD makes a number of decisions that this spec inherits as assumptions rather than open questions:

- **Single-user model.** The web edition is intended for a single user (possibly across multiple browser tabs or machines on a small LAN they control). It does not need to gate concurrent connections or arbitrate edits.
- **Same licence as the IDE plugins** (MIT). The web edition is part of the same product, free, and source-available under the existing repository licence.
- **Pairing token is static for the lifetime of the install** unless the user regenerates it. No automatic rotation in scope.
- **Each install owns its engine.** A machine with both IDE plugins and the web edition runs two engine processes. This is deliberate to keep state separation explicit.
- **Engine binary is unchanged**; only its transport layer gains a new option. Existing plugin behaviour is untouched.
- **Browser storage quota.** The schema cache uses browser-managed storage and respects browser-imposed quotas; eviction is LRU.
- **AI providers covered in M6** are the same providers already supported by the IDE plugin AI features. The web edition does not introduce new providers as part of this scope.
- **Maximum SQL document size is 10 MB**, matching the existing engine-side per-document limit.
- **No telemetry beyond what the IDE plugin already does.** The web edition does not introduce new telemetry surfaces.
- **Installer surface area is the existing AKML SQL installer.** A new "Web edition" component is added; no second installer is introduced.

## Dependencies

- **AKML SQL engine binary** must continue to ship as the same self-contained Windows executable the IDE plugins use today; the web edition's live-schema features depend on it being installable and runnable alongside the plugins.
- **Existing AKML SQL installer** is the integration point for the web edition install component; no separate installer is introduced.
- **Existing formatter, analyser, and core libraries** must remain reusable from a portable runtime so they can power both the IDE plugins and the browser surface without divergence.
- **A modern evergreen browser** is required on the user's machine; no specific minimum version is enforced by this spec beyond "current major release".
- **(Optional) IIS** is required for the preferred install mode; absence triggers the lightweight fallback host.

## Definition of Done

This spec is considered done when, for every functional requirement above, an automated or manual test exists and passes, and every success criterion has been measured against the shipped product. The spec is not phase-gated — individual milestones (M0–M6) carry their own scoped completion criteria in `doc/WEB/M*-*.md`.
