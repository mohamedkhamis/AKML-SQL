# AKML Web: third review of the fixes

**2026-10-18 update:** REV-06 and REV-09 now pass the original acceptance tests. Three further edge cases found during verification were also corrected. See [the latest fix verification](AKML_WEB_FIX_VERIFICATION_2026-10-18.md) for current results and the remaining HTTPS deployment validation gap. The dated review below is preserved as historical evidence.

Date: 2026-10-17. **Verdict: not all issues are done. Two confirmed defects remain.**

This supersedes the completion verdict in [the second review](AKML_WEB_RE_REVIEW_2026-10-17.md), while preserving its historical findings and evidence. The fixes substantially improve the application. Of REV-01 through REV-08, six are resolved in the tested paths, REV-03 is corrected in scripts with deployment acceptance still pending, and REV-06 remains partial. A regression in the REV-06 implementation is recorded below as REV-09. SEC-01's identified dependency versions are remediated.

## Scope and checkout

Reviewed the fixes introduced by `1e08a9c1` against `1d09ef4c`, including the Web UI, services, SQL Server write path, installer ownership scripts and package graph. The checkout advanced during the review to `3826c552` through another actor's commits. That added a schema-system toggle/high-contrast style change, a documentation result, and captured the two audit tests created during this review. The affected insert implementation did not change. No Git mutation was performed by this reviewer.

The original 22 findings remain documented in [the first review](AKML_WEB_REVIEW_2026-10-17.md). This review concentrates on independently validating the eight follow-up findings, security remediation and regressions in those changes. It is not a claim that every possible defect in the application has been found.

## Open defects for the fixing agent

### REV-09 — P2: cross-database inserts fail on tables with triggers

**Status: new regression, reproduced against local SQL Server.**

Location: [ApplyChangesHandler.cs](src/AkmlSql.Engine/Execution/ApplyChangesHandler.cs), `HasEnabledTriggersAsync`, lines 213–223; the `returnInserted` decision at lines 116–118.

The new trigger check queries the connection's `sys.triggers`, but passes the fully qualified target table to `OBJECT_ID`. When the connection is in `master` and the grid edits a table in `tempdb`, these refer to different databases. The trigger is missed and the handler generates `OUTPUT INSERTED` without `INTO`. SQL Server rejects the insert. The whole Apply transaction is rolled back, including other edits in that Apply.

Reproduction:

1. Use a connection whose current database is `master`.
2. Create a uniquely named table in `tempdb.dbo` with an identity key and an enabled `AFTER INSERT` trigger. An empty trigger with `SET NOCOUNT ON` is sufficient.
3. Select that table through its three-part name and add a row through the grid. The corresponding Apply request has `BaseCatalog = "tempdb"` and nonempty `ReturnColumns`.
4. Apply fails with: `The target table ... cannot have any enabled triggers if the statement contains an OUTPUT clause without INTO clause.`

Independent failing test: `Round3InsertAuditTests.Cross_database_triggered_insert_succeeds`. The transport test uses the actual router and SQL execution path, with a master session and a disposable tempdb table/trigger. Cleanup runs in `finally`.

**Required correction:** resolve trigger metadata in the target catalog, with safely quoted identifiers; do not assume the session database is the result table's database. Alternatively, use an insert-return strategy that safely handles triggered tables without this incorrect decision. Keep transaction rollback and parameterized values intact.

**Acceptance:** the independent test passes; same-database and cross-database inserts work with and without enabled triggers; disabled triggers do not cause failures. Verify the three-part-name grid flow in a browser and retain all-or-nothing Apply behavior.

### REV-06 — P2: triggered inserts still leave incorrect values in the grid

**Status: partially fixed, independently reproduced. This is the remaining portion of REV-06 / WEB-20.**

Locations:

- [ApplyChangesHandler.cs](src/AkmlSql.Engine/Execution/ApplyChangesHandler.cs), identity-only branch at lines 147–155.
- [ResultSetGrid.razor](src/AkmlSql.Web/Shared/ResultSetGrid.razor), `BakeCommittedChangesIntoSet`, especially lines 1680–1685, and `ApplyAsync` around line 1738.
- [Editor.razor](src/AkmlSql.Web/Pages/Editor.razor), `OnAppliedReExecuteAsync`, lines 1186–1201.

The fix returns actual inserted values for tables without enabled triggers and supplies the identity for triggered tables. However, the triggered path returns `Row = null`. The grid fills only the identity and the values typed by the user, leaving other columns null or stale. For a multi-statement query, the editor correctly avoids rerunning the original batch, so the incorrect display remains after Apply reports success and clears the edit state.

