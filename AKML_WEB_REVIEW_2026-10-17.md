# AKML Web code, UI/UX, and security review

Reviewed **2026-10-17 (Africa/Cairo)** against **`c70a3dde`**. This is an implementation backlog for a fixing agent, not a claim that every possible bug has been found.

**Result: 22 actionable findings: 8 P1 and 14 P2.** Fix connection/write targeting, transport security, secret storage, and asynchronous data loss first. No application code was changed during this review.

## Read this before fixing

Read `AGENTS.md`, `PROJECT_MEMORY.md`, `CLAUDE.md`, and `.specify/memory/constitution.md`. Recheck the current source and tests before implementing each item. Source line numbers below refer to the reviewed checkout.

- Preserve unrelated edits. Do not stage, commit, push, create branches/PRs, deploy, or change the installed service without the required user authorization.
- Reuse the existing dialog, notification, theme, connection, and RPC systems. Do not edit generated theme CSS.
- Keep this report outside `doc/`: that directory feeds the public product documentation.
- Treat SQL examples below as tests for disposable databases and isolated engines. Do not run write reproductions against the user's databases.
- **P1:** high priority because of data loss, wrong-target writes, credential exposure, or serious confidentiality risk. **P2:** normal-priority correctness, reliability, accessibility, or misleading interaction. These are engineering priorities, not CVSS scores.
- **Browser-confirmed** means a fresh, isolated Chromium context reproduced the behavior. **Code-confirmed** means the relevant caller and callee were traced; the proposed end-to-end reproduction still needs an isolated regression test. **Test-confirmed** means the existing automated run exposed it.

## Scope and verification

Reviewed the web editor, results grid and Apply path, connection/pairing flows, schema/AI context, AI streaming and key storage, History, Snippets, Format styles, Settings, relevant engine execution/transport code, and the IIS hosting setup. Consulted current specifications 041/042, the latest progress entries, and the older contracts referenced by these components. The separate public `AkmlSql.Site` and the SSMS UI were not audited in depth.

| Check | Result |
| --- | --- |
| Web .NET tests, rebuilt Debug | **1,514 passed; 42 failed; 0 skipped**, total 1,556 |
| Web JavaScript tests | **121 passed; 0 failed** |
| Selected serial Playwright suites | **17 passed; 0 failed; 0 skipped** |
| Additional Chromium probes | Reproduced unsaved-edit loss, inaccessible import/list markup, concurrent wrapping-key corruption, blocked DB upgrade, and insecure-context Web Crypto absence |
| Visual inspection | Captured Editor, Snippets, Styles, Settings, History at 1440, 768, and 390 px; inspected representative screenshots |

Playwright suites: `SettingsPageTests`, `DocumentFlowTests`, `TabStripKeyboardTests`, `WorkspaceAccessibilityTests`. These passing tests are useful baseline coverage, not coverage of every finding below. The browser suites used their existing sandbox-engine Debug executable; this review rebuilt Web but did **not** rebuild or certify the current engine binary. The full solution build, all engine suites, native screen-reader testing, adversarial network testing, and a live remote PostgreSQL/SQL Server matrix were not performed.

The host selected SDK `11.0.100-rc.1.26425.128`; projects still target .NET 10. The web-test build emitted WebAssembly assembly-version conflict warnings (10.0.12 versus 10.0.13) and existing compiler/analyzer warnings. No dependency upgrades or baseline regeneration were performed.

Evidence:

- [.NET test results](.review-artifacts/2026-10-17/web-review.trx)
- [Browser test results](.review-artifacts/2026-10-17/browser-review.trx)
- [Layout and accessibility measurements](.review-artifacts/2026-10-17/browser-probe.json)
- [Functional and crypto probe results](.review-artifacts/2026-10-17/functional-probes.json)
- [Desktop editor](.review-artifacts/2026-10-17/editor-1440.png), [768 px editor](.review-artifacts/2026-10-17/editor-768.png), [390 px snippets](.review-artifacts/2026-10-17/snippets-390.png), [390 px styles](.review-artifacts/2026-10-17/styles-390.png)

## Prioritized index

| ID | Priority | Finding | Evidence |
| --- | --- | --- | --- |
| WEB-01 | P1 | Old results can apply edits through a different server connection | Code-confirmed |
| WEB-02 | P1 | Remote SQL Server connections always bypass certificate validation | Code-confirmed |
| WEB-03 | P1 | LAN app hosting uses HTTP although secret storage requires a secure context | Source + browser capability probe |
| WEB-04 | P1 | Concurrent first secret saves create competing wrapping keys | Browser-confirmed |
| WEB-05 | P2 | First reconnect after PIN pairing forgets the newly issued token | Code-confirmed |
| WEB-06 | P1 | Disconnect leaves the real SQL session and transaction alive | Code-confirmed |
| WEB-07 | P1 | AI can disclose schema from a different cached database | Code-confirmed |
| WEB-08 | P1 | Grid edits remain mutable while Execute/Apply is in flight | Code-confirmed |
| WEB-09 | P2 | Reload/close bypasses protection for pending grid changes | Code-confirmed |
| WEB-10 | P1 | Delayed formatting can overwrite newer document edits | Code-confirmed |
| WEB-11 | P2 | Snippet selection/refresh/navigation silently discards drafts | Browser + code |
| WEB-12 | P2 | Leaving Format styles silently discards unsaved changes | Browser-confirmed |
| WEB-13 | P2 | History and Snippets lists lack keyboard selection | Browser DOM + code |
| WEB-14 | P2 | Snippet/style import cannot be reached with the keyboard | Browser DOM + code |
| WEB-15 | P2 | Crypto DB connections prevent later IndexedDB upgrades | Browser-confirmed |
| WEB-16 | P2 | History selection can mix one entry's label with another entry's SQL/versions | Code-confirmed |
| WEB-17 | P2 | History drops filter changes during a pending load | Code-confirmed |
| WEB-18 | P2 | AI stream errors can be mistaken for successful completion | Code-confirmed |
| WEB-19 | P2 | SQL Server Connect reports success before credentials are validated | Code-confirmed |
| WEB-20 | P2 | New grid rows cannot correctly use database column defaults | Code-confirmed |
| WEB-21 | P2 | Raw AI error bodies can be persisted in diagnostics | Code-confirmed |
| WEB-22 | P2 | Formatter parity gate compares normalized output with unnormalized baselines | Test + code |

