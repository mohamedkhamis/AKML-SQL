# Pre-feature test baseline (spec 042, task T001)

SC-003 and FR-091 require every existing automated gate to produce results identical to the state before this feature.
This folder holds that state, so the comparison at the end of each story is mechanical and not a memory.

## How it was captured

- **Source**: a pristine export of commit `97efd46` (`git archive HEAD`, no working-tree changes), built and tested in a
  scratch folder so the baseline could run while implementation proceeded. The machine-specific performance baseline folder
  `tests/AkmlSql.Engine.Tests/baselines/` (git-ignored, recaptured per machine) was copied into the export.
- **Date**: 2026-10-14. **Configuration**: Debug, `dotnet test --logger trx`, one suite after another.
  Shell.Shared was re-run alone (its clipboard tests collide with anything else touching the clipboard).
- **Results**: `.trx` files are kept outside the repository in `%LOCALAPPDATA%\AKML SQL\spec042-tests\before\`
  (about 14 MB). The sorted names of the tests that passed are here, one file per suite: `<Suite>.passed.txt`.

## How to compare

```powershell
# run the suites, saving trx files named <Suite>.after.trx (Shell.Shared.after.trx -> suite "Shell"), then:
powershell -NoProfile -File scripts/compare-test-results.ps1 `
  -Before "$env:LOCALAPPDATA\AKML SQL\spec042-tests\before" `
  -After  "$env:LOCALAPPDATA\AKML SQL\spec042-tests\after" `
  -OutDir specs/042-postgresql-support/baseline/after
```

A test that passed before and does not pass after is a regression. The allow-list below is the only exception.

## Counts and known reds at the baseline

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Core | 1349 | 1 | 3 |
| IntelliSense | 25 | 0 | 0 |
| Analysis | 5 | 0 | 0 |
| Formatting | 1635 | 0 | 0 |
| AI | 56 | 0 | 0 |
| Web | 1129 | 66 | 0 |
| Shell.Shared (net472) | 986 | 1 | 0 |
| Engine | 2133 | 1 | 1 |

Failures that already existed at `97efd46` (none is caused by spec 042; each also fails in the main working tree):

- **Core**: `Ipc.ProfileGetMessageTests.Response_key_layout_is_positional_and_append_only` (expects 7 keys, the type has 9).
- **Web**: 66 cases of `Services.FormatterServiceTests.Formatter_MatchesIdeBaseline_AcrossCorpusAndProfiles` (the known
  pending golden baselines for the sp031 corpus and several others).
- **Shell.Shared**: `AiChatPanelCopyButtonTests.A_second_click_inside_the_flash_window_still_restores_the_true_label` (clipboard
  access; flaky, `OpenClipboard failed`; passes or fails with what else is using the clipboard). In the full run at the end of user
  story 3 three more tests of the same class failed with `CLIPBRD_E_CANT_OPEN` (`Message_text_is_a_selectable_read_only_text_host`,
  `Copy_conversation_attributes_and_orders_every_turn`, `Copy_conversation_falls_back_to_Assistant_when_an_answer_has_no_agent_name`);
  run alone the whole class passed (9 of 9), and nothing in `AkmlSql.Shell.Shared` was changed by this spec.
- **Engine**: `PerformanceBaselineTests.Capture_or_compare_M0_baseline` (environmental drift; about 17 minutes by itself).

## Gate results per user story

Each story ends with the same comparison (a copy of the working tree, every suite built and run once, then
`scripts/compare-test-results.ps1`). `after/*.passed.txt` is the latest. A test that passed before and does not pass after is
re-run alone before it is called a regression.

| After | Passed (Core / IntelliSense / Analysis / Formatting / AI / Web / Shell / Engine) | Regressions |
|---|---|---|
| US4, 2026-10-14 | 1450 / 25 / 5 / 1635 / 92 / 1407 / 985 / 2285 | none. Three tests failed in the full run and passed alone: two history timing tests that ran while other builds and tests shared the machine, and one clipboard test of the known flake (`AiChatPanelCopyButtonTests`) |
| US5, 2026-10-14 | 1448 / 25 / 5 / 1635 / 92 / 1424 / 987 / 2298 | none. `compare.txt` in `after-us5`. Failing in the full run and failing before the feature too: `ProfileGetMessageTests.Response_key_layout_is_positional_and_append_only` (Core), the 66 `FormatterServiceTests` sp031 baselines (Web), `PerformanceBaselineTests` (Engine) |
| US6, 2026-10-14 | 1454 / 25 / 5 / 1635 / 92 / 1437 / 986 / 2350 | none. `compare.txt` in `after-us6`. One test failed in the full run and passed alone (9 of 9): the known clipboard flake `AiChatPanelCopyButtonTests.Copy_conversation_falls_back_to_Assistant_when_an_answer_has_no_agent_name`; nothing in `AkmlSql.Shell.Shared` changed. The failures that also failed before the feature are the same three as for US5 |

