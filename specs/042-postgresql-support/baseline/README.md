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
  access; flaky, `OpenClipboard failed`; passes or fails with what else is using the clipboard).
- **Engine**: `PerformanceBaselineTests.Capture_or_compare_M0_baseline` (environmental drift; about 17 minutes by itself).

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
| `Ipc.HistoryUs5ContractTests.Record_request_is_draft_is_key_12` | asserts `IsDraft` is at position 12 instead of a total length of 13 | `HistoryRecordRequest` gains `Kind` at key 13 |
| `InProcess.AllMessageTypesInProcessTests` | `RedactRequest` (223) added to `ExpectedUnwired` with its reason | the handler is task T117 (US4); the entry is removed when it lands. The credential pair (221) is registered |

