# AKML Web fix verification and second review

Date: 2026-10-17. Reviewed commit: `1d09ef4c` (`fix(web): the 22 findings of the 2026-10-17 web review`). Original baseline: `c70a3dde`.

**Verdict: the fixes are substantial, but the claim that all 22 findings are fully closed is premature.** This review accepts **15 as resolved for the current Web/Engine build**, leaves **7 partially resolved**, and identifies **one additional History execution defect**. The actionable backlog below has **8 items: 3 P1 and 5 P2**. Some remaining behaviors predate the fix commit; the report distinguishes those from regressions.

The original report and its implementation ledger remain intact in [AKML_WEB_REVIEW_2026-10-17.md](AKML_WEB_REVIEW_2026-10-17.md). This report supersedes its completion claims where they differ. No production code was fixed or deployed during this review.

## Verification performed

Reviewed the 83-file fix commit, its added tests, current implementation, workspace guidance, and relevant Web/PostgreSQL requirements. Rebuilt current Web and Engine Debug prerequisites before the browser run. Used fresh browser contexts, synthetic secrets and schema markers, and existing isolated SQL test fixtures.

| Check performed in this review | Result |
| --- | --- |
| Existing Web .NET suite | **1,632 passed, 0 failed, 0 skipped** |
| Web JavaScript suite | **131 passed, 0 failed** |
| Engine Debug build | Succeeded |
| Selected Chromium browser suites, serial | **48 passed, 0 failed, 0 skipped**; 11 min 13 sec |
| Targeted Engine connection/defaults/CRUD tests | **97 passed, 0 failed, 0 skipped**, including live local SQL target-binding, disconnect rollback and default-insert checks |
| Independent acceptance probes added temporarily for this review | **6 failed as expected**, reproducing REV-01, 02, 04, 05, 06, 07; these are separate from the passing existing suite |
| Additional real-browser probes | Draft guards passed; 12/12 concurrent wraps decrypted with a fresh module; IndexedDB upgrade was available; Snippets keyboard visibility failed |

Evidence: [.review-artifacts/2026-10-17-followup](.review-artifacts/2026-10-17-followup). The independent acceptance probe sources are preserved as `ReReviewAuditTests.cs.txt` and `ReReviewSchemaAuditTests.cs.txt`; their temporary copies were removed from the test project, which was rebuilt afterward. Production sources and existing tests were not changed.

Scope limits: no installer execution, deployment, second LAN machine, remote SQL certificate handshake, live PostgreSQL server, Firefox, screen reader, full solution build, or full Engine suite in this follow-up. Passing component tests establish the tested behavior, not those unperformed deployment checks. Build warnings include dependency security advisories (tracked separately as SEC-01 below) and the WebAssembly assembly-version conflict. The earlier agent's full-build results are not counted as this review's results.

## Original 22 findings: independent disposition

“Resolved” means the original defect is addressed in the reviewed implementation and relevant existing regressions passed. It does not certify every deployment or legacy-client combination. “Partial” means useful fixes landed, but a related concrete acceptance gap remains.

