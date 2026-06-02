# M5 Visual Parity Audit — Web edition vs WPF surface

**Spec**: [spec.md](./spec.md) · **Contract**: [contracts/e2e-and-parity-contract.md](./contracts/e2e-and-parity-contract.md) · **FR**: FR-026 · **SC**: SC-009

**Status**: ⏳ **PENDING CAPTURE** — this audit requires an interactive Windows workstation running **both** the WPF IDE plugin and the web edition at the same OS theme (same constraint as the M2 theme-parity audit). It cannot be produced in a headless session. The structure below is ready for the maintainer to fill: drop paired screenshots into `screenshots/`, complete the deltas table, and record dispositions.

## How to run

1. On a Windows host, run the WPF plugin (SSMS 22 or VS 2026) and the web edition (`dotnet run --project src/AkmlSql.Web`) side by side, OS theme matched (capture once in Light, repeat in Dark if deltas differ by theme).
2. For each surface below, capture a WPF screenshot and a web screenshot at the same DPI/zoom.
3. Record the host environment (OS theme, DPI %, font-smoothing) once, here:
   - **Host**: _<fill: Windows build, theme, DPI%, font smoothing>_
4. Fill the deltas table; close the highest-impact deltas in `src/AkmlSql.Web/wwwroot/css/`; file the rest as named follow-ups. **≤ 3 deltas may remain open** (SC-009, excluding the deferred multi-tab gap).

## Surfaces compared

| # | Surface | Web location | WPF reference |
|---|---------|--------------|---------------|
| 1 | Snippet picker / expansion + management page | `/snippets` (`Pages/Snippets.razor`); surround picker on the editor | `SnippetManagerDialog` |
| 2 | Refactoring menu + preview (lightweight + heavyweight) | Editor "Refactor ▾" → `RefactorPreviewPanel` / `RefactorInputDialog` | `RefactoringPreviewDialog` |
| 3 | Suppression actions on a finding | `ProblemsListComponent` `⊘line`/`⊘all` | lightbulb suppress menu (`LightbulbProvider`) |
| 4 | Cache-aware status indicator | `StatusBar` (Live / Cached / Offline / Disconnected) | connection/status affordance |

## Deltas

| ID | Surface | WPF rendering | Web rendering | Disposition |
|----|---------|---------------|---------------|-------------|
| _D1_ | _<fill>_ | _<fill>_ | _<fill>_ | _closed in css / accepted-with-reason / follow-up_ |

## Closed deltas

- _<list the css edits made under `src/AkmlSql.Web/wwwroot/css/`>_

## Accepted-with-reason / follow-ups

- _<list, each with a one-line reason — e.g. DPI sub-pixel variance, WPF-only chrome>_

## Screenshots

Paired captures live under `specs/027-m5-offline-closure/screenshots/` named `<surface>-wpf.png` / `<surface>-web.png`.
