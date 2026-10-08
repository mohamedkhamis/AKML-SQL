---
description: "Task list for 041-web-ssms-ux-parity"
---

# Tasks: SSMS-grade workspace for the web edition

**Input**: Design documents from `/specs/041-web-ssms-ux-parity/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/ipc.md](./contracts/ipc.md), [contracts/ui.md](./contracts/ui.md), [contracts/testids.md](./contracts/testids.md), [quickstart.md](./quickstart.md)
**Background**: `baseline/*.png` and `baseline/README.md` (the 2026-10-08 walk-through the spec's gap list comes from)

**Tests are not optional here.** FR-104 and Constitution III require changed behaviour to land
with automated checks. In every story, write the test tasks first, see them fail, then implement.

**Organization**: one phase per user story, in spec priority order. US1–US3 are P1, US4–US5 are
P2, US6 is P3. Phase 2 holds the shared pieces several stories need: dialogs, notifications,
tab strip, context menu, key capture, status service, icons, shell styles, Core type helpers,
and the grid split.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel. The task touches a different file and does not depend on an unfinished task.
- **[Story]**: `[US1]`…`[US6]`, mapping to the user stories in spec.md.
- `R12`, `N5`, `X3` refer to the decisions, findings and arbitrations in research.md. Read the
  referenced entry before starting a task. It names the files, the rejected alternatives and
  the traps.
- Line numbers (`~:123`) are from branch `041-web-ssms-ux-parity` at `a41826f` and will drift.
  If they no longer match, search for the named method.

## Path conventions and rules for every task

- Repository root: `C:\Repos\AKML\AKML-SQL`. Sources are under `src/`, tests under `tests/`.
- **Web edition**: `src/AkmlSql.Web` (Blazor WebAssembly). Pages are in `Pages/`, shared
  components in `Shared/` (new grid helpers in `Shared/Grid/`, settings sections in
  `Shared/Settings/`), services in `Services/` (settings services in `Services/Settings/`),
  JS modules in `wwwroot/js/`, styles in `wwwroot/css/components/`.
- **Browser tests need Debug builds.** `WebAppFixture` runs `dotnet run --no-build -c Debug`.
  A Release-only build silently tests stale code (CLAUDE.md). Before any
  `tests/AkmlSql.Web.E2E.Tests` run:
  ```bash
  dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug
  dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Debug
  ```
- **bUnit**: set `JSInterop.Mode = JSRuntimeMode.Loose`. Use `JSInterop.SetupModule("./js/<module>.js")`
  only where a return value matters, and assert JS calls through `JSInterop.Invocations`.
  Keep fixtures to a few hundred rows, because bUnit's `Virtualize` renders every item (R68).
  Shared fakes live in `tests/AkmlSql.Web.Tests/Fakes/`, one fake per file except the moved
  `WebTestFakes.cs`.
- **JS module tests**: `tests/AkmlSql.Web.Tests/js/*.test.mjs`, run with
  `node --test tests/AkmlSql.Web.Tests/js/`. Test the pure exports. Stub `document`/`window`
  the way `akml-bridge.connect.test.mjs` does.
- **Test ids**: use only the ids in `contracts/testids.md`. Never rename an id from its
  "Never change" list. `error-banner` must stay empty in every scenario.
- **Colours**: use only `var(--akml-*)` tokens. Add a new token to `docs/theme-tokens.json`, then
  regenerate both theme folders with `scripts/generate-theme-css.ps1`. Once T008 lands,
  `WebCssTokenTests` fails on any new hex or rgb literal.
- **No browser pop-ups**: never call JS `alert`, `confirm` or `prompt`. Use `IDialogService` (T017).
- **Core** (`src/AkmlSql.Core`) must stay `netstandard2.0`-safe: plain `{ get; set; }`, no `init`,
  no records, `IndexOf(char)` instead of LINQ `Contains` on strings.
- **MessagePack DTOs**: append `[Key(n)]` exactly as `contracts/ipc.md` numbers them. Never
  renumber. Every new request type is sent only when its capability is advertised, because an
  unknown request gets no reply and would hang the bridge.
- **Shell projects** (`AkmlSql.Ssms22`, `AkmlSql.Shell.Shared.Tests`) build with full MSBuild only:
  ```bash
  MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
  "$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet && "$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal
  ```
- **Shared files**: `Program.cs`, `MainLayout.razor`, `Pages/Editor.razor` and
  `Shared/ResultSetGrid.razor` are touched by many tasks. Tasks that edit them are never `[P]`
  with each other. Run them in ID order.
- **Constants** (sizes, keys, command ids, settings ids) come from `contracts/ui.md`. Do not
  invent new values.
- **Git**: no git commands at all. Leave every change uncommitted (Constitution IV).
- **Manual checks**: use `tempdb` or a throw-away Northwind copy.

---

## Phase 1: Setup (baseline)

**Purpose**: a trustworthy before-picture, so SC-008 can be enforced check by check.

- [ ] T001 Build the baseline from `C:\Repos\AKML\AKML-SQL`. Fix nothing; record the results in a new `specs/041-web-ssms-ux-parity/baseline.md` (spec 040's `baseline.md` format, "Build" section).
  - Restore and Release-build `AKML-SQL.slnx` with the MSBuild commands above.
  - `dotnet build -c Debug` for `src/AkmlSql.Web/AkmlSql.Web.csproj`, `src/AkmlSql.Engine/AkmlSql.Engine.csproj` and `tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj`.
  - Run `tests/AkmlSql.Web.E2E.Tests/bin/Debug/net10.0/playwright.ps1 install chromium`.
  - Run `powershell -NoProfile -File scripts/generate-theme-css.ps1 -CheckOnly -RepoRoot .`, then the same with `-OutputFolder src/AkmlSql.Site/wwwroot/css/themes`.
- [ ] T002 [P] Create `scripts/compare-test-results.ps1` for Windows PowerShell 5.1, with no module dependencies (R72).
  - Parameters: `-Before <dir>`, optional `-After <dir>`, `-OutDir <dir>`.
  - For every `*.trx`: suite = the file name before the first `.`. Index `UnitTestResult@testName → @outcome`.
  - Write `<OutDir>/<suite>.passed.txt` (sorted test names) for each side given.
  - With `-After`: print, per suite, tests passed-before-not-after, missing-after, and skipped↔passed transitions, then a summary table. Exit 1 when any passed-before test is not passed after.
- [ ] T003 Record the SC-008 "before" run into `specs/041-web-ssms-ux-parity/baseline/tests/before/` and `baseline.md` (depends on T001, T002).
  - Run each suite with `--logger "trx;LogFileName=<suite>.before.trx" --results-directory "$env:LOCALAPPDATA\AKML SQL\spec041-tests\before"`. Keep `.trx` files out of the repo (`TestResults/` is ignored; an Engine trx is about 1.6 MB).
  - Suites:
    - `tests/AkmlSql.Web.Tests`.
    - `tests/AkmlSql.Web.E2E.Tests` with `--filter "FullyQualifiedName!~SiteScreenshotTour"`, with the Debug engine and local SQL Server up.
    - `tests/AkmlSql.Engine.Tests` filtered to the namespaces of its `Execution`, `InProcess`, `Handlers` and `Parser` folders.
    - `tests/AkmlSql.Core.Tests` filtered to `Ipc`, `Text` and `Theme`.
    - `tests/AkmlSql.Site.Tests --filter ThemeCssSyncTests`.
    - `tests/AkmlSql.Shell.Shared.Tests` (MSBuild build, then `dotnet test` on the built dll).
    - `node --test tests/AkmlSql.Web.Tests/js/`, output saved to `before/node.txt`.
  - Run `scripts/compare-test-results.ps1 -Before <dir> -OutDir specs/041-web-ssms-ux-parity/baseline/tests/before`.
  - In `baseline.md`, add per-suite pass/fail/skip counts and the known reds, listed once: the 42 `FormatterServiceTests` sp031 goldens, `PerformanceBaselineTests`, and anything else red now.

**Checkpoint**: baseline recorded. From here on, any new red against T003's lists is a regression this feature caused.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: the shared pieces several stories use. **No user story starts before this phase is done.**

### Test scaffolding

- [ ] T004 [P] Create `tests/AkmlSql.Web.Tests/Fakes/WebTestFakes.cs`. Move `FakeEngineBridge` (~:294) and `FakeSchemaSync` (~:332) out of `tests/AkmlSql.Web.Tests/Bridge/SchemaTreeComponentTests.cs` into it, keeping namespace and `internal` visibility. Every suite must still compile and pass.
- [ ] T005 [P] Extend the E2E harness in `tests/AkmlSql.Web.E2E.Tests/Harness/` (R70).
  - `tests/AkmlSql.Web.E2E.Tests/Harness/WebAppFixture.cs`: add `WithSqlOrSkipAsync(...)`. It pairs the sandboxed Debug engine, connects to `(local)` / `tempdb` with Windows authentication, and calls `Skip.If` when no local SQL Server answers.
  - New `tests/AkmlSql.Web.E2E.Tests/Harness/Keys.cs`:
    - an `AddInitScript` that records `defaultPrevented` per key from a bubble-phase `window` keydown listener;
    - `AssertClaimedAsync(page, key)` and `AssertNotClaimedAsync(page, key)`.
    - Synthetic Playwright keys never trigger browser accelerators (N16), so key capture can only be proven this way.

### Theme, icons and shell styles

- [ ] T006 [P] Add the new tokens and font fallbacks to `docs/theme-tokens.json` and `scripts/generate-theme-css.ps1` (R61, R67).
  - `docs/theme-tokens.json`, each in light, dark and highContrast:
    - `Akml.Brush.Surface.Editor`: today's value of the `--akml-surface-editor` alias in `src/AkmlSql.Web/wwwroot/css/app.css` (~:47).
    - `Akml.Brush.Surface.Scrim`: light `#660F172A`, dark `#99000000`, HC `Canvas`.
    - `Akml.Brush.Border.SplitterHover`: the `Accent.Primary` values.
    - `Akml.Brush.Badge.Neutral`: the `Surface.Elevated` values.
    - `Akml.Brush.Badge.NeutralText`: the `Text.Primary` values.
    - A `fallback` field on each `typography` entry. WPF's ThemeRegistry ignores it.
  - `scripts/generate-theme-css.ps1`: emit font stacks `Consolas, 'Cascadia Mono', 'Courier New', monospace` and `"Segoe UI", system-ui, sans-serif`.
  - Regenerate `src/AkmlSql.Web/wwwroot/css/themes/*.css` and `src/AkmlSql.Site/wwwroot/css/themes/*.css`.
  - Gates: both `-CheckOnly` runs green; `tests/AkmlSql.Site.Tests` `ThemeCssSyncTests` green.
  - These tokens are web-only. Do not add them to `ThemePalette.cs`.
- [ ] T007 Fix the four undefined token names in the `src/AkmlSql.Web` CSS files and razor `<style>` blocks (depends on T006; N17, R61).
  - `--akml-accent` → `--akml-accent-primary`
  - `--akml-status-error` → `--akml-status-danger`
  - `--akml-history-match-highlight` → `--akml-history-matchhighlight`
  - Delete the `--akml-surface-editor` alias in `src/AkmlSql.Web/wwwroot/css/app.css` (~:47).
  - Find every use with a grep over `src/AkmlSql.Web/**/*.css` and `**/*.razor`.
- [ ] T008 Create the CSS token gate `tests/AkmlSql.Web.Tests/Theme/WebCssTokenTests.cs` and `tests/AkmlSql.Web.Tests/Theme/css-literal-allowlist.txt` (depends on T007; R74). Mirror `tests/AkmlSql.Core.Tests/Theme/HardcodedHexScannerTests.cs`.
  - Scan `src/AkmlSql.Web/wwwroot/css/app.css`, `wwwroot/css/components/*.css` and every `<style>` block in `src/AkmlSql.Web/**/*.razor`. Strip comments and `url()`. Exclude `Pages/Spike.razor`.
  - Count hex/`rgb(a)` literals against the allow-list, which records today's real inventory (rgba scrims and shadows in eight components, eight fallbacks in Styles, one in History). The count may only go down.
  - Count `var(--x, #hex)` fallbacks separately.
  - Assert every `--akml-*` used is defined in `src/AkmlSql.Web/wwwroot/css/themes/light.css`.
- [ ] T009 [P] Vendor the Lucide icon sprite under `src/AkmlSql.Web/tools/icons/`, the way `src/AkmlSql.Web/tools/codemirror/` is vendored (R62, X10).
  - `package.json`: `lucide-static` as an exact-version devDependency.
  - `build-sprite.mjs`: reads `icons.json` and writes `src/AkmlSql.Web/wwwroot/lib/icons/akml-icons.svg`. Each icon is a `<symbol id="akml-icon-{name}" viewBox="0 0 24 24">` with `stroke="currentColor"`. Also writes `src/AkmlSql.Web/wwwroot/lib/icons/THIRD-PARTY-NOTICES.txt` with the ISC licence text.
  - `icons.json`: every name in `contracts/ui.md` §12, plus `pencil`, `plus`, `check`, `arrow-up`, `arrow-down` and `arrow-up-down` for grid glyphs, menu checks and sort.
  - `.gitignore`: `node_modules/`.
  - Run `npm ci && node build-sprite.mjs` once. The generated SVG and notices are kept as source files. Nothing loads from a CDN.
- [ ] T010 [P] Write the icon tests in `tests/AkmlSql.Web.Tests/Shell/` before the component.
  - `tests/AkmlSql.Web.Tests/Shell/IconTests.cs`:
    - `<Icon Name="play"/>` renders `<svg class="akml-icon" aria-hidden="true"><use href="lib/icons/akml-icons.svg#akml-icon-play"/></svg>` at 16 px;
    - `Label="Run"` renders `role="img" aria-label="Run"` instead of `aria-hidden`;
    - `Size` sets width and height.
  - `tests/AkmlSql.Web.Tests/Shell/IconSpriteCoverageTests.cs`: every `<Icon Name="…"` literal in `src/AkmlSql.Web/**/*.razor`, and every name in `tools/icons/icons.json`, exists as a `<symbol id="akml-icon-{name}">` in `wwwroot/lib/icons/akml-icons.svg`.
- [ ] T011 Create `src/AkmlSql.Web/Shared/Icon.razor` with parameters `Name`, `Size = 16`, `Label` and `Class` (depends on T009, T010; R62). T010 must pass.
- [ ] T012 [P] Create `src/AkmlSql.Web/wwwroot/css/components/shell.css` and `@import` it in `src/AkmlSql.Web/wwwroot/css/app.css` right after `components/results-grid.css` (R61). Token-only colours. Classes:
  - Buttons: `.akml-btn` with `--primary`, `--danger`, `--icon`, `.is-active` and `:disabled`; `.akml-btn-group`; `.akml-toolbar-sep`.
  - Overlays: `.akml-scrim` (`position:fixed`, z-index 1100, `--akml-surface-scrim`); `.akml-dialog` with header, body and footer.
  - Forms: `.akml-field` / `.akml-field-inline`, inputs and selects, `.akml-hint`.
  - Page structure: `.akml-page` / `.akml-page-header` with title, subtitle, spacer and actions; `.akml-empty`.
  - Tables: `.akml-table` with a sticky head, a hover state and a selected-row state. Under `[data-akml-theme=high-contrast]`, selected-row text uses `--akml-text-onaccent`.
  - Tabs: `.akml-tabs` / `.akml-tab` / `.akml-tab-badge[data-kind]` (28 px tall; active marker = 2 px bottom border in `--akml-accent-primary`).
  - Menus and toasts: `.akml-menu` / `.akml-menu-item`; `.akml-toast-stack` (z-index 1050) / `.akml-toast`.
  - `.akml-icon`.
  - A `:focus-visible` ring in `--akml-border-focus`.
  - `@media (prefers-reduced-motion: reduce)` turns off transitions and animations.
  - Compatibility aliases: `.akml-tool-button` and `.akml-connmgr-btn` take the `.akml-btn` rules during migration.

### Shared JavaScript

- [ ] T013 [P] Write `tests/AkmlSql.Web.Tests/js/akml-ui.test.mjs` before the module.
  - The focus stash is a stack: stash A, stash B, restore → B, restore → A.
  - `copyText(text)` uses `navigator.clipboard.writeText` when `isSecureContext`. Otherwise it uses a hidden `<textarea>` with `document.execCommand('copy')` and resolves `false` on failure.
  - `trapTabStripKeys(el)` calls `preventDefault` only for ArrowLeft, ArrowRight, Home and End.
- [ ] T014 Create `src/AkmlSql.Web/wwwroot/js/akml-ui.js` (depends on T013; X9, N21, R5).
  - Move the modal focus helpers out of `src/AkmlSql.Web/wwwroot/js/akml-connection-manager.js` (~:15, ~:23). Make the stash a stack and add a `panelSelector` parameter, so there is no `.akml-connmgr-panel` guard.
  - Add `trapTabStripKeys(el)` and `copyText(text) → Promise<boolean>`.
  - `akml-connection-manager.js` re-exports the moved helpers so its callers do not change.
- [ ] T015 [P] Add `downloadText(filename, mime, text, withBom)` to `src/AkmlSql.Web/wwwroot/js/akml-download.js`, beside `downloadBase64` (R12). It builds a Blob (prefix `\uFEFF` when `withBom`), clicks a temporary anchor and revokes the URL. Write `tests/AkmlSql.Web.Tests/js/akml-download.test.mjs` with stubbed `URL`, `Blob` and `document` first.

### Dialogs, notifications, tab strip, context menu, command state

- [ ] T016 [P] Write `tests/AkmlSql.Web.Tests/Shell/DialogHostTests.cs` before the service.
  - `ConfirmAsync` renders `role="dialog" aria-modal="true"` with `dialog-title` and `dialog-message`.
  - Cancel (`dialog-cancel`) is focused on open (a `FocusAsync` invocation). Escape resolves `false`.
  - With `Destructive = true`: `dialog-confirm` carries the danger style, and Enter on the panel does not confirm.
  - `PromptAsync` shows the validator's message inline and keeps `dialog-confirm` disabled while the input is invalid.
  - A second request waits until the first closes. `AlertAsync` shows one OK button.
- [ ] T017 Create `src/AkmlSql.Web/Services/IDialogService.cs` and `src/AkmlSql.Web/Shared/DialogHost.razor` (depends on T012, T014, T016; R59, X7).
  - Service: `ConfirmAsync(DialogRequest)`, `PromptAsync(PromptRequest)` with a validation callback, and `AlertAsync`. Models: `DialogRequest { Title, Message, Details, ConfirmLabel = "OK", CancelLabel = "Cancel", Destructive }` and `PromptRequest`.
  - Register a singleton in `src/AkmlSql.Web/Program.cs`.
  - Mount `<DialogHost/>` in `src/AkmlSql.Web/Shared/MainLayout.razor` after the connection modal. It is `position:fixed`, so it adds no in-flow row to MainLayout's three-row grid.
  - Focus lifecycle through `akml-ui.js`. Requests are queued.
  - Add `tests/AkmlSql.Web.Tests/Fakes/FakeDialogService.cs`, which scripts answers and records requests. T016 must pass.
- [ ] T018 [P] Write `tests/AkmlSql.Web.Tests/Shell/NotificationHostTests.cs` before the service.
  - Info and success toasts dismiss themselves after their duration. Use a short duration, or an injected delay.
  - Errors stay until `toast-close` is clicked. At most three toasts are visible; the oldest goes first.
  - `role="status" aria-live="polite"` for info and success, `role="alert"` for errors.
  - Each toast is `data-testid="toast"` and never uses `error-banner`.
- [ ] T019 Create `src/AkmlSql.Web/Services/INotificationService.cs` and `src/AkmlSql.Web/Shared/NotificationHost.razor` (depends on T012, T017, T018; R60).
  - Service: `Notify(text, kind, duration)` and a `Changed` event.
  - Register it in `Program.cs`. Mount the host in `MainLayout.razor` between `<main>` and the status bar; it is `position:fixed`.
  - Add `tests/AkmlSql.Web.Tests/Fakes/FakeNotificationService.cs`. T018 must pass.
- [ ] T020 [P] Write `tests/AkmlSql.Web.Tests/Shell/TabStripTests.cs` before the component.
  - `role="tablist"` containing `role="tab"` buttons with `aria-selected`, `aria-controls` and a roving tabindex.
  - ArrowRight/ArrowLeft wrap and raise `ActiveIdChanged` at once. Home/End go to the first/last tab. Enter and Space activate.
  - A badge renders with `data-kind` and updates in place.
  - `Trailing` renders as a sibling of the tablist element, never inside it.
  - `TabItem.TestId` becomes the tab's `data-testid`, so existing ids such as `ai-tab-chat` and `preview-mysql` survive.
  - `trapTabStripKeys` is invoked on first render.
- [ ] T021 Create `src/AkmlSql.Web/Shared/TabStrip.razor` and `src/AkmlSql.Web/Shared/TabItem.cs` (depends on T012, T014, T020; R58).
  - `TabItem(Id, Label, Badge, BadgeKind, TestId, Title)`.
  - Parameters: `Tabs`, `ActiveId`, `ActiveIdChanged`, `AriaLabel`, `Trailing`.
  - T020 must pass.
- [ ] T022 [P] Write `tests/AkmlSql.Web.Tests/Shell/ContextMenuTests.cs` before the component.
  - `role="menu"` holding `role="menuitem"` buttons with shortcut text. Separators render.
  - A disabled item has `aria-disabled="true"` and `title` = its reason, and selecting it does nothing.
  - Up/Down move a roving tabindex. Right opens a submenu; Left and Escape close it. Enter and Space activate and raise `OnClose`.
  - Type-ahead moves to the next item starting with the typed letter.
  - A checked item shows the `check` icon. Clicking the transparent scrim closes the menu.
- [ ] T023 Create `src/AkmlSql.Web/Shared/ContextMenu.razor` and `src/AkmlSql.Web/Shared/MenuItem.cs` (depends on T012, T014, T022; R7).
  - `MenuItem { Label, Shortcut, Disabled, DisabledReason, Children, OnSelect, IsSeparator, IsChecked }`.
  - Parameters `X`, `Y`, `OnClose`. The panel is flipped into the viewport, with the size read through `akml-ui.js`.
  - T022 must pass.
- [ ] T024 [P] Write `tests/AkmlSql.Web.Tests/Services/CommandRegistryStateTests.cs`.
  - An action with `IsEnabled` returning false renders `aria-disabled` in `src/AkmlSql.Web/Shared/CommandPalette.razor`, and Enter does not run it.
  - `IsChecked` returning true shows a check glyph.
  - Actions with neither delegate behave exactly as today.
- [ ] T025 Add optional `Func<bool>? IsChecked` and `Func<bool>? IsEnabled` to `CommandAction` in `src/AkmlSql.Web/Services/ICommandRegistry.cs` (~:47-63), and render and honour them in `src/AkmlSql.Web/Shared/CommandPalette.razor` (depends on T024; R38). T024 must pass.

### Workspace status, page keys, editor diagnostics

- [ ] T026 [P] Write `tests/AkmlSql.Web.Tests/Services/WorkspaceStatusTests.cs`.
  - `Begin()` sets `Phase = Running` and `StartedAt`.
  - `Complete(ExecuteQueryResult)` with Ok gives `Succeeded`. `RowCount` = rows returned across sets, or rows affected when there is no set.
  - Error, TimedOut, Cancelled and NoConnection give `Failed`. `Fail(message)` gives `Failed` with `ErrorMessage`.
  - `SetCaret` and `SetDocument` update their fields. Each call raises `Changed` exactly once.
- [ ] T027 Create `src/AkmlSql.Web/Services/IWorkspaceStatus.cs` with `WorkspaceStatus` (depends on T026; R41, X8).
  - `Phase { Idle, Running, Succeeded, Failed }`, `StartedAt`, `ElapsedMs`, `RowCount`, `Outcome`, `ErrorMessage`, `CaretLine`, `CaretColumn`, `DocumentName`, `IsModified`, `Changed`.
  - Register a singleton in `Program.cs`. T026 must pass.
- [ ] T028 [P] Write the key tests in `tests/AkmlSql.Web.Tests/js/` and `tests/AkmlSql.Web.Tests/Shell/` before the module (R37, FR-049).
  - `tests/AkmlSql.Web.Tests/js/akml-workspace.keys.test.mjs`, for the pure `resolveKey(bindings, state, evt)`:
    - each binding in `contracts/ui.md` §2 resolves to its command id, matched on `e.code` (`KeyR`, not `e.key`);
    - `e.repeat` is ignored, and nothing resolves while an `[aria-modal="true"]` element is open;
    - Ctrl+K then Ctrl+F within 1.5 s → `editor:format`; after 1.5 s → nothing;
    - a lone Ctrl+F is not claimed, so CodeMirror's Find still works;
    - Ctrl+Alt+KeyO with `e.key = 'ó'`, or with `getModifierState('AltGraph')` true, resolves to nothing (the AltGr rule of `contracts/ui.md` §2).
  - `tests/AkmlSql.Web.Tests/Shell/ShortcutCollisionTests.cs`, over `WorkspaceKeyMap.Bindings`:
    - no duplicate key;
    - no binding equals a CodeMirror key (Mod-Enter, Mod-F, F3, Mod-G, F12, Escape, Tab, Mod-Z, Mod-Y, Mod-/, Mod-D), except as a chord's second stroke;
    - no binding uses Ctrl+N, Ctrl+T, Ctrl+W, Ctrl+Tab or Ctrl+Shift+Tab.
- [ ] T029 Create the key map `src/AkmlSql.Web/Services/WorkspaceKeyMap.cs` and the key module `src/AkmlSql.Web/wwwroot/js/akml-workspace.js` (depends on T028; R37, FR-049).
  - `src/AkmlSql.Web/Services/WorkspaceKeyMap.cs`: a static `Bindings` table of `KeyBinding(Code, Ctrl, Shift, Alt, CommandId, ChordPrefix, Display)` holding `contracts/ui.md` §2.
  - `src/AkmlSql.Web/wwwroot/js/akml-workspace.js`:
    - `initKeys(dotNetRef, bindings)` installs a document capture-phase keydown listener. For a claimed key it calls `preventDefault()` and `stopPropagation()` synchronously, then `dotNetRef.invokeMethodAsync('OnWorkspaceKey', id)`.
    - Before claiming, apply the AltGr rule of `contracts/ui.md` §2: a Ctrl+Alt binding is claimed only when `e.key` is the plain letter of `e.code` and `AltGraph` is not set.
    - `disposeKeys()` removes it.
    - Export the pure `resolveKey`.
  - T028 must pass.
- [ ] T030 Move the editor page's keys into the module in `src/AkmlSql.Web/Pages/Editor.razor` (depends on T025, T029).
  - Delete `@onkeydown="OnKeyDownAsync"` (~:38) and its chord state machine (~:1159-1186).
  - On first render, import `akml-workspace.js` and call `initKeys` with `WorkspaceKeyMap.Bindings`.
  - Add `[JSInvokable] OnWorkspaceKey(string id)`. It finds the registered `CommandAction` by id, skips it when `IsEnabled` is false, and runs it. Ids not registered yet are ignored.
  - Register `editor:format`, `editor:analyse`, `editor:surround` and `editor:save` where missing. The Ctrl+K chords keep working, their second stroke no longer opens CodeMirror's Find (FR-049(d)), and Ctrl+S no longer opens the browser's save dialog.
  - Call `disposeKeys` in `DisposeAsync`. Update the comment in `src/AkmlSql.Web/wwwroot/js/akml-editor.js` (~:589-592).
- [ ] T031 [P] Write `tests/AkmlSql.Web.Tests/js/akml-editor.diagnostics.test.mjs`, for a pure `toLintDiagnostics(docLength, lineStarts, diags)` export.
  - Severity 0–3 maps to hint, info, warning and error.
  - `from` and `to` are clamped to the document. `to <= from` widens to the whole line.
  - A line beyond the document is dropped.
- [ ] T032 Add `setDiagnostics(hostId, diags)`, `clearDiagnostics(hostId)` and the pure `toLintDiagnostics` to `src/AkmlSql.Web/wwwroot/js/akml-editor.js`. They dispatch `cm.lint.setDiagnostics`; `lintGutter()` is already installed (depends on T031; R39).
  - `src/AkmlSql.Web/Shared/EditorComponent.razor`: `SetDiagnosticsAsync(IReadOnlyList<EditorDiagnostic>)` and `ClearDiagnosticsAsync()`, with `EditorDiagnostic { Line, StartColumn, EndColumn, Severity, Message }`.
  - Theme the lint underline (`text-decoration: underline wavy var(--akml-status-*)`), gutter marker and tooltip with tokens, scoped under `.akml-editor` at equal or higher specificity than the library's injected classes.
  - Do **not** install `lintKeymap`: it binds F8, which the Schema panel needs.

### Core type helpers and the grid split

- [ ] T033 [P] Write `tests/AkmlSql.Core.Tests/Text/SqlTypeNameTests.cs` (R10, N2, N13).
  - `Format(baseName, columnSize, precision, scale)`:
    - length types: `nvarchar(50)`; `nvarchar(max)` when size is `int.MaxValue` or -1; `varbinary(max)`; `char(5)`; `binary(2)`;
    - `decimal(10,2)`, with `numeric` reported as `decimal`;
    - `datetime2(3)`, `time(0)`, `datetimeoffset(7)`;
    - bare names for int, bigint, smallint, tinyint, bit, date, datetime, smalldatetime, money, smallmoney, float, real, uniqueidentifier, xml, text, ntext, image, timestamp, sql_variant and `dbo.MyType`.
  - The form is decided by the base name, never by precision 255. int precision 10 → `int`; datetime 23/3 → `datetime`; smalldatetime 16/0 → `smalldatetime`.
  - `Parse` accepts both bare and declared forms and returns `BaseName`, `Length`, `IsMax`, `Precision`, `Scale`.
  - `IsCharacterType` is true exactly for char, nchar, varchar, nvarchar, text, ntext, xml and sql_variant.
  - Null and empty input are safe.
- [ ] T034 Create `src/AkmlSql.Core/Text/SqlTypeName.cs` (netstandard2.0-safe; depends on T033). T033 must pass.
- [ ] T035 [P] Write `tests/AkmlSql.Core.Tests/Text/EngineCutIndicatorTests.cs`.
  - `[text 123 chars]` and `[binary 9 bytes]` match exactly and return kind and size.
  - Partial matches, a different text and extra whitespace do not match.
- [ ] T036 Create `src/AkmlSql.Core/Text/EngineCutIndicator.cs` with the anchored regex `^\[(text|binary) (\d+) (chars|bytes)\]$`. These are the only in-band forms `src/AkmlSql.Engine/Execution/ResultSetReader.cs` emits (~:197, :211, :221; R8). Depends on T035; T035 must pass.
- [ ] T037 Split `src/AkmlSql.Web/Shared/ResultsGridComponent.razor` with no behaviour or test-id change (X6, R9).
  - Move one-set rendering into a new `src/AkmlSql.Web/Shared/ResultSetGrid.razor`, with parameters `Set`, `SetIndex`, the editability context and the Apply callbacks. That covers the header, the `Virtualize` body iterating `_rowIndexes`, edits, inserts, deletes, the inline editor, the Apply bar and the CRUD markers.
  - `ResultsGridComponent` keeps its current set tabs and Messages-when-more-than-one-set behaviour, and renders a `ResultSetGrid` for the active set.
  - Switch `tests/AkmlSql.Web.Tests/Pr247_ResultsGridApplyMessageTests.cs` to `JSInterop.Mode = Loose`. Otherwise it stays unchanged and green.

**Checkpoint**: shared pieces ready. The Phase 2 tests are green, and everything that passed in T003 still passes. User stories can start.

---

## Phase 3: User Story 1 — Result columns that behave like SSMS (Priority: P1) 🎯 MVP

**Goal**: columns sized to content and resizable by drag, double-click and keyboard; row numbers; spreadsheet selection; Copy, Copy with headers, Copy as; sort; View value; Save results; safe click-to-select editing with Set to NULL and invalid-cell gating.

**Independent Test**: run `SELECT name, database_id, create_date FROM sys.databases`. Check the content-fitted widths, resize and auto-fit, select rows, columns and all, then copy with and without headers into a spreadsheet. Run quickstart §1 steps 1–11.

### Tests for User Story 1 (write first; they must fail)

- [ ] T038 [P] [US1] Write `tests/AkmlSql.Web.Tests/Grid/GridColumnLayoutTests.cs` (R1, R2).
  - Constants: `MinColumnPx = 48`, `MaxColumnPx = 480`, `DefaultColumnPx = 120`.
  - `SelectCandidates` scans every row and keeps the header plus the three longest display texts per column.
  - `Fit(measuredPx)` adds padding plus the sort-glyph/resizer allowance, then clamps to `[max(48, min(headerPx, 480)), 480]`. A header longer than every value sets the width.
  - The fallback estimate, used when measurement is unavailable, is 7 px per character.
  - The row-number track width follows the digit count.
  - `ToCssTracks()` emits the row-number track first, e.g. `44px 72px 236px`.
  - `SetUserWidth` marks `UserSet`. `ResetWidths` clears user widths. `ForNewResult` discards everything.
- [ ] T039 [P] [US1] Write `tests/AkmlSql.Web.Tests/Grid/GridSelectionTests.cs` (R4, FR-008, FR-010). Coordinates are display positions; `c = -1` is the row-number column, `r = -1` the header.
  - Pointer: click a cell; Shift-click extends from the anchor; Ctrl-click adds, or removes through `Deselected`; row number, header and corner select row, column and all; drag selects a rectangle.
  - Keyboard: Ctrl+A; Arrow, Home/End, PageUp/PageDown and Ctrl+Home/End move the focus, and with Shift they extend.
  - `IsSelected` honours the exclusions. `EnumerateSelected` yields grid order.
  - Select-all keeps one rect, with no per-cell allocation.
  - `RemapThroughPermutation(old, new)` keeps the focus on the same fetched row.
  - `Clear()`.
- [ ] T040 [P] [US1] Write `tests/AkmlSql.Web.Tests/Grid/GridSortComparerTests.cs` (R6, FR-012). Keys come from `ClrTypeHints`.
  - Numbers and dates: Int64 numeric (2 < 10), with a decimal fallback for overflow; Double; Decimal; DateTime and DateTimeOffset parsed with `"o"`.
  - Other types: Bool as 0/1; Guid in `SqlGuid` order; Binary by decoded bytes, with engine-cut indicators last; strings `OrdinalIgnoreCase`; unparsable values ordinal.
  - NULL sorts first ascending and last descending.
  - The sort is stable, with the original index as tiebreak.
- [ ] T041 [P] [US1] Write `tests/AkmlSql.Web.Tests/Grid/GridCellTextTests.cs` (R8).
  - `IsEngineCutIndicator` delegates to Core `EngineCutIndicator`.
  - `DescribeSize("[text 70000 chars]")` → "70,000 characters". `DescribeSize` of a normal value gives its length.
  - `Tooltip` = the first 1,000 characters.
- [ ] T042 [P] [US1] Capture the Copy-As goldens in `tests/AkmlSql.Shell.Shared.Tests/GridCopyAsGoldenCaptureTests.cs` **before** any formatter moves (R5, R13).
  - Create the throwaway `tests/AkmlSql.Shell.Shared.Tests/GridCopyAsGoldenCaptureTests.cs`. It calls the `internal static` formatters in `src/AkmlSql.Shell.Shared/Productivity/Grid/GridCopyAsMenu.cs` (~:254-562): `FormatAsCsv`, `FormatAsTsv`, `FormatAsJson`, `FormatAsXml`, `FormatAsHtml`, `FormatAsInsert`, `FormatAsInClause`, `FormatAsMarkdown` and `QuoteSqlValue`.
  - Fixture values: NULL, double quotes, commas, tabs, CR/LF, `|`, `1,000`, `12.50`, unicode, empty strings, and two columns with the same name.
  - Write the outputs to `tests/AkmlSql.Core.Tests/Text/GridTextFormats.goldens.json`.
  - Build with MSBuild and run it once. Keep the JSON.
- [ ] T043 [US1] Write `tests/AkmlSql.Core.Tests/Text/GridTextFormatsTests.cs` (depends on T042's JSON).
  - Every `GridTextFormats` method reproduces its golden byte for byte.
  - New `Delimited(headers, rows, delimiter, retainLineBreaks)` cases: a value holding the delimiter, `"`, CR or LF is quoted with inner quotes doubled; NULL is written `NULL`; there is a header row; lines end in CRLF; with `retainLineBreaks = false`, CR/LF become a space.
  - `Insert(headers, rows, tableName)` defaults to the SSMS placeholder.
- [ ] T044 [P] [US1] Write `tests/AkmlSql.Web.Tests/Grid/GridClipboardTests.cs` (R5, FR-009, FR-031).
  - `BuildTsv(includeHeader: false|true)`: only the copied columns' headers, in grid order; NULL → `NULL`.
  - Values: a value cut by the column width is copied in full; an engine-cut value is copied verbatim and counted in `EngineCutCount`; CR/LF become a space unless `RetainLineBreaksOnCopy`.
  - Duplicate column names are copied by position. A disjoint Ctrl-click selection copies the union in grid order.
  - Above 20 MB, a bounded prefix is copied with `Truncated = true`.
  - `CopyAs(Csv|Json|Markdown|Insert)` delegates to `GridTextFormats`.
- [ ] T045 [P] [US1] Write the engine tests for the 255 sentinel in `tests/AkmlSql.Engine.Tests/Execution/` (N2, R10).
  - Extend `tests/AkmlSql.Engine.Tests/Execution/CrudWriteGeneratorTests.cs`: a null Precision or Scale leaves the `SqlParameter` defaults, so `Scale` is never 255.
  - Extend `tests/AkmlSql.Engine.Tests/Execution/ExecuteQueryIntegrationTests.cs` (skip without SQL):
    - provenance Precision and Scale are null for `date`, `int` and `money` columns;
    - an Apply edit on a `date` column and on a `money` column of a tempdb table both succeed.
- [ ] T046 [P] [US1] Write `tests/AkmlSql.Web.Tests/Grid/ResultsGridLayoutTests.cs` (bUnit on `ResultSetGrid`; R1–R3, FR-001–FR-007).
  - The root `style` carries `--akml-grid-cols` with the row-number track first.
  - `measureColumns` is invoked once per new result, with at most 4 candidates per column. When interop returns default, the fallback estimate is used.
  - `OnColumnResizedFromJs(2, 300)` changes only track 2.
  - Widths survive re-render and parameter updates for the same result. A new result is fitted afresh.
  - Shift+Alt+ArrowRight on a focused header adds 16 px.
  - Double-clicking `results-col-resizer-{c}` auto-fits that column. The header menu's "Auto-fit all columns" and "Reset column widths" work.
  - A zero-row result keeps headers sized to the header text.
- [ ] T047 [P] [US1] Write `tests/AkmlSql.Web.Tests/Grid/ResultsGridSelectionTests.cs` (R4, FR-008–FR-010).
  - Clicking a cell sets `aria-selected` and `aria-activedescendant="akml-grid-{set}-{r}-{c}"`.
  - Pointer: `results-rownum-{r}`, `results-col-{c}` and `results-corner`; Shift and Ctrl clicks.
  - Keyboard: Ctrl+A; arrows.
  - Ctrl+C invokes `copyText` with TSV, and Ctrl+Shift+C adds the header line. A notification reports engine-cut values.
  - The grid root is the only tab stop (`tabindex="0"`); the row delete buttons have `tabindex="-1"`.
  - A new result clears the selection.
- [ ] T048 [P] [US1] Write `tests/AkmlSql.Web.Tests/Grid/ResultsGridEditingTests.cs` (R4, FR-013, FR-016).
  - Starting an edit: one click selects and shows no input. A second click, double-click, Enter or F2 shows `results-cell-input`. A printable character starts editing with that character replacing the value.
  - Inside the editor: Escape cancels and the fetched value is back. Enter commits and focus returns to the root. Tab and Shift+Tab move to the next and previous editable cell.
  - Rows: Delete on a focused row-number cell toggles the row's delete. Delete while a cell is being edited never deletes the row.
  - Apply still produces today's request (`results-apply`, `results-apply-message`).
- [ ] T049 [P] [US1] Write `tests/AkmlSql.Web.Tests/Grid/ResultsGridNullAndInvalidTests.cs` (R9, R14, FR-014, FR-016).
  - NULL: Ctrl+0 stores a pending `null`. "Set to NULL" is disabled with a reason when the column has a `BaseColumnName` and `AllowDBNull` is false.
  - An emptied `nvarchar` cell stores `""`.
  - An emptied `int`, `bit` or `varbinary` cell renders `results-cell-invalid-{r}-{c}` with title "Enter a value or set NULL". Apply is disabled and the bar says why.
  - An unset NOT NULL, non-identity column of a new row is invalid.
  - NULL and the empty string render differently.
  - Edited, new and deleted rows carry a glyph in the row-number cell as well as the edge colour.
- [ ] T050 [P] [US1] Write `tests/AkmlSql.Web.Tests/Grid/ResultsGridSortTests.cs` (R6, N23, FR-012, FR-016).
  - `results-sort-{c}` cycles ascending, descending, none, with `aria-sort`. A header click selects the column and does not sort.
  - Numbers sort numerically.
  - Pending edits stay on their fetched rows after a sort, and Apply's `KeyCells` target the edited fetched row.
  - After an Apply the page does not re-run (bake + `RebuildRowIndexes`), the sort is re-applied.
  - A new result clears the sort.
- [ ] T051 [P] [US1] Write `tests/AkmlSql.Web.Tests/Grid/ResultsGridContextMenuTests.cs` and `tests/AkmlSql.Web.Tests/Grid/CellValueDialogTests.cs` (R7, R8, FR-002, FR-005, FR-017).
  - Cell menu (`results-context-menu`):
    - the exact order and shortcuts of `contracts/ui.md` §5, with "Save results as CSV…" (`results-save-csv`) followed by "Save results as tab-delimited…" (`results-save-tsv`);
    - Set to NULL appears only for editable results; Delete row / Restore row follow a separator for editable results;
    - Shift+F10 opens it at the focused cell.
  - Header menu (`results-header-menu`) items in FR-005 order.
  - View value (`results-view-value`):
    - shows the column and declared type with NULL/NOT NULL, a size line, the whole value in a read-only textarea, and Copy (invokes `copyText`);
    - it is disabled for NULL;
    - for an engine-cut value it says the content is unavailable and suggests a `LEFT(col, 8000)` projection.
- [ ] T052 [P] [US1] Write `tests/AkmlSql.Web.Tests/Grid/ResultsGridHeaderTests.cs` (R15, FR-011).
  - The header `title` shows the declared type from `ColumnSqlTypes`. Keyboard focus shows an `aria-describedby` tooltip that Escape dismisses.
  - `ExecutionSettings.ShowColumnTypes = true` renders the second line `results-col-type-{c}`.
  - An empty column name reads "(No column name)".
- [ ] T053 [P] [US1] Write `tests/AkmlSql.Web.Tests/js/akml-results-grid.test.mjs` for the pure exports.
  - `replaceTrack(tracks, index, px)`.
  - `shouldTrapKey(evt, isInlineInput)`: the grid keys of `contracts/ui.md` §2 are trapped; with the inline input focused, only Tab, Enter and Escape are; Ctrl+0 and Ctrl+Shift+C only while a cell has focus.
  - `isClick(dx, dy)`: true under 3 px.
- [ ] T054 [P] [US1] Write `tests/AkmlSql.Web.E2E.Tests/ResultsGridTests.cs` (`BridgeE2E`, `SkippableFact`, `WithSqlOrSkipAsync`; R70). Cover quickstart §1:
  - sized columns for `sys.databases`;
  - dragging the `name` edge 120 px with `Mouse.Move/Down/Move(Steps)/Up`, then asserting bounding boxes; double-click fit;
  - row 3 + Shift row 6 + Ctrl+C, read with `GrantPermissions(clipboard-read/write)` + `readText`: four lines;
  - header + Ctrl+Shift+C; the cell context menu items; the sort cycle;
  - an editable tempdb table created by the fixture: click, Enter, Escape, Ctrl+0, the invalid `int`, then Apply on `date` and `money`;
  - the pending-edits confirmation on F5;
  - the keyboard-only path of step 11.

### Implementation for User Story 1

- [ ] T055 [P] [US1] Create `src/AkmlSql.Web/Shared/Grid/GridColumnLayout.cs` (R1, R2). T038 must pass.
- [ ] T056 [P] [US1] Create `src/AkmlSql.Web/Shared/Grid/GridSelection.cs`: `GridRect` list, `Deselected` set, `Anchor`, `Focus` (R4). T039 must pass.
- [ ] T057 [P] [US1] Create `src/AkmlSql.Web/Shared/Grid/GridSortComparer.cs` (R6). Keys are parsed once per sort. T040 must pass.
- [ ] T058 [P] [US1] Create `src/AkmlSql.Web/Shared/Grid/GridCellText.cs` (R8). T041 must pass.
- [ ] T059 [US1] Move the formatters into `src/AkmlSql.Core/Text/GridTextFormats.cs` as a `public static class` (depends on T042, T043; R5, R12).
  - Move CSV, TSV (with `includeHeader`), JSON, XML, HTML, INSERT (optional table name, defaulting to the placeholder), IN clause, Markdown, `QuoteSqlValue`, the escape helpers and the `NULL` sentinel. Use an explicit `"\r\n"` and `IndexOf(char)`.
  - Add `Delimited(headers, rows, delimiter, retainLineBreaks)`.
  - Replace the bodies in `src/AkmlSql.Shell.Shared/Productivity/Grid/GridCopyAsMenu.cs` with one-line delegations. The SSMS edition is unchanged, quirks included (`1,000` → `1000`, `12.50` → `12.5` in JSON).
  - Re-run T042's capture test: output identical. Then T043 must pass, and `AkmlSql.Ssms22` must build with MSBuild.
  - Delete `tests/AkmlSql.Shell.Shared.Tests/GridCopyAsGoldenCaptureTests.cs`. The goldens JSON stays and is pinned by T043.
- [ ] T060 [US1] Create `src/AkmlSql.Web/Shared/Grid/GridClipboard.cs` (depends on T058, T059; R5). It works over `ResultSetGrid`'s single `DisplayText(r, c)` accessor and `ExecutionSettings.RetainLineBreaksOnCopy`. T044 must pass.
- [ ] T061 [P] [US1] Fix the 255 sentinel in `src/AkmlSql.Engine/Execution/ResultSetReader.cs` and `src/AkmlSql.Engine/Execution/CrudWriteGenerator.cs` (N2, R10).
  - `src/AkmlSql.Engine/Execution/ResultSetReader.cs` `TryPopulateProvenance` (~:303-304): map 255 to null for `Precision` and `Scale`.
  - `src/AkmlSql.Engine/Execution/CrudWriteGenerator.cs`: set `SqlParameter.Precision`/`Scale` only when non-null.
  - T045 must pass.
- [ ] T062 [P] [US1] Create `src/AkmlSql.Web/wwwroot/js/akml-results-grid.js` (R2, R3, R4, R7, N22).
  - Setup and measurement:
    - `init(rootEl, dotNetRef)` attaches one delegated `pointerdown`.
    - `measureColumns(rootEl, headerTexts, candidates)` uses a cached offscreen canvas. Read `ctx.font` from a probe header cell and a probe body cell via `getComputedStyle`, so zoom and font fallback are honoured.
  - Column resize:
    - On a `.akml-col-resizer`: `preventDefault`, `setPointerCapture`, add `.akml-resizing`, and rewrite that track of `--akml-grid-cols` on each `pointermove` (rAF-throttled).
    - On `pointerup`: call `OnColumnResizedFromJs(col, px)`. A move under 3 px counts as a click. Call `rootEl.focus()` after a drag.
  - Drag select: `elementFromPoint` → `closest('[data-r][data-c]')`. Call `OnDragSelectFromJs(r, c)` only when the cell changes.
  - Keys: a keydown trap using `shouldTrapKey`.
  - Context menu: one root `contextmenu` listener. It calls `preventDefault` unless the target is the inline input. Keyboard-originated events (`clientX/Y = 0`) are routed to the focused cell's rect via `getCellRect`, then `OnContextMenuFromJs(x, y, r, c)`.
  - Also export `scrollCellIntoView(r, c)`, `focusRoot()`, `dispose()` and the pure helpers of T053.
  - T053 must pass.
- [ ] T063 [P] [US1] Restyle `src/AkmlSql.Web/wwwroot/css/components/results-grid.css` (R1, R4, R9). Token-only colours.
  - Rows and tracks:
    - rows `display:grid; grid-template-columns: var(--akml-grid-cols); width:max-content; min-width:100%`;
    - delete the flex sizing (`flex:1 0 120px`, `min-width:80px`);
    - the Virtualize spacers stay height-only.
  - Columns: `.akml-col-resizer` (8 px hit area, `col-resize` cursor, `--akml-border-splitterhover` on hover); `.akml-resizing { user-select:none }`; the row-number column.
  - Cell states:
    - `.akml-cell-selected`, with `--akml-text-onaccent` text in high contrast; a `.akml-cell-focus` outline;
    - `.akml-cell-invalid`, `.akml-cell-null` (muted) and engine-cut (muted);
    - ellipsis overflow; `.akml-cell-tooltip`; row-glyph styles.
- [ ] T064 [US1] Implement column layout in `src/AkmlSql.Web/Shared/ResultSetGrid.razor` (depends on T055, T062, T063; R1–R3, FR-001–FR-007).
  - Layout: an inline `--akml-grid-cols` on the root; `data-r`/`data-c` on cells; both render branches iterate `_rowIndexes`.
  - Add `DisplayText(r, c)`, the single accessor that render, copy and save use for a cell's shown text. Until T104 it returns the wire text (null → `NULL`).
  - Resizing:
    - a `results-col-resizer-{c}` handle per header;
    - `[JSInvokable] OnColumnResizedFromJs`;
    - edge double-click → `AutoFitColumn`;
    - Shift+Alt+Left/Right on a focused header → ±16 px.
  - Auto-fit after `ShowResult`, with the fallback when interop is unavailable.
  - Header menu commands: Auto-fit this column, Auto-fit all, Reset widths, Copy column name.
  - Lifecycle: `IAsyncDisposable` with a `DotNetObjectReference`.
  - T046 must pass.
- [ ] T065 [US1] Implement row numbers, selection, keys and copy in `src/AkmlSql.Web/Shared/ResultSetGrid.razor` (depends on T056, T060, T064; R4, R5, FR-008–FR-010).
  - Add `results-rownum-{r}`, `results-corner` and `results-col-{c}`.
  - Root: `tabindex="0"`, `role="grid"`, `aria-multiselectable="true"`, `aria-activedescendant`.
  - A root `@onkeydown` handles the grid keys of `contracts/ui.md` §2. Wire `OnDragSelectFromJs`.
  - Ctrl+C and Ctrl+Shift+C go through `GridClipboard` and `akml-ui.js` `copyText`. `INotificationService` reports the copied count and any engine-cut values.
  - Delete/restore buttons get `tabindex="-1"`.
  - T047 must pass.
- [ ] T066 [US1] Implement editing in `src/AkmlSql.Web/Shared/ResultSetGrid.razor` (depends on T065; R4, FR-013).
  - Starting an edit: one click selects. A second click, double-click, Enter, F2 or a printable character calls `BeginEdit`; a printable character replaces the value.
  - The input's `@onkeydown`:
    - Enter commits and refocuses the root;
    - Escape sets `_cancelEdit`, so the following `onchange`/`onblur` discards;
    - Tab and Shift+Tab commit and move to the next and previous editable cell.
  - The input stops `contextmenu`, so its native paste menu works.
  - Delete on a focused row-number cell toggles the row's delete.
  - T048 must pass.
- [ ] T067 [US1] Implement NULL, validity and row glyphs in `src/AkmlSql.Web/Shared/ResultSetGrid.razor` (depends on T066, T034, T011; R9, R14, FR-014, FR-016).
  - Set to NULL, by command and Ctrl+0. It is disabled with a reason when the set is editable, the column has a `BaseColumnName` and `AllowDBNull` is false.
  - Emptied cells: `""` when `SqlTypeName.IsCharacterType`, otherwise invalid. An unset NOT NULL, non-identity column of a new row is invalid. Apply is disabled while any cell is invalid, and the bar says why.
  - Display: NULL as `NULL` (muted); the empty string with title "(empty string)".
  - Row glyphs: `<Icon Name="pencil|plus|x"/>` in the row-number cell, plus the edge bar.
  - Expose `HasPendingEdits` and `PendingEditCount`.
  - T049 must pass.
- [ ] T068 [US1] Implement sorting in `src/AkmlSql.Web/Shared/ResultSetGrid.razor` (depends on T057, T067; R6, N23, FR-012).
  - The `results-sort-{c}` button is separate from the header click, with `aria-label="Sort"` and `aria-sort`. It cycles ascending → descending → none.
  - Sorting permutes `_rowIndexes` only. It sorts on the current (edited) values, and the selection is remapped.
  - `ShowResult` clears the sort.
  - After `BakeCommittedChangesIntoActiveSet` + `RebuildRowIndexes` (~:507-519 in the old component, now in this file), re-apply the sort.
  - T050 must pass.
- [ ] T069 [P] [US1] Create `src/AkmlSql.Web/Shared/CellValueDialog.razor` on `.akml-scrim`/`.akml-dialog` with the `akml-ui.js` focus helpers, test id `results-view-value` (depends on T012, T014, T058; R8). It needs a scrollable body and a Copy button, which `DialogRequest` cannot express.
- [ ] T070 [US1] Wire the context menus and tooltips in `src/AkmlSql.Web/Shared/ResultSetGrid.razor` (depends on T023, T068, T069; R7, FR-002, FR-005, FR-017, FR-031, FR-103).
  - Cell/selection menu and header menu use `ContextMenu`, with the labels, order and shortcuts of `contracts/ui.md` §5. Copy as ▸ offers CSV, JSON, Markdown table and INSERT statements through `GridClipboard.CopyAs`.
  - Add `[JSInvokable] OnContextMenuFromJs`. View value opens `CellValueDialog`.
  - Cut cells: a `title` with the first 1,000 characters, and a rendered `results-cell-tooltip` on the focused cell that Escape dismisses.
  - T051 must pass.
- [ ] T071 [US1] Add the column type on hover and the "show column types" option in `src/AkmlSql.Web/Shared/ResultSetGrid.razor` and `src/AkmlSql.Web/Services/IExecutionSettingsStore.cs` (depends on T070; R15, FR-011, N19).
  - `src/AkmlSql.Web/Services/IExecutionSettingsStore.cs`: add `ShowColumnTypes` (false) and `RetainLineBreaksOnCopy` (false) to `ExecutionSettings`, as additive JSON.
  - `PersistCapsAsync` in `src/AkmlSql.Web/Pages/Editor.razor` (~:778) replaces the whole record. Make it carry the two fields over until T146 deletes it.
  - `src/AkmlSql.Web/Shared/ResultSetGrid.razor`:
    - the header shows the declared type in `title` and in the focus tooltip;
    - with `ShowColumnTypes` on, it shows a second line;
    - an empty name reads "(No column name)".
  - T052 must pass.
- [ ] T072 [US1] Implement "Save results as" in `src/AkmlSql.Web/Shared/ResultSetGrid.razor` and `src/AkmlSql.Web/Pages/Editor.razor` (depends on T059, T015, T017, T070; R12, FR-030).
  - "Save results as CSV…" writes comma-delimited and "Save results as tab-delimited…" writes tab-delimited, both through `GridTextFormats.Delimited`. Values are the text the grid shows, and `RetainLineBreaksOnCopy` decides CR/LF.
  - When the set is truncated or holds engine-cut values, `IDialogService.ConfirmAsync` asks first.
  - Download through `downloadText` as UTF-8 with BOM, named `<DocumentName>.csv` / `.tsv`. Take the name from a `DocumentName` parameter, falling back to `SQLQuery1`; T175 supplies the real name.
- [ ] T073 [US1] Gate pending edits in `src/AkmlSql.Web/Pages/Editor.razor` (depends on T067, T017; R9, FR-015).
  - The results host (`ResultsGridComponent` now, `ResultsPaneComponent` after T106) exposes `HasPendingEdits` and `PendingEditCount`, summed over its `ResultSetGrid`s.
  - `src/AkmlSql.Web/Pages/Editor.razor`:
    - Execute (button, F5, palette) first asks "Discard N pending edits?" through `IDialogService`;
    - a Blazor `NavigationLock` asks the same before in-app navigation;
    - `ShowResult` never resets edits silently;
    - add a reusable `ConfirmDiscardPendingEditsAsync()` for T175.
  - The grid's Discard button confirms too.
- [ ] T074 [US1] Story gate: run the US1 suites and quickstart §1, then `scripts/compare-test-results.ps1`.
  - Run `tests/AkmlSql.Web.Tests` (`Grid/*`, `Pr247_*`, `Shell/*`) and `tests/AkmlSql.Core.Tests` (`Text`).
  - Run the `CrudWriteGeneratorTests` and `ExecuteQueryIntegrationTests` classes in `tests/AkmlSql.Engine.Tests`, and `tests/AkmlSql.Shell.Shared.Tests`.
  - Run `node --test tests/AkmlSql.Web.Tests/js/`, then `ResultsGridTests` after the Debug builds.
  - Walk quickstart §1 in Light, Dark and High contrast.
  - Run `scripts/compare-test-results.ps1` against T003. It must show no passed→not-passed test.
  - Build the whole solution in one pass (MSBuild restore + Release build, see "Path conventions") and run `scripts/generate-theme-css.ps1 -CheckOnly` for both theme folders. Both must be green (Constitution II).
  - Update the docs this story changed, in the same change: `CLAUDE.md` (structure notes), this story's rows in the spec 041 entry of `doc/progress.md`, and any affected `doc/*.md` (Constitution, Documentation currency).

**Checkpoint**: US1 is complete and testable on its own. This is the MVP.

---

## Phase 4: User Story 2 — Results and Messages like SSMS (Priority: P1)

**Goal**: permanent Results / Messages / Problems tabs; stacked result sets; SSMS-formatted messages with click-to-line; `GO` batches that keep running after a failure; kept partial results; SSMS value display; F5; Parse; an honest running state.

**Independent Test**: run a batch with two SELECTs, a PRINT and an UPDATE, then a query failing on line 4. Then run three `GO` batches with a failing middle one, and Parse. Run quickstart §2 steps 1–11.

### Tests for User Story 2 (write first; they must fail)

- [ ] T075 [P] [US2] Rewrite and extend the splitter tests in `tests/AkmlSql.Engine.Tests/Parser/TsqlParserServiceTests.cs` (R16, N3).
  - Adapt the seven existing tests to the new `SqlBatchSpan` shape.
  - Where `GO` is a separator: on a line of its own in any case, indented, after a same-line block comment, with CRLF, at end of file, and twice in a row.
  - Where it is not: inside strings, comments and `[GO]`.
  - Repeat counts: `GO 3 -- c` gives `RepeatCount = 3`.
  - `SeparatorError` for `GO SELECT 2`, `GO;`, `GO 0` and negative counts.
  - An unterminated string swallows the rest into the last batch. Whitespace-only batches are skipped.
  - `StartLine` = the previous GO line + 1, including leading blank lines. A script with no line starting with `go` skips lexing.
  - These tests stay in `tests/AkmlSql.Engine.Tests/Parser/`, beside the existing `TsqlParserService` tests, although the class lives in `AkmlSql.IntelliSense` and `tests/AkmlSql.IntelliSense.Tests` exists. Keeping one home for the parser's tests is the existing convention (Constitution III is met: the behaviour lands with tests).
- [ ] T076 [P] [US2] Write `tests/AkmlSql.Core.Tests/Ipc/ExecuteQueryMessagesCompatTests.cs` (R20, contracts/ipc.md).
  - Legacy payloads deserialise into the new classes with defaults: an 8-element `ExecuteQueryResult` and an 11-element `ExecuteResultSet` give empty `MessageDetails`/`Batches`, `CompletedAtUnixMs = 0`, null `CurrentDatabase` and `BatchIndex = 0`.
  - A 13-element payload deserialises into a copy of the old class (the older-bundle case).
  - `ExecuteMessageDto` and `ExecuteBatchDto` round-trip with the key order of contracts/ipc.md.
- [ ] T077 [P] [US2] Write `tests/AkmlSql.Core.Tests/Text/SqlMessageTextTests.cs` (R18, FR-022).
  - Errors:
    - `Msg 208, Level 16, State 1, Line 4` + newline + text;
    - with a procedure: `Msg 50000, Level 16, State 1, Procedure dbo.p, Line 3`.
  - Row counts: `(1 row affected)`, `(N rows affected)`, `(N rows returned; more exist)`.
  - Info gives the text only. System lines are verbatim.
  - `CompletionLine(DateTimeOffset local)` → `Completion time: 2026-10-08T12:34:56.1234567+02:00`.
- [ ] T078 [P] [US2] Write `tests/AkmlSql.Core.Tests/Text/SqlValueDisplayTests.cs` (R11, FR-026). Cover every row of data-model.md §2.1:
  - Dates and times:
    - `time` arrives in `"c"` form without a zero fraction;
    - `datetime2(7)` shows seven digits, `time(0)` none;
    - `datetimeoffset` puts a space before the offset;
    - a bare-name fallback shows seven digits.
  - Binary: `0x` + upper-case hex, with a preview after 1,024 bytes (`IsPreview`).
  - Other types: `money` padded to four decimals from the type name; `bit`; upper-case `uniqueidentifier`; an unknown type passes through.
  - Special values: NULL; the empty string; an engine-cut indicator verbatim with `IsEngineCut`.
- [ ] T079 [P] [US2] Write `tests/AkmlSql.Engine.Tests/Execution/ExecuteOutcomeTests.cs`, DB-free (R17–R19, R23).
  - Status and errors:
    - top-level status aggregation (Ok, Error, TimedOut, Cancelled);
    - `ErrorMessage` = the first error's text.
  - Legacy fields:
    - `Messages[]` SSMS-formatted through `SqlMessageText`;
    - `TotalRowsAffected` = the sum of DML counts, -1 when none.
  - Caps:
    - `MaxMessages = 10,000` → `MessagesOmitted` + one System summary;
    - `MaxResultSets = 1,000`;
    - `MaxBatchRepeat` clamp with a System message;
    - the budget counts each message's UTF-8 bytes twice.
  - Batch lines: "Beginning execution loop" / "Batch execution completed N times."; after a connection closes, the remaining batches are `Skipped` with "The remaining N batch(es) did not run because the connection was closed."
- [ ] T080 [P] [US2] Extend `tests/AkmlSql.Engine.Tests/Execution/ExecuteQueryIntegrationTests.cs` (skip without SQL; same non-parallel collection, one connection per class; R71).
  - Messages and counts:
    - per-statement counts in order, with `ResultSetIndex`;
    - `SELECT 1; SELECT 1/0; SELECT 2` gives two sets with rows, one empty set, and Error 8134 at Line 2;
    - PRINT gives Info with number 0;
    - an error inside a temp procedure has `Procedure` populated;
    - NOCOUNT gives no count lines; a truncated SELECT reads "(N rows returned; more exist)";
    - `CompletedAtUnixMs > 0`.
  - GO batches:
    - three batches with a failing middle one give sets 1 and 3 plus the error with batch index 1 and its `StartLine`;
    - `GO 2` repeats;
    - `CREATE PROCEDURE #p … GO EXEC #p` gives Ok (N1);
    - `WAITFOR` with a 1 s timeout gives TimedOut, and the remaining batches are `Skipped`;
    - severity 20 gives `Skipped`, with the test skipped unless sysadmin.
  - Types and budget:
    - `ColumnSqlTypes` holds `datetime2(3)`, `decimal(10,2)` and `nvarchar(max)`;
    - the budget spans batches (with a small internal budget);
    - a 12,000-line PRINT loop gives `MessagesOmitted = 2,000`.
  - ApplyChanges after an execute still fails properly, which proves the info-message flag was restored.
  - Parse:
    - a two-batch script with `CREATE TABLE` creates nothing;
    - a syntax error on line 3 of batch 2 gives Error at Line 3 with `Batches[1].StartLine`;
    - Execute after Parse returns rows.
- [ ] T081 [P] [US2] Extend the registration tests in `tests/AkmlSql.Engine.Tests/InProcess/` and `tests/AkmlSql.Engine.Tests/Handlers/` (R21, N25).
  - `tests/AkmlSql.Engine.Tests/InProcess/AllMessageTypesInProcessTests.cs`: a factory entry for 219 (`ExecuteQueryRequest`); 219 is registered and 220 is not.
  - `tests/AkmlSql.Engine.Tests/Handlers/HandshakeHandlerTests.cs`: the capabilities contain `execute.v2`.
- [ ] T082 [P] [US2] Write `tests/AkmlSql.Web.Tests/Services/QueryExecutionServiceTests.cs` and extend `tests/AkmlSql.Web.Tests/History/WebHistoryLogicTests.cs` (R22).
  - Deadline and errors:
    - a never-replying fake bridge gives `NoReply` after the deadline (inject a short timeout);
    - a bridge `InvalidOperationException` gives `Disconnected`;
    - a caller cancel gives `Cancelled`; the deadline token is told apart from the caller's.
  - Batch count: from `SplitBatches`, including repeat counts.
  - Parse: `ParseAsync` sends 219 only when `execute.v2` is advertised.
  - Completion time: falls back to receipt time when `CompletedAtUnixMs` is 0.
  - History: `NoReply` and `Disconnected` are recorded as Error.
- [ ] T083 [P] [US2] Write `tests/AkmlSql.Web.Tests/Results/ExecuteLineMapperTests.cs` (R24).
  - The formula `(selStart − 1) + (Batches[b].StartLine − 1) + Line`.
  - Line 0 or a non-empty Procedure → null.
  - A selection starting at line 10, and a batch starting at line 5.
  - A whitespace-only selection → start 1.
- [ ] T084 [P] [US2] Write `tests/AkmlSql.Web.Tests/Results/MessagesViewTests.cs` (R20, R24, FR-022, FR-023, FR-035).
  - Lines appear in order as `messages-line-{i}`.
  - Errors (`messages-error-{i}`) raise `OnNavigate(documentLine)` only when mappable; errors with a Procedure are not clickable.
  - The last line, `messages-completion`, is in local time.
  - With empty `MessageDetails` (an older engine), the legacy `Messages[]` strings are shown.
  - The `MessagesOmitted` summary appears.
  - DDL-only Ok, and Parse Ok, read "Commands completed successfully.".
- [ ] T085 [P] [US2] Write `tests/AkmlSql.Web.Tests/Results/ResultsPaneComponentTests.cs` (R34, FR-020, FR-021, FR-029, FR-033, FR-034).
  - Tabs and states:
    - `results-tab-results`, `results-tab-messages` and `results-tab-problems` are always present;
    - `results-empty` reads "Run a query to see results here (F5)";
    - `results-running` shows the elapsed time and "A running statement can't be interrupted.", with the previous results marked stale;
    - with zero sets, the Results tab says there is nothing to show;
    - the `results-elapsed`/`results-affected` ids are on the pane; `ActiveTab` binds two-way.
  - Stacked sets:
    - `results-set-{n}` with `results-set-header-{n}` "Result N (M rows)" and a divider;
    - `results-jump-{n}` when there are more than 5 sets;
    - each editable set has its own `results-apply-{n}` / `results-discard-{n}`, and applying set 1 leaves set 2's edits alone;
    - a truncated set reads "N rows returned; more exist".
  - Problems:
    - `results-problems-badge` takes `OnVisibleCountChanged` and is hidden at 0;
    - `ProblemsListComponent` with `ShowHeader=false` hides its header;
    - `OnVisibleCountChanged` fires once per count change.
- [ ] T086 [P] [US2] Write `tests/AkmlSql.Web.Tests/Grid/ResultsGridDisplayTests.cs` (R11, FR-026).
  - `date`, `datetime` and `datetime2(7)` display as in SSMS. NULL is muted.
  - Editing shows the full-precision wire value.
  - Binary shows `0x…` hex, never `[binary N bytes]`.
  - Copy and Save use the display text.
- [ ] T087 [P] [US2] Write `tests/AkmlSql.Web.Tests/Shell/StatusBarSegmentsTests.cs` (R41, R65, FR-027, FR-083).
  - `status-outcome` per phase: icon + "Query executed successfully." / "Query completed with errors." / "Executing query…", in `role="status" aria-live="polite"`.
  - `status-rows` reads "16 rows · 87 ms", and while running it counts the elapsed time.
  - The `status-connection` text keeps its `Server/Database` format.
- [ ] T088 [P] [US2] Write `tests/AkmlSql.Web.E2E.Tests/ResultsMessagesTests.cs` (`BridgeE2E`, `WithSqlOrSkipAsync`). Cover the automatable steps of quickstart §2:
  - F5 in the editor, results pane, toolbar and Schema panel, proven with `Keys.AssertClaimedAsync` + `execute-complete`. F5 on Settings is not claimed.
  - Click-to-line from an error inside a selection and after `GO`.
  - `GO 3` loop messages, and the `GO SELECT 2` refusal.
  - The timeout and Skipped path, the running state and the disabled Execute.
  - Parse, and the datetime/money/binary displays.

### Implementation for User Story 2

- [ ] T089 [US2] Rewrite `SplitBatches` in `src/AkmlSql.IntelliSense/Parser/TsqlParserService.cs` (~:134-181) over `GetTokenStream` and `TSqlTokenType.Go` (R16, X2). It returns `List<SqlBatchSpan> { Index, StartOffset, EndOffset, StartLine, RepeatCount, Text }` plus a nullable `SeparatorError` ("Incorrect syntax was encountered while parsing GO."), with the cheap `go` pre-check. Its only callers are the tests. T075 must pass.
- [ ] T090 [P] [US2] Add the result DTOs and message types in `src/AkmlSql.Core/Ipc/Messages/ExecuteQueryMessages.cs` and `src/AkmlSql.Core/Ipc/RpcMessage.cs` (R20, R21, X4).
  - `src/AkmlSql.Core/Ipc/Messages/ExecuteQueryMessages.cs`:
    - `ExecuteMessageDto` (keys 0–9) and `ExecuteBatchDto` (keys 0–7);
    - `ExecuteResultSet` key 11 `BatchIndex`;
    - `ExecuteQueryResult` keys 8 `MessageDetails`, 9 `Batches`, 10 `CompletedAtUnixMs`, 11 `MessagesOmitted`, 12 `CurrentDatabase` (T189 populates key 12);
    - `ExecuteStatus` 5 `Skipped`, 6 `Disconnected`, 7 `NoReply`; the message-kind constants;
    - doc comments for the declared `ColumnSqlTypes` form and for null precision/scale.
  - `src/AkmlSql.Core/Ipc/RpcMessage.cs`: `ExecuteParse = 219`, `ExecuteParseResult = 220`.
  - T076 must pass.
- [ ] T091 [US2] Create `src/AkmlSql.Core/Text/SqlMessageText.cs`. It formats from primitive arguments, with an `ExecuteMessageDto` overload (depends on T090; R18). T077 must pass.
- [ ] T092 [P] [US2] Create `src/AkmlSql.Core/Text/SqlValueDisplay.cs`, returning `DisplayValue { Text, IsEngineCut, IsPreview }`. It uses `SqlTypeName` and `EngineCutIndicator` (depends on T034, T036; R11). T078 must pass.
- [ ] T093 [US2] Update `src/AkmlSql.Engine/Execution/ResultSetReader.cs` (depends on T034, T090; R10, R17, R19, R23).
  - Read `GetColumnSchema()` unconditionally, in a try/catch with the bare name on failure (~:83-87, :102). Compose `ColumnSqlTypes` with `SqlTypeName.Format`.
  - Take the byte budget in and out, so one budget spans all sets and messages. Stamp `BatchIndex`.
  - Raise a set-boundary callback, so `StatementCompleted` counts land on the current set.
- [ ] T094 [US2] Create `src/AkmlSql.Engine/Execution/ExecuteOutcome.cs`, a pure aggregator for messages, batches, sets, caps, status and the legacy fields (depends on T090, T091; R17, R23). T079 must pass.
- [ ] T095 [US2] Turn `ExecuteOnConnectionAsync` in `src/AkmlSql.Engine/Execution/ExecuteQueryHandler.cs` into a script loop inside the one `RunExclusiveAsync` callback (depends on T089, T093, T094; R17, X3, N1, N10).
  - Inject `TsqlParserService` (`src/AkmlSql.Engine/EngineHandlerRegistry.cs` ~:339-345). A `SeparatorError` gives Error with no batch run.
  - For each batch, repeat `RepeatCount` times (clamped to 10,000), each time with a new `SqlCommand` and the clamped timeout.
  - Use `CommandBehavior.KeyInfo` only when the first token is not `CREATE/ALTER` + `PROC/PROCEDURE/VIEW/FUNCTION/TRIGGER/SCHEMA/DEFAULT/RULE` and the batch has a `SELECT` token; otherwise `Default`.
  - Drain through `ResultSetReader` with the shared budget.
  - After each command:
    - `conn.State != Open` → the rest `Skipped` + one System message, then stop;
    - cancellation → `Cancelled`, stop;
    - timeout (-2) → `TimedOut`, the rest `Skipped`, stop;
    - any other `SqlException` → record its errors and continue.
  - Always return `ResultSets`, never an `Err` envelope. Record `ElapsedMs` per batch and in total. Stamp `CompletedAtUnixMs` (UTC) at the end.
- [ ] T096 [US2] Record errors as info messages in `src/AkmlSql.Engine/Execution/ExecuteQueryHandler.cs` (depends on T095; R18, N5).
  - Set `conn.FireInfoMessageEventOnUserErrors = true` inside the gate, and restore it in `finally`.
  - Record every `SqlError`, from `InfoMessage` and from still-thrown exceptions, in stream order as `ExecuteMessageDto`: Error when Class ≥ 11, else Info.
  - Note the flag's scope in `src/AkmlSql.Engine/Execution/SessionConnection.cs`.
- [ ] T097 [US2] Count rows per statement in `src/AkmlSql.Engine/Execution/ExecuteQueryHandler.cs` (depends on T096; R19).
  - Subscribe `StatementCompleted` per command and append RowCount messages. Attribute a SELECT's count to its set.
  - A truncated set's text reads "(N rows returned; more exist)", while `RowCount` keeps the server count.
  - `TotalRowsAffected` = the DML sum. NOCOUNT gives no lines.
- [ ] T098 [US2] Add Parse mode in `src/AkmlSql.Engine/Execution/ExecuteQueryHandler.cs` and `src/AkmlSql.Engine/EngineHandlerRegistry.cs` (depends on T097; R21, N6).
  - `src/AkmlSql.Engine/EngineHandlerRegistry.cs`: register 219 with `RegisterRaw` to the same handler in parse mode. Never register 220.
  - Under the gate:
    - `SET PARSEONLY ON` as its own command;
    - each batch once, with `CommandBehavior.Default`, results ignored and no `StatementCompleted`;
    - then `SET PARSEONLY OFF`. If OFF throws, call `conn.Close()` inside the gate, so the next execute reopens and reports `ConnectionWasReset`.
  - Add `execute.v2` to `src/AkmlSql.Engine/Capabilities.cs`.
  - T080 and T081 must pass.
- [ ] T099 [P] [US2] Document the execute family in `doc/ipc-api.md`, as contracts/ipc.md's "Documentation" section lists (depends on T090).
  - Constants 212–216 and 219/220, and sections for ExecuteQuery/ExecuteQueryResult, ExecuteCancel, ApplyChanges and ExecuteParse.
  - The capability id `execute.v2`.
  - The declared-type form of `ColumnSqlTypes`, and the null-sentinel rule.
- [ ] T100 [P] [US2] Create `src/AkmlSql.Web/Services/ExecuteLineMapper.cs` (pure; R24). T083 must pass.
- [ ] T101 [US2] Update `src/AkmlSql.Web/Services/IQueryExecutionService.cs` (~:63, ~:76-79) (depends on T089, T090; R22).
  - Deadline:
    - a linked token with `CancelAfter(timeout × max(1, batchCount) + 15 s)`, where `batchCount` comes from the browser's `TsqlParserService.SplitBatches` and includes repeat counts;
    - tell the deadline apart from the caller's token.
  - Failures:
    - the deadline → `NoReply`, "No result was received within the timeout; the statement may still be running on the engine.";
    - a bridge `InvalidOperationException` → `Disconnected`, "The engine connection dropped; the statement may still have run.".
  - Add `ParseAsync`, sending 219 only with `execute.v2`.
  - `src/AkmlSql.Web/Services/WebHistoryLogic.cs`: record NoReply and Disconnected as Error.
  - T082 must pass.
- [ ] T102 [P] [US2] Report the selection's start line from `src/AkmlSql.Web/wwwroot/js/akml-editor.js` (R24).
  - `src/AkmlSql.Web/wwwroot/js/akml-editor.js`: add `getSelectionInfo(hostId) → { text, startLine }` beside `getSelectedText`. A whitespace-only selection runs the whole document and reports line 1.
  - Add a pure `selectionStartLine(doc, ranges)` export, tested in `tests/AkmlSql.Web.Tests/js/akml-editor.diagnostics.test.mjs`.
  - `src/AkmlSql.Web/Shared/EditorComponent.razor`: add `GetSelectionInfoAsync()`, and highlight the target line when `GotoLineAsync` jumps.
- [ ] T103 [US2] Create `src/AkmlSql.Web/Shared/MessagesView.razor` (depends on T091, T100; FR-022, FR-023).
  - Format with `SqlMessageText`. Map lines with `ExecuteLineMapper`. Format the completion time from `CompletedAtUnixMs` in local time.
  - Raise `OnNavigate(documentLine)`.
  - Test ids `messages-list`, `messages-line-{i}`, `messages-error-{i}`, `messages-completion`.
  - T084 must pass.
- [ ] T104 [US2] Route display through `SqlValueDisplay` in `src/AkmlSql.Web/Shared/ResultSetGrid.razor` (depends on T092; R11, FR-026). If US1's grid tasks are in flight, run this after them, since they share the file.
  - `DisplayText(r, c)` = `SqlValueDisplay.Format(wire, SqlTypeName.Parse(ColumnSqlTypes[c]), ClrTypeHints[c])`.
  - Delete the local `[binary N bytes]` condensation, carried over from the old component's ~:673-680.
  - The editor and Apply keep the wire text.
  - T086 must pass.
- [ ] T105 [US2] Add `ShowHeader` (default true) and `OnVisibleCountChanged` to `src/AkmlSql.Web/Shared/ProblemsListComponent.razor`. `OnVisibleCountChanged` is raised only when the count changes, since it fires from `OnParametersSet` (R34).
- [ ] T106 [US2] Create `src/AkmlSql.Web/Shared/ResultsPaneComponent.razor` (depends on T021, T103, T105; R34, X6, FR-020, FR-021, FR-029, FR-033). T085 must pass.
  - `TabStrip` with Results, Messages and Problems, and a Problems badge.
  - The stacked `ResultSetGrid`s: `results-cell-{r}-{c}` is scoped inside `results-set-{n}`, and each set owns its own Apply/Discard.
  - The Messages slot (`MessagesView`) and the Problems slot.
  - The empty and running states, and the jump control.
  - `HasPendingEdits` and `PendingEditCount` summed over the sets.
- [ ] T107 [P] [US2] Add the outcome segment `status-outcome` and the `status-rows` segment (rows · ms, or an elapsed counter on its own timer) to `src/AkmlSql.Web/Shared/StatusBar.razor`, reading `IWorkspaceStatus` (depends on T027, T011; R41). T087 must pass.
- [ ] T108 [US2] Integrate the pane in `src/AkmlSql.Web/Pages/Editor.razor` (depends on T030, T032, T101, T102, T106, T027; FR-020–FR-035).
  - Layout:
    - replace `ResultsGridComponent` with `ResultsPaneComponent`;
    - move Problems from its 320 px right-hand column into the Problems tab.
  - Tab rules:
    - after Execute: Results when there is at least one set and no error, else Messages (FR-020);
    - after Analyse: Problems when there are findings; otherwise a "No problems found" notification, with the badge cleared (FR-034).
  - Running state:
    - `IWorkspaceStatus.Begin`/`Complete`/`Fail` in `RunSqlAsync`, the autorun path and the apply-refresh path; delete the toolbar's execution status text;
    - while Running, Execute is not offered: the button is disabled, `editor:execute`'s `IsEnabled` is false, and the key does nothing;
    - the existing `execute-cancel` toolbar button (shown during every run, though the engine can cancel only a query it has not started, and the web cannot tell the two apart) becomes "Cancel if not started", titled "Stops the query only if the engine has not started it. A running statement cannot be interrupted." (FR-027).
  - Commands:
    - register `editor:execute` (F5) and `editor:parse` (Ctrl+F5);
    - Parse is enabled with `execute.v2` and a connection, with the `parse-button` toolbar button.
  - Errors:
    - capture the selection's start line at execute;
    - on a message click: `GotoLineAsync` + highlight + `SetDiagnosticsAsync` with an error marker on that document line (FR-023).
  - Add the hidden `execute-complete` (`data-version`, `data-status`) and `execute-running` markers.
  - Port `tests/AkmlSql.Web.Tests/Pr247_ResultsGridApplyMessageTests.cs` to render `ResultsPaneComponent` with one set (same ids, selected inside `results-set-0`), then delete `src/AkmlSql.Web/Shared/ResultsGridComponent.razor` (FR-090).
- [ ] T109 [US2] Story gate: run the US2 suites and quickstart §2, then `scripts/compare-test-results.ps1`.
  - Run `tests/AkmlSql.Engine.Tests` (`Parser`, `Execution`, `InProcess`, `Handlers`), `tests/AkmlSql.Core.Tests` (`Ipc`, `Text`), `tests/AkmlSql.Web.Tests` (`Results/*`, `Services/*`, `Shell/*`, `Grid/*`) and the node tests.
  - Publish the Debug engine, then run `ResultsMessagesTests`.
  - Walk quickstart §2. Run the compare script against T003.
  - Build the whole solution in one pass (MSBuild restore + Release build, see "Path conventions") and run `scripts/generate-theme-css.ps1 -CheckOnly` for both theme folders. Both must be green (Constitution II).
  - Update the docs this story changed, in the same change: `CLAUDE.md` (structure notes), this story's rows in the spec 041 entry of `doc/progress.md`, and any affected `doc/*.md` (Constitution, Documentation currency).

**Checkpoint**: US1 and US2 both work on their own.

---

## Phase 5: User Story 3 — A workspace I can shape (Priority: P1)

**Goal**: Schema left, editor centre, results bottom, AI right. Every region hideable and resizable by pointer and keyboard; folds away on narrow windows; remembered per browser; Reset layout; a View menu.

**Independent Test**: toggle each panel by click and by key, drag each splitter, reload, narrow the window below the fold width, then reset the layout. Run quickstart §3 steps 1–9 and 11.

### Tests for User Story 3 (write first; they must fail)

- [ ] T110 [P] [US3] Validate the layout constants marked *(validate)* in `specs/041-web-ssms-ux-parity/contracts/ui.md` §1 against the real CodeMirror gutter (R35). In a Debug browser at the default editor font, measure 120 Consolas-13 characters + gutter + scrollbar. Update the 920 px editor minimum in `contracts/ui.md` if the measurement differs, and use the confirmed number in T121.
- [ ] T111 [P] [US3] Write `tests/AkmlSql.Web.Tests/Workspace/WorkspaceLayoutStoreTests.cs` over `InMemoryIndexedDbAdapter` (R36, FR-045, FR-047).
  - `LoadAsync` parses the known field keys and ignores unknown ones. A different `layoutVersion` discards the whole layout.
  - `SetAsync(field, value)` writes exactly one key (assert the adapter's writes).
  - `layoutVersion` is written on the first user change and never on load.
  - `ResetAsync` clears the store. `Changed` is raised.
- [ ] T112 [P] [US3] Write `tests/AkmlSql.Web.Tests/Workspace/WorkspaceLayoutFitTests.cs` (R35, FR-044–FR-046). Use the limits from contracts/ui.md §1.
  - `Fit`:
    - clamps sizes to the minimums and the current window;
    - un-hides everything when every region would be hidden;
    - keeps the results height within `[120, workspace − 320]`.
  - `Fold`:
    - when `workspace − visible side widths − splitters < editorMin`, the AI panel folds first, then Schema;
    - at 1100 px both are folded;
    - widening brings back panels the user had not hidden;
    - fold state is never written.
- [ ] T113 [P] [US3] Write `tests/AkmlSql.Web.Tests/Workspace/SplitterComponentTests.cs` (R35, FR-044).
  - ARIA: `role="separator"`, `aria-orientation`, `aria-valuenow/min/max`, `aria-controls`, `aria-label`.
  - Keys raise `OnCommitted`: Arrow ±16 px, Shift+Arrow ±64 px, Home/End go to min/max, Enter and Space collapse and restore.
  - `trapSeparatorKeys` is invoked, and Tab is never prevented.
- [ ] T114 [P] [US3] Write `tests/AkmlSql.Web.Tests/js/akml-workspace.clamp.test.mjs` (R35).
  - `clamp(min, max, px)`.
  - The drag delta for each axis and side.
  - `trapSeparatorKeys` prevents only Arrow, Home, End, Space and Enter.
- [ ] T115 [P] [US3] Write `tests/AkmlSql.Web.Tests/Services/CommandRegistryViewActionsTests.cs` (R38, FR-042). `BuildViewActions` returns the ids, titles and shortcuts of `contracts/ui.md` §3, with shortcut text from `WorkspaceKeyMap`. `IsChecked` mirrors the layout. `view:toggle-ai` replaces `editor:ai`.
- [ ] T116 [P] [US3] Write `tests/AkmlSql.Web.Tests/js/akml-indexeddb.versionchange.test.mjs` with a stubbed `indexedDB` (R36, N26).
  - After `onversionchange`, the cached promise is dropped and the reopen calls `indexedDB.open(name)` with **no** version argument.
  - Upgrading to `DB_VERSION` 4 creates the `workspaceLayout` store.
- [ ] T117 [P] [US3] Write `tests/AkmlSql.Web.Tests/Workspace/MainLayoutNavTests.cs` (R42, FR-041).
  - The nav is hidden only on `/` or `/editor` (a query string is fine) when `NavVisible` is false.
  - `workspace-edge-nav` reads "Show navigation bar". Every other route shows the nav.
  - `.akml-main` has no padding on the editor route.
- [ ] T118 [P] [US3] Extend `tests/AkmlSql.Web.Tests/Bridge/SchemaTreeComponentTests.cs` (FR-048). With no SQL connection the panel still renders and shows `schema-connect` "Connect to SQL Server…", which raises `OnConnectRequested`.
- [ ] T119 [P] [US3] Write `tests/AkmlSql.Web.E2E.Tests/WorkspaceLayoutTests.cs` (`BridgeE2E`; R70). Cover quickstart §3 steps 1–9 and 11:
  - toggles by click, F8 and Ctrl+R (through `Keys`); the edge strips;
  - splitter drags; keyboard splitters; maximise;
  - reload persistence, and persistence via `LaunchPersistentContext`;
  - an 1100 × 700 context: both side panels folded, and a 120-character line with no horizontal scrollbar (the existing overflow script);
  - at 1440 × 900 with Schema and AI hidden, the editor column is at least 95 % of the window width (SC-003);
  - at 800 px the page scrolls as a whole and Execute stays visible;
  - at 150 % zoom the splitters and edge strips are still hit-able;
  - Reset layout; the View menu and palette lists; F6 focus cycling; hiding every region then executing brings results back.

### Implementation for User Story 3

- [ ] T120 [US3] Bump IndexedDB to version 4 in `src/AkmlSql.Web/wwwroot/js/akml-indexeddb.js` (R36, N26).
  - `src/AkmlSql.Web/wwwroot/js/akml-indexeddb.js`: `DB_VERSION` 3 → 4, create `workspaceLayout` in `onupgradeneeded`, and fix `onversionchange`: close, drop the cached promise, reopen with no version.
  - Add `StoreNames.WorkspaceLayout` to `src/AkmlSql.Web/Services/JsIndexedDbAdapter.cs`, and to `InMemoryIndexedDbAdapter.cs` if it enumerates stores.
  - T116 must pass.
- [ ] T121 [US3] Create `src/AkmlSql.Web/Services/IWorkspaceLayoutStore.cs` (depends on T110, T120; R36, data-model.md §1.2).
  - `WorkspaceLayout`, with the eleven fields of data-model.md §1.2.
  - `WorkspaceLayoutLimits`, holding the constants from contracts/ui.md §1.
  - The pure `WorkspaceLayout.Fit(layout, width, height, limits)` and `Fold(...)`.
  - The store: `LoadAsync`, `SetAsync(field, value)`, `ResetAsync`, `Current`, `Changed`.
  - Register it in `Program.cs`. Add `tests/AkmlSql.Web.Tests/Fakes/FakeWorkspaceLayoutStore.cs`.
  - T111 and T112 must pass.
- [ ] T122 [P] [US3] Add the splitter functions to `src/AkmlSql.Web/wwwroot/js/akml-workspace.js` (R35). T114 must pass.
  - `attachSplitter(el, hostEl, cssVar, axis, min, max, dotNetRef, id)`:
    - pointer capture, rAF-throttled CSS-variable writes, `touch-action:none`, a dragging class;
    - one `OnSplitterCommitted(id, px)` on release, and double-click → `OnSplitterReset(id)`.
  - `observeWorkspace(hostEl, dotNetRef)`: a rAF-debounced `ResizeObserver` → `OnWorkspaceResized(w, h)`.
  - `trapSeparatorKeys(el)`, and the pure `clamp`.
- [ ] T123 [US3] Create `src/AkmlSql.Web/Shared/SplitterComponent.razor`: a 6 px track in `--akml-border-splitter`, a 12 px hit area, accent on hover and focus (depends on T122; R35). T113 must pass.
- [ ] T124 [US3] Rebuild the page as one `.akml-workspace` CSS grid in `src/AkmlSql.Web/Pages/Editor.razor` (depends on T121; R33).
  - Columns, rows and named areas as R33 gives them. Every size is a CSS variable written inline from `Fit(...)`.
  - Regions:
    - Schema on the left: change `SchemaTreeComponent.razor`'s `border-left` to `border-right` (~:101);
    - AI on the right; results under the editor only.
  - The results host is always mounted and toggled by class, never by `@if`, so `ShowResult` and pending edits survive. A maximised modifier class collapses the editor row; adjust `EditorComponent.razor`'s maximised rule.
  - Delete `GridColumns` (~:470-474) and the old `.akml-editor-grid` styles (~:285-333). Make the pane fill its area in `results-grid.css` (~:8-17).
  - Test ids `workspace`, `workspace-schema`, `workspace-ai`, `workspace-results`.
  - Check that `Virtualize` (grid and schema tree) re-renders when a pane is un-hidden. Call `RefreshDataAsync` if it does not.
- [ ] T125 [US3] Add splitters, edge strips and folding in `src/AkmlSql.Web/Pages/Editor.razor` (depends on T123, T124; FR-043, FR-044, FR-046).
  - Splitters:
    - three `SplitterComponent`s (`workspace-splitter-left`, `-right`, `-bottom`), wired with `attachSplitter`;
    - `OnSplitterCommitted` → `IWorkspaceLayoutStore.SetAsync` for that field only.
  - Edge strips:
    - `workspace-edge-left`, `-right` and `-bottom` are `<button>`s with `writing-mode: vertical-rl` and `title="Show <panel> (<key>)"`;
    - a strip restores its panel at the previous size.
  - Folding: `observeWorkspace` → derived fold state.
- [ ] T126 [US3] Add the View menu, view commands and layout persistence in `src/AkmlSql.Web/Pages/Editor.razor` (depends on T125, T023, T025; R38, FR-032, FR-042, FR-045).
  - View menu: a toolbar `view-menu` button opens a `ContextMenu` with checked items `view-toggle-{panel}`.
  - Register `view:toggle-nav`, `view:toggle-schema`, `view:toggle-results`, `view:toggle-ai`, `view:maximise-results`, `view:cycle-panes` and `view:reset-layout` (`workspace-reset-layout`), all with `IsChecked`.
  - Toggles:
    - `view:toggle-ai` replaces `editor:ai`; the unpersisted `_showAiPanel` becomes `aiVisible`/`aiTab`;
    - `view:cycle-panes` moves focus editor → results → schema → AI → toolbar (Shift+F6 goes back);
    - Reset layout calls `ResetAsync` and applies the defaults.
  - Persistence: remember the results tab, restored until the next execution.
  - Auto-show: a hidden results pane comes back on Execute, and on an Analyse that finds something.
  - T115 must pass.
- [ ] T127 [US3] Update `src/AkmlSql.Web/Shared/MainLayout.razor` (~:62-65) (depends on T121; R42).
  - `.akml-main` has no padding on `/` and `/editor`.
  - The nav is hidden there when `NavVisible` is false, replaced by the `workspace-edge-nav` strip.
  - Inject `IWorkspaceLayoutStore` and `NavigationManager`.
  - T117 must pass.
- [ ] T128 [US3] Keep the Schema panel present without a connection in `src/AkmlSql.Web/Shared/SchemaTreeComponent.razor` (depends on T126; FR-048).
  - `src/AkmlSql.Web/Shared/SchemaTreeComponent.razor`: always render, and show `schema-connect` "Connect to SQL Server…" when disconnected, raising `OnConnectRequested`.
  - `Pages/Editor.razor`: wire `OnConnectRequested` to `IConnectionManagerController.Open()`.
  - T118 must pass.
- [ ] T129 [US3] Story gate: run the US3 suites and quickstart §3, then `scripts/compare-test-results.ps1`.
  - Run `tests/AkmlSql.Web.Tests` (`Workspace/*`, `Services/*`, `Bridge/*`), the node tests and `WorkspaceLayoutTests`.
  - Walk quickstart §3 steps 1–9 and 11. Step 10's in-editor underline arrives with T200.
  - Run the compare script against T003.
  - Build the whole solution in one pass (MSBuild restore + Release build, see "Path conventions") and run `scripts/generate-theme-css.ps1 -CheckOnly` for both theme folders. Both must be green (Constitution II).
  - Update the docs this story changed, in the same change: `CLAUDE.md` (structure notes), this story's rows in the spec 041 entry of `doc/progress.md`, and any affected `doc/*.md` (Constitution, Documentation currency).

**Checkpoint**: all three P1 stories work on their own.

---

## Phase 6: User Story 4 — Settings in one place, and every setting true (Priority: P2)

**Goal**: one Settings page with nine sections named as in the SSMS Options window. Per-section Restore defaults, plus Restore all, Export and Import with no secrets. Query defaults live here, with a session-only toolbar override. "Enable code analysis" really works. Suppressed rules can be undone. A filter searches all sections. The theme is applied before first paint.

**Independent Test**: from a fresh browser, reach every setting in at most two clicks, change one per section, reset one section, then export → change → import and confirm the file holds no secrets. Run quickstart §4 steps 1–12.

### Tests for User Story 4 (write first; they must fail)

- [ ] T130 [P] [US4] Write `tests/AkmlSql.Web.Tests/Settings/SettingsPageRoutingTests.cs` (R43, FR-050, FR-051), using `BunitNavigationManager`.
  - Routes and aliases:
    - `/settings` opens General; each of the nine `/settings/{id}` routes opens its section;
    - `/settings/ai` opens `ai-assistance`; `/diagnostics` opens `diagnostics`; unknown ids open General.
  - Clicking `settings-nav-{id}` navigates.
  - At `/settings/ai` only the AI section renders, and its active-provider radios are the first radios in the DOM.
  - `src/AkmlSql.Web/Shared/NavMenu.razor` has exactly five entries: Editor, Snippets, Format styles, History, Settings.
- [ ] T131 [P] [US4] Write and extend the store tests under `tests/AkmlSql.Web.Tests/` (R47, R49, R52, R53, R56).
  - `tests/AkmlSql.Web.Tests/Services/ExecutionSettingsStoreTests.cs`:
    - `SessionOverride` and `GetEffectiveAsync` = override ?? saved;
    - saving defaults clears the override;
    - `ResetAsync` deletes only the `executionSettings` key; `ReloadAsync`.
  - `tests/AkmlSql.Web.Tests/Services/EditorSettingsStoreTests.cs`:
    - defaults 13 / false / 4, clamped to 9–32 and 1–8;
    - key `editorSettings` in the `analysisSettings` store.
  - Extend `Services/AnalysisSettingsStoreTests.cs`: a default `ProblemsFilter`; an old record deserialises; `ResetAsync` deletes `current`.
  - Extend `Theme/ThemeServiceTests.cs`: `ResetAsync` deletes the record even when already on System, and clears the `localStorage` mirror.
  - Extend `Ai/KeyVaultTests.cs`: `UpsertConfigAsync` keeps an existing wrapped key, or creates a record with `HasKey = false`.
  - Extend `Diagnostics/RingBufferTests.cs`: `RestoreAsync` is idempotent (N20).
- [ ] T132 [P] [US4] Extend `tests/AkmlSql.Web.Tests/Services/AnalyserServiceTests.cs` (R50, N18). `Enabled = false` gives an empty response and no engine call. A null store counts as enabled, and the existing tests stay green.
- [ ] T133 [P] [US4] Write `tests/AkmlSql.Web.Tests/Settings/SettingsSectionsTests.cs` (R44, R51, FR-052–FR-057, FR-060, FR-064).
  - Every section renders its `SettingRow`s with `setting-{id}`.
  - General: the theme applies at once with a notification.
  - Queries: the default row limit writes the store.
  - Code analysis:
    - enable, re-analyse, and the default filters;
    - each suppressed rule shows `suppressed-rule-{id}`, and `suppressed-rule-undo-{id}` removes the key as stored.
  - AI assistance: `provider-needs-key-{id}`; Remove provider confirms through `FakeDialogService`.
  - Format: the active-style select, labelled "Style", and the "Format styles…" link.
  - Editor: the reset-session text says exactly what it clears, and the confirmation names the document.
  - Diagnostics: level filters, Export and Clear (Clear confirms).
  - Schema cache: the Clear-all confirmation names N databases and their size.
  - Connections: `engine-add-*` and `settings-manage-connections` exist; Remove engine connection and Delete saved connection confirm through `FakeDialogService`; Re-pair asks for a new PIN and replaces the pairing token; a saved connection that fails `ValidateTarget` shows its reason in place and stays listed; no password field and no "Remember password" render.
  - Loading, filtering and switching sections make zero store writes (assert the adapter's write count).
- [ ] T134 [P] [US4] Write `tests/AkmlSql.Web.Tests/Settings/SettingsResetTests.cs` (R46, R47, FR-058, FR-059).
  - Each section's `DescribeResetAsync` lines. With three providers, AI reads "This removes your 3 providers and their keys".
  - Resetting one section leaves the other stores untouched.
  - Restore all lists every line in one confirmation and covers General, Editor, Format, Queries, Code analysis, AI assistance and Layout.
  - It never touches connections, pairing tokens, the schema cache, snippets, history, chat or the document.
- [ ] T135 [P] [US4] Write `tests/AkmlSql.Web.Tests/Settings/SettingsPortabilityTests.cs` (R48, R49, FR-059, SC-013).
  - Export:
    - the envelope matches data-model.md §1.5;
    - seed secrets into the key vault, the pairing vault and the saved connections, then assert that no secret string and no key-material field name appears in the export.
  - Import:
    - applies only the sections present and leaves absent ones unchanged;
    - counts unknown properties, nested ones included through `[JsonExtensionData]`, and unknown sections: "N settings skipped";
    - imported providers have `HasKey = false`; the active provider is applied only if it exists; the fully-local guard is re-applied;
    - a wrong `format` is rejected; a newer `version` is accepted.
- [ ] T136 [P] [US4] Write the filter and catalog tests in `tests/AkmlSql.Web.Tests/Settings/` (R55, FR-061).
  - `tests/AkmlSql.Web.Tests/Settings/SettingsFilterTests.cs`:
    - "timeout" shows only matching rows, each under its section name;
    - sections with no match get `aria-disabled`;
    - clearing the box restores the open section in full.
  - `tests/AkmlSql.Web.Tests/Settings/SettingsCatalogConsistencyTests.cs`: every catalog entry renders a `setting-{id}`, and every rendered `data-setting` is in the catalog.
- [ ] T137 [P] [US4] Write `tests/AkmlSql.Web.Tests/js/akml-theme-boot.parity.test.mjs` (R54, FR-092).
  - The theme maps in `akml-theme-boot.js` and `akml-theme.js` are equal.
  - The boot script reads `localStorage['akml.theme']`, falling back to OS detection, and sets the link `href` and `data-akml-theme` synchronously.
- [ ] T138 [P] [US4] Write `tests/AkmlSql.Web.E2E.Tests/SettingsPageTests.cs` (`BridgeE2E`), and make the by-design E2E updates (R69, R70).
  - New suite, covering quickstart §4:
    - routing and the five nav entries; the row-limit default vs the quick control; analysis off; suppressed-rule Undo;
    - the AI reset confirmation text; an export with no secrets, read from the download; the filter;
    - no theme flash, checked by an `AddInitScript` MutationObserver capturing the first applied `href`;
    - two tabs using next-load values.
  - By-design updates: make the pairing flows in `UserStory2Tests.cs`, `UserStory4Tests.cs`, `UserStory5AiTests.cs`, `EngineAutoConnectTests.cs` and `SqlServerAliasWebTests.cs` navigate to `settings/connections`, wherever they navigate for pairing. They keep the `engine-add-*` ids.

### Implementation for User Story 4

- [ ] T139 [P] [US4] Update `src/AkmlSql.Web/Services/IExecutionSettingsStore.cs`: an in-memory `SessionOverride { MaxRows, CommandTimeoutSeconds }`, `GetEffectiveAsync`, `ResetAsync` (deletes only `executionSettings`, never clears the shared store, and drops the override), `ReloadAsync`, and saving defaults clears the override (R52, R47).
- [ ] T140 [P] [US4] Create `src/AkmlSql.Web/Services/IEditorSettingsStore.cs`: `EditorSettings { FontSize = 13, WordWrap = false, TabSize = 4 }` under key `editorSettings` in the `analysisSettings` store, with `ResetAsync`, `ReloadAsync` and `Changed`. Register it in `Program.cs` (R53).
- [ ] T141 [P] [US4] Update `src/AkmlSql.Web/Services/IAnalysisSettingsStore.cs`: `ProblemsFilter { ShowInfo, ShowWarning, ShowError }` (additive), `ResetAsync` (deletes `current`), `ReloadAsync` (R47, R51).
- [ ] T142 [P] [US4] Add `ResetAsync` to `src/AkmlSql.Web/Services/IThemeService.cs` (R47, R54).
  - It bypasses `SetAsync`'s unchanged-value early return, so a reset while already on System still deletes the record.
  - `src/AkmlSql.Web/wwwroot/js/akml-theme.js` writes `localStorage['akml.theme']` (`remember = true`) on initialise and set, and clears it on reset, inside try/catch.
- [ ] T143 [P] [US4] Update the AI stores in `src/AkmlSql.Web/Services/IAiFeatureSettings.cs` and `src/AkmlSql.Web/Services/IAiKeyVault.cs` (R47, R49).
  - `src/AkmlSql.Web/Services/IAiFeatureSettings.cs`: `ResetAsync` and `ReloadAsync`.
  - `src/AkmlSql.Web/Services/IAiKeyVault.cs`: `UpsertConfigAsync(config)` rewrites name, model and endpoint, keeping an existing wrapped key, or creates the record with `HasKey = false`.
- [ ] T144 [P] [US4] Add a restored guard to `src/AkmlSql.Web/Services/IDiagnosticsRingBuffer.cs` `RestoreAsync` (~:88-103), so it is idempotent (N20). Add `ResetActiveAsync` to `src/AkmlSql.Web/Services/IProfileStore.cs`, which returns to the store default; `SetActiveIdAsync(null)` does not (R47).
- [ ] T145 [US4] Honour "Enable code analysis" in `src/AkmlSql.Web/Services/IAnalyserService.cs` and its callers (depends on T141; R50, R51, FR-034, FR-055). T132 must pass.
  - `src/AkmlSql.Web/Services/IAnalyserService.cs`: `AnalyseAsync` (~:75) reads the store first and returns empty when disabled.
  - `src/AkmlSql.Web/Pages/Editor.razor`: keep `_analysisEnabled` and check it before calling the service, so the "No problems." text (~:885) no longer lies.
  - `src/AkmlSql.Web/Shared/ProblemsListComponent.razor`:
    - takes `AnalysisEnabled` + `OnEnableAnalysis` and renders "Code analysis is off. Turn on", while the pane hides the badge;
    - takes a `DefaultFilter`, applied once on initialisation.
- [ ] T146 [US4] Replace the toolbar's persistence with a session override in `src/AkmlSql.Web/Pages/Editor.razor` (depends on T139; R52, N19, FR-052).
  - Delete `PersistCapsAsync` (~:778-779).
  - `caps-rows` and `caps-timeout` write `SessionOverride` only.
  - The editor seeds from `GetEffectiveAsync()` on mount.
- [ ] T147 [US4] Apply the editor options through `src/AkmlSql.Web/wwwroot/js/akml-editor.js` (depends on T140; R53, FR-053).
  - `src/AkmlSql.Web/wwwroot/js/akml-editor.js`: two CodeMirror `Compartment`s (`EditorView.lineWrapping`, and `EditorState.tabSize` + `indentUnit`), a `setEditorOptions(hostId, opts)` export, and an optional create-time argument.
  - `src/AkmlSql.Web/Shared/EditorComponent.razor`: apply `EditorSettings` on create and on `Changed`. Font size is CSS-only, through `--akml-editor-font-size` on the host.
- [ ] T148 [US4] Apply the theme before first paint with `src/AkmlSql.Web/wwwroot/js/akml-theme-boot.js` (depends on T142; R54, FR-092). T137 must pass.
  - Turn `src/AkmlSql.Web/wwwroot/js/akml-theme-boot.js` into a classic synchronous external script. No inline script: the CSP is `script-src 'self'`.
  - Place it right after `<link id="akml-theme-css">` in `src/AkmlSql.Web/wwwroot/index.html`.
  - It reads the mirror, falling back to OS detection, and sets the link's `href` and `data-akml-theme` before first paint.
- [ ] T149 [P] [US4] Create `src/AkmlSql.Web/Services/Settings/SettingsCatalog.cs` and `src/AkmlSql.Web/Services/Settings/SettingsFilter.cs` (R55).
  - The catalog: the nine sections in FR-051 order, with the ids and titles of contracts/ui.md §6, and one `SettingEntry { Id, SectionId, Label, Help, Keywords }` per simple preference.
  - `SettingsFilter` is a cascading value: `Text`, `Matches(entry)` (ordinal ignore-case on label or help) and per-section match counts.
- [ ] T150 [US4] Create `src/AkmlSql.Web/wwwroot/css/components/settings.css` and `src/AkmlSql.Web/Shared/Settings/SettingRow.razor` (depends on T149; R45, FR-060).
  - `src/AkmlSql.Web/wwwroot/css/components/settings.css`, imported in `app.css`:
    - the Styles page's list + main layout, with the 900 px single-column breakpoint;
    - nav rows with an inset 3 px `--akml-accent-primary` marker;
    - `.akml-setting` rows: label | control, with help spanning.
  - `src/AkmlSql.Web/Shared/Settings/SettingRow.razor`:
    - parameters `Id`, `Label`, `Help`, `Kind`, `ChildContent`;
    - renders `data-setting` and `data-testid="setting-{id}"`;
    - hides itself when the cascading `SettingsFilter` does not match.
- [ ] T151 [US4] Create the section contract and its implementations in `src/AkmlSql.Web/Services/Settings/` (depends on T139–T144, T121; R46, R47, R48).
  - `ISettingsSection.cs`: `Id`, `Title`, `DescribeResetAsync`, `ResetAsync`, `ExportAsync`, `ImportAsync(JsonElement)`.
  - Implementations for General, Editor, Format, Queries, CodeAnalysis and AiAssistance.
    - The AI reset calls `IAiKeyVault.RemoveAsync` per provider, keeping the zeroise path, then clears the active id. It does not clear chat history.
  - Layout, over `IWorkspaceLayoutStore` (from US3, T121).
  - Section DTOs. Every DTO and every nested object carries `[JsonExtensionData]`. Enums are strings.
- [ ] T152 [US4] Create `src/AkmlSql.Web/Services/Settings/SettingsPortabilityService.cs` (depends on T151, T015; R48, R49). T134 and T135 must pass.
  - Restore all, Export and Import.
  - The export envelope reads only the preference stores and `IAiKeyVault.ListAsync` configs, so secrets are excluded by construction.
  - Export downloads `akmlsql-web-settings-yyyyMMdd.json` via `downloadText`.
  - Import reports applied and skipped counts.
  - Register it and the sections in `Program.cs`.
- [ ] T153 [US4] Create `GeneralSection.razor`, `EditorSection.razor`, `FormatSection.razor` and `QueriesSection.razor` under `src/AkmlSql.Web/Shared/Settings/` (depends on T150, T151; FR-052–FR-054).
  - Every simple preference is a `SettingRow`. Each section calls `ReloadAsync` on init and writes only on a user change.
  - Changes apply at once with a notification.
  - Editor's "Reset editor session" says exactly what it clears and confirms naming the document (R64).
  - Format's active style is labelled "Style", with a link to `/styles` labelled "Format styles…".
  - Queries holds the row limit, timeout, "Show column types" and "Retain line breaks on copy or save".
- [ ] T154 [US4] Create `src/AkmlSql.Web/Shared/Settings/CodeAnalysisSection.razor` (depends on T150, T145; R51, FR-055).
  - The enable switch, the re-analyse-after-format switch and the default Problems filters.
  - Rules suppressed everywhere (`RuleOverrides` value `off`) with Undo; other values listed read-only as severity remaps.
  - Rule titles come from the in-process `RuleRegistry`.
- [ ] T155 [US4] Extract `src/AkmlSql.Web/Shared/Settings/AiAssistanceSection.razor` from `src/AkmlSql.Web/Pages/SettingsAi.razor`, then delete the page (depends on T150, T143, T017, T019; R44, R49, FR-056).
  - The extraction is mechanical: drop `@page`, `<PageTitle>`, `<h2>` and the duplicated `<style>`. Keep `@code` and every label and role text the E2E suites select.
  - Status lines go to `INotificationService`.
  - Provider hints match what the form allows. Providers without a key show the Needs key badge. Remove provider confirms through `IDialogService`.
- [ ] T156 [US4] Extract `src/AkmlSql.Web/Shared/Settings/SchemaCacheSection.razor` from `src/AkmlSql.Web/Pages/SchemaCacheSettings.razor`, then delete the page (depends on T150, T017; R44, R57). Clear all confirms, naming N databases and their size. A single-entry clear confirms too. Restore defaults equals Clear all.
- [ ] T157 [US4] Extract `src/AkmlSql.Web/Shared/Settings/DiagnosticsSection.razor` from `src/AkmlSql.Web/Pages/Diagnostics.razor`, then delete the page (depends on T150, T144; FR-062). It keeps the export, clear (with confirmation) and level filters, and must work while mounted hidden. Restore defaults resets the level filters.
- [ ] T158 [US4] Create `src/AkmlSql.Web/Shared/Settings/ConnectionsSection.razor` from the engine and SQL-connection parts of today's `src/AkmlSql.Web/Pages/Settings.razor` (depends on T150, T017; R32, FR-057).
  - Engine connections: add, connect, remove (with confirmation), and re-pair with a new PIN.
  - SQL Server connections: saved connections with connect, disconnect, manage and delete (with confirmation, also in `src/AkmlSql.Web/Shared/ConnectionManagerModal.razor`).
  - One vocabulary throughout.
  - A saved connection that fails the server check says why in place and is never removed.
  - No password field and no "Remember password".
  - This section has no Restore defaults.
- [ ] T159 [US4] Rewrite `src/AkmlSql.Web/Pages/Settings.razor` (depends on T152–T158; R43, R55, FR-050, FR-058, FR-059, FR-061). T130, T133 and T136 must pass.
  - Routes `@page "/settings"`, `"/settings/{Section}"` and `"/diagnostics"`, with the alias map.
  - The section list (`settings-nav-{id}`).
  - Header:
    - the filter box `settings-filter`;
    - `settings-restore-all`, `settings-export`, and `settings-import` with `settings-import-input`, an `InputFile` accepting `.json` up to 1 MB.
  - Per-section `settings-restore-{id}` buttons, none for Connections, confirming through `IDialogService` with the `DescribeResetAsync` lines.
  - Filter mode renders every section, with zero-match sections greyed.
  - Test ids `settings-page`, `settings-section-{id}`.
- [ ] T160 [US4] Update the navigation and links, starting with `src/AkmlSql.Web/Shared/NavMenu.razor` (depends on T159; R43, FR-062).
  - `src/AkmlSql.Web/Shared/NavMenu.razor`: five entries.
  - `src/AkmlSql.Web/Shared/CommandPalette.razor` (~:489-497): navigation entries generated from `SettingsCatalog`.
  - `src/AkmlSql.Web/Shared/StatusBar.razor` (~:36, :55-58): "Set up engine" → `/settings/connections`; the engine indicator → `/settings/diagnostics`.
  - `src/AkmlSql.Web/Shared/AiPanel.razor` (~:23, :34) and `src/AkmlSql.Web/Shared/AiChatPanel.razor` (~:25): links → `/settings/ai-assistance`.
- [ ] T161 [US4] Update `src/AkmlSql.Web/Shared/ProfilePickerComponent.razor` (depends on T144, T017; FR-054).
  - The label reads "Style", not "Profile".
  - Deleting a style calls `ResetActiveAsync`, so every place falls back to the same default.
  - The picker and Settings › Format always agree, through `IProfileStore`'s active id and `Changed`.
  - Its JS `confirm` (~:201) becomes `IDialogService.ConfirmAsync`, naming the style.
- [ ] T162 [US4] Story gate: run the US4 suites and quickstart §4, then `scripts/compare-test-results.ps1`.
  - Run `tests/AkmlSql.Web.Tests` (`Settings/*`, `Services/*`, `Ai/*`, `Theme/*`, `Diagnostics/*`), the node tests, `SettingsPageTests` and the by-design-updated suites.
  - Walk quickstart §4. Run the compare script against T003. The only expected differences are the R69 by-design list.
  - Build the whole solution in one pass (MSBuild restore + Release build, see "Path conventions") and run `scripts/generate-theme-css.ps1 -CheckOnly` for both theme folders. Both must be green (Constitution II).
  - Update the docs this story changed, in the same change: `CLAUDE.md` (structure notes), this story's rows in the spec 041 entry of `doc/progress.md`, and any affected `doc/*.md` (Constitution, Documentation currency).

**Checkpoint**: US4 works on its own. Every visible setting changes something (SC-005).

---

## Phase 7: User Story 5 — Tabs and documents that behave like SSMS (Priority: P2)

**Goal**: every tab strip looks and works the same. A named document with New query, Open, Save and Save as; a modified marker; the browser title; History naming.

**Independent Test**: operate every tab strip with mouse and keyboard. Start a new query, open a file, save under a name, and find it under that name in History. Run quickstart §5.

### Tests for User Story 5 (write first; they must fail)

- [ ] T163 [P] [US5] Write `tests/AkmlSql.Web.Tests/Editor/EditorDocumentTests.cs` (R64, FR-072, FR-089).
  - Names:
    - `DefaultName(n)` = `SQLQuery{n}`; `IsDefaultName`;
    - `SanitizeFileName` removes characters not allowed in file names and the extension, trims, and turns an empty result into the default.
  - Titles: `TitleFor("orders-audit", true, "SERVER", "db")` = `orders-audit* - SERVER.db - AKML SQL`; with no connection, `SQLQuery1 - AKML SQL`.
  - Files:
    - `DecodeFile(bytes)` reads UTF-8 with and without BOM, and UTF-16 LE and BE with BOM;
    - the size check bounds both the characters (`DocumentSizeLimit.MaxDocumentSizeChars`) and the UTF-8 byte length, with a message naming the 10 MB limit.
- [ ] T164 [P] [US5] Extend `tests/AkmlSql.Web.Tests/EditorSessionKeyTests.cs` (R64, edge case "two browser tabs").
  - `DocumentName`, `IsModified` and `NextDefaultNumber` round-trip, and both writers carry them.
  - `StartDocumentAsync` writes a fresh record with a new `SessionKey`. `ClearAsync` is unchanged.
  - An old record without the fields gets the defaults.
  - Load and restore never write.
- [ ] T165 [P] [US5] Extend `tests/AkmlSql.Web.Tests/History/WebHistoryLogicTests.cs` (R64, N11). `BuildRecordRequest` sends `tabTitle` = the name when it is not a default name, else null.
- [ ] T166 [P] [US5] Write `tests/AkmlSql.Web.Tests/Editor/EditorDocumentFlowTests.cs` for the flow service of T174 (R64, FR-015, FR-072).
  - New query:
    - with a modified document it confirms, and Cancel changes nothing;
    - confirming starts `SQLQuery{next}`: the counter increments, the replaced name is never reused, and the modified marker is clear.
  - Open:
    - confirms first when modified, then decodes the file;
    - refuses a file over the limit with a message;
    - names the document after the file.
  - Save:
    - a default-named document prompts for a name; a named one does not;
    - Save as always prompts and renames under the same `SessionKey`;
    - the download is `{name}.sql`, UTF-8 with BOM.
  - Modified state: modified = text ≠ baseline, and a programmatic set-text does not mark the document modified.
  - Pending grid edits are confirmed first, through `ConfirmDiscardPendingEditsAsync`.
- [ ] T167 [P] [US5] Update `tests/AkmlSql.Web.Tests/Ai/AiPanelTests.cs` and `tests/AkmlSql.Web.Tests/Styles/StylesPageTests.cs` (R58, FR-070, FR-071). The AI dock tabs keep `ai-tab-actions` and `ai-tab-chat`, the style preview tabs keep `preview-sample` and `preview-mysql`, and both render through `TabStrip` (`role="tablist"`). The badge markup matches the results pane's.
- [ ] T168 [P] [US5] Write the US5 browser suites in `tests/AkmlSql.Web.E2E.Tests/` (R70).
  - `tests/AkmlSql.Web.E2E.Tests/TabStripKeyboardTests.cs`: Left, Right, Home and End activate at once in the results, AI and style-preview strips; the active marker's computed style is identical across strips; `aria-selected`.
  - `tests/AkmlSql.Web.E2E.Tests/DocumentFlowTests.cs`, covering quickstart §5 steps 2–5:
    - `SetInputFilesAsync` with a UTF-16 and a UTF-8 file;
    - the download event's file name and BOM bytes; the page title;
    - the History entry names, read on the History page.

### Implementation for User Story 5

- [ ] T169 [P] [US5] Move the dock tab strip in `src/AkmlSql.Web/Shared/AiPanel.razor` onto `TabStrip`, keeping its ids. `Trailing` holds the close button (depends on T021).
- [ ] T170 [P] [US5] Move the preview tab strip in `src/AkmlSql.Web/Pages/Styles.razor` onto `TabStrip`, keeping `preview-sample` and `preview-mysql` (depends on T021).
- [ ] T171 [P] [US5] Create `src/AkmlSql.Web/Services/EditorDocument.cs` (pure; R64). T163 must pass.
- [ ] T172 [P] [US5] Add the document fields in `src/AkmlSql.Web/Services/IEditorSessionStore.cs` and `src/AkmlSql.Web/Services/EditorSessionKeys.cs` (R64, data-model.md §1.3).
  - `EditorSessionRecord` in `src/AkmlSql.Web/Services/IEditorSessionStore.cs`: `DocumentName`, `IsModified`, `NextDefaultNumber`.
  - `src/AkmlSql.Web/Services/EditorSessionKeys.cs`: thread them through both existing writers (the whole-record `SaveAsync` trap) and add `StartDocumentAsync(store, name, text)`.
  - T164 must pass.
- [ ] T173 [P] [US5] Add the `tabTitle` parameter to `BuildRecordRequest` in `src/AkmlSql.Web/Services/WebHistoryLogic.cs` (R64). T165 must pass.
- [ ] T174 [US5] Create `src/AkmlSql.Web/Services/EditorDocumentFlow.cs`, which orchestrates New, Open, Save and Save as over `IEditorSessionStore`, `IDialogService`, a text accessor and `downloadText`. Register it in `Program.cs` (depends on T171, T172, T017, T015). T166 must pass.
- [ ] T175 [US5] Wire documents into `src/AkmlSql.Web/Pages/Editor.razor` (depends on T174, T173, T073; FR-072, FR-073, FR-089).
  - The document bar: `doc-name` (Icon `file-code` + name) and `doc-modified` (`*`).
  - The toolbar: `doc-new`, `doc-open` with a hidden `InputFile` `doc-open-input` (`accept=".sql,.txt"`, the Snippets idiom), `doc-save`, `doc-saveas`.
  - Commands `editor:new`, `editor:open`, `editor:save` and `editor:saveas`, bound to Ctrl+Alt+N, Ctrl+Alt+O, Ctrl+S and Ctrl+Shift+S.
  - Saving: replace the `downloadBase64` path in `SaveAsync` (~:906) with the flow.
  - Text state: keep `_initialText` in sync; suppress the modified marker until the baseline is set after a programmatic `SetTextAsync`.
  - Title and status:
    - `<PageTitle>` from `EditorDocument.TitleFor`;
    - `IWorkspaceStatus.SetDocument`.
  - History: record `tabTitle` and the current `SessionKey`, new per New and Open.
  - Grid tie-ins:
    - clear the editor diagnostics on New and Open;
    - pass the document name to `ResultSetGrid`'s Save results;
    - gate New and Open on pending edits.
- [ ] T176 [US5] Story gate: run the US5 suites and quickstart §5, then `scripts/compare-test-results.ps1`.
  - Run `tests/AkmlSql.Web.Tests` (`Editor/*`, `History/*`, `Shell/*`, `Ai/*`, `Styles/*`), `TabStripKeyboardTests` and `DocumentFlowTests`.
  - Walk quickstart §5. Run the compare script against T003.
  - Build the whole solution in one pass (MSBuild restore + Release build, see "Path conventions") and run `scripts/generate-theme-css.ps1 -CheckOnly` for both theme folders. Both must be green (Constitution II).
  - Update the docs this story changed, in the same change: `CLAUDE.md` (structure notes), this story's rows in the spec 041 entry of `doc/progress.md`, and any affected `doc/*.md` (Constitution, Documentation currency).

**Checkpoint**: US4 and US5 work on their own.

---

## Phase 8: User Story 6 — A shell that reads as one professional tool (Priority: P3)

**Goal**:
- A grouped toolbar with icons and tooltips, and an SSMS database selector that follows `USE`.
- Status-bar segments with short versions. Truthful style group names. Analyser findings shown in the editor.
- A Schema panel filter and Refresh. One header, button, table, dialog and notification style everywhere, with no browser pop-ups.
- Empty states, styled start-up screens, and contrast that holds in three themes.

**Independent Test**: compare the editor, History, Snippets, Schema cache and Diagnostics at one window size. Hover every toolbar button, switch database from the toolbar, read the status bar, and trigger an analyser finding. Run quickstart §6.

### Tests for User Story 6 (write first; they must fail)

- [ ] T177 [P] [US6] Write `tests/AkmlSql.Engine.Tests/Handlers/ListDatabasesHandlerTests.cs`, with no SQL (R25).
  - With `SessionId` set and an empty connection string, the handler uses the session's stored connection string (fake session manager).
  - An unknown or disconnected session gives `Ok = false` with an error.
  - Alias resolution and `BridgeSqlTargetGuard` are re-run; a blocked target is refused.
- [ ] T178 [P] [US6] Write `tests/AkmlSql.Engine.Tests/Handlers/ChangeDatabaseHandlerTests.cs`, with no SQL (R26, N27).
  - An unknown session gives an error.
  - Database names with `;`, `=`, quotes or control characters are rejected.
  - A guard refusal gives an error.
  - Success calls `SessionManager.SetDatabase` and a claimed Phase A/B populate on a fake cache manager. It never calls `SchemaRefreshService.Refresh` on a missing key.
- [ ] T179 [P] [US6] Extend the registration tests in `tests/AkmlSql.Engine.Tests/Handlers/` and `tests/AkmlSql.Engine.Tests/InProcess/` (R29, X4).
  - `tests/AkmlSql.Engine.Tests/Handlers/HandshakeHandlerTests.cs`: `HostKind` and `RunsAs` come from `EngineIdentity` in all three Ok branches; the capabilities include `session.database.v1`.
  - `tests/AkmlSql.Engine.Tests/InProcess/AllMessageTypesInProcessTests.cs`: a factory entry for 217; 217 is registered and 218 is not.
- [ ] T180 [P] [US6] Extend `tests/AkmlSql.Engine.Tests/Execution/ExecuteQueryIntegrationTests.cs` (skip without SQL; R26, R27).
  - Switching keeps the session: create `#t` in tempdb, switch to master, and `#t` is still readable, `DB_NAME()` = master, and the session is re-keyed.
  - `USE master; SELECT DB_NAME()` reports `CurrentDatabase = master`. `USE master; RAISERROR(...)` still carries `CurrentDatabase` on the error envelope.
  - An Apply after a later `USE` writes to the source database through the `BaseCatalog` fallback.
- [ ] T181 [P] [US6] Extend `tests/AkmlSql.Core.Tests/AppVersionTests.cs` (R31). `StripBuildMetadata("1.26.1007.0613+abc…")` = `1.26.1007.0613`; input with no `+` is unchanged; null and empty are safe. `Informational` returns the raw string.
- [ ] T182 [P] [US6] Write the web database and identity tests in `tests/AkmlSql.Web.Tests/Bridge/` and `tests/AkmlSql.Web.Tests/Services/` (R25–R29).
  - `tests/AkmlSql.Web.Tests/Bridge/DatabaseSelectorComponentTests.cs`:
    - the list loads lazily on open, and the current database is selected even when absent from the list;
    - disabled without the capability, without a connection, or while a query runs;
    - shows a busy state and an inline error;
    - picking a database calls `ChangeDatabaseAsync`.
  - Extend `Bridge/SqlConnectionServiceGuardTests.cs`:
    - `ListDatabasesForSessionAsync` and `ChangeDatabaseAsync` run `ValidateTarget` first and require the capability;
    - `ReportCurrentDatabase` raises `StateChanged` only on a change;
    - `Login` is retained.
  - Extend `Bridge/HandshakeClientTests.cs`: `EngineHostKind` and `EngineRunsAs` are set from the handshake and cleared on disconnect.
  - Extend `Services/ProfileStoreTests.cs`: `EngineStylesGroupName` is "Shared with SSMS" for `ide`, otherwise and for null "On this engine".
  - Extend `Bridge/StatusIndicatorTests.cs`: the cache probe uses `(SqlConn.Server, SqlConn.Database)`.
- [ ] T183 [P] [US6] Extend `tests/AkmlSql.Web.Tests/Shell/StatusBarSegmentsTests.cs` (R65, FR-083).
  - `status-caret` reads "Ln 1, Col 12".
  - `status-version-web` and `status-version-engine` show the stripped versions, with the raw strings as `title`. `status-version-mismatch` shows when the stripped versions differ.
  - The login shows beside `status-connection`, whose text format is unchanged.
  - Clicking the server opens the connection manager.
- [ ] T184 [P] [US6] Write the shell tests in `tests/AkmlSql.Web.Tests/` `Shell/`, `Styles/`, `Bridge/` and `Cache/` (R40, R63, FR-063, FR-080, FR-084, FR-093).
  - `tests/AkmlSql.Web.Tests/Shell/NavMenuTests.cs`: a `NavLink` is active for the current route, `/editor` included; `nav-{route}` ids; `BunitNavigationManager`.
  - `tests/AkmlSql.Web.Tests/Styles/ProfilePickerDisambiguationTests.cs`:
    - duplicate names show "(built-in)" / "(on this engine)" in the closed control;
    - the group reads "Shared with SSMS" only for `ide`;
    - empty groups are hidden; no disabled "future" options.
  - Extend `Bridge/SchemaTreeComponentTests.cs`:
    - the header `schema-header` reads "server · database";
    - `schema-filter` narrows the tree after 150 ms, keeping parents, expanding matches and hiding empty kinds only while filtering;
    - `schema-refresh` sends notification 6 through `SendNotificationAsync`, then calls `ISchemaSync.RefreshAsync`; `schema-loading` shows meanwhile;
    - kind icons render.
  - Extend `Cache/SchemaSyncTests.cs`: `RefreshAsync` re-arms the poll, fetches Phase A and B unconditionally and raises `ChecksumDrifted`.
- [ ] T185 [P] [US6] Rewrite the dialog tests, starting with `tests/AkmlSql.Web.Tests/Styles/StylesReviewFixTests.cs` (R59, R69, SC-012).
  - Rewrite `tests/AkmlSql.Web.Tests/Styles/StylesReviewFixTests.cs` against `FakeDialogService`, registered in the Styles fixtures. Today its four tests assert JS `prompt`/`confirm` calls.
  - Add `tests/AkmlSql.Web.Tests/History/HistoryDialogTests.cs`: the rename prompt, the delete confirmation, the remove-older confirmation.
  - Add `tests/AkmlSql.Web.Tests/Snippets/SnippetDeleteConfirmTests.cs`.
  - Add a palette `schema:clear` confirmation test in `tests/AkmlSql.Web.Tests/Services/CommandRegistryStateTests.cs`.
- [ ] T186 [P] [US6] Write `tests/AkmlSql.Web.E2E.Tests/ShellPolishTests.cs` (`BridgeE2E`, `WithSqlOrSkipAsync`), and make the by-design E2E updates (R69, R70).
  - New suite, covering quickstart §6:
    - toolbar groups and tooltips;
    - the database selector: a switch keeps a `#temp` table, and `USE master` moves the selector;
    - the status-bar segments;
    - every destructive action confirms in-app, while a `page.Dialog` handler that fails the test proves no browser pop-up opens anywhere (SC-012);
    - page headers compared by computed style;
    - the analyser underline on line 7 (`.cm-lintRange` on that line); the styled start-up screen;
    - record every request with `page.Request` for the whole suite and assert each goes to the page's own origin or the paired engine's bridge. The suite triggers no AI call, so nothing else is expected (FR-101).
  - By-design updates:
    - in `FormatStylesTests.cs` and `FormatStylesSharedEngineTests.cs`, replace the `page.Dialog` subscriptions with the in-app `dialog-*` ids;
    - `FormatStylesSharedEngineTests` expects "On this engine" from its `--web` sandbox engine (N28).

### Implementation for User Story 6

- [ ] T187 [P] [US6] Update the Core messages under `src/AkmlSql.Core/Ipc/` and the version helper `src/AkmlSql.Core/AppVersion.cs` (R25, R26, R29, R31, contracts/ipc.md).
  - `src/AkmlSql.Core/Ipc/Messages/ListDatabasesMessages.cs`: key 1 `SessionId`.
  - New `src/AkmlSql.Core/Ipc/Messages/ChangeDatabaseMessages.cs`: request keys 0–1 and response keys 0–3.
  - `src/AkmlSql.Core/Ipc/RpcMessage.cs`: `ChangeDatabase = 217`, `ChangeDatabaseResult = 218`.
  - `src/AkmlSql.Core/Ipc/Messages/HandshakeResponse.cs`: key 8 `HostKind`, key 9 `RunsAs`.
  - `src/AkmlSql.Core/AppVersion.cs`: `StripBuildMetadata(string)` (the existing `+` logic, extracted) and a raw `Informational`.
  - T181 must pass.
- [ ] T188 [US6] Add the database switch to the engine in the new `src/AkmlSql.Engine/Handlers/Control/ChangeDatabaseHandler.cs` and in `src/AkmlSql.Engine/Server/SessionManager.cs` (depends on T187; R25, R26, N7, N27). T177 and T178 must pass.
  - `src/AkmlSql.Engine/Server/SessionManager.cs`: `SetDatabase(sessionId, db, connStr)`, which updates `DatabaseName` and `ConnectionString` without the `ConnectionChanged` dispose path.
  - `src/AkmlSql.Engine/Handlers/Control/ListDatabasesHandler.cs`: the session branch.
  - New `src/AkmlSql.Engine/Handlers/Control/ChangeDatabaseHandler.cs`, a typed handler:
    - check the identifier, rewrite `Initial Catalog`, run `BridgeSqlTargetGuard`;
    - call `EnsureOpenAsync`, then the **synchronous** `conn.ChangeDatabase(db)` under the session gate;
    - call `SetDatabase`;
    - create the cache under a claimed Phase A/B populate, exactly as `ConnectionChangedHandler` does;
    - reply with `conn.Database` and `ConnectionWasReset`.
  - Register 217 in `src/AkmlSql.Engine/EngineHandlerRegistry.cs`, and add `session.database.v1` to `src/AkmlSql.Engine/Capabilities.cs`.
- [ ] T189 [US6] Report the current database after every execute in `src/AkmlSql.Engine/Execution/ExecuteQueryHandler.cs` (depends on T188 and US2's T098; R27).
  - Fill key 12 `CurrentDatabase` from `conn.Database`, captured in a `finally` inside the gated callback after the reader is disposed. Every reply carries it, error and timeout envelopes included.
  - When it differs from the session's database, re-key the session as T188 does.
  - T180 must pass.
- [ ] T190 [P] [US6] Add the engine identity in the new `src/AkmlSql.Engine/EngineIdentity.cs` (depends on T187; R29, N8). T179 must pass.
  - New `src/AkmlSql.Engine/EngineIdentity.cs`: static `HostKind` and `RunsAs`.
  - `src/AkmlSql.Engine/EngineHost.cs`:
    - `RunAsync` (`--pipe`) → `ide`;
    - `RunWebAsync` → `service` when `WindowsServiceHelpers.IsWindowsService()`, else `console`;
    - `RunsAs` = `WindowsIdentity.GetCurrent().Name`.
  - `src/AkmlSql.Engine/Handlers/Handshake/HandshakeHandler.cs`: fill keys 8 and 9 in the three Ok branches.
- [ ] T191 [P] [US6] Document in `doc/ipc-api.md`: 94/194 `SessionId`, 217/218, `session.database.v1` and handshake keys 8–9 (depends on T187).
- [ ] T192 [US6] Update `src/AkmlSql.Web/Services/ISqlConnectionService.cs` (depends on T187; R25–R28, R32). T182's service parts must pass.
  - Keep `WindowsAuth` and `Login` from connect.
  - Add `ListDatabasesForSessionAsync`.
  - Add `ChangeDatabaseAsync`: `ValidateTarget` → 217 → set `Database` → `StateChanged`.
  - Add `ReportCurrentDatabase`.
  - `IQueryExecutionService.cs` forwards `result.CurrentDatabase`.
  - History records `result.CurrentDatabase ?? SqlConn.Database`.
  - Fill `ApplyChangesRequest.BaseCatalog` from the set's `BaseCatalog`, or else from the `CurrentDatabase` in effect when the set was produced.
- [ ] T193 [US6] Create `src/AkmlSql.Web/Shared/DatabaseSelectorComponent.razor` (`db-selector`, `db-selector-list`) and place it in the toolbar's first group in `Pages/Editor.razor` (depends on T192; R28, FR-082). T182's selector tests must pass.
- [ ] T194 [US6] Name style groups truthfully in `src/AkmlSql.Web/Shared/ProfilePickerComponent.razor` and its sources (depends on T187, T161; R29, FR-063, FR-084).
  - `src/AkmlSql.Web/Services/IEngineBridge.cs`: `EngineHostKind` and `EngineRunsAs` as default-interface members, set from the handshake and cleared on disconnect.
  - `src/AkmlSql.Web/Services/IProfileStore.cs`: `EngineStylesGroupName`.
  - `src/AkmlSql.Web/Shared/ProfilePickerComponent.razor` (~:22):
    - group names from `EngineStylesGroupName`;
    - the closed control shows the group for duplicate names;
    - empty groups are hidden, and no disabled "future" options.
  - `src/AkmlSql.Web/Pages/Styles.razor` (~:344): the group label.
  - `Shared/Settings/DiagnosticsSection.razor`: show `HostKind` and `RunsAs`.
  - T184's picker tests must pass.
- [ ] T195 [US6] Add the remaining status-bar segments to `src/AkmlSql.Web/Shared/StatusBar.razor` (depends on T107, T187; R65, FR-083). T183 must pass.
  - In `src/AkmlSql.Web/Shared/StatusBar.razor`, the login beside `status-connection`.
  - The caret, `status-caret`, from a throttled `selectionSet` branch in the update listener in `src/AkmlSql.Web/wwwroot/js/akml-editor.js` → `[JSInvokable] OnSelectionChangedFromJs` in `EditorComponent.razor` → `IWorkspaceStatus.SetCaret`.
  - Versions:
    - the web version from the web assembly's informational version (N29); the engine version from the handshake;
    - both stripped, with the raw strings on hover;
    - a `triangle-alert` mismatch marker.
  - Fix `ProbeCacheAsync`'s key (~:254-271).
  - A server click opens the connection manager.
- [ ] T196 [US6] Regroup the toolbar in `src/AkmlSql.Web/Pages/Editor.razor` (depends on T193, T011; FR-081).
  - Groups, separated by `.akml-toolbar-sep`: database selector | Execute, Parse | Format, Analyse | Refactor ▾, AI ▾ | New, Open, Save, Save as | View ▾, then the quick control.
  - Every button is an `.akml-btn` with an `Icon`, a label, and `title` = "Name (Keys)" from `WorkspaceKeyMap`.
  - Keep the accessible names through `aria-label`: `Format`, exact, is load-bearing. Keep the `execute-button` and `ai-button` ids.
- [ ] T197 [US6] Complete the Schema panel in `src/AkmlSql.Web/Shared/SchemaTreeComponent.razor` and `src/AkmlSql.Web/Services/ISchemaSync.cs` (R40, N27, FR-093). T184's schema and sync tests must pass.
  - Header: `schema-header` reads "server · database".
  - Refresh: `schema-refresh` sends `SchemaRefreshRequest` (6) through `IEngineBridge.SendNotificationAsync`, then calls the new `ISchemaSync.RefreshAsync`. That re-arms the poll, waits for the engine's phase to settle, fetches Phase A and B unconditionally and raises `ChecksumDrifted`.
  - Filter: `schema-filter` (`aria-label="Filter objects"`, 150 ms debounce) filters `EnumerateKinds` by `ObjectName` (ordinal ignore-case contains). Parents are kept and matches expanded. The `Virtualize` branch gets the filtered list.
  - Icons: one `Icon` per kind, coloured by `--akml-iconbadge-*` (keys use the `index` token); chevrons via `Icon`.
  - Loading: a `schema-loading` row.
  - `Pages/Editor.razor` calls `ReportEditorActive` on editor activity.
  - Update `tests/AkmlSql.Web.Tests/Fakes/WebTestFakes.cs` `FakeSchemaSync`.
- [ ] T198 [US6] Highlight the current page in `src/AkmlSql.Web/Shared/NavMenu.razor` (R63, FR-080). T184's nav tests must pass.
  - Use `NavLink` with `Prefix` for Snippets, Format styles, History and Settings.
  - Editor uses `href=""` with `Match.All`, plus a `LocationChanged` check that also marks `/editor`.
  - Add the `nav-{route}` ids.
  - The palette and schema-object navigation canonicalise to `/`.
  - `.akml-nav-link.active` uses `--akml-text-primary`, with an HC rule using `--akml-text-onaccent`.
- [ ] T199 [US6] Remove every browser pop-up from `src/AkmlSql.Web` (R59, FR-086, SC-012).
  - `src/AkmlSql.Web/Pages/History.razor`: the prompt (~:498) and confirms (~:511, ~:519) → `IDialogService`. Move the hand-rolled Compare modal onto `.akml-scrim`/`.akml-dialog`.
  - `src/AkmlSql.Web/Pages/Styles.razor`: the two wrappers (~:722, ~:728), which serve eight call sites → `IDialogService`. Delete their fail-open "confirm on JS error" path.
  - Add confirmations:
    - Snippets delete in `src/AkmlSql.Web/Pages/Snippets.razor`;
    - the palette's `schema:clear` in `src/AkmlSql.Web/Shared/CommandPalette.razor`.
  - Afterwards, a grep of `src/AkmlSql.Web` (excluding `bin/`, `obj/`, `wwwroot/lib/`) for `InvokeAsync<bool>("confirm"`, `("prompt"` and `("alert"` returns nothing. T185 must pass.
- [ ] T200 [US6] Show analyser findings in the editor in `src/AkmlSql.Web/Pages/Editor.razor` (depends on T032; R39, FR-087).
  - After every successful analyse, call `EditorComponent.SetDiagnosticsAsync` with all findings. Severity 0–3 maps to hint, info, warning and error; the message reads `"{RuleId}: {Message}"`.
  - Clear them on New, Open, and when the findings reset.
  - Register palette commands "Next problem" and "Previous problem".
- [ ] T201 [US6] Switch the clipboard calls to `copyText`, so copy works on the http LAN deployment (depends on T014, T199; R5, N14). This covers `src/AkmlSql.Web/Pages/History.razor` (~:527) and `src/AkmlSql.Web/Shared/AiChatPanel.razor` (~:391). Report the result through `INotificationService`.
- [ ] T202 [US6] Migrate the duplicated styles to `shell.css` (R61, FR-085, FR-086).
  - Delete:
    - the 13 per-file `.akml-tool-button` `<style>` blocks (find them with a grep over `src/AkmlSql.Web/Pages` and `Shared`);
    - the three `.akml-overlay` copies and the two conflicting `.akml-field` definitions;
    - `.akml-profile-button` and `.akml-status-action`.
  - Switch the markup to `.akml-btn` / `.akml-scrim` / `.akml-dialog` / `.akml-field`, including `ConnectionManagerModal.razor` and `ConnectionPickerComponent.razor`.
  - `RefactorPreviewPanel.razor`'s footer buttons keep the `akml-tool-button` class, because a bUnit test selects it; the alias covers them.
  - Lower the `WebCssTokenTests` allow-list counts to the new inventory.
- [ ] T203 [US6] Apply one page header and one table style from `src/AkmlSql.Web/wwwroot/css/components/shell.css` (depends on T202; FR-085).
  - The page header (`page-header`, `page-title`, `page-actions`) goes on `Pages/History.razor` (new), `Pages/Snippets.razor`, `Shared/Settings/SchemaCacheSection.razor`, `Shared/Settings/DiagnosticsSection.razor`, `Pages/Styles.razor` and `Pages/Settings.razor`. Styles keeps its inline `styles-status` line.
  - One `.akml-table` with a selected-row style on History, Snippets, Schema cache and Diagnostics, under one content-width rule.
- [ ] T204 [US6] Use the one icon set (`src/AkmlSql.Web/Shared/Icon.razor`) everywhere (R62, FR-081).
  - Replace emoji and text symbols with `Icon` in `CommandPalette.razor`, `StatusBar.razor`, `AiPanel.razor`, `AiChatPanel.razor`, `Pages/Snippets.razor` and `Pages/History.razor`. Check that the toolbar (T196), schema tree (T197) and grid glyphs (T067) are already done.
  - Add a `NoEmojiInRazor` assertion to `tests/AkmlSql.Web.Tests/Shell/IconSpriteCoverageTests.cs`: no character from the emoji, dingbat or arrow ranges (✎ ✕ ⚠ ▶ ▾ ★ and the like) in `src/AkmlSql.Web/**/*.razor` markup, except menu glyphs on an explicit allow-list such as `▸` for submenus.
- [ ] T205 [US6] Add empty and loading states across the panes, and style the start-up screens in `src/AkmlSql.Web/wwwroot/index.html` (R66, FR-088).
  - Every pane uses `.akml-empty` with a next step: Messages before a run; Problems with "No problems found. Run Analyse (Ctrl+K, Ctrl+L)"; History, Snippets and the AI panel when empty. Panes that load also get a loading state.
  - `src/AkmlSql.Web/wwwroot/index.html` boot and crash screens get token-styled classes in `src/AkmlSql.Web/wwwroot/css/app.css`. No inline styles. The Blazor error UI keeps its id and `.reload` link.
  - The spinner uses `app.css` keyframes under the reduced-motion rule.
- [ ] T206 [US6] Make labels truthful and remove placeholders across `src/AkmlSql.Web/Pages` and `src/AkmlSql.Web/Shared` (FR-090).
  - Audit every `<button>`, menu item and link in `src/AkmlSql.Web/Pages/*.razor` and `src/AkmlSql.Web/Shared/**/*.razor` against this checklist:
    - the label names what its handler does: a button that inserts text says "Insert", not "Copy"; one that downloads says "Save" or "Export";
    - no handler is empty, a TODO, or a stub that only logs;
    - every disabled control has a `title` saying why;
    - no text such as "coming soon", "future" or "not available yet" (grep), and no disabled "future" option (FR-063);
    - no `_statusMsg`-style field or markup is left that the notification migration made dead;
    - no commented-out markup block, and no component that no page or layout renders (tests aside).
  - Fix or remove every item that fails.
  - Record the audit list in `specs/041-web-ssms-ux-parity/baseline.md`.
- [ ] T207 [US6] Pass accessibility and themes over every new surface, fixing tokens in `docs/theme-tokens.json` (FR-091, FR-103, SC-010).
  - In High contrast, text on a selected or hovered row uses `--akml-text-onaccent` in grids, tables, menus and tabs.
  - Every new control has a visible focus ring.
  - Hover-only information also shows on keyboard focus and dismisses with Escape: toolbar tooltips via `title` + `aria-describedby`, the column type, the cell value, and the analyser message (CodeMirror lint tooltip on keyboard focus of the line marker).
  - Animations respect `prefers-reduced-motion`.
  - Touch: measure the hit areas (splitters 12 px, column resizers 8 px, edge strips 24 px) at 100 % and 150 % zoom, and with Playwright's `hasTouch` emulation drag one splitter and one column edge. Nothing may depend on hover alone (spec Edge Cases, "Touch pointer").
  - Measure the WCAG AA contrast of every new token pair in Light, Dark and High contrast, and record the ratios in `baseline.md`.
  - Fix any failure in `docs/theme-tokens.json` and regenerate both theme folders.
- [ ] T208 [US6] Story gate: run the US6 suites and quickstart §6, then `scripts/compare-test-results.ps1`.
  - Run `tests/AkmlSql.Engine.Tests` (`Handlers`, `Execution`, `InProcess`), `tests/AkmlSql.Core.Tests`, `tests/AkmlSql.Web.Tests` (`Shell/*`, `Bridge/*`, `Styles/*`, `Theme/*`, `History/*`, `Snippets/*`, `Cache/*`) and the node tests.
  - Publish the Debug engine, then run `ShellPolishTests` and the updated `FormatStylesTests` / `FormatStylesSharedEngineTests`.
  - Walk quickstart §6 in all three themes with reduced motion on. Run the compare script against T003.
  - Build the whole solution in one pass (MSBuild restore + Release build, see "Path conventions") and run `scripts/generate-theme-css.ps1 -CheckOnly` for both theme folders. Both must be green (Constitution II).
  - Update the docs this story changed, in the same change: `CLAUDE.md` (structure notes), this story's rows in the spec 041 entry of `doc/progress.md`, and any affected `doc/*.md` (Constitution, Documentation currency).

**Checkpoint**: every user story works on its own.

---

## Phase 9: Polish & cross-cutting concerns

- [ ] T209 [P] Verify key capture per browser and record it in `specs/041-web-ssms-ux-parity/baseline.md` (FR-049(c), R37).
  - In Chromium, Edge and Firefox, on the editor page, check that each claimed key stops the browser's own action: F5, Ctrl+F5, Ctrl+R, F8, F6/Shift+F6, Ctrl+S, Ctrl+Shift+S, Ctrl+Alt+N, Ctrl+Alt+O, the three Ctrl+K chords, and the grid's Ctrl+Shift+C, Ctrl+0 and Shift+Alt+Arrow. Shift+Alt is the Windows input-language hotkey, so test it with two keyboard layouts installed.
  - With a Polish (Programmers) keyboard layout, typing ó and ń in the editor still works (AltGr arrives as Ctrl+Alt).
  - Record a browser × key table in `specs/041-web-ssms-ux-parity/baseline.md`.
  - For a key a browser keeps, add an alternative binding to `src/AkmlSql.Web/Services/WorkspaceKeyMap.cs` and show it in the View menu and palette (FR-049(b)). `ShortcutCollisionTests` must stay green.
- [ ] T210 [P] Add the performance and sizing checks to `tests/AkmlSql.Web.E2E.Tests/ResultsGridTests.cs` (SC-001, SC-009, FR-102, edge case "200 columns").
  - A 1,000-row × 30-column result has its tracks set within 1 s of `execute-complete`, measured with `performance.now()` marks.
  - In the same result, no rendered cell in a column narrower than the 480 px maximum has `scrollWidth > clientWidth`, checked at the top, middle and bottom of the scroll range (SC-001).
  - On a 50-column result, a drag-resize and a drag-select produce no long task over 100 ms (`PerformanceObserver('longtask')`).
  - Auto-fit completes on a 200-column result.
  - Record the numbers in `baseline.md`.
- [ ] T211 [P] Create `tests/AkmlSql.Web.E2E.Tests/WorkspaceScreenshotTour.cs` (untagged, and excluded from SC-008 by name like `SiteScreenshotTour`).
  - Capture the editor, Results, Messages, Problems, each Settings section, History and Snippets in Light, Dark and High contrast at 1440 × 900 and 1100 × 700.
  - Write the images to `specs/041-web-ssms-ux-parity/baseline/after/` for comparison with `baseline/*.png`.
- [ ] T212 [P] Finish the documentation: check `doc/progress.md` and `CLAUDE.md` against the shipped stories (each story gate already updated them), and add the release notes.
  - `doc/progress.md`: the spec 041 entry has the per-phase task table and the table from this file's "Deferred" section.
  - `CLAUDE.md`: web-structure notes for `Shared/Grid`, `Shared/Settings`, `Services/Settings`, the three new JS modules and IndexedDB v4; the "spec 041" line in "Latest merged work".
  - Release notes for two expected effects:
    - users with two app tabs open see the "upgrade is blocked" message once after the update (N26);
    - developer builds show the web/engine version-mismatch marker (N29).
- [ ] T213 Confirm the ratchets did not move. `tests/AkmlSql.Formatting.Tests` (format-parity goldens) and `CorpusGateTests` (completion corpus) must report the same numbers as T003. This feature does not touch the formatter pipeline or the completion engine.
- [ ] T214 Run the final SC-008 comparison with `scripts/compare-test-results.ps1`.
  - Re-run every suite of T003 into `$env:LOCALAPPDATA\AKML SQL\spec041-tests\after` with `*.after.trx` names.
  - Run `scripts/compare-test-results.ps1 -Before …\before -After …\after -OutDir specs/041-web-ssms-ux-parity/baseline/tests/after`.
  - Complete `baseline.md`:
    - the per-suite table;
    - the SC-008 diff, which must be empty apart from the R69 by-design list (StylesReviewFixTests rewrite, the Pr247 grid test ported to the pane, the `page.Dialog` suites, pairing navigation, the FormatStylesSharedEngineTests label, SiteScreenshotTour);
    - the three-theme results, the FR-049(c) table and the performance numbers.
- [ ] T215 Run quickstart.md end to end (§0–§7) on the dev VM against Debug web, Debug engine and local SQL Server, and tick each step in `baseline.md`. The SC-006 hallway test (10 SSMS users, six tasks in 4 minutes) cannot be automated; record it as pending the user's availability.
- [ ] T216 Refresh the product-site screenshots deliberately through `tests/AkmlSql.Web.E2E.Tests/SiteScreenshotTour.cs` after the new UI is deployed. Their images change by design (spec "Visual tests" risk). Record this as pending deployment in `baseline.md` until done.

---

## Deferred

Recorded here and in `doc/progress.md` (Constitution, Development Workflow), so scope is never lost silently.

| Item | Reason | Goes to |
|---|---|---|
| Multiple query document tabs | Clarified out of scope 2026-10-08 (spec Clarifications) | A later feature |
| Interrupting a running statement (a true Cancel) | Needs engine transport work (spec Out of Scope) | Engine follow-up |
| An engine `CutCells` key for engine-cut values | Engine work is limited to the spec's Dependencies list (R8) | Engine follow-up |
| Parsing displayed values back on commit | Strict ISO input is accepted for now (R11) | Grid follow-up |
| Navigating errors inside `CREATE PROCEDURE` bodies | The server reports `Procedure`, not a document line (R24) | Messages follow-up |
| A user setting for the maximum column width | Spec Assumptions: "may become a setting later" | Settings follow-up |
| Docking, results-to-text, execution plans, column reorder/hide/freeze | Spec Out of Scope | — |

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies. T003 must finish before the first source change.
- **Foundational (Phase 2)**: depends on Setup and blocks every story.
- **US1, US2, US3 (P1)**: start after Phase 2. Deliver them before the P2 stories.
- **US4, US5 (P2)**: start after Phase 2, and preferably after the P1 stories.
- **US6 (P3)**: starts after Phase 2. Its engine work depends on US2 (see below).
- **Polish (Phase 9)**: after all the stories you mean to ship.

### Cross-story touch points (soft dependencies)

| Task | Needs | Why |
|---|---|---|
| T104 [US2] | T064 [US1] (and the other US1 `ResultSetGrid` tasks, if in flight) | Uses the `DisplayText` accessor; same file (`ResultSetGrid.razor`), so sequence them |
| T106 [US2] | T067 [US1] | The pane sums `ResultSetGrid.HasPendingEdits`, which T067 adds |
| T108 [US2] | T073 [US1], if done | The pending-edit gate moves from `ResultsGridComponent` to `ResultsPaneComponent` |
| T124 [US3] | — | Hosts whichever results host the page renders (pane after US2, grid host before) |
| T126 [US3] | US2's Problems tab, for "re-show on Analyse with findings" | Without US2 the old Problems column is still on screen |
| T145 [US4] | T106 [US2] | Hides the Problems badge in the results pane |
| T151 [US4] | T121 [US3] | The Layout section exports `IWorkspaceLayoutStore` |
| T175 [US5] | T073 [US1] | New and Open reuse `ConfirmDiscardPendingEditsAsync` |
| T175 [US5] | T072 [US1] | Passes the document name to Save results |
| T189 [US6] | T098 [US2] | `CurrentDatabase` is captured inside the per-batch loop |
| T194 [US6] | T161 [US4] | Same file (`ProfilePickerComponent.razor`) |
| T195 [US6] | T107 [US2] | Extends the status-bar segments US2 introduced |

### Within each story

- Tests are written first and must fail before implementation.
- Pure helpers (`Shared/Grid/*`, Core `Text/*`, `ExecuteOutcome`, `WorkspaceLayout.Fit`) come before the components that use them.
- Engine: DTOs → reader → aggregator → handler loop → info messages → counts → Parse.
- Every story ends with a gate task: suites, quickstart section, and the compare script against T003.

### Parallel opportunities

- **Phase 2**: T004, T005, T006, T009, T010, T012, T013, T015, T016, T018, T020, T022, T024, T026, T028, T031, T033 and T035 can all start at once. Each component then follows its own test.
- **US1**: all 17 test tasks (T038–T054, except T043, which waits for T042's output) in parallel. Then T055–T058, T061, T062, T063 and T069 in parallel. The `ResultSetGrid.razor` tasks T064–T068, T070–T072 run in order.
- **US2**: all 14 test tasks (T075–T088) in parallel. The engine chain (T089 → T093 → T094 → T095 → T096 → T097 → T098) runs alongside the web chain (T100, T102, T103, T105, T106, T107).
- **US3**: T110–T119 in parallel. T120 → T121 runs alongside T122 → T123.
- **US4**: T130–T138 in parallel, then the six store tasks T139–T144 in parallel. T149 runs alongside them.
- **US5**: T163–T168 in parallel, then T169–T173 in parallel.
- **US6**: T177–T186 in parallel. The engine chain (T187 → T188 → T189, plus T190 and T191) runs alongside the web chain (T192 → T193, T194, T197, T198, T199).
- With several developers after Phase 2: one on US1, one on US2's engine chain, one on US3.

---

## Parallel Example: User Story 1

```text
# All US1 test tasks together (different files):
T038 GridColumnLayoutTests      T039 GridSelectionTests        T040 GridSortComparerTests
T041 GridCellTextTests          T042 golden capture (Shell)    T044 GridClipboardTests
T045 engine 255 tests           T046 ResultsGridLayoutTests    T047 ResultsGridSelectionTests
T048 ResultsGridEditingTests    T049 NullAndInvalidTests       T050 ResultsGridSortTests
T051 ContextMenu/CellValue      T052 ResultsGridHeaderTests    T053 akml-results-grid.test.mjs
T054 E2E ResultsGridTests

# Then the independent helpers together:
T055 GridColumnLayout.cs   T056 GridSelection.cs   T057 GridSortComparer.cs   T058 GridCellText.cs
T061 ResultSetReader/CrudWriteGenerator 255 fix     T062 akml-results-grid.js  T063 results-grid.css
T069 CellValueDialog.razor

# Then ResultSetGrid.razor in order: T064 → T065 → T066 → T067 → T068 → T070 → T071 → T072
```

## Parallel Example: User Story 2

```text
# Engine developer:  T089 → T090 → T091 → T093 → T094 → T095 → T096 → T097 → T098 → T099
# Web developer:     T092, T100, T102 (parallel) → T103 → T105 → T106 → T107 → T108
```

## Parallel Example: User Story 4

```text
# Stores together:   T139 ExecutionSettings  T140 EditorSettings  T141 AnalysisSettings
#                    T142 Theme reset        T143 AI stores       T144 RingBuffer/ProfileStore
# Then sections:     T151 → T152;  T153, T154, T155, T156, T157, T158 → T159 → T160
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 (Setup): record the baseline.
2. Phase 2 (Foundational): it blocks every story.
3. Phase 3 (US1): columns, selection, copy, sort and safe editing.
4. **Stop and validate** with T074. The user's first named ask, column width like SSMS, ships on its own.

### Incremental delivery

1. Setup + Foundational → foundation ready.
2. US1 → validate → demo (MVP).
3. US2 → validate. Messages, `GO`, F5 and Parse need a new engine build, so publish the engine with the web bundle.
4. US3 → validate → the P1 set is complete. This is the release threshold the spec's priority mapping sets.
5. US4 → US5 (P2) → validate each.
6. US6 (P3) → validate.
7. Phase 9 → SC-008 comparison, quickstart, docs.

Each story adds value without breaking the previous ones. The compare script at every gate enforces that.

### Parallel team strategy

After Phase 2:

- Developer A: US1, then US5.
- Developer B: US2's engine chain, then US6's engine chain (T187–T191).
- Developer C: US3, then US4.

The shared-file rule applies: `Editor.razor`, `MainLayout.razor`, `Program.cs` and `ResultSetGrid.razor` tasks run in ID order.

---

## Notes

- `[P]` = a different file with no dependency on an unfinished task. `[USn]` maps a task to a user story for traceability.
- Leave every change uncommitted. The user decides when to commit (Constitution IV).
- The research decisions marked **[corrected]** are the traps found during planning. Re-read the referenced R-entry before each engine, grid or IndexedDB task.
- Choices already made for the user (research.md, "Carried to the user"):
  - Lucide icons; UTF-8 with BOM for saved files;
  - `query-NN` for default-named documents;
  - a timeout stops later batches; `GO SELECT 2` refuses the script;
  - grid-only Ctrl+0 and Ctrl+Shift+C;
  - "Open in editor" keeps the session;
  - the AI reset keeps chat; there is no Connections reset;
  - Delete row / Restore row in the cell menu.
  - If the user reverses one, the affected tasks are T009, T175, T095, T053/T062, T151 and T070.