| Original ID | Verdict | Evidence / remaining work |
| --- | --- | --- |
| WEB-01 — stale result target | Resolved for current clients | Browser generation freezes and engine connection stamps are implemented; `ResultsGridTargetBindingTests` and service target tests pass. **Compatibility caveat:** `ConnectionStamp.Allows` deliberately accepts a missing stamp, so this is not mandatory protection for old clients. |
| WEB-02 — SQL certificate validation | Resolved | Remote SQL connections default to encryption and certificate validation; the explicit trust choice is persisted and passed through Connect/Test/listing. Service tests pass. Actual remote-certificate deployment remains untested. |
| WEB-03 — insecure LAN hosting | **Partial** | HTTPS provisioning and secure-context guards exist, but the new installer can overwrite/delete another application's certificate binding. See **REV-03**. A real LAN HTTPS installation still needs verification. |
| WEB-04 — crypto key creation race | Resolved | Shared promise plus atomic read/write transaction; JS tests and the independent browser probe pass, including fresh-module decryption of all 12 records. |
| WEB-05 — reconnect pairing token | Resolved | Bridge adopts the newly issued bearer and uses vault fallback; reconnect/revocation regressions pass. |
| WEB-06 — browser-only Disconnect | Resolved | Engine disconnect RPC, explicit rollback, connection disposal, and truthful unconfirmed-disconnect messages are implemented. Service tests pass; live SQL coverage is in the targeted Engine run. Live PostgreSQL rollback remains untested. |
| WEB-07 — wrong AI schema | **Partial** | Different database/kind selection is fixed; two engines with the same engine-relative SQL address still collide in the cache. **REV-01** reproduces the wrong schema entering prompt context. |
| WEB-08 — editing during Execute/Apply | Resolved | Grid mutation guards, Apply snapshot protection, and Execute/Parse checks are implemented; component regressions pass. |
| WEB-09 — external navigation loses drafts | Resolved | Pending-edit callbacks reach `NavigationLock.ConfirmExternalNavigation`; component and focused browser coverage exists. |
| WEB-10 — stale Format replacement | Resolved | Request gate and atomic JS compare/replace protect current text/document/dialect; .NET and JS regressions pass. Delayed PostgreSQL browser round-trip remains untested. |
| WEB-11 — snippet draft loss | **Partial** | Selection/New/Refresh/navigation guards work, including the independent browser check. Typing during an awaited Save still loses newer edits. **REV-04**. |
| WEB-12 — style route departure | Resolved | Dirty navigation guard is implemented; component tests and independent browser route-navigation probe pass. |
| WEB-13 — keyboard list operations | **Partial** | Native History controls and Snippets listbox navigation are implemented. Snippets moves its active option out of view without scrolling. **REV-08**. |
| WEB-14 — inaccessible import controls | Resolved | Both file inputs now use the focusable visually-hidden pattern; component assertions pass and browser coverage exercises Tab to Import. Native picker/screen-reader checks remain unperformed. |
| WEB-15 — crypto blocks IDB upgrades | Resolved | Short-lived connections and `onversionchange` closure are implemented; independent browser upgrade probe succeeds. |
| WEB-16 — History selection race | Resolved | Selection generations, cleared previews and pending-action guards fix the old A/B race; controlled race tests pass. A distinct failure-path defect is recorded as **REV-02**. |
| WEB-17 — dropped History filter loads | Resolved | Latest-load generation replaces in-flight searches; controlled filter race test passes. |
| WEB-18 — failed AI stream treated as complete | **Partial** | Parsers now reject errors/truncation and the live panel warns. That warning is not stored with the partial answer and disappears on reopening or another send. **REV-07**. |
| WEB-19 — premature connection success | Resolved | SQL Server Connect awaits TestSqlConnection and preserves the old target on refusal; service regressions pass. |
| WEB-20 — inserted-row defaults | **Partial** | Omitted cells and DEFAULT VALUES now work. SQL Server returned/default values still are not faithfully reflected without rerunning, and default metadata depends on the cache. **REV-06**. |
| WEB-21 — provider diagnostics disclosure | **Partial** | New provider failure messages no longer copy the whole body, and new log messages are redacted. Existing persisted sensitive entries bypass that protection when restored/exported. **REV-05**. |
| WEB-22 — parity line endings | Resolved | Expected and actual normalization is consistent. The full Web suite passes without changing goldens. |

## Remaining actionable backlog

### REV-01 — P1: Scope schema cache and AI context to the engine that owns the SQL connection

Related: WEB-07. **Test-confirmed remaining privacy/correctness defect.**

**Evidence:** `src/AkmlSql.Web/Services/IAiSchemaContextProvider.cs:51` identifies a target only by server/database/kind, and line 78 retrieves that cache key. `ISqlConnectionService.cs:241` returns the raw SQL Server address as `SchemaIdentity`. `ISchemaCacheStore.cs:69` keys snapshots by server/database; its `SourceConnectionId` is not checked by the AI provider. `Shared/MainLayout.razor:170` starts sync using the same identity. A loopback SQL address is relative to the paired engine's computer, so two different engines can both have `localhost/Sales` while referring to different databases.

