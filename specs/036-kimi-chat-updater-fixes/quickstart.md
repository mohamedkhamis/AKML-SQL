# Quickstart / Validation: spec 036

**Date**: 2026-09-02 | **Branch**: `036-kimi-chat-updater-fixes`

Per the constitution's Development Workflow section, these scenarios are the acceptance gate for "done". Each names the FR it proves. Slices are independent — a slice may be signed off on its own.

## Prerequisites

| Need | For | Notes |
|---|---|---|
| SSMS 22 or VS 2026 with the extension deployed | all | build + deploy per `doc/deployment.md`; clear the MEF cache |
| A SQL Server database with ≥ 2 tables, varied column types, a PK and an FK | Slice D | schema questions need real answers to check against |
| A large database (> 500 objects) | Slice D, truncation | may be simulated by lowering `schemaContextMaxObjects` |
| A valid Kimi (Moonshot) API key | Slice B, live path | unit-level work needs no key |
| A clean Windows machine with the **previous** published build installed | Slice E, FR-046 | cannot be done on the dev box |

## Build and test

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Insiders/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet
"$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
dotnet test tests/AkmlSql.AI.Tests/AkmlSql.AI.Tests.csproj
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
dotnet test tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj
dotnet test tests/AkmlSql.Site.Tests/AkmlSql.Site.Tests.csproj
```

**Ratchet check (Constitution III)**: format-parity goldens and the completion-corpus gate must remain at or above their current pass rates. Nothing in this feature touches those paths, so any movement is a signal that something unintended changed.

---

## Slice A — Options navigation readability (Story 4)

1. **Hover an unselected nav item** in Light theme. Label stays readable. *(FR-001)*
2. **Hover the currently selected nav item** in Light theme. Label stays readable — this is the reported bug: white text on the near-white `#F1F5F9` hover background. *(FR-002)*
3. Repeat 1–2 in **Dark**, in the **host-derived** theme, and with Windows **High Contrast** active. *(FR-001, FR-002, FR-004)*
4. **Move the pointer off** an item. It returns to exactly its prior appearance, no residual colour. *(FR-005)*
5. **Type in the Options search box**, then hover results including the selected one. Same guarantee. *(FR-003)*
6. **Change the host theme while Options is open.** Hover states adopt the new theme with no unreadable combination. *(edge case)*
7. Run the contrast sweep test. Every state × theme combination meets 4.5:1; the test fails the build on regression. *(FR-004, SC-001)*

## Slice B — Kimi provider (Story 2)

8. **Open Options → AI Assistance.** "Kimi (Moonshot)" is in the provider list. *(FR-006)*
9. **Select it.** Model and endpoint pre-fill with working defaults; both remain editable. *(FR-007)*
10. **Paste a key.** It is masked. Check `akmlsql-*.log` afterwards: the key appears nowhere. *(FR-008)*
11. **Press Test connection.** Success within the configured timeout. Then break it four ways — wrong key, wrong model, unreachable endpoint, no network — and confirm each message names its cause and no raw provider JSON appears. *(FR-009, FR-014, SC-006)*
12. **Save, close, reopen Options.** Kimi still selected with the same model, endpoint and key. *(FR-010)*
13. **Inspect `config.json`.** `apiKey` starts with `dpapi:` — not plaintext. *(FR-008)*
14. **Backward compatibility**: hand-write a plaintext key into `config.json`, restart, run an AI action. It still works; the next save upgrades it in place. *(R4)*
15. **Exercise every AI feature** with Kimi active: chat, explain, fix, optimize, index analysis, text-to-SQL, ghost text. Each returns a result. *(FR-011)*
16. **Leave `gpt-4o` in the model box with Kimi selected.** The refusal names both vendors and the fix, before any network call. Then reverse it — `kimi-latest` under OpenAI — same guarantee. *(FR-012)*
17. **Select each of the eight providers in turn**, save, and run one AI action. None reports "Unknown AI provider" — this is the regression that Azure OpenAI and LM Studio fail today. *(FR-013)*
18. **Time a first-run**: from opening Options to a successful answer, under 2 minutes, no file editing, no docs. *(SC-002)*

## Slice C — Chat copy (Story 3)