Reproduction:

1. Create a disposable table with `Id int IDENTITY PRIMARY KEY`, `Name nvarchar(50) NOT NULL`, and `Tag nvarchar(50) NOT NULL DEFAULT N'tagged'`.
2. Add an enabled, empty `AFTER INSERT` trigger.
3. Connect to that same database and load the result with a multi-statement batch, for example `SET NOCOUNT ON; SELECT Id, Name, Tag FROM dbo.[table];`.
4. Insert a row supplying only `Name = 'first'` and apply it.
5. The server inserts the default, but the returned row is null. The grid's local merge can show the Tag as NULL despite its NOT NULL stored value. Trigger-modified values and server-generated nonidentity keys have the same missing-return problem.

Independent failing test: `Round3InsertAuditTests.Triggered_insert_returns_defaults_for_the_grid`. It expects `["1", "first", "tagged"]`; actual `CrudEditResult.Row` is `null`.

The new existing test `InsertDefaultsIntegrationTests.An_insert_returns_the_stored_row_and_a_trigger_table_its_identity` explicitly asserts a null Row for the triggered case. Its passing result therefore does not close this acceptance gap. This limitation was also acknowledged in the previous completion matrix; it should not have been labeled fully fixed.

**Required correction:** obtain and return the actual stored row after trigger effects, using a safe key-based read or another strategy that preserves correct correlation to each insert. Preserve typed/default/explicit-NULL semantics. If a row cannot be read back reliably, represent it as needing refresh and prevent operations against fabricated keys; do not display assumed values as committed facts. Never rerun a mutating original batch to refresh it.

**Acceptance:** the independent test passes and a browser test shows identity, defaults and trigger-modified values after Apply from a multi-statement batch, with no repeated side effects. Include a server-generated nonidentity key and confirm a subsequent edit/delete targets the inserted row correctly.

## Recheck of the previous findings

| ID | Current verdict | Evidence and limits |
| --- | --- | --- |
| REV-01 | Resolved in tested paths | SQL Server and PostgreSQL cache keys include the engine authority; active-target generation is rechecked. `AiSchemaEngineScopeTests` and offline completion tests pass. Two engines were simulated through the real connection/cache/provider services; no two-machine deployment was exercised. Replacing an engine behind the exact same URL remains outside that identity guarantee. |
| REV-02 | Resolved | History distinguishes full SQL from the search excerpt; Copy/Open/Re-execute are gated until complete text or a stored version exists. Null, thrown retrieval, retry and version-service failures are covered in `HistoryFullSqlTests`. |
| REV-03 | Script correction verified; deployment acceptance pending | Binding decisions refuse a foreign certificate/other IIS site, and removal consults the recorded port and certificate. All 14 `HttpsBindingDecisionTests` pass. Actual install/repair/uninstall and second-machine HTTPS pairing were not run. See DEPLOY-01 below. |
| REV-04 | Resolved in tested paths | Save captures the submitted snippet buffers and updates the baseline to that snapshot. New typing stays dirty; concurrent Save is disabled. `SnippetSaveRaceTests` covers delayed/failed saves. |
| REV-05 | Resolved for identified legacy diagnostic path | Legacy bare-array rings migrate immediately to the versioned envelope; old AI message/data payloads are removed and other messages redacted. `DiagnosticsLegacyMigrationTests` passes. This is not a guarantee about arbitrary future structured diagnostic data. |
| REV-06 | **Partial: still open** | Unknown defaults are allowed to reach the server, and ordinary insert rows are returned. Triggered insert values still fail the independent acceptance test. The new cross-database trigger regression is REV-09. |
| REV-07 | Resolved | Incomplete reason stays on the chat turn through persistence, later messages and Markdown export; subsequent model context marks the cut-off answer. `AiChatIncompleteTurnTests` passes. |
| REV-08 | Resolved | Keyboard changes schedule reveal of the active snippet within its list. Both added JS tests and the real Chromium Home/End visibility test pass. Screen-reader testing remains unperformed. |
| SEC-01 | Identified versions remediated | Engine assets and published Engine deps resolve MessagePack 3.1.9 and Pkcs 9.0.21. Web assets resolve MessagePack 3.1.9. Independent Web and Engine NuGet vulnerability queries report no vulnerable packages for their current sources. This is a dependency audit, not exploit testing or a whole-solution audit. |

### DEPLOY-01 — Remaining HTTPS acceptance work

This is a validation gap, not another reproduced application defect. On a disposable IIS host, verify a foreign certificate binding survives install, repair and uninstall; changing the AKML HTTPS port cleans up only its previous owned binding; a LAN-to-localhost change preserves unrelated bindings; failure is reported accurately; and a browser on a second machine can securely pair and retain its token. Unit-tested ownership decisions alone do not establish those outcomes.