## Detailed fix tickets

### WEB-01 — Bind editable results to their original connection

**P1 · Code-confirmed · Data integrity**

**Evidence:** `src/AkmlSql.Web/Pages/Editor.razor:874` only refreshes the schema target on connection changes; it does not invalidate results. `Shared/ResultSetGrid.razor:1796` builds writes with catalog/schema/table but no originating server or connection generation. `Services/IQueryExecutionService.cs:200` stamps the *current* session on Apply. `src/AkmlSql.Engine/Execution/ApplyChangesHandler.cs:51` resolves that session's current connection string.

**Reproduce:** On disposable servers A and B, create matching database/table/key names. Fetch an editable row from A. Switch to B through the connection modal without rerunning the query. Edit the retained row and Apply. The request targets B's session with A's displayed values. An existing `BaseCatalog` does not protect against a server change.

**Fix:** Stamp results with an immutable engine/connection generation and target identity. Refuse Apply after that generation changes; freeze or clear stale editable results, with confirmation for drafts. Enforce the binding engine-side as well as in the UI.

**Acceptance:** Switch servers, engines, databases, credentials, and database kinds after fetching rows. No stale result may write to a new target. Include identical table/key names on both targets.

### WEB-02 — Stop silently trusting every remote SQL Server certificate

**P1 · Code-confirmed · Security**

**Evidence:** `Services/ISqlConnectionService.cs:697` permits remote TCP SQL-auth connections, but `BuildConnectionString` at line 750 always sets `TrustServerCertificate=true` at line 761. The local-development exception therefore applies to remote servers too; there is no user choice in the SQL Server connection form.

**Impact/reproduction:** Inspect the generated connection string for a remote SQL-auth target, then test against an isolated server with an untrusted/wrong-name certificate. The application requests certificate-validation bypass. A network attacker who can intercept that connection can impersonate the server; TLS to the browser's engine bridge does not secure this separate hop. Microsoft documents certificate-validation behavior in [SqlClient connection strings](https://learn.microsoft.com/en-gb/sql/connect/ado-net/connection-strings?view=sql-server-ver17).

**Fix:** Explicitly require encrypted, authenticated remote connections. Make any self-signed-certificate exception an explicit, clearly described connection setting; preserve deliberate local development compatibility.

**Acceptance:** A valid remote certificate succeeds; wrong-name/untrusted certificates fail by default. Persist and round-trip any explicit exception without silently enabling it for other connections.

### WEB-03 — Serve the LAN application itself over HTTPS

**P1 · Source + browser capability probe · Security and onboarding**

**Evidence:** `src/AkmlSql.Installer/web-iis-setup.ps1:95` creates an HTTP site; `web-installer.iss:722` advertises `http://<computer>/`. The TLS setup secures the bridge separately. `wwwroot/js/akml-crypto.js:57` requires `crypto.subtle.generateKey`; pairing stores the new bearer through `Vault.StoreAsync` in `Shared/ConnectionPickerComponent.razor:313`.