19. **Ask for a query.** Copy the SQL block; paste. Only SQL, no prose, no ``` fences. *(FR-015)*
20. **Ask something producing two SQL blocks.** Each has its own copy action and it is clear which block each belongs to. *(FR-015)*
21. **Copy a whole message.** Prose and all SQL arrive. Do the same on one of your own messages. *(FR-016)*
22. **Drag across part of a bubble** and press Ctrl+C. Exactly the selection is copied. *(FR-017)*
23. **Copy the conversation.** Every turn present, in order, attributed. *(FR-018)*
24. **Confirm feedback**: success shows briefly; force a failure (hold the clipboard from another app) and confirm the user is told and the message is still there and still copyable. *(FR-019)*
25. **Tab to every copy control** and activate it by keyboard; check the accessible name with an inspector. *(FR-020)*

## Slice D — Schema access (Story 1) — the headline fix

26. **Connect an editor to the test database. Ask "what tables are in this database?"** The answer lists the real tables. Repeat 10 times; 10/10 must be correct. *(FR-022, FR-024, SC-003)*
27. **Ask about a specific table.** Real columns with types; the PK is named. *(FR-023)*
28. **Ask how two FK-related tables relate.** The real relationship, and a join on the correct columns. *(FR-023)*
29. **Ask "summarise my schema"** — no object names in the prompt. Still the real inventory. This is the noise-token case that fails today. *(FR-024, FR-025)*
30. **Ask a question containing a short noise word** that incidentally substring-matches one object (e.g. a prompt with "do" against a `Documents` table). The inventory is still complete. *(R6, FR-025)*
31. **Ask for a query joining two tables.** Generated SQL references only existing objects and columns; 9/10 attempts. *(SC-004)*
32. **Switch the editor to another database**, ask again. The answer reflects the new database and the header shows it. *(FR-027, Story 1 scenario 5)*
33. **Close all editors, ask a schema question.** The assistant says it has no connection and how to get one — it does not answer from nothing. *(FR-028)*
34. **Ask immediately after connecting**, while the cache is still loading. The user is told; the answer uses the schema once loaded. *(FR-029)*
35. **Against the large database** (or with a lowered budget): the answer notes the inventory was truncated, and the user sees the note. *(FR-026)*
36. **Set `privacyMode` to `anonymous`.** The assistant explains why it cannot name objects and which setting controls it. *(FR-030)*
37. **Repeat 26–28 through explain, fix, optimize, index analysis and text-to-SQL** — not chat alone. *(FR-031)*
38. **Verify ghost text stayed lean**: inline completion latency is unchanged from before this feature. *(contract: level 1)*
39. **Measure assembly cost** on the 500-object database: < 200 ms added per request. *(performance goal)*
40. **Confirm no row data** appears in any prompt — inspect a logged request. *(FR-032)*

## Slice E — Update channel (Story 5)

Local, without a clean machine:

41. **`Constants.UpdateManifestUrl`** points at the live site, not `updates.akmlsql.com`. *(FR-033)*
42. **Fetch the manifest in a browser** with no credentials. It loads. *(FR-034, FR-035)*
43. **Compare the manifest with the newest `releases.json` entry.** Same version, file and hash. *(FR-036, SC-010)*
44. **Run `--check` against a staged newer manifest.** `update-available.json` written with the version, notes URL and hash. *(FR-038)*
45. **Run `--check` when up to date.** No result file, nothing user-visible. *(FR-037)*
46. **Run `--check` offline / against a blocked host.** Exit 0, nothing shown, reason logged. *(FR-041)*
47. **Run `--download`.** File fetched, hash verified, `VerifiedInstallerPath` set, `DownloadState` = `verified`. *(FR-039)*
48. **Corrupt the staged file so the hash mismatches, re-run `--download`.** Aborts, deletes the file, exit 2, explicit reason. The installer never runs. *(FR-040)*
49. **Cancel mid-download.** No `.partial` remains; the update is still offered next time. *(FR-039a)*
50. **Use "Check for updates" from the menu with a recent `LastUpdateCheck`.** It still runs and reports its outcome. *(FR-042)*
51. **Reach the confirmation dialog.** It names the applications that must close; Cancel holds focus; the proceed button is not the default. Decline — nothing installs, nothing closes, the offer is retained. *(FR-039, spec scenario 4a)*

On a clean machine — **FR-046, cannot be automated**:

52. Install the **previous** published build. Populate it: change settings, run queries (history), save a snippet, create a format style, save a SQL credential.
53. Publish the newer release through the normal deploy path.
54. Let the automatic check run, or trigger it manually. The notification names the new version and links the notes. *(FR-038)*
55. Follow the flow through download, verification, confirmation and install. *(FR-039, FR-040)*
56. **Confirm the upgrade was in place** — no manual uninstall was required. *(FR-043)*
57. **Verify every artefact survived**: settings, query history, snippets, format styles, saved credentials. *(FR-043, SC-009)*
58. **Repeat with the hosts open.** The user is told what must close; the install completes with no half-installed state. *(FR-044)*
59. **Confirm three-way version agreement**: in-product version, installed version, published version. *(FR-045)*
60. **Record the evidence** — date, each step, each result — in `doc/progress.md`, referenced from `tasks.md`. *(FR-046, SC-011)*

---

## Sign-off

### Setup & baseline notes (recorded during implementation, 2026-09-02)

- **T002 verification database**: `AkmlSqlVerify` on `localhost` (SQL Server 2022, trusted connection).
  Objects: `dbo.Customers` (PK `CustomerId`; columns: `CustomerId` int, `Email` nvarchar(320),
  `FullName` nvarchar(200), `IsActive` bit, `CreatedAt` datetime2(3), `Balance` decimal(18,2)) and
  `dbo.Orders` (PK `OrderId` bigint; columns: `OrderId`, `CustomerId` int, `OrderNumber` varchar(20),
  `OrderedAt` datetime, `TotalAmount` money, `Notes` nvarchar(max), `PayloadRowVersion` rowversion;
  FK `FK_Orders_Customers` `Orders.CustomerId → Customers.CustomerId`; unique `UQ_Orders_OrderNumber`).
- **T003 fixture**: `specs/036-kimi-chat-updater-fixes/fixtures/update/update-manifest.json`
  (version 1.999.0902.0000 → real CDN asset of 1.26.0901.1502 with its real hash). Usage in the
  fixture README.
- **T005 ratchet floor (Constitution III)** — may only go up:
  - Format-parity goldens (`FormatParityTests`): **264/264 passed** (Release).
  - Completion corpus (`CorpusGateTests`): **OVERALL 1311/1343 = 97.6%**, 2/2 tests green
    (1,376 cases loaded; per-family thresholds at their spec-032 ratchet values).
- **T023 schema-context assembly perf** (2026-09-02, synthetic 500-object cache — 10 schemas ×
  50 tables, 8 columns each, 250 FKs, `SchemaContextAssemblyTests.Assembly_on_a_500_object_database_stays_under_200ms`,
  level 3 build + format, Release): **avg 5.8 ms, max 19 ms over 10 runs** [3, 3, 5, 5, 3, 9, 3, 3, 19, 5] —
  well under the 200 ms budget in `contracts/schema-context.md`. The test keeps an explicit
  assertion (average < 200 ms) so a regression fails the suite.
- **Environment caveat (this dev box)**: `PerformanceBaselineTests.Capture_or_compare_M0_baseline`
  crashes the test host on this machine — **verified pre-existing**: the identical crash reproduces
  on a pristine worktree of the base commit `054b5db`. It is excluded from suite runs here
  (`--filter "FullyQualifiedName!~PerformanceBaselineTests"`). Two load-sensitive flakes observed,
  both on spec-036-untouched paths and both documented as machine-timed: one engine test failed
  intermittently under memory pressure (1.9 GB free; suite green on a quiet re-run, 1734/1734),
  and `QuerySessionStoreTests.Retry_backs_off_between_busy_attempts...` sits 2 ms under its
  5900 ms threshold when the box is busy (its own comment calls the margin machine-specific;
  History paths are untouched by this spec). Neither is related to spec-036 paths.
- **Slice E — update channel local validation (2026-09-02, this dev box, scenarios 41–51)**.
  Fixture served via `fixtures/update/serve.ps1` (HttpListener on 127.0.0.1:8099); the updater
  ran as the real trimmed single-file publish
  (`dotnet publish src/AkmlSql.Updater -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`)
  with `Constants.UpdateManifestUrl` temporarily pointed at the fixture and `AKML_APP_DATA_ROOT`
  redirecting all state into a temp root. The constant was reverted afterwards —
  `git diff HEAD src/AkmlSql.Core/Constants.cs` shows only the FR-033 change.
  - **41 PASS** — `UpdateManifestUrl` is `https://akml.khamis.work/update-manifest.json`;
    asserted by `ConstantsTests`.
  - **42 PARTIAL (pending deploy)** — anonymous fetch works against the fixture (HTTP 200, no
    credentials). The live URL returns 404 today: the manifest is generated at deploy time
    (T058) and no deploy has run since; the fixture stands in until then.
  - **43 PENDING-DEPLOY** — the invariant is enforced by `UpdateManifestConsistencyTests`
    (Site.Tests, assert-only-when-present). The committed fixture mirrors the newest
    `releases.json` entry's file + hash; the generated manifest becomes comparable after a deploy.
  - **44 PASS** — `--check` against the newer fixture wrote `update-available.json` with version,
    downloadUrl, releaseNotesUrl and sha256Hash; exit 0.
  - **45 PASS** — fixture version lowered to 1.0.0: exit 0, the stale result file was deleted,
    nothing user-visible.
  - **46 PASS** — server stopped: exit 0, no result file, network error recorded in the log
    (FR-041).
  - **47 PASS** — `--download` fetched the real 75,466,260-byte GitHub CDN asset anonymously,
    SHA-256 matched, `verifiedInstallerPath` absolute, `downloadState=verified`, exit 0.
  - **48 PASS** — corrupted hash in the served manifest: exit 2, the downloaded file was deleted,
    `downloadState=failed`, `failureReason="checksum mismatch"`; the installer never ran.
  - **49 PASS (mechanism noted)** — a hard kill mid-download (what the progress window's Cancel
    does) caught the `.partial` on disk; the offer survived (`available=true`) and the next
    `--download` deleted the stale partial, re-downloaded and verified (exit 0, `verified`).
    The two real cancel paths are covered deterministically by unit tests: graceful in-proc
    cancellation (token → `finally` deletes the `.partial` → exit 0, state rolled back —
    `UpdateDownloaderTests.Cancelled_download_leaves_no_partial_and_keeps_the_offer`) and the
    shell's kill-then-clean path (`UpdateDownloadCleanupTests`). Delivering Ctrl+C to the
    hidden-console updater could not be automated from this Git Bash/mintty environment
    (console-control-event delivery to a detached console failed repeatedly), so the graceful
    console-cancel leg is asserted by the unit test rather than a live Ctrl+C.
  - **50 / 51 — covered by unit tests, in-host pass pending**: T054 (`UpdateCheckFlowTests` +
    `UpdateLauncherThrottleTests`: the manual check launches the updater directly with a recent
    `LastUpdateCheck`, and all three outcomes are reported) and T060
    (`UpdateInstallConfirmDialogTests`: Cancel is `IsCancel` + holds initial focus, "Install now"
    is not the default, the dialog names the new version and SSMS/Visual Studio).
  - **Additional finding fixed during T062**: the trimmed single-file updater had
    reflection-based System.Text.Json disabled, so every JSON call threw
    (`InvalidOperationException: Reflection-based serialization has been disabled`) — no
    published build could ever complete a check even against a live endpoint (a second,
    independent cause under research R10). Fixed with the source-generated `UpdateJsonContext`
    and a `JsonNode`-based `lastUpdateCheck` stamp; the publish now emits zero IL2026 trim
    warnings and every scenario above ran against the trimmed exe.