## Independent verification

All evidence for this round is under [`.review-artifacts/2026-10-17-round3/`](.review-artifacts/2026-10-17-round3/). This directory is now ignored by the repository, so retain the local evidence separately if handing the report to an agent without this workspace.

| Check | Result | Evidence |
| --- | --- | --- |
| Full Web .NET suite, review-fix build | 1,655 passed, 0 failed, 0 skipped | `web-retry.trx`, `web-retry.log` |
| Full Web .NET suite, final `3826c552` checkout including the menu-style change | 1,656 passed, 0 failed, 0 skipped | `web-final.trx`, `web-final.log` |
| Web JavaScript | 133 passed, 0 failed | `js.log` |
| Targeted existing Engine tests plus independent probes | 93 existing tests passed; **2 new acceptance probes failed**; 0 skipped | `engine-probes.trx`, `engine-probes.log` |
| HTTPS binding decisions | 14 passed, 0 failed, 0 skipped | `installer.trx`, `installer.log` |
| Chromium: ReviewFixBrowserTests, SecretStorageTests, ReviewRegressionTests | 17 passed, 0 failed, 0 skipped | `browser.trx`, `browser.log` |
| Web and Engine dependency audit | Neither reports vulnerable packages from current sources | `web-package-audit.log`, `engine-package-audit.log`, `resolved-packages.txt` |

The browser suite exercised fresh Debug Web/Engine builds containing the review fixes. It covered snippet keyboard visibility, inserted-row values on ordinary tables, frozen grid editing, unload protection, browser secret storage, history/export and existing cross-database update flows. It did not cover the two triggered-insert defects; those were reproduced by the independent live Engine tests. The later schema-toggle style commit was inspected separately and was not part of that served browser bundle; the final 1,656-test Web run includes it.

The first Web build attempt failed while a Core DLL disappeared from the output directory; another build/publish was running concurrently. The retry succeeded. That first environmental failure is retained in `web.log`, not counted as a confirmed application bug.

The two probe tests were initially temporary. A concurrent commit included `tests/AkmlSql.Engine.Tests/Execution/Round3InsertAuditTests.cs`; its content is preserved as committed, and a copy is also saved as `Round3InsertAuditTests.cs.txt` in the evidence folder. These are deliberately failing acceptance tests, not a reason to weaken or delete the assertions. They require the local SQL Server and create/drop only uniquely named tempdb objects.

No production fixes, deployment, installed service changes, full solution build, full Engine suite, live PostgreSQL, remote SQL certificate handshake, Firefox or screen-reader acceptance were performed by this review. Tests and script inspection are reported at their actual scope.

## Commands to reproduce

```powershell
# The two live SQL acceptance failures. Requires local Windows-integrated SQL access.
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~Round3InsertAuditTests

# Web component/service tests and JavaScript tests.
dotnet test tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj -c Debug --no-restore
node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"

# Pure installer decision tests; does not install or modify bindings.
dotnet test tests/AkmlSql.Installer.Tests/AkmlSql.Installer.Tests.csproj -c Debug --no-restore --filter FullyQualifiedName~HttpsBindingDecisionTests

# Build Debug prerequisites before the browser suite; do not rebuild them during it.
dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug --no-restore
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Debug --no-restore
dotnet test tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~ReviewFixBrowserTests|FullyQualifiedName~SecretStorageTests|FullyQualifiedName~ReviewRegressionTests' -- xUnit.ParallelizeTestCollections=false
```

## Handoff prompt

> Read AGENTS.md, PROJECT_MEMORY.md, CLAUDE.md, the constitution and this report. Fix REV-09 and the remaining portion of REV-06. First reproduce the two Round3InsertAuditTests failures against disposable tempdb tables. Keep the desired-behavior assertions, then add coverage for the catalog boundary, actual post-trigger values, server-generated nonidentity keys and the multi-statement browser flow. Preserve transaction semantics, parameterization, default-versus-NULL behavior and the rule against rerunning mutating batches. Keep the earlier regression suites passing. Record DEPLOY-01 separately until actual disposable-host evidence exists. Update the completion matrix with changed files and real test results. Preserve unrelated edits and evidence; follow the repository's consent rules for Git mutations and deployment.

## Completion matrix (fixing agent, 2026-10-18)

Uncommitted working-tree changes on `042-postgresql-support` over `3826c552`. The independent `Round3InsertAuditTests` assertions are unchanged and now pass.