**Observed:** An isolated Chromium HTTP non-loopback-origin probe returned `isSecureContext=false` and `typeof crypto.subtle='undefined'`. This was a browser capability check, not a remote installed-app test. [SubtleCrypto requires a secure context](https://developer.mozilla.org/en-US/docs/Web/API/SubtleCrypto).

**Impact:** A LAN user can reach a successful engine handshake and then fail while persisting the token/API key. HTTP delivery also allows an on-path attacker to replace the application JavaScript and capture subsequently entered credentials; `wss://` does not protect the delivered page.

**Fix:** Provision trusted HTTPS for the web site as well as the bridge, update installer links/help, and detect unsupported insecure origins before asking for secrets. Never substitute plaintext storage as a workaround.

**Acceptance:** Pair, reload, reconnect, save an AI key, and make a mocked AI request from a second machine over HTTPS. HTTP non-loopback access must redirect safely or show a clear setup requirement before accepting secrets.

### WEB-04 — Make wrapping-key creation atomic

**P1 · Browser-confirmed · Stored credentials become unusable**

**Evidence:** `wwwroot/js/akml-crypto.js:52–66` checks a cached key, reads IndexedDB, generates a key, and unconditionally stores it. Neither an in-flight promise nor an atomic cross-tab create protects that sequence.

**Observed reproduction:** In a fresh browser context, call the real `wrap()` concurrently 12 times with synthetic values, then import a fresh instance of the crypto module and unwrap the results. **11 of 12 failed with `OperationError`**; only the record encrypted with the last persisted key could be read. See `functional-probes.json`.

**Fix:** Deduplicate initialization within a module and coordinate creation across tabs. Only one persisted key may win, and every writer must use that winning key. Wait for transaction completion before reporting durable storage.

**Acceptance:** Concurrent AI-key/pairing-token writes from one and multiple tabs all decrypt after reload. Existing records remain readable. Never overwrite an established wrapping key during normal initialization.

### WEB-05 — Retain the bearer returned by a PIN handshake

**P2 · Code-confirmed · Reconnection**

**Evidence:** `Services/IEngineBridge.cs:234` saves the *input* bearer into `_lastBearerToken`. First pairing passes null and returns `NewBearerToken`, which the picker persists, but the bridge never adopts it. `ReconnectLoopAsync:502` reuses the null token. Its `PinRequired` branch at lines 515–542 labels this revocation and deletes the stored token.

**Reproduce:** Pair a LAN engine with a PIN. Without reloading the page, interrupt and restore the bridge. The initial reconnect lacks the valid newly issued bearer and can erase the valid persisted pairing.

**Fix:** Adopt `NewBearerToken` after a successful handshake, or retrieve the current token from the vault when reconnecting. Distinguish missing local credentials from an actually rejected bearer.

**Acceptance:** First pairing → socket loss → automatic reconnect succeeds without another PIN and without deleting the token. Real revocation must still require re-pairing.

### WEB-06 — Disconnect must release the engine's database session

**P1 · Code-confirmed · Transactions and misleading status**

**Evidence:** `Services/ISqlConnectionService.cs:574` only clears browser fields, including transaction state, and sends no engine request. `Shared/Settings/ConnectionsSection.razor:176` immediately announces disconnection. The engine's `SessionConnectionRegistry.cs:19` evicts idle connections opportunistically on later executions after 20 minutes; it is not a disconnect operation.

**Reproduce:** On a disposable database, start a transaction and modify a test row. Disconnect in Settings. The browser forgets the transaction, while the engine still holds the physical connection/transaction and can retain locks. Reconnecting with the same session and same connection string need not dispose it (`ConnectionChangedHandler` disposes on changed credentials/connection string).

**Fix:** Add an acknowledged session-close path with explicit transaction handling. Confirm rollback when appropriate, release the connection and session resources, and only then show success. Define recovery when the engine cannot be reached.

**Acceptance:** Another session is no longer blocked after a confirmed disconnect. Reconnecting starts without the old transaction/temp/session state. Test SQL Server and PostgreSQL, including an aborted transaction.

### WEB-07 — Resolve AI schema by the active database, not global LRU order

**P1 · Code-confirmed · Confidentiality and AI correctness**

**Evidence:** `Services/IAiSchemaContextProvider.cs:100–103` takes the last item from the entire schema cache. It does not use `ISqlConnectionService` or verify server/database/kind. Its comment acknowledges this heuristic. `IAiPromptService` includes the returned schema in outbound prompts.

**Reproduce:** Cache database A, then switch to a new database B before its snapshot arrives; alternatively have another tab touch A's snapshot. With a schema-sharing privacy mode enabled, ask AI about B. The selected snapshot can still be A, including when A is the other database kind.

**Fix:** Resolve by an explicit active connection identity and dialect. Return no schema when that exact snapshot is absent. Capture the identity for the whole request so an asynchronous switch cannot mix SQL, schema, and dialect.

**Acceptance:** Two databases with unique sentinel table names, two tabs, and delayed schema loading never send the wrong sentinel. `NoSchema` must remain empty. No real schema or provider credentials are needed for the test.

### WEB-08 — Freeze or version grid drafts during Execute and Apply

**P1 · Code-confirmed · Silent edit loss / false saved state**

**Evidence:** `ResultsPaneComponent.razor:95` passes no running/read-only state to its grids. `.akml-results-stale` in `wwwroot/css/components/results-grid.css:351` only changes opacity. `ResultSetGrid.razor:1519` allows edits without an `_applying` check. Apply snapshots a request at line 1656, then bakes the *current mutable overlays* and clears them at lines 1672–1673 when the reply arrives.

**Reproduce A:** Start a slow SELECT with existing results and no pending drafts. Edit the dimmed old grid while the query runs. The initial discard check has already passed; the new results reset the newly added drafts without confirmation.

**Reproduce B:** Apply a change with a delayed response, then change the same cell again. The second value was never in the sent request, but the completion path can clear it or bake it as though committed, particularly on the SQL Server path without returned row values.

**Fix:** Disable mutation while operations own the result, or retain generation-tagged immutable edit batches and merge only acknowledged edits. Coordinate Execute, Apply, connection changes, and automatic refresh at workspace level. A CSS dimming effect is insufficient.

**Acceptance:** Delayed fake responses prove that unsent edits never disappear or appear committed. Cover keyboard/context-menu mutation, inserts/deletes, rerun, navigation, and multiple result sets.

### WEB-09 — Protect pending grid drafts on reload and tab close

**P2 · Code-confirmed · Data loss**

**Evidence:** `Pages/Editor.razor:53` uses only `OnBeforeInternalNavigation`; there is no external-navigation confirmation or `beforeunload` handler in the web source. Grid drafts exist only in component memory.

**Reproduce:** Edit a grid cell, then press browser Reload, close the tab, or navigate to another site. The in-app route guard never runs and drafts are lost. Browser back/forward behavior must also be tested separately from Blazor navigation.

**Fix:** Maintain an aggregate dirty state and use the supported external-navigation warning mechanism while unsaved drafts exist. Do not rely on asynchronous component disposal to save them during page termination.

**Acceptance:** Native browser navigation warns only while needed; cancelling preserves drafts. Successful Apply/Discard clears the warning. Test at least Chromium and Firefox.

### WEB-10 — Reject stale formatting responses before replacing the document

**P1 · Code-confirmed · Editor data loss**

**Evidence:** `Pages/Editor.razor:1416` awaits `Formatter.FormatAsync(_currentText, ...)` and then unconditionally sets the editor's entire text. There is no document-generation or revision comparison. PostgreSQL formatting in `Services/PostgresFormat.cs` crosses the engine bridge, making the delay a normal path.

**Reproduce:** Delay the PostgreSQL formatting reply. Click Format and continue typing, or start/open a different document before the reply arrives. The old formatting result can replace newer text or the new document.

**Fix:** Capture document identity and revision with live editor text. Apply a response only if both still match, or explicitly mediate the conflict. Cancel superseded requests where possible; retain a single undoable formatting operation.

**Acceptance:** Typing, New/Open, dialect switching, navigation, and two overlapping Format requests cannot apply a stale result to the current document.

### WEB-11 — Guard unsaved snippet edits

**P2 · Browser-confirmed · Data loss / UX**

**Evidence:** `Pages/Snippets.razor:199–236` replaces edit buffers during Refresh, selection, and New without dirty tracking. Kind filtering can clear selection. There is no navigation lock.

**Observed:** Create a snippet draft with `SELECT 918273 AS unsaved_marker;`, then select a built-in. The body immediately becomes the built-in text; there are **zero confirmation dialogs**.

**Fix:** Compare draft fields with the loaded baseline. Route selection, New, Refresh, filter changes, import-driven refresh, and navigation through one save/discard/cancel decision. Preserve a failed save's draft.

**Acceptance:** Every replacement path preserves the draft on Cancel and discards only on explicit consent. A successful Save clears dirty state.

### WEB-12 — Guard navigation away from unsaved Format styles

**P2 · Browser-confirmed · Data loss / UX**

**Evidence:** `Pages/Styles.razor:376` confirms switching styles, but the page has no `NavigationLock`. `DisposeAsync:804` releases resources without preserving the draft.

**Observed:** Toggle a formatting checkbox, verify Save is enabled, then click Snippets in navigation. Navigation succeeds with **zero dialogs** and the unsaved component state is lost.

**Fix:** Extend existing dirty/discard logic to internal and external navigation, including browser back/forward. Avoid separate inconsistent confirmation implementations.

**Acceptance:** Unsaved style edits survive a cancelled navigation and reload warning; a saved or explicitly discarded style navigates cleanly.

### WEB-13 — Make History and Snippets operable by keyboard

**P2 · Browser DOM + code · Accessibility**

**Evidence:** `Pages/Snippets.razor:56–59` renders clickable `<li role="option">` without focusability or keyboard handling. `Pages/History.razor:108–180` uses clickable divs for entries, groups, and versions, plus clickable spans for favorites. There is no list-level keyboard selection implementation. The browser measured snippet options with `tabIndex=-1`.

**Reproduce:** Navigate with Tab/Shift+Tab and attempt to select a snippet, choose a History entry/version, toggle its favorite, or collapse a history group without a pointer. The compare checkbox is not an equivalent way to select an entry.

**Fix:** Use native buttons where appropriate and a consistent keyboard list pattern with roving focus or `aria-activedescendant`, selected state, and visible focus. Implement Arrow/Home/End and Enter/Space behavior as appropriate. This addresses [WCAG keyboard operability](https://www.w3.org/WAI/WCAG22/Understanding/keyboard.html).

**Acceptance:** Complete these workflows using only a keyboard, with focused/selected states announced correctly by a screen reader.

### WEB-14 — Make file import controls keyboard-accessible

**P2 · Browser DOM + code · Accessibility**

**Evidence:** `Pages/Snippets.razor:36–38,161` and `Pages/Styles.razor:35–37,286` place `InputFile` inside a non-focusable label and set the file input to `display:none`. Browser inspection confirmed the hidden input and label `tabIndex=-1`.

**Fix:** Use a real focusable button to invoke the picker, or a visually hidden but accessible native file input with a proper label. Follow the already accessible Open/Settings import pattern rather than duplicating a different control.

**Acceptance:** Tab reaches both import actions; Enter/Space opens the picker; cancelling restores focus; selecting a file imports it and announces the result. Check all themes.

### WEB-15 — Close crypto database handles or handle version changes

**P2 · Browser-confirmed · Upgrade reliability**

**Evidence:** `wwwroot/js/akml-crypto.js:14–44` repeatedly opens IndexedDB directly, never closes those connections, and never handles `versionchange`. The main `akml-indexeddb.js` connection does handle upgrades, but these independent handles bypass that mechanism.

**Observed:** After wrapping secrets, requesting database version 5 in the isolated browser context triggered **`blocked`**, despite the main adapter's version-change close handler. No user database was upgraded by the probe.

**Fix:** Reuse a managed connection lifecycle or close each direct handle after the transaction finishes; handle version changes consistently. Preserve CryptoKey structured-clone storage.

**Acceptance:** Save a secret in tab A, upgrade the DB in tab B, then read the secret after upgrade. No indefinite blocked upgrade or unnecessary requirement to close all app tabs.

### WEB-16 — Keep History selection and asynchronously loaded details together

**P2 · Code-confirmed · Wrong preview/actions**

**Evidence:** `Pages/History.razor:444–451` assigns `_selected` before awaiting SQL and versions, then writes both results without checking selection identity. Previous `_previewSql` remains while the new request is pending. Open/Re-execute use the selected entry and `_previewSql` later in the same page.

**Reproduce:** Select A, then B with delayed detail responses. During loading, B's heading can coexist with A's SQL; delayed responses can also publish stale versions. Clicking Open/Re-execute in that interval risks opening/running the wrong displayed query.

**Fix:** Clear or mark detail content busy immediately, disable actions until the selected entry's details are ready, and use a request generation/cancellation guard. Publish SQL and versions atomically for the selected ID.

**Acceptance:** Controlled A/B response ordering never mixes identities, previews, versions, or action payloads. Selection disappearing after a filter must also invalidate pending detail loads.

### WEB-17 — Do not drop History filters while loading

**P2 · Code-confirmed · Misleading search results**

**Evidence:** `Pages/History.razor:402` returns immediately when `_loading` is true. Filter handlers at lines 488–506 still update their bound state, then call this dropped reload. The old in-flight response is later displayed under the new filter controls.

**Reproduce:** Delay a history search, then change Server/Database/Kind/Favorites or submit another search. The visible controls indicate the new query while rows come from the earlier request; no automatic follow-up load is queued.

**Fix:** Cancel/replace superseded searches or queue the latest requested filter state. Tie response publication and Load more pagination to the request generation.

**Acceptance:** Rapid filter changes during initial load and pagination leave only the latest request's rows visible. No duplicate or mixed pages.

### WEB-18 — Surface errors occurring inside an AI event stream

**P2 · Code-confirmed · False success / misleading AI output**

**Evidence:** `Services/IAiClientFactory.cs:353–379` only extracts OpenAI-compatible content deltas and `[DONE]`; `AnthropicWire.ParseSse:438–473` only recognizes text deltas and `message_stop`. Error payloads and malformed JSON are ignored, and plain EOF is accepted as completion. Callers log completion and retain partial output.

**Reproduce:** A mocked provider returns HTTP 200, a text delta, then an SSE error object, or disconnects before its completion marker. The UI can present partial output as a completed answer without explaining the failure.

**Fix:** Parse provider error events into a safe typed failure and distinguish explicit completion from truncated EOF. Keep partial output but label it incomplete and offer Retry. Do not reveal raw response bodies in diagnostics (WEB-21).

**Acceptance:** Both wire formats handle successful termination, in-stream error, malformed data, truncated connection, and cancellation with correct user-visible status.

### WEB-19 — Do not announce SQL Server connection success after only sending a notification

**P2 · Code-confirmed · Connection UX**

**Evidence:** `Services/ISqlConnectionService.cs:446–493` sends `ConnectionChanged` and sets `IsConnected=true` without receiving an acknowledgement. `Shared/ConnectionManagerModal.razor:925–960` closes on that result; it does not call Test first. PostgreSQL's connect path already tests before proceeding.

**Reproduce:** Enter a wrong SQL Server password or an unavailable TCP server and click Connect directly. The modal closes and the UI shows connected state before any database connection has been validated. Execution/schema failures arrive later.

**Fix:** Prefer an acknowledged connect operation that establishes the real session; otherwise at least validate before announcing success and keep the connection form open on failure. Avoid a test-success/real-connect-failure race in the final design.

**Acceptance:** Wrong password, unavailable server, denied database, alias refusal, and valid connection each produce truthful state and actionable errors. Preserve entered non-secret fields for correction.

### WEB-20 — Preserve the distinction between an omitted insert value and NULL

**P2 · Code-confirmed · Grid insert correctness**

**Evidence:** `Shared/ResultSetGrid.razor:468–473` marks every unset NOT NULL/non-identity insert field invalid without considering a database default. The insert loop in `BuildApplyRequest` at approximately line 1766 adds every insertable column, converting missing dictionary entries to null.

**Reproduce:** Use a disposable table with an identity key, an entered name, and `created_at NOT NULL DEFAULT ...`. The grid demands a value instead of letting the server generate it. A nullable column with a default is explicitly sent as NULL, suppressing its default.

**Fix:** Carry default/generated metadata and represent omitted/default separately from explicit NULL. Generate INSERT using only supplied columns (or intentional DEFAULT). Preserve returned/generated values in the displayed row.

**Acceptance:** SQL Server and PostgreSQL tests cover NOT NULL defaults, nullable defaults, explicit NULL, identity/generated columns, and inserts whose values are entirely defaulted.

### WEB-21 — Keep raw provider error bodies out of persisted diagnostics

**P2 · Code-confirmed · Privacy**

**Evidence:** `Services/IAiClientFactory.cs:174,203` includes the entire provider response body in `HttpRequestException.Message`. `Shared/AiChatPanel.razor`'s general catch builds `_lastError` from that message and logs it. `Services/IDiagnosticsRingBuffer.cs:59–80` persists messages to IndexedDB without redaction.

**Reproduce:** Configure a mocked local provider that returns a failing HTTP response containing a synthetic credential/prompt marker. Send a chat request and inspect the diagnostic ring/export. The marker flows into persisted diagnostics. Real exposure depends on what a provider or proxy includes in its error body; this review did not send real credentials or prompts to a provider.

**Fix:** Use typed status/error codes, a bounded sanitized display message, and redaction before persistence/export. Avoid logging provider bodies, prompts, or raw key-bearing exception messages. Audit SQL error logging through the same boundary because server errors may contain row values.

**Acceptance:** Synthetic secrets in headers/body/error text never appear in the diagnostic store/export. Users still receive useful authentication, quota, endpoint, and connectivity errors.

### WEB-22 — Repair line-ending normalization in the parity test loader

**P2 · Test-confirmed · Verification reliability**

**Evidence:** `tests/AkmlSql.Web.Tests/Services/FormatterServiceTests.cs:133` normalizes actual output. `tests/AkmlSql.Web.Tests/Parity/ParityCorpusLoader.cs:153` returns baseline body text without normalization, despite the test's LF-normalization contract.

**Observed:** All **42 failures** belong to `Formatter_MatchesIdeBaseline_AcrossCorpusAndProfiles`. Comparing the emitted expected/actual diff pairs found **zero non-line-ending differences**. This is evidence of a newline-sensitive gate failure in this checkout, not evidence of 42 SQL formatting defects. It does not prove there can be no other differences beyond the test's emitted comparison.

**Fix:** Normalize both sides consistently with the existing baseline contract, and inspect repository line-ending attributes. Do not regenerate goldens or whitelist divergences to hide the failure.

**Acceptance:** The same committed baseline loads consistently under LF and CRLF checkouts; substantive SQL changes still fail the gate. Re-run the whole Web test project afterward.

## UI/UX improvements and documented limitations — separate from bugs

These are design follow-ups, not additional confirmed defects in the table above.

1. **Narrow screens:** At 768 px the workspace measures 968 px wide because the editor minimum is 920 px plus two 24 px strips. Snippets reserves 280 px for its list at 390 px, leaving an impractically narrow detail pane. Screenshots show the effect. Spec 041 explicitly targets desktop/laptop widths of 1100 px and above and excludes phones, so this is a scope extension: introduce a compact navigation/detail mode and an adaptive editor minimum if smaller screens are now a requirement. Do not call it a regression against the accepted desktop minimum.
2. **Cancellation:** `WebSocketTransport.ServeAsync` awaits a handler before reading the next message. A cancel notification on that same socket therefore cannot interrupt the running request. The UI already says “Cancel if not started” and “A running statement can't be interrupted”; spec 041 explicitly deferred true cancellation. Prioritize transport concurrency if true Cancel is now wanted, including tests for the advertised queued-cancel behavior. Do not merely rename the button and claim cancellation is fixed.
3. **AI usability:** Add a visible Stop action, automatic scroll that respects a user reading older turns, explicit provider attribution, and clear incomplete-answer state. Consider context-budget management for long chats and an explicit choice before moving an existing conversation to another provider. These require product decisions beyond the confirmed streaming/error defects.
4. **Source and target visibility:** Editable result headers should display their originating engine/server/database and stale/read-only state. This complements WEB-01 and reduces confusion after connection changes.
5. **Security hardening to validate separately:** The installer CSP has no `frame-ancestors` restriction; assess clickjacking protection for the actual hosting topology. The bridge's reported TLS fingerprint is a diagnostic received after sending the handshake, not browser-verified certificate pinning; describe it accurately and rely on trusted transport certificates. Assess authenticated connection expiry/revocation and pre-authentication resource limits against the intended shared-engine trust model. These were not penetration-tested. [OWASP's WebSocket guidance](https://cheatsheetseries.owasp.org/cheatsheets/WebSocket_Security_Cheat_Sheet.html) is a useful test reference.
6. **Existing deferred scope:** Multiple query-document tabs, PostgreSQL refactoring, and the hallway usability study are already explicitly deferred. They are not counted as new bugs here.

## Suggested implementation order

1. **Connection and data safety:** WEB-01, 06, 08, 09, 10. Introduce the smallest shared connection/document/result generation boundaries necessary; avoid independent competing state trackers.
2. **Transport and secrets:** WEB-02, 03, 04, 05, 15. Preserve existing credentials and compatibility; test migration and recovery without touching installed services.
3. **AI privacy and failures:** WEB-07, 18, 21. Use synthetic markers and mocked providers.
4. **Editor-adjacent UX:** WEB-11, 12, 13, 14, 16, 17, 19, 20.
5. **Verification:** WEB-22 should be addressed early enough that the test gate is trustworthy, then rerun the appropriate gates after each work group.

Suggested regression locations: `tests/AkmlSql.Web.Tests` for controlled service/component races; `tests/AkmlSql.Web.Tests/js` for JS lifecycle contracts; browser tests for real CryptoKey/IndexedDB behavior, navigation, focus, and file pickers; engine integration tests for target binding, transactions, and insert defaults. JS mocks alone cannot establish browser crypto correctness.

Verification commands used during review:

```powershell
dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj -c Debug --no-restore --logger 'trx;LogFileName=web-review.trx' --results-directory .review-artifacts/2026-10-17
node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~SettingsPageTests|FullyQualifiedName~DocumentFlowTests|FullyQualifiedName~TabStripKeyboardTests|FullyQualifiedName~WorkspaceAccessibilityTests' --logger 'trx;LogFileName=browser-review.trx' --results-directory .review-artifacts/2026-10-17 -- xUnit.ParallelizeTestCollections=false
```

For fix verification, build current Web **and Engine Debug prerequisites before** browser suites. Run browser suites serially and do not rebuild an engine while its sandbox is running. Follow the repository's full-MSBuild solution gate when implementation is complete.

## Prompt to give the fixing agent

> Read `AKML_WEB_REVIEW_2026-10-17.md` and the repository guidance. Revalidate each finding against the current checkout, then implement the confirmed fixes in the suggested order. Start with P1 data-integrity and security findings. Add targeted regressions that reproduce the issue before fixing it. Keep the existing architecture, generated-theme workflow, and host boundaries. Preserve unrelated edits. Do not mutate Git history/index or deploy without explicit authorization. For each ID, record changed files, the regression test, actual test results, and any unresolved limitation. Do not mark a finding fixed just because an unrelated existing suite passes. Keep design extensions and documented limitations separate from bug fixes; do not regenerate formatting goldens to conceal parity failures. Deliver a concise completion matrix and leave the work reviewable and uncommitted.

## Fix completion ledger

All findings were open at report creation; the reviewer made no fixes. The fixes below were made on **2026-10-17** against `c70a3dde` plus the working tree, uncommitted. "Fixed" means the change is in place and the named regression passed; limitations are listed where a part of the acceptance could not be shown here.

| ID | Status | Regression evidence / fix notes |
| --- | --- | --- |
| WEB-01 | Fixed | Engine: `ExecuteQueryResult.ConnectionStamp` (key 15) — an HMAC under a per-process key over kind + session connection string, taken after the run — is sent back as `ApplyChangesRequest.ConnectionStamp` (key 6); `ApplyChangesHandler` refuses a stamp that is not the session's current one before opening anything (`Execution/ConnectionStamp.cs`). Web: `ISqlConnectionService.ConnectionGeneration` moves on connect, disconnect, database change (selector or `USE`) and the bridge reopening on another engine; the editor freezes results read under an older generation (`StaleResultsReason`), drafts stay visible and discardable, never applied. Tests: `ConnectionStampTests` (5), `SessionTargetIntegrationTests.Rows_read_through_one_connection_are_not_written_through_the_next` (live, tempdb, identical table/keys), `SqlConnectionServiceTargetTests.Every_change_of_target_moves_the_generation`, `ResultsGridTargetBindingTests` (stamp sent; frozen grid). Limitation: an Apply from an older web bundle (no stamp) is not checked — by design for compatibility. |
| WEB-02 | Fixed | `BuildConnectionString` now always sets `Encrypt=True`; `TrustServerCertificate` is true only for a loopback server or when the connection's new **Trust the server certificate** option is ticked (`ConnectionTarget.TrustServerCertificate`, persisted as `SavedSqlConnection.TrustServerCertificate`; older records read false). Legacy overloads never trust a remote certificate. Tests: `SqlConnectionServiceTargetTests` (default validates, explicit choice per connection, legacy overloads, loopback keeps skipping, saved record round-trip). Limitation: no test against a live remote server with an untrusted certificate. |
| WEB-03 | Fixed (installer not run) | Installer: TLS setup now runs before IIS setup; in LAN mode `web-iis-setup.ps1 -HttpsPort` adds an HTTPS binding with the bridge certificate (whose SANs cover the host names and addresses); new "Secure site port" (default 443, `/WEB_HTTPS_PORT`, registry `HttpsPort`), its own firewall rule, repair and uninstall cleanup; the summary advertises `https://<computer>/`. App: `isSecureStorageAvailable()` in `akml-crypto.js`; `SecureContextNotice` with the site's HTTPS address in the pairing form and AI keys; a PIN pairing or key save is refused **before** the handshake in an insecure context; no plaintext fallback. Tests: `SecureContextPairingTests` (3), `akml-crypto.wrapkey.test.mjs` (insecure context refuses). ISCC compiles (`/O-`), the three scripts parse. Limitation: the installer was not run and no second machine was used — the HTTPS site, firewall rule and a remote pair/reload/AI request remain to be verified on a LAN install. |
| WEB-04 | Fixed | `akml-crypto.js`: one shared promise per module; creation is a single readwrite transaction that keeps an existing key and stores the candidate only if none is there (IndexedDB serialises those transactions across tabs), resolving after `oncomplete`. Tests: `akml-crypto.wrapkey.test.mjs` (12 concurrent wraps, two "tabs", key never replaced) and **real Chromium** `SecretStorageTests.Concurrent_first_wraps_in_one_tab_and_in_two_all_decrypt_after_a_reload` — passes; against the HEAD module the same test fails with 11 of 12 undecryptable, as the review observed. |
| WEB-05 | Fixed | `EngineBridge` adopts `NewBearerToken` after a successful handshake; the reconnect loop falls back to the vault token; `PinRequired` with no token offered is no longer treated as a revocation (nothing is deleted). Tests: `ReconnectLoopTests.A_reconnect_after_a_PIN_pairing_uses_the_token_that_pairing_issued`, `PinRequired_without_a_token_offered_is_not_treated_as_a_revocation`; the existing `RevocationTerminatesLoop` still passes. |
| WEB-06 | Fixed | New `SessionDisconnect` 225 → 226 (`session.disconnect.v1`): the engine forgets the session's connection, cancels queued work, waits up to 15 s for a running command, rolls back an open transaction and disposes the connection. Every session-connection close now rolls back explicitly (a pooled SqlConnection would otherwise keep a script-begun transaction and its locks). Web: `DisconnectSessionAsync` waits for the engine; Settings asks first when a PostgreSQL transaction is open, says when one was rolled back, and warns when the engine was unreachable or too old. Tests: `SessionTargetIntegrationTests.Disconnect_rolls_back_the_open_transaction_and_releases_its_locks` (live: another connection reads the row under `LOCK_TIMEOUT 2000`, the session then has no connection, a reconnect has `@@TRANCOUNT = 0`), `SqlConnectionServiceTargetTests` (4 disconnect cases). Limitation: PostgreSQL rollback on disconnect is not exercised live (no PostgreSQL server here). |
| WEB-07 | Fixed | `AiSchemaContextProvider` takes the active connection: it looks up exactly `(SchemaIdentity ?? Server, Database)`, captured before any await, and returns nothing when that snapshot is absent, when not connected, or when the connection moved during the request. Tests: `AiSchemaTargetTests` (6; sentinel tables, last-used other database, the other kind, mid-request switch, `NoSchema`). |
| WEB-08 | Fixed | The grid takes a `FrozenReason` (a run in flight, or stale results) and locks every mutation path while frozen or while its own Apply waits; a reply that arrives after a new result changes nothing; the editor refuses Execute/Parse while an Apply waits. Tests: `ResultsGridTargetBindingTests` (frozen grid, delayed Apply: edits refused and only the sent values marked committed, late reply after a new result), `ResultsPaneComponentTests.While_running_the_old_grids_take_no_edit…`, browser `ReviewFixBrowserTests.While_a_query_runs_the_previous_grid_takes_no_edit`. |
| WEB-09 | Fixed | Grid → pane → editor report pending edits; `NavigationLock ConfirmExternalNavigation` is on only while there are some. Tests: `ResultsPaneComponentTests.The_page_is_told_when_pending_edits_appear_and_go`; browser `ReviewFixBrowserTests.Pending_grid_edits_make_the_browser_ask_before_the_tab_closes` (Chromium: `beforeunload` shown, dismiss keeps the edit; after Discard no prompt). Limitation: Firefox and browser back/forward were not run. |
| WEB-10 | Fixed | Format reads the live text, takes a `FormatRequestGate` ticket (document key + dialect) and applies the reply only if it is the latest Format for the same document and kind, through `replaceTextIfUnchanged` — compare and replace in one JS turn, one undoable transaction. Tests: `FormatRequestGateTests` (4), `akml-editor.replace.test.mjs` (3). Limitation: no end-to-end test with a delayed PostgreSQL engine reply. |
| WEB-11 | Fixed | Snippets tracks a baseline; selection, New and Refresh ask before replacing a draft; Refresh after import keeps a draft; the kind filter no longer closes a draft; `NavigationLock` (in-app and browser); Save ends the draft, a failed Save keeps it. Tests: `SnippetDraftGuardTests` (select/New/Refresh/navigation/save). |
| WEB-12 | Fixed | `NavigationLock` on Format styles uses the existing dirty flag and discard question; the browser prompt while dirty. Tests: `StylesLeaveGuardTests` (Cancel stays with the change, confirm leaves, clean style leaves without asking). Limitation: back/forward and the native prompt not run in a browser for this page. |
| WEB-13 | Fixed | Snippets: one-tab-stop listbox with `aria-activedescendant`, Arrow/Home/End move, Enter/Space select (through the draft guard), visible focus. History: group headers, entries, favorites and versions are native buttons (`aria-expanded`, `aria-current`, `aria-pressed`), roving focus with Arrow/Home/End, the current row's favorite and compare controls in the tab order; `trapListKeys` cancels only the list's keys. Tests: `HistoryRaceAndKeyboardTests` (2 keyboard cases), `SnippetDraftGuardTests.The_list_is_one_tab_stop…`, `akml-ui.test.mjs` (2), browser `ReviewFixBrowserTests.Snippets_select_and_import_from_the_keyboard`. `HistoryDialogTests` updated: the selection is `aria-current` on the row's button, not `aria-selected` on a non-option. Limitation: no screen-reader check. |
| WEB-14 | Fixed | Snippets and Styles import inputs use the Settings/Open pattern (`akml-visually-hidden` native file input inside its label, focus ring via `:focus-within`); the snippet status is `role="status"`. Tests: `SnippetDraftGuardTests.Import_is_a_focusable_file_input…`, `StylesLeaveGuardTests.Import_is_a_focusable_file_input…`, browser Tab-reaches-Import check. Limitation: choosing a file through the native picker and the theme check were not automated. |
| WEB-15 | Fixed | Each crypto connection closes when its transaction completes and closes itself on `versionchange`. Tests: `akml-crypto.wrapkey.test.mjs` (every connection closed, handler set); **real Chromium** `SecretStorageTests.Stored_secrets_do_not_block_another_tabs_upgrade_and_still_decrypt_after_it` — passes; against the HEAD module the upgrade is blocked. |
| WEB-16 | Fixed | History numbers each selection, clears the preview and versions at once, publishes SQL and versions together only for the current selection, and disables Copy/Open/Re-execute until they arrive; `ClearDetail` invalidates a pending load. Tests: `HistoryRaceAndKeyboardTests.A_late_answer_for_the_previous_entry_never_shows_under_the_new_one`. |
| WEB-17 | Fixed | History numbers each search; a new search or filter replaces one still loading; a superseded answer (including a Load more page) is dropped. Tests: `HistoryRaceAndKeyboardTests.A_filter_changed_during_a_search_replaces_it`. |
| WEB-18 | Fixed | Both SSE parsers raise `AiStreamException` for an error event (with its short code), data that is not JSON, and a stream that ends without `[DONE]`/`finish_reason` (OpenAI) or `message_stop` (Anthropic); empty `data:` keep-alives are skipped. Chat and AI panel keep the partial text, say it is incomplete and offer Retry. Tests: `AiStreamFailureTests` (6 parser cases + cancellation). `StreamingParserTests.OpenAiSse_SkipsMalformedChunks` was replaced by `…RejectsMalformedChunks_KeepingWhatCameBefore` — the old test asserted the behaviour this finding reports. |
| WEB-19 | Fixed | SQL Server Connect runs the engine's `TestSqlConnection` first; a refusal returns the engine's error, sends no `ConnectionChanged`, keeps the dialog open with what was typed and leaves the generation unchanged. Tests: `SqlConnectionServiceTargetTests` (refused login, accepted login tested before the switch, refused connect keeps the old target). Test fakes that never answered the test were given an answer. Limitation: a server that disappears between the test and the switch is reported by the first run, as any later drop is. |
| WEB-20 | Fixed | `ColumnProvenanceDto.HasDefault` (key 12) from the SQL Server schema cache and PostgreSQL `pg_attrdef`; the grid no longer requires NOT NULL columns with a default, shows unset cells as "(default)", and sends only the cells the user set; an empty insert is `INSERT … DEFAULT VALUES` in both writers. Tests: `ResultsGridInsertDefaultsTests` (3), `CrudWriteGeneratorTests.Insert_WithNoCells_TakesEveryDefault`, `PgCrudWriteGeneratorTests.An_insert_with_no_values_takes_every_default_and_returns_the_row` (replaces the test that refused it), live `InsertDefaultsIntegrationTests` (NOT NULL and nullable defaults, explicit NULL, identity, all-defaults row). Limitations: `HasDefault` is false until a SQL Server schema's columns load (the grid then asks for a value, as before); a SQL Server inserted row shows NULL for defaulted cells until the query is re-run; PostgreSQL defaults are not exercised live. |
| WEB-21 | Fixed | HTTP failures carry status, the provider's short error code and an actionable hint — never the body; the diagnostics ring masks credential-shaped text and bounds every message to 1,000 characters before it is stored or exported; the panels log type names, not raw exception text. Tests: `AiStreamFailureTests` (4 status cases + streaming path, synthetic prompt marker and key never in the message or the ring; 4 redaction cases; bounding; ordinary text untouched). |
| WEB-22 | Fixed | `ParityCorpusLoader.LoadFormatterBaseline` normalises the expected side exactly as the actual side. No golden was regenerated. Tests: `FormatterServiceTests.Formatter_baselines_load_LF_normalised_whatever_the_checkout`; the parity theory now passes in this CRLF checkout (74/74 in `FormatterServiceTests`, was 42 failures). |

### Verification on 2026-10-17

| Check | Result |
| --- | --- |
| Full solution, MSBuild Release (restore + build) | Succeeded, 0 errors (existing NU1902 MessagePack advisory warnings) |
| Web .NET tests (Debug) | **1,632 passed, 0 failed** (was 1,514 / 42 failed at review) |
| Web JavaScript tests | **131 passed, 0 failed** (one unexplained single failure in the first of ten runs; not reproduced in nine reruns) |
| Engine tests (Debug, local SQL Server live tests included) | 2,464 passed, 128 skipped, 2 failed in the full run: `PgCrudWriteGeneratorTests.An_insert_with_no_values…` (the new test's own quoting expectation, corrected; now passes) and `AgentFallbackChainTests.A_cloud_candidate_without_consent…` (AI agent code, untouched here; passes 3 of 3 alone — intermittent in the full run) |
| Core / Shell.Shared / PostgreSql tests | 1,455 passed, 1 failed, 3 skipped / 987 passed / 2,237 passed. The Core failure, `ProfileGetMessageTests.Response_key_layout_is_positional_and_append_only` (expects 7 `ProfileGetResponse` keys, the committed DTO has 9), is pre-existing: no profile message was changed. |
| Browser (Playwright, Chromium, serial) | New: `SecretStorageTests` 2/2, `ReviewFixBrowserTests` 3/3. Existing: Settings, DocumentFlow, TabStrip, WorkspaceAccessibility, ResultsGrid, ReviewRegression, FormatStyles, ShellPolish, QuickstartAcceptance — 59/62 on the first run; the 3 ShellPolish failures were caused by the new navigation locks (a page with nothing unsaved now navigated a tick later), fixed by rendering the lock only while there are unsaved changes; ShellPolish + new suites then 15/15. |
| Installer | `ISCC /O-` compiles; the three PowerShell scripts parse. Not run. |

Side effect: `ISCC /O-` deleted the previously built `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe` (an untracked build output; the next installer build recreates it).

Not done: the installer was not run or deployed; no second machine, remote SQL Server, PostgreSQL server, Firefox, or screen reader was used; nothing was committed.