| Slice | Scenarios | Blocked on | Status (2026-09-02) |
|---|---|---|---|
| A — hover readability | 1–7 | nothing | **Automated: green.** FR-004 sweep (`OptionsHoverContrastTests`, 6 tests: 4 states × Light/Dark + HC token pairs, nav tree + search list) passes and gates the build. In-host visual sweep (1–6) and live-HC desktop check (3, T049) pending a manual pass |
| B — Kimi provider | 8–18 | a Kimi key for 11, 15, 18 | **Automated: green.** 8-provider round-trip, alias normalisation (31 cases), family guard both directions, Kimi factory, 5-cause failure taxonomy, key wrap/unwrap — all unit-green. Live-key scenarios (11/15/18) need a real Kimi key |
| C — chat copy | 19–25 | nothing | **Automated: green** (`AiChatPanelCopyButtonTests`, 6 tests incl. real clipboard-lock failure path). In-host pointer/keyboard pass (22, 25) pending |
| D — schema access | 26–40 | a test database | **Automated: green** (assembly/binding/panel tests; assembly perf 5.8 ms avg vs 200 ms budget). Verification DB `AkmlSqlVerify` provisioned on localhost; in-host scenarios (26–37) need an SSMS session against it |
| E — update channel | 41–51 local; 52–60 manual | a clean machine + a real published release | **Local: green** — 41–49 validated against the staged fixture with the real trimmed updater (results above); 50/51 covered by unit tests, in-host pass pending. **52–60 (FR-046): not done** — requires the clean machine and a real release |

A slice is done when its scenarios pass **and** the full solution builds green in one pass with the corpora ratchets unmoved.