| US7, 2026-10-14 | 1452 / 25 / 5 / 1634 / 92 / 1452 / 986 / 2364 (full run, before the three fixes below) | four flagged, all resolved. `compare.txt` in `after-us7`. Two Core key-layout tests and the shell's editor-schema snapshot are deliberate edits (see below); the fourth is the known clipboard flake `AiChatPanelCopyButtonTests`, which passes alone. The failures that also failed before the feature are the same three as for US5 |
| US8, 2026-10-14 | 1457 / 25 / 5 / 1635 / 92 / 1474 / 987 / 2376 | one flagged, not a regression. `compare.txt` in `after-us8`. `RefactoringPerformanceTests.SC001_RemoveSemicolons_Under100ms_On2000LineDocument` (a timing test of the T-SQL refactoring) failed once in the full run and passes alone, 5 of 5; nothing it calls changed. The failures that also failed before the feature are the same three as for US5 |
| Final, 2026-10-15 | 1457 / 25 / 5 / 1635 / 92 / 1474 / 986 / 2376 | two flagged, neither a regression. `compare.txt` in `final`. `HistoryScaleTests.A_free_text_search_over_names_and_servers_comes_back_quickly` (a History search timing test) failed once in the full run and passes alone; `AiChatPanelCopyButtonTests` (the known clipboard flake) fails alone too, with `CLIPBRD_E_CANT_OPEN`, because another process held the Windows clipboard; nothing of the shell changed. The failures that also failed before the feature are the same three as for US5 |

Skipped by design: three `Theme` tests in Core (`[Fact(Skip = ...)]`) and `WebSocketTransportLanTests.LanMode_round_trip_wss_handshake`
(needs elevation).

## Deliberate edits to existing tests

A change that must touch an existing test is listed here with its reason, so the comparison stays honest. The test names and
outcomes do not change.

| Test | Edit | Reason |
|---|---|---|
| `History.HistorySchemaV3Tests` (v2 to current upgrade) | expected `schema_version` `"3"` becomes `"4"` | history schema v4 adds the `kind` columns (data-model §10) |
| `History.HistorySchemaVersionTests` | expected `schema_version` `"3"` becomes `"4"` | same |
| `Ipc.ExecuteQueryMessagesCompatTests` (two tests) | the array length of `ExecuteMessageDto` (10) and `ExecuteQueryResult` (13) is no longer pinned; the positions of the existing keys still are | spec 042 appends keys 10 to 13 and key 13; a length pin fails on every append-only change, which is the opposite of what the test is for. The history search test beside it already pins position, not length |
| `Ipc.ExecuteQueryMessagesCompatTests.The_result_and_set_keys_are_appended_in_contract_order` (result set part) | the array length of `ExecuteResultSet` (12) is no longer pinned; `BatchIndex` is still asserted at position 11 | spec 042 appends `ReadOnlyReason` at key 12 (user story 6); the new `EditMessagesContractTests` pins the new key by position |
| `Ipc.HistoryUs5ContractTests.Record_request_is_draft_is_key_12` | asserts `IsDraft` is at position 12 instead of a total length of 13 | `HistoryRecordRequest` gains `Kind` at key 13 |
| `PostgreSql.NoTsqlOnPostgresSessionTests.Until_PostgreSQL_formatting_is_routed_in_the_T_SQL_formatter_refuses...` | replaced by `A_PostgreSQL_session_is_formatted_by_the_PostgreSQL_formatter` | spec 042 user story 7 routes PostgreSQL formatting in; the test said it would be replaced then. It also asserts the T-SQL parser count did not move |
| `Refactoring.RefactoringServiceTests` (test double `TestFormatterService`) | implements the two new members of `IFormatterService` | `FormatAsync` / `PreviewAsync` (user story 7); no assertion changed |
| `InProcess.AllMessageTypesInProcessTests` | `RedactRequest` (223) added to `ExpectedUnwired` with its reason | the handler is task T117 (US4); the entry is removed when it lands. The credential pair (221) is registered |
| `Ipc.FormattingContractTests.Format_request_carries_actions_as_key_5` and `...Format_selection_request_carries_actions_as_key_5` | the request array length (6) is no longer pinned; `Actions` is still asserted at position 5 | spec 042 appends `ProfileJson` as key 6 to the format requests |
| `SqlPrompt.EditorSchemaFixtureTests.The_shell_test_snapshot_matches_the_catalog` | the snapshot `tests/AkmlSql.Shell.Shared.Tests/Fixtures/sqlprompt-editor-schema.json` is regenerated (`AKML_UPDATE_SNAPSHOTS=1`) | the editor schema is version 2003: `noEffectForPostgreSql`, `noEffectReason`, `samplePostgreSql` are appended to the options and groups |