**Reproduce:** Pair engine A and connect to its `localhost/Sales`; cache a table marker `EngineASecretPayroll`. Switch to engine B and connect to its own `localhost/Sales`. Before B's schema replaces the cache, request schema context with Schema names only or Full schema. The A marker is returned for B. The independent `Localhost_on_another_engine_must_not_disclose_the_previous_engines_schema` test uses the real connection service/cache/provider with a scripted bridge and fails at that assertion. It sends nothing to an external provider.

**Impact:** AI features can disclose schema from the wrong engine/database and generate SQL against the wrong objects. Schema browsing/offline completion share the same ambiguous cache identity. Merely checking the current connection generation before/after an await cannot distinguish an old snapshot after the switch has completed.

**Fix:** Give cached schemas a stable source identity that distinguishes engines and database kinds, especially loopback addresses and engine-local aliases. Carry that identity through sync, tree/completion and AI context. Reject ambiguous legacy cache entries for AI until refreshed. Validate the captured target/generation again before publishing context. Keep No schema behavior unchanged.

**Acceptance:** Two paired engines with equal SQL address/database strings and different sentinel tables never share schema context; repeat with PostgreSQL localhost, warm cache/reload, engine switch during a request, and absent new-target cache. Existing cross-database/kind tests continue passing.

### REV-02 — P1: Do not re-execute History's 500-character excerpt when full SQL could not load

New finding, adjacent to WEB-16. **Test-confirmed; the unsafe fallback also existed before this fix commit.**

**Evidence:** `src/AkmlSql.Engine/History/HistoryDatabase.cs:1222` and `:1270` select `substr(h.sql_text, 1, 500)` for search results. `src/AkmlSql.Web/Services/IHistoryService.cs` returns null for failed full-SQL retrieval. `Pages/History.razor:574` silently substitutes `entry.SqlText`, then line 588 clears `_detailLoading`. Re-execute at line 782 uses `_previewSql` and navigates to `/?autorun=1`; the button only checks the loading flag.

**Reproduce:** Select a history entry longer than 500 characters while GetFullSql fails/returns null but the bridge remains available. The first 500 characters become the preview and Re-execute is enabled. `Missing_full_history_SQL_must_not_enable_reexecute_of_search_excerpt` reproduces this without executing SQL.

**Impact:** The app can execute only a prefix of the original query. A cut before a WHERE clause or a later transaction/rollback statement can change the meaning materially; some prefixes merely produce syntax errors, but safety cannot depend on that.

**Fix:** Track successful retrieval of complete SQL separately from “request finished.” Treat the search excerpt as preview-only. Show a useful retry/error state and keep Re-execute disabled until full SQL is confirmed. Open/Copy must also distinguish an excerpt from complete SQL. Do not discard an already fetched full query merely because optional version retrieval failed.

**Acceptance:** Failed, timed-out, null, and unsuccessful full-SQL responses never trigger autorun or present an excerpt as the complete query. A retry restores the full text and enables execution. Keep existing selection-race tests passing.

### REV-03 — P1: Preserve unrelated HTTPS certificate bindings during setup and uninstall

Related: WEB-03. **Code-confirmed regression; installer was not executed.**

**Evidence:** `src/AkmlSql.Installer/web-iis-setup.ps1:121` adds a wildcard HTTPS binding; line 123 unconditionally deletes the HTTP.sys binding at `0.0.0.0:$HttpsPort`, then line 124 attaches AKML's certificate. The default port is 443. There is no ownership check. `web-installer.iss:918` similarly deletes the configured HTTPS-port binding during uninstall based only on the stored port and the existence of AKML's thumbprint file. `:675` records that port even for localhost mode.

**Reproduce on a disposable host:** Put another application's non-SNI HTTPS binding on 443, then install AKML LAN hosting with default ports. Also test uninstall after another application has taken over the port, and a LAN-to-localhost upgrade. Inspect whether the other binding/certificate survives. This sequence is an acceptance procedure, not a claim it was executed here. Microsoft documents `delete sslcert` as removing the IP/port certificate binding; it is not scoped to an IIS site. [Microsoft netsh HTTP reference](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/netsh-http).

