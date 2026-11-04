# Baseline and verification log — spec 046 AI Chat

Evidence for the acceptance gate in [quickstart.md](./quickstart.md). "Before" records the state on
2026-11-04 before any spec 046 change; "After" is filled as each phase lands. SSMS scenarios are
run by the owner (they cannot be automated); automated counts are recorded by the implementer.

## Before (2026-11-04)

### The reported refusal

- **Owner's report**: after running SQL the assistant produced, the owner copied the error text from
  the results **Messages** pane, pasted it into the chat and sent it; the chat answered
  `No database connection — this chat answers questions about the database the active SQL editor is
  connected to. …` while the header still showed `dbdevtestvm.westeurope.cloudapp.azure.com.dbCentralQuota`.
  "fix it" was refused the same way.
- **Owner's reproduction of quickstart S1-1 on the current build**: _pending — owner to run: click
  once inside the Messages pane of a connected query window, click into the chat, send any question;
  record "refused" or "answered" here._

### Log evidence (`%AppData%\AKML SQL\logs\akmlsql-20261104.log` and `_001.log`)

| Time (UTC+2) | Line |
|---|---|
| 16:57:31 | `AiChatPanelCommand: AI Chat tool window shown` |
| 16:58:31 | engine `AiChat: success, tokens=2574, latency=2643ms` (session 9d22d98e…) |
| 16:59:36 | engine `AiChat: success, tokens=2720, latency=2023ms` |
| 17:03:53 | engine `AiChat: success, tokens=4106, latency=13465ms` — the last chat request that reached the engine |
| 17:03:54 → 17:19:40 | `TryDetectConnection(provider): window caption = 'SQLQuery…sql - dbdevtestvm….dbCentralQuota (testuser (…))'` every ~2 s — the header's source kept naming a connected query window |
| 17:04:01 | `ExecutionCommandFilter: intercepted Query.Execute` — the query was run in that window |
| after 17:03:53 | no `AiChat:` line at all, and no line from the refusal path (it logged nothing) |

Per-view document matching works for unsaved windows (`SsmsConnectionDetector: matched text view to
document 'SQLQuery1.sql - …'` at 16:57:25 and `'SQLQuery2.sql - …'` at 17:09:37), and a brand-new
window's caption is just `SQLQuery2.sql` for about a second before SSMS adds the connection (17:09:35).

### Automated suites before any change (2026-11-04, master + spec docs only)

| Suite | Filter | Passed | Failed | Skipped |
|-------|--------|--------|--------|---------|
| `tests/AkmlSql.Shell.Shared.Tests` | `FullyQualifiedName~AiChat` | 51 | 1 | 0 |
| `tests/AkmlSql.Web.Tests` | `FullyQualifiedName~AiChat` | 15 | 0 | 0 |
| `tests/AkmlSql.Engine.Tests` | `FullyQualifiedName~Agent\|FullyQualifiedName~AiSessionBinding` | 33 | 0 | 0 |
| `tests/AkmlSql.Core.Tests` | `Category!=InstallerSmoke` | 1585 | 0 | 1 |

The one shell failure is environmental, not a product defect:
`AiChatPanelCopyButtonTests.A_second_click_inside_the_flash_window_still_restores_the_true_label`
threw `COMException: OpenClipboard Failed (CLIPBRD_E_CANT_OPEN)` — another process held the
clipboard while the owner's desktop was in use. It is the only clipboard-dependent test that
failed and it touches no code this spec changes before US2. The Core skip is
`HardcodedHexScannerTests.NoHardcodedChromeHex`, skipped by design.

### Browser test prerequisites

- `dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug`: 0 errors (2 existing warnings).
- `dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Debug`: 0 errors (23 existing warnings).
- Playwright browsers present in `%LocalAppData%\ms-playwright`: `chromium-1248`,
  `chromium_headless_shell-1248`, `firefox-1509`. The browser suites can run on this machine.

## Results by scenario

| # | Scenario | Result | Build / date | Notes |
|---|----------|--------|--------------|-------|
| S1-1 | Messages pane, then send | | | |
| S1-2 | Send right after execute | | | |
| S1-3 | Results grid, then send | | | |
| S1-4 | Second window, other database | | | |
| S1-5 | Object Explorer active | | | |
| S1-6 | Disconnected window | | | disconnected caption text: |
| S1-7 | No query window | | | |
| S2-1 | Two code panels, rendered prose | | | |
| S2-2 | Insert block 2 | | | |
| S2-3 | Follow-scroll | | | |
| S2-4 | Themes and screen reader | | | |
| S3-1 | Ctrl+Enter in SSMS 22 | | | |
| S3-2 | Send tooltip states | | | |
| S3-3 | Control tooltips | | | |
| S4-1 | Selector labels | | | |
| S4-2 | Switch and note | | | |
| S4-3 | Selection persists | | | |
| S5-1 | New chat | | | |
| S5-2 | No earlier context | | | |
| S5-3 | Starter question | | | |
| W1 | Web selector | | | |
| W2 | Web switch changes shared agent | | | |
| W3 | Web tooltip and Ctrl+Enter | | | |
| W4 | Web code panel and Insert at caret | | | |
| W5 | Web New chat while streaming | | | |
| W6 | Web export keeps fences | | | |