| ID | Status | Fix | Evidence |
| --- | --- | --- | --- |
| REV-09 | **Fixed** | `InsertReadBack.LoadAsync` reads enabled triggers and the read-back key from the **target catalog's** `[catalog].sys.triggers` / `sys.indexes` / `sys.columns`, with `OBJECT_ID` over the quoted three-part name; the session database is never assumed. The old `HasEnabledTriggersAsync` is gone. | `Round3InsertAuditTests.Cross_database_triggered_insert_succeeds` passes; `InsertReadBackIntegrationTests` covers enabled, disabled and no trigger from a `master` session, plus all-or-nothing rollback across databases; `CrudWriteGeneratorTests.ReadBack_Metadata_ReadsTheCatalogViewsOfTheTableDatabase`; browser `A_three_part_name_insert_into_a_triggered_table_with_a_generated_key_applies_from_master`. |
| REV-06 (remainder) | **Fixed** | Every SQL Server insert with `ReturnColumns` writes its key `OUTPUT INSERTED.<key> INTO @akml_k` (allowed with triggers), takes `SCOPE_IDENTITY()` into an output parameter, and selects the stored row **by that key after the triggers ran**. The key is the PK, else the narrowest unfiltered unique index, else the identity column. Key columns are declared with their own type and collation. All values stay parameters, and the Apply's single transaction is unchanged. A keyless table with no trigger keeps plain `OUTPUT INSERTED`. When the stored row cannot be named (no key plus a trigger, an INSTEAD OF trigger, a trigger that changed the key), `Row` is null and the grid marks the row **not read back**: unknown cells show "…" (never NULL), the row cannot be edited or deleted, and the Apply message asks for a re-run. Nothing re-runs a batch. | `Round3InsertAuditTests.Triggered_insert_returns_defaults_for_the_grid` passes. `InsertReadBackIntegrationTests` (7): trigger-modified values in the same and another database, a `NEWSEQUENTIALID()` key read back and then used by an UPDATE and a DELETE, a composite BIN2-collated key across databases (a mutation check without COLLATE fails with a collation conflict), an INSTEAD OF trigger, keyless tables. `InsertDefaultsIntegrationTests` now expects the stored row for the triggered table. bUnit: `ResultsGridInsertDefaultsTests` (not-read-back row: unknown cells, no edit/F2/delete, marker kept across the next Apply; read-back row has none). Browser: `A_triggered_insert_from_a_batch_shows_the_stored_row_and_the_next_edit_and_delete_find_it` (multi-statement batch with its own side effect: identity, default and trigger value shown, batch ran once, next edit and delete hit the right rows). |
| DEPLOY-01 | Open (validation gap) | Not attempted: needs a disposable IIS host and a second machine. | — |

Both new browser tests were also run against the HEAD engine files: both failed as expected (the triggered row showed "…" instead of `tagged`; the cross-database Apply failed and stayed pending), then passed on the fixed engine.

Changed files: `src/AkmlSql.Engine/Execution/InsertReadBack.cs` (new), `ApplyChangesHandler.cs`, `CrudWriteGenerator.cs`; `src/AkmlSql.Web/Shared/ResultSetGrid.razor`, `wwwroot/css/components/results-grid.css`; `src/AkmlSql.Core/Ipc/Messages/ExecuteQueryMessages.cs` (doc comments only, no wire change); `doc/ipc-api.md`; tests `InsertReadBackIntegrationTests.cs` (new), `CrudWriteGeneratorTests.cs`, `InsertDefaultsIntegrationTests.cs`, `ResultsGridInsertDefaultsTests.cs`, `ReviewFixBrowserTests.cs`, `SnippetSaveRaceTests.cs` (waits for the save to reach the store before typing; the intermittent full-suite failure did not recur in 8 runs; assertions unchanged).

| Run | Result |
| --- | --- |
| Engine, full suite | 2,486 passed, 128 skipped, 1 failed: `RefactoringPerformanceTests.SC001` (100 ms budget, run while the browser suite loaded the machine). It passed 3/3 when re-run alone. |
| Engine, insert/write-path set after the final build | 116 passed, 0 failed |
| Web .NET | 1,658 passed, 0 failed (8 consecutive full runs green after the snippet test change) |
| Web JavaScript | 133 passed, 0 failed |
| Chromium, serial: ReviewFixBrowserTests, SecretStorageTests, ReviewRegressionTests, ResultsGridTests | 29 passed, 0 failed, 0 skipped |

Seen once, not touched: `NotificationHostTests.An_info_toast_is_a_polite_status_and_leaves_on_its_own` (failed once in six full Web runs with `ElementRemovedFromDomException` on the toast; not investigated).