**Impact:** Installation/repair can replace a certificate used by another site, cause certificate errors or binding conflicts, and uninstall can remove a binding AKML does not own.

**Fix:** Inspect port/site/binding ownership before mutation. Refuse an occupied foreign endpoint with a useful alternate-port message, or use an explicitly designed host/SNI binding. Record the exact binding AKML created and only remove it if current ownership still matches. Handle upgrades, mode changes and port changes. Require actual HTTPS provisioning success before reporting a usable LAN HTTPS URL; missing certificate currently only logs a warning.

**Acceptance:** Automated script-level tests cover existing foreign binding, existing owned binding, failed TLS setup, changed port, LAN-to-localhost upgrade and uninstall after port reassignment. Then verify install/repair/uninstall on a disposable IIS host and pair from a second machine. Script parsing/ISCC compilation alone does not close this item.

### REV-04 — P2: Preserve edits made while a snippet Save is waiting

Related: WEB-11. **Test-confirmed remaining data-loss path.**

**Evidence:** `src/AkmlSql.Web/Pages/Snippets.razor:355` copies buffers into the snippet, awaits persistence at line 369, then sets `_baseline = Buffers` at line 370 using whatever the user has typed since the save started. The ensuing `RefreshAsync` sees a clean draft and replaces it with stored text. Fields remain editable during the await. The actual store serializes before awaiting IndexedDB and may also await the engine sync.

**Observed:** Save `SELECT 1;`, type `SELECT 2; -- typed while saving` while the persistence completion is gated, then release it. The field reverts to `SELECT 1;`. `Typing_while_snippet_save_waits_must_preserve_the_new_draft` fails with those exact expected/actual values.

**Fix:** Capture the snippet ID, saved values and draft revision before awaiting. Mark only that snapshot saved; preserve newer buffers as dirty. Ignore stale completions after selection changes, and serialize overlapping saves or disable them clearly. A Save failure must preserve the draft too.

**Acceptance:** Delayed local storage and delayed engine sync both preserve subsequent typing. Switching snippets, New, double Save and failed Save do not clear an unrelated/current draft. The saved baseline corresponds to persisted values, not later buffers.

### REV-05 — P2: Sanitize or retire legacy diagnostics before restore/export

Related: WEB-21. **Test-confirmed upgrade/privacy gap.**

**Evidence:** `src/AkmlSql.Web/Services/IDiagnosticsRingBuffer.cs:63` cleans newly logged messages, but `RestoreAsync` at line 100 enqueues old entries unchanged. `Snapshot` and `FlushAsync` return/persist them unchanged. A browser updated from the original vulnerable build can already contain provider bodies, echoed credentials and prompts.

**Observed:** Seed the diagnostics ring with a synthetic old AI error containing an API-key marker. Restore and flush under the new implementation. The marker remains in the snapshot and persisted JSON. `Restored_diagnostics_must_not_export_legacy_credentials` fails. No real credential was used.

**Fix:** Migrate or purge legacy AI diagnostic entries that may contain raw provider bodies; generic key-pattern masking cannot reliably remove arbitrary echoed prompts/schema. Apply appropriate sanitization at restore and export boundaries, including structured data where relevant, and persist the cleaned migration. Preserve useful safe status/type information.

**Acceptance:** Seed an old-format ring with synthetic credentials, prompt text and schema markers, upgrade/restore/export it, and verify sensitive content is absent from memory, persistence and export. New failure-path tests must remain green.

### REV-06 — P2: Finish SQL Server insert-default metadata and returned-row handling

Related: WEB-20. **Code- and test-confirmed partial implementation, already acknowledged in the fix ledger.**

**Evidence:** `src/AkmlSql.Engine/Execution/ResultSetReader.cs:393` learns defaults only from populated schema-cache columns. A cold/missing snapshot, newly created table or temp table can still leave `HasDefault=false`. SQL Server `ApplyChangesHandler.cs:130` returns `NewIdentity`, not a returned row. `src/AkmlSql.Web/Shared/ResultSetGrid.razor:1654` creates appended rows from supplied cells; the Apply merge at line 1726 ignores responses whose `Row` is null and never consumes `NewIdentity`. `Pages/Editor.razor:1186` intentionally declines automatic rerun for multi-statement batches.