## After (2026-11-04, implementation complete; owner SSMS checks pending)

### Automated gates

| Gate | Result |
|------|--------|
| `tests/AkmlSql.Shell.Shared.Tests` (whole) | 1080 passed, 3 failed — all three `CLIPBRD_E_CANT_OPEN` (another process held the clipboard); two of them are copy tests this spec did not change, and each passes when re-run alone |
| `tests/AkmlSql.Shell.Shared.Tests` chat filter (`ChatBinding\|AiChat\|AiFailureMessage\|SsmsConnectionDetector`) | 177–179 per run, all green except a rotating clipboard-lock failure |
| `tests/AkmlSql.Web.Tests` (whole) | 2144 passed, 2 failed → fixed: `ThemeContrastScanTests` caught link text on the elevated surface at 4.07:1 (dark) / 4.19:1 (light) in the new code-panel buttons; moved to the panel surface in both editions; contrast scan, CSS-token and renderer tests re-run green (14/14) |
| `tests/AkmlSql.Web.Tests` chat filter (`AiChat\|AiAgentSelector\|ChatMessageView`) | 44 passed |
| `node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"` | 309 passed |
| `tests/AkmlSql.Core.Tests` (`Category!=InstallerSmoke`) | 1637 passed, 1 skipped (by design); baseline 1585 + 52 new |
| `tests/AkmlSql.Engine.Tests` (whole) | 2716 passed, 132 skipped, 1 failed — `PerformanceBaselineTests.Capture_or_compare_M0_baseline`, the known environmental timing drift listed in CLAUDE.md (ran 13 min beside the other suites) |
| Browser: `AiChatComposerKeysTests` + `UserStory5AiTests`, serial, Debug build | 7 passed (3 new: Send tooltip, fenced answer → code panel → Insert at caret lands in the editor, New chat while streaming stops it and stays empty after reload) |
| `generate-theme-css.ps1 -CheckOnly` | OK (light, dark, high contrast); needs `-RepoRoot` when run from this host's PowerShell |
| `AkmlSql.Core` netstandard2.0 build | succeeded |
| Release solution build (MSBuild, one pass: Restore then Build `-m`) | succeeded; `AkmlSql.Ssms22` rebuilt with 0 errors; the threading-analyzer warnings from the two new DTE helpers were fixed (explicit UI-thread assertions), the rest of its 438 warnings are pre-existing |

### Web scenarios (automated or unit-level)

| # | Result | Evidence |
|---|--------|----------|
| W1 | pass (unit) | `AiAgentSelectorTests` — labels, active selection, usable agents only |
| W2 | pass (unit) | `AiAgentSelectorTests.Choosing_an_agent_makes_it_the_active_agent…`, `AiChatPanelAgentTests` notice |
| W3 | pass (browser) | `Send_names_ctrl_enter_on_hover`, existing Enter / Ctrl+Enter tests |
| W4 | pass (browser) | `A_fenced_answer_renders_a_code_panel_whose_insert_lands_in_the_editor` |
| W5 | pass (browser) | `NewChat_while_streaming_stops_the_answer_and_starts_empty` |
| W6 | pass (unit) | `AiChatPanelCopyTests.Copying_a_rendered_answer…` — export keeps the fences; not re-checked by hand |

### Success criteria

| SC | Status |
|----|--------|
| SC-001, SC-002 | Pending the owner's S1 runs; the decision table they depend on is covered by `ChatBindingResolverTests` |
| SC-003 | Automated: every refusal path writes the Information line (`AiChatSessionBindingTests`); the owner's log check in S1-6/S1-7 confirms it on a real host |
| SC-004 | Hint visible in both empty composers; Send tooltips in both editions (unit + browser). The 1-second hover timing is the platform tooltip delay (WPF and browser defaults) |
| SC-005 | Two clicks from the composer (open selector, pick); attribution with provider and model verified by `AiChatResponseAgentFieldsTests` and the web agent tests |
| SC-006 | One activation; New chat tests assert the empty conversation and the focus request |
| SC-007 | Code panels with per-block Copy/Insert in 100 % of rendered fences (renderer tests). Copilot side-by-side checklist: pending the owner's S2 run |
| SC-008 | Existing chat tests kept (updated where the greeting and detached action rows were replaced, as research R12 planned); screen-reader pass pending (T029) |
