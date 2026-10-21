# AKML Web fix verification — 2026-10-18

The two defects from the third review, **REV-06 and REV-09, pass their original acceptance tests** with the fixes already present in this checkout. This follow-up reviewed those staged changes, reproduced three additional edge cases, and corrected them. Existing staged work was preserved; this reviewer made no commits or deployment changes.

During this work another actor committed the preceding fixes as `4e31d6b9` and staged this follow-up's changes. The corrections described here remain in the working tree on that commit; unrelated generated site metadata was preserved.

## Findings corrected in this follow-up

| ID | Finding | Correction | Before/after evidence |
| --- | --- | --- | --- |
| R4-01 | An enabled `INSTEAD OF INSERT` trigger can discard or reroute an insert whose attempted key matches an existing row. Reading back by that key falsely returned the existing row as the new row, making it available for subsequent grid edits/deletes. | The target-catalog metadata query now identifies enabled `INSTEAD OF INSERT` events. Such inserts do not claim a returned row; the grid uses its unconfirmed-row state and requires a fresh SELECT. Normal AFTER-trigger and cross-database read-back remain supported. | New live SQL test `InsertReadBackIntegrationTests.An_INSTEAD_OF_insert_must_not_return_an_existing_row_with_the_attempted_key` failed before the fix: expected null, received `["7", "existing"]`. It passes after the fix, also checking the existing row and the trigger's audit insert. A new real-browser regression covers the collision and locked placeholder. |
| R4-02 | Unconfirmed-row protection was stored only in the grid instance. Recreating a grid for the same result dropped the warning and re-enabled editing/deleting a row whose stored key had not been confirmed. | Unconfirmed metadata follows the result object's lifetime using a weak-key table in the Web component. Grid recreation retains the warning and lock; a new result starts clean. No IPC fields were added. | New component test `ResultsGridInsertDefaultsTests.An_unconfirmed_row_stays_locked_when_its_result_grid_is_recreated` failed before the fix because Delete became enabled. It passes after the fix and checks F2, unknown cells, and a fresh query result. |
| R4-03 | Unknown inserted values were shown as unknown on screen but still copied or exported as SQL NULL, with a success notification. | Copy refuses selections containing unconfirmed rows; whole-result CSV/TSV export asks for a SELECT first. Copying confirmed rows remains available. View value also respects the unconfirmed state. | Three `Unconfirmed_rows_cannot_be_exported_as_fabricated_NULLs` cases failed before correction (Copy, CSV and TSV). They assert no clipboard/download call, a clear explanation, and that copying confirmed rows still works. The browser collision test also verifies the clipboard stays unchanged. |

The unconfirmed-row message now recommends checking values with a **SELECT query**, avoiding advice to repeat a potentially mutating original batch. It says the insert completed without claiming that a trigger necessarily stored a new row.

## Earlier defects

| ID | Verified disposition |
| --- | --- |
| REV-09 | Fixed: metadata is read from the target table's catalog, and cross-database inserts work with enabled, disabled and absent triggers. The original `Round3InsertAuditTests.Cross_database_triggered_insert_succeeds` passes unchanged. |
| REV-06 | Fixed in the covered cases: key-based read-back supplies defaults, generated keys and AFTER-trigger changes; unresolved inserts stay visibly unconfirmed and cannot be edited/deleted. The original `Round3InsertAuditTests.Triggered_insert_returns_defaults_for_the_grid` passes unchanged. Integration coverage includes composite/collated keys, generated GUID keys, subsequent update/delete and transaction rollback. |
| DEPLOY-01 | Still unverified: actual HTTPS install/repair/uninstall on a disposable IIS host and pairing from a second machine. This review did not modify installed services or bindings. |

## Verification performed here

Evidence: [`.review-artifacts/2026-10-18-fix-verification/`](.review-artifacts/2026-10-18-fix-verification/). That directory is ignored by Git; preserve it separately when transferring the report.

| Check | Result | Evidence |
| --- | --- | --- |
| Existing targeted Engine tests before the additional corrections | 111 passed, 0 failed, 0 skipped | `engine-baseline.trx` |
| New independent regressions before corrections | 1 live SQL failure and 1 component failure, as described above | `engine-new-before.trx`, `web-new-before.trx` |
| Export regressions before correction | 3 failures: Copy, CSV and TSV incorrectly reported success | `export-before.trx` |
| Targeted Engine tests after corrections | 112 passed, 0 failed, 0 skipped | `engine-after.trx` |
| Full Web .NET suite before final export regressions | 1,659 passed, 0 failed, 0 skipped | `web-after.trx` |
| Final full Web .NET suite, including all new regressions and async test correction | 1,662 passed, 0 failed, 0 skipped | `web-verified-rebuild.trx` |
| Full Web confirmation run after the async test correction | 1,662 passed, 0 failed, 0 skipped | `web-confirmation.trx` |
| Final grid tests after the message wording change | 9 passed, 0 failed, 0 skipped | `grid-final.trx` |
| Debug Engine build | Succeeded, 0 errors; existing warnings remain | `engine-debug.log` |
| Chromium: ReviewFix, ReviewRegression, SecretStorage, ResultsGrid (before final export guard) | 30 passed, 0 failed, 0 skipped | `browser-after.trx`, `browser-after.log` |
| Chromium: final trigger-collision and unchanged-clipboard regression on the final Web build | 1 passed, 0 failed, 0 skipped | `browser-export-final.trx`, `browser-export-final.log` |

One full Web run caught an existing test timing issue: `SnippetSaveRaceTests` checked the confirmation list immediately after an asynchronous click, before the handler had run. The test now waits for the same confirmation assertion and for Save to be enabled before the next save. Production snippet code and its behavioral assertions were not changed. That failure remains in `web-final.trx`; all three new export regressions passed in that run. A subsequent build without project references failed because the Core reference DLL was missing (`web-verified.log`); rebuilding dependencies restored it and the complete suite passed.

Live tests use uniquely named disposable tempdb objects and clean them up. Browser tests use fresh Debug Web/Engine builds and run serially. Full-solution, full-Engine, PostgreSQL-server, Firefox, screen-reader and deployment acceptance were not rerun in this follow-up. Earlier agents' results are not counted as this verification.

## Changes made by this follow-up

- `src/AkmlSql.Engine/Execution/InsertReadBack.cs`: safe handling of INSTEAD OF INSERT triggers.
- `src/AkmlSql.Web/Shared/ResultSetGrid.razor`: keep unconfirmed-row protection with the result and clarify refresh guidance.
- `tests/AkmlSql.Engine.Tests/Execution/InsertReadBackIntegrationTests.cs`: real SQL attempted-key collision regression.
- `tests/AkmlSql.Web.Tests/Grid/ResultsGridInsertDefaultsTests.cs`: grid recreation and fresh-result regression.
- `tests/AkmlSql.Web.E2E.Tests/ReviewFixBrowserTests.cs`: browser collision/locked-row regression.
- `tests/AkmlSql.Web.Tests/Snippets/SnippetSaveRaceTests.cs`: wait for asynchronous confirmation/save readiness while retaining the assertions.
- IPC documentation, project memory, progress and review status updated to describe the corrected behavior.

To repeat the targeted Engine verification:

```powershell
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Debug --no-restore --filter 'FullyQualifiedName~Round3InsertAuditTests|FullyQualifiedName~InsertReadBackIntegrationTests|FullyQualifiedName~InsertDefaultsIntegrationTests|FullyQualifiedName~CrudWriteGeneratorTests'
```