**Observed:** A successful insert response with `NewIdentity="4"` leaves the appended identity cell null; `Inserted_SQL_Server_row_must_merge_returned_identity_without_rerunning_batch` fails. Defaulted values are likewise unknown in that local row. Existing live insertion tests verify database contents by running SELECT afterward; they do not establish correct displayed values.

**Impact:** SQL Server may store correct defaults while the grid displays NULL, including for NOT NULL columns and the row key. Subsequent edits/deletes can target a null key and fail. For a cold schema, a required defaulted field can still unnecessarily block Apply. The normal single-SELECT auto-refresh can hide the display gap; multi-statement results expose it.

**Fix:** Obtain reliable default/generated metadata for the actual result target, or explicitly represent unknown metadata instead of assuming no default. Return authoritative inserted values for SQL Server and merge them, including identity/default-generated keys and trigger effects as appropriate. Keep the rule against rerunning mutating batches.

**Acceptance:** Through the real Web grid, insert into a table returned by a multi-statement batch with identity, NOT NULL/default, nullable/default and explicit NULL fields. Check stored and displayed values, then edit/delete the new row without rerunning. Repeat with a freshly created/temp table and a cold schema cache; retain PostgreSQL returned-row behavior.

### REV-07 — P2: Persist incomplete-answer status on the AI turn

Related: WEB-18. **Test-confirmed remaining UX/correctness defect.**

**Evidence:** `src/AkmlSql.Web/Shared/AiChatPanel.razor:348` places the incomplete warning in transient `_lastError`, while line 374 persists the partial content. `Services/IChatHistoryStore.cs:11` gives `ChatTurn` no completion/error status; Markdown export also emits it as an ordinary assistant turn. Reinitialization at `AiChatPanel.razor:239` restores only the conversation, and the next send clears `_lastError` at line 284.

**Observed:** A mocked stream yields `SELECT unfinished`, then raises `AiStreamException(Truncated)`. The initial warning appears. Dispose/reopen the panel with its real persisted conversation: the partial text remains, but its incomplete warning is gone. `Partial_AI_answer_must_stay_marked_incomplete_after_reopening` fails.

**Fix:** Store completion status and a safe failure category on the specific assistant turn. Render/export that status after reload and subsequent turns. Define whether incomplete answers are included in future model context and preserve an actionable retry path without mislabeling the old text as completed.

**Acceptance:** Stream-error, malformed-event and premature-EOF partial answers stay visibly incomplete after navigation, reload, export and another send. Successful turns remain unmarked; retry success has a clear persisted outcome.

### REV-08 — P2: Scroll the active Snippets option into view during keyboard navigation

Related: WEB-13. **Real-browser-confirmed incomplete keyboard fix.**

**Evidence:** `src/AkmlSql.Web/Pages/Snippets.razor:243` changes `_active` for Arrow/Home/End and updates `aria-activedescendant`; `OnAfterRenderAsync` at line 261 only installs a key trap. Nothing scrolls the active option into view. `wwwroot/js/akml-ui.js:184` suppresses the browser's default scrolling for those keys.

**Observed in Chromium at 1440 x 900:** Focus the list and press End. There are 22 options; the active last row occupies y=882..924, but the list ends at y=856 and `scrollTop` remains 0. The active row/focus indication is invisible. Evidence: [browser-probes.json](.review-artifacts/2026-10-17-followup/browser-probes.json) and [screenshot](.review-artifacts/2026-10-17-followup/snippets-keyboard-end.png). The existing browser regression only moves one row down and misses the overflow case.

**Fix:** After the active option changes, scroll that option into the list's visible area with nearest alignment while keeping DOM focus on the listbox. Handle filtering and selection without unexpectedly scrolling the whole page. WAI-ARIA calls for the active descendant to remain visible or be scrolled into view. [WAI-ARIA focus guidance](https://www.w3.org/TR/wai-aria/#managingfocus).

**Acceptance:** In both themes and a short desktop viewport, End, Home and repeated Arrow keys keep the active row visible. Enter/Space selects it, the draft guard still works, and Tab exits normally. Include an overflow browser test and a screen-reader spot check.

## Fix order and handoff

1. **REV-01, REV-02:** prevent wrong schema disclosure and execution of incomplete History text.
2. **REV-03:** make HTTPS binding ownership safe before any installer deployment.
3. **REV-04, REV-06:** preserve drafts and accurately represent committed inserted rows.
4. **REV-05, REV-07, REV-08:** complete upgrade privacy and UI state/keyboard behavior.

### SEC-01 — Dependency security follow-up, separate from the eight application findings

**P2 remediation/triage task; affected dependency versions confirmed, application-level exploitability not established.** The targeted Engine build reports NU1902/NU1903 for dependencies already present before the fix commit. These warnings should not be treated as a clean security gate.

| Resolved dependency | Source evidence | Maintainer advisory and patched version |
| --- | --- | --- |
| MessagePack 3.1.8 | `src/AkmlSql.Engine/AkmlSql.Engine.csproj:36`; resolved in `obj/project.assets.json` | Memory-exhaustion issue in untrusted `object` / PrimitiveObjectFormatter deserialization; the 3.x fix is **3.1.9**. [Maintainer advisory](https://github.com/MessagePack-CSharp/MessagePack-CSharp/security/advisories/GHSA-qhrr-8q5h-9q3h). |
| System.Security.Cryptography.Pkcs 9.0.18, transitive | Engine `obj/project.assets.json:1817`; warnings retained in `engine-targeted-tests.log` | `SignedCms.CheckSignature` CPU-exhaustion advisory rated high and `EnvelopedCms.Decode` advisory rated moderate; both are fixed in **9.0.21** on the 9.x line (10.x: 10.0.13). [Microsoft SignedCms advisory](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx), [Microsoft EnvelopedCms advisory](https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7). |

Trace the relevant resolver/CMS paths and transitive owners, update to compatible patched packages, and verify the resolved **published** Web/Engine dependency graphs. Do not suppress the advisories as a substitute. Run protocol/serialization compatibility tests and the required full-MSBuild gate after dependency changes. The package advisories are verified; this review did not send resource-exhaustion payloads or demonstrate an exploitable AKML endpoint.

Do not reopen unrelated completed fixes. Narrow-screen redesign, true running-query cancellation, multiple query tabs and other explicitly deferred features in the original report remain separate product work.

> Read `AGENTS.md`, `PROJECT_MEMORY.md`, `CLAUDE.md`, the constitution, the original review and this follow-up. Reproduce REV-01 through REV-08 against the current checkout, then fix confirmed defects in the order above; also address the separately scoped dependency task SEC-01. Use the preserved acceptance-probe sources to build permanent regressions in the appropriate existing suites; keep their behavioral assertions. For every ID, record changed files, the before/after test, actual outcomes and remaining deployment limitations. Preserve unrelated edits and the existing review evidence. Follow the repository's explicit-consent rules for Git mutations and deployment. Do not mark an item complete because only a mock, parser check or unrelated suite passed. For installer validation use a disposable IIS host and verify foreign bindings survive. Keep production code changes reviewable and provide an updated per-ID completion matrix.

## Reproduction commands and artifacts

The existing-suite Web run produced `web-re-review.trx`. The serial browser run produced `browser-re-review.trx`, with build/output in `browser-tests.log`. Targeted Engine output is `engine-targeted-tests.log` / `engine-targeted-re-review.trx`. Independent probes are separated into `independent-acceptance-probes.trx` (five acceptance failures) and `schema-isolation-probe.trx` (one failure).

```powershell
dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj -c Debug --no-restore --logger 'trx;LogFileName=web-re-review.trx' --results-directory .review-artifacts/2026-10-17-followup
node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Debug --no-restore
dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~ReviewFixBrowserTests|FullyQualifiedName~SecretStorageTests|FullyQualifiedName~SettingsPageTests|FullyQualifiedName~DocumentFlowTests|FullyQualifiedName~TabStripKeyboardTests|FullyQualifiedName~WorkspaceAccessibilityTests|FullyQualifiedName~ResultsGridTests|FullyQualifiedName~ShellPolishTests|FullyQualifiedName~FormatStylesTests' --logger 'trx;LogFileName=browser-re-review.trx' --results-directory .review-artifacts/2026-10-17-followup -- xUnit.ParallelizeTestCollections=false
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Debug --no-restore -p:BuildProjectReferences=false --filter 'FullyQualifiedName~ConnectionStampTests|FullyQualifiedName~SessionTargetIntegrationTests|FullyQualifiedName~InsertDefaultsIntegrationTests|FullyQualifiedName~CrudWriteGeneratorTests' --logger 'trx;LogFileName=engine-targeted-re-review.trx' --results-directory .review-artifacts/2026-10-17-followup
```

To rerun the independent acceptance probes, copy the two `.cs.txt` evidence files into `tests/AkmlSql.Web.Tests` as `.cs`, then run that project with `--filter 'FullyQualifiedName~ReReviewAuditTests|FullyQualifiedName~ReReviewSchemaAuditTests'`. They deliberately assert the desired safe behavior and currently fail. Integrate them properly when fixing the defects; do not weaken them to make the review green. The browser probe script assumes a Debug Web development server at `http://127.0.0.1:5077` and uses the Playwright package supplied by the E2E project.

## Fix completion matrix (2026-10-17, working tree on top of `1d09ef4c`, uncommitted)

Every reviewer probe was moved into a permanent suite with its assertions unchanged; each failed before its fix and passes after.

| ID | Status | Changed files | Before → after regression | Remaining limitations |
| --- | --- | --- | --- | --- |
| REV-01 | Fixed | `Services/ISqlConnectionService.cs` (`SchemaIdentity` = `"<server> (engine <host:port>)"`, `ScopeToEngine`), `Services/IAiSchemaContextProvider.cs` (target includes `ConnectionGeneration`), `Services/ISchemaCacheStore.cs` (`GetActiveAsync`), `Services/ICompletionService.cs`, `Services/IQuickInfoService.cs` (offline completion / quick info use the connected database's snapshot) | `Ai/AiSchemaEngineScopeTests` (probe + PostgreSQL localhost + legacy key), `CompletionServiceOfflineTests.Offline_completion_follows_the_connected_database…`; `StatusIndicatorTests`/`ConnectionSwitchTests` updated to the scoped key | The engine scope is the bridge URL's authority: two different engines answering at the same URL in one browser profile (a reinstall on the same machine) count as one. Old unscoped cache entries are no longer read and age out through LRU eviction. Two real engines not exercised. |
| REV-02 | Fixed | `Pages/History.razor` (`_fullSqlLoaded`, `HasCompleteSql`, excerpt notice + Retry; versions load separately) | `History/HistoryFullSqlTests` (probe; throw; retry; versions failing; stored version) | — |
| REV-03 | Fixed in scripts, **not executed** | new `src/AkmlSql.Installer/web-https-binding.ps1` (decision/ownership functions, `-RemoveOwned`), `web-iis-setup.ps1` (refuse foreign/other-site ports, rebind only owned, clean up owned binding on port/mode change, `https-site.ok`/`.failed` markers), `web-installer.iss` (ships the library; uninstall via `-RemoveOwned`; https URL only after success), `web-iis-repair.ps1` | `AkmlSql.Installer.Tests/HttpsBindingDecisionTests` (14: free/own/recorded/foreign/other site/record on another port; removal only while still owned; netsh parsing incl. localized labels) | Installer not run: install/repair/uninstall on a disposable IIS host with a foreign 443 binding, a port change, a LAN→localhost upgrade and pairing from a second machine remain to be done. Scripts parse; `ISCC` compiles. |
| REV-04 | Fixed | `Pages/Snippets.razor` (save snapshots the buffers; baseline = snapshot; `_saving` blocks a second Save) | `Snippets/SnippetSaveRaceTests` (probe; still a draft afterwards + same snippet updated; second Save waits; failed save keeps the draft) | Delayed engine sync covered through the same awaited store call, not with a live engine. |
| REV-05 | Fixed | `Services/IDiagnosticsRingBuffer.cs` (versioned envelope; legacy ring migrated on restore — AI entries purged, others masked — and rewritten at once) | `Diagnostics/DiagnosticsLegacyMigrationTests` (probe; prompt/schema markers; non-AI kept and masked; current format round-trips) | — |
| REV-06 | Fixed | Core `ExecuteResultSet.DefaultsKnown` (key 13); `ResultSetReader.cs`, `PgProvenanceCollector.cs`; `CrudWriteGenerator.cs` + `ApplyChangesHandler.cs` (`OUTPUT INSERTED.<ReturnColumns>` when no enabled trigger; else identity only); `ResultSetGrid.razor` (unknown defaults left to the server, "(not set)"; identity merged when no row) | grid `ResultsGridInsertDefaultsTests` (probe; stored row shown and editable by key; unknown defaults); engine `CrudWriteGeneratorTests.Insert_ReturningInserted…`, live `InsertDefaultsIntegrationTests` (returned row; trigger table; fresh table reports unknown); browser `ReviewFixBrowserTests.An_inserted_row_shows_what_the_server_stored…` (multi-statement batch, edit without rerun) | A trigger table's other trigger effects are not reflected (identity only). Temp tables are not editable at all (unchanged). PostgreSQL unchanged (already returned the row). |
| REV-07 | Fixed | `Services/IChatHistoryStore.cs` (`ChatTurn.IncompleteReason`, export note), `Shared/AiChatPanel.razor` (mark kept on the turn, shown under it; incomplete answers go into context with a cut-off note) | `Ai/AiChatIncompleteTurnTests` (probe; survives next message and export, complete turn unmarked, context note; retry replaces it) | The AI panel's one-shot actions are not persisted, so they have no reload case. |
| REV-08 | Fixed | `Pages/Snippets.razor` (reveal the active option after the keys move it), `wwwroot/js/akml-ui.js` (`revealInList`) | `js/akml-ui.test.mjs` (2), browser `ReviewFixBrowserTests.The_active_snippet_stays_visible_while_the_keys_move_it` (1440×900, all 22 snippets: failed on the previous build, passes now) | Screen-reader spot check not done. |
| SEC-01 | Fixed | `AkmlSql.Core.csproj`, `AkmlSql.Engine.csproj`, `tests/AkmlSql.E2E.Tests/*.csproj`: MessagePack 3.1.8 → **3.1.9**; Engine pins System.Security.Cryptography.Pkcs **9.0.21** (SqlClient's `>= 9.0.18`) | `dotnet list package --vulnerable --include-transitive`: 0 for every project in the solution; restore and the full MSBuild build show no NU190x; published Engine `deps.json` resolves MessagePack 3.1.9 and Pkcs 9.0.21; Web resolves MessagePack 3.1.9 | No exploit testing (as in the review). |

### Verification

| Check | Result |
| --- | --- |
| Full solution, MSBuild Release | Succeeded, 0 errors, no NU190x |
| Web .NET | 1,655 passed, 0 failed (four consecutive runs) |
| Web JavaScript | 133 passed, 0 failed |
| Engine (full, live local SQL Server tests included) | 2,469 passed, 0 failed, 128 skipped |
| Shell.Shared / PostgreSql / AI | 987 / 2,237 / 92 passed |
| Core | 1,455 passed, 1 failed (pre-existing `ProfileGetMessageTests` key count — no profile message changed), 3 skipped |
| Installer unit classes | 57 passed (incl. the 14 new) |
| Browser (Chromium, serial) | BROWSER_RESULT |

Two pre-existing test races were tightened while verifying (`ReconnectLoopTests.RevocationTerminatesLoop` now waits for the clean-up it asserts; one new snippet test waits for the list reload). Side effect: a syntax check with `ISCC /O-` again deleted `src/AkmlSql.Installer/Output/AKMLSQLSetup.exe` (untracked build output; the next installer build recreates it).
