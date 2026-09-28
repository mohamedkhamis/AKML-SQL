# Quickstart: validating SQL Prompt UI/UX parity (spec 040)

**Feature**: 040-sqlprompt-ui-parity

This file is the acceptance gate for "done" (Constitution, Development Workflow). Run the
automated suites first, then the manual scenarios in SSMS 22.

**Rules for manual checks:**
- Use the **Northwind** sample only; screenshots must never show other databases.
- Back up `%AppData%\AKML SQL\config.json` and `%AppData%\AKML SQL\history\` before you
  start. Several scenarios reset settings or delete history.

## 0. Build, deploy, automated suites

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Insiders/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet
"$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64

dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
dotnet test tests/AkmlSql.IntelliSense.Tests/AkmlSql.IntelliSense.Tests.csproj
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj
dotnet test tests/AkmlSql.Site.Tests/AkmlSql.Site.Tests.csproj
# Shell suite (net472, compiled with the Shell.Shared projitems)
"$MSBUILD" tests/AkmlSql.Shell.Shared.Tests/AkmlSql.Shell.Shared.Tests.csproj -t:Build -p:Configuration=Release -v:minimal
dotnet test tests/AkmlSql.Shell.Shared.Tests/bin/Release/net472/AkmlSql.Shell.Shared.Tests.dll
```

**Gates:**
- **Completion corpus:** no case may drop. `CorpusGateTests` must not go down from ~97.5 %.
- **Format-parity goldens:** unchanged.
- **Pre-existing red tests are excused** (listed in `doc/progress.md`): `PerformanceBaselineTests`,
  the 2 ms History timing test, `VisualReferenceCoverageTests` and the `sp031-*` goldens.

**Deploy for manual checks:**
1. Close SSMS.
2. Copy `src/AkmlSql.Ssms22/bin/Release/net472/*.dll` plus `AkmlSql.Ssms22.pkgdef` to
   `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\`.
3. Copy the engine publish output to that folder's `Engine\` subfolder.
4. Clear `%LocalAppData%\Microsoft\SSMS\22.0_*\ComponentModelCache`.
5. Start SSMS, connected to `(local)` with the Northwind database.

---

## US1 — Options I can trust (P1)

1. **Settings that do nothing are gone.** Open AKML SQL › Options and walk every page. None of
   the gap plan's Appendix A settings appear. Suggestions › Database and Labs are gone from
   the tree.
2. **Maximum suggestions.** Suggestions › Behavior: type **10** in *Maximum suggestions*
   (a number box, not a slider). Press OK. Type `SELECT * FROM dbo.` → at most 10 items.
   Set it back to 50 → more items again.
3. **Trigger delay.** Set *Trigger delay* to 1000 and press OK. Type `SELECT * FROM dbo.Pro`
   slowly: the list appears about 1 s after you stop. Ctrl+Space still opens it at once.
   Set it back to 100.
4. **Fuzzy matching.** Switch off *Enable fuzzy matching*. In `SELECT * FROM dbo.Products AS p WHERE p.`,
   type `unit` → only `UnitPrice`, `UnitsInStock` and `UnitsOnOrder` are listed.
   `QuantityPerUnit` is not listed, although substring matching includes it when the switch is
   on. Switch it back on.
5. **Detail text.** Switch off *Show nullability info*. After `p.`, the right-hand text shows
   `int, PK, IDENTITY` with no `NOT NULL`. Switch off all three detail switches: no detail
   text, and no stray `•`.
6. **Error List.** Code analysis: switch off *Show in Error List*. Open a script with
   `SELECT * FROM dbo.Products;` → no AKML entries in the Error List; the squiggles still show.
7. **Restore defaults keeps hidden data.** Manage Code Analysis Rules: switch ST001 off and
   save. Options › Code analysis › **Restore defaults**. The prompt reads *"Reset the settings
   on Code analysis?"*. Answer Yes, then OK, then reopen Manage Code Analysis Rules: ST001 is
   still off.
8. **AI reset warns.** Options › AI assistance › Restore defaults. The prompt names how many
   agents and keys it removes. Answer **No**.
9. **Cancel means Cancel.**
   1. General › Theme: Dark. Press OK.
   2. Reopen Options, change *Maximum suggestions*, press **Restore all defaults**, then
      **Cancel**.
   3. `config.json`'s modified time is unchanged. The window didn't close and reopen by itself.
10. **System theme.** Set Theme to *System* with SSMS in a dark theme and press OK. Reopen
    Options: it opens in dark and saves nothing (check `config.json`'s modified time). Your AI
    agents are all still there.

## US2 — SQL History I can trust (P1)

11. **Full preview.** Run the long Northwind query in the gap plan (HIS-01). SQL History
    (Ctrl+Alt+H) → select it: the preview reaches `ORDER BY`.
12. **Open state.** Keep a query tab open after running it:
    1. Its row shows the open bar.
    2. The *open* filter lists it.
    3. Close the tab: within 1 s the bar goes, and the *closed* filter lists it.
    4. `open:true` in search matches the *open* filter.
13. **Scroll to the end.** With more than 100 entries, clear filters and scroll to the bottom.
    The last row is your oldest query, and the list count equals "N entries found".
14. **Grouped delete.** In one tab run `SELECT 1;`, then `SELECT 2;`, then `SELECT 3;` → one
    row, "×3". Delete it: a confirmation appears → Yes → Refresh. The row stays gone.
15. **Grouped star.** Star a row, run its tab again, then un-star it: the star turns off, and
    stays off after Refresh.
16. **Search after snapshot.**
    1. Run `SELECT 'alpha' AS x;` in a new tab.
    2. Change the text to `SELECT 'omega' AS x;` without running it.
    3. Switch tabs and back.
    4. Search `omega` → found. Search `alpha` → that row is not found.
17. **History settings.** Options › Queries › History:
    - no *Encrypt at rest* and no *Record failed executions*;
    - the grouping setting reads *Group repeated runs of the same query*;
    - Enable / Retention / Max entries show *Takes effect after SSMS restarts*;
    - *Maximum query size* and the restore settings are here.

## US3 — Style editor I can read and trust (P1)

18. **Labels.** Format Styles at its default size: every page (all 14) shows full labels, with
    no cut words. Clicking the words *Align aliases* toggles the checkbox.
19. **Tab-true preview.** Select **Khamis Style**, then the Whitespace and Lists pages: preview
    lines line up. Compare with the same SQL formatted in an editor set to tab size 2.
20. **Import.** Import a SQL Prompt `.json` style with a new name:
    - it becomes active;
    - the ACTIVE pill, the header "Active:" chip and "Set as active style" update at once.

    If the current style had unsaved edits, you are asked **before** the import.
21. **Export with unsaved edits.** Change one option on a style of yours and export it:
    *"Save changes to 'X' before exporting?"* appears. Choose Yes → the file contains the
    change.

## US4 — Find, compare and switch styles (P2)

22. **Option search.**
    1. Ctrl+F in Format Styles, type `comma`.
    2. The tree keeps only pages with matches, with counts, and the matching rows are
       highlighted.
    3. Enter jumps to the first match. Esc clears the search; a second Esc closes the window.
23. **Change markers.**
    1. Change *Place commas before items*: its label turns bold, a ↺ appears, and the Lists
       leaf's count goes up by one.
    2. The preview lines that moved flash.
    3. Press ↺ → the value returns to SQL Prompt's default, and the count drops back.
24. **Coloured preview.** In the Light theme, the preview is light with coloured keywords,
    strings and comments, and line numbers.
25. **Active Style menu.**
    1. AKML SQL › Active Style ▸ lists every style, with ✔ on the active one.
    2. Pick another style and run Format Document: the new style is used.
    3. The status bar shows the new name (if *Show active style in status bar* is on). With the
       switch off, it never shows the style.
26. **Editor context menu.** Right-click in a query editor. Active Style ▸ and Format Document
    are present. If SSMS exposes no context command bar, a log line says so and this scenario
    is waived.
27. **List actions.**
    - ⋮ › Copy asks for a name, pre-filled *X copy*.
    - Typing an existing name shows the error at once, and OK is disabled.
    - F2 renames; Delete on a built-in is refused with a message; Ctrl+S saves.
28. **Format feedback.** Format Document → the status bar shows *Formatted with 'X'* for
    about 4 s. Delete the active style's file outside SSMS, then run Format Selection → the
    fallback warning appears.

## US5 — SQL History like SQL Prompt's (P2)

29. **Search as you type.**
    - Typing filters after a short pause, and the first row is selected and previewed.
    - Searching a tab name without `name:` finds it.
    - The **?** button lists the syntax.
30. **Advanced search.**
    1. Choose *Last week* + a server + *Northwind* + *Starred*: only matching rows, with chips
       under the box.
    2. Reset clears everything.
    3. With *Remember advanced search settings* on, the choices survive an SSMS restart.
31. **Rows.**
    - A row whose server matches a colour rule shows `server · database` and the environment
      name in its colour.
    - The groups read Today / Yesterday / This week / Last week / This month / Older, with
      counts.
32. **Versions.**
    1. On a query with several versions, select an older one and press Open: the new tab holds
       that version.
    2. *Compare with current* highlights the changed lines, and both sides are labelled.
33. **Keyboard only.** Unplug the mouse:
    - search, move through rows, Enter to open, Space to star, F2 to rename, Delete (with
      confirmation);
    - Tab into the preview, Ctrl+A, Ctrl+C.
34. **Row menu.**
    - With two rows selected, ⋯ › Remove on a third row removes only that row.
    - Export and Clear history are under the toolbar ⋯.
    - The *Remove older* date is readable.
35. **Live refresh.** Keep History open, scrolled half-way with a row selected, then run a
    query → it appears within 1 s, and the scroll position and selection are kept.
36. **Disconnected.** Stop the AKML engine process: History shows the unavailable message with
    Retry. Restart SSMS's engine (or wait): it refreshes by itself.
37. **Restore.**
    1. Open two query tabs: one run, one never run and unsaved.
    2. Close SSMS without saving and restart. Depending on *Restore open queries when SSMS
       starts*:
       - **Always:** both reopen, reconnected if that setting is on;
       - **Prompt:** a themed list offers them.
    3. Close the never-run tab, restart, and find it under the *closed* filter, marked *Not
       executed*.

## US6 — Options arranged like SQL Prompt (P3)

38. **Tree.** The Options tree matches `contracts/ui.md` §1 exactly, in sentence case. The
    breadcrumb equals the tree path. The chat panel's "open AI options" link still lands on AI
    assistance.
39. **Child options.**
    - Switch off *Enable IntelliSense*: every other Behavior row is indented and greyed, with a
      tooltip naming the parent. Switch it on: they come back.
    - Repeat for History, Snippets, Code analysis, Special characters and Color.
40. **Numbers.** History › Max entries: type `250000` → it is kept after OK and reopen. Type
    `abc` → red border, and the old value is kept.
41. **Palette options.**
    - Ctrl+Shift+P, type `nullab` → *Suggestions › Behavior › Show nullability info* with its
      state. Enter toggles it, and the palette stays open.
    - Type `retention` → Enter opens Options on that row, flashing.
42. **Tab colours.**
    - Queries › Color shows a grid. Add a rule `*PROD*` → Production without typing a colour
      code. Reorder rules.
    - *Edit environments…* uses a colour picker.
    - A Production tab turns red, and the Safety prompts still recognise production.
43. **Dark theme.** Switch to Dark and open every window reachable from Options (Manage
    Code Analysis Rules, rule editor, credentials): no light panels or light-grey buttons.

## US7 — Polish and sharing (P3)

44. **Menu.** The AKML SQL menu matches `contracts/ui.md` §2: 12 top-level entries, About
    last under Help ▸. The keyboard shortcuts still work.
45. **Titles.** Open Options, Format Styles, Snippet Manager and Code Analysis Rules: every
    title reads `AKML SQL – …`.
46. **F1.** F1 on Options › Queries › History, in Format Styles, and in SQL History opens the
    matching page on `https://akml.khamis.work/docs/…` (the page exists).
47. **Screen reader.** With Narrator on the SQL History toolbar, every icon button is
    announced by name.
48. **Team styles.**
    1. Options › Format › Styles › *Team style folder* → a shared folder containing a `.json`
       style. It appears under TEAM STYLES.
    2. Its options are read-only when the folder is read-only; Copy works.
    3. Point it at an unreachable path → *Team styles unavailable*, and your own styles still
       work.
49. **Format SQL actions.**
    - Options › Format › Styles › *When you run Format SQL, AKML SQL will:* shows only these
      actions: apply layout, apply casing, semicolons, square brackets, expand wildcards,
      qualify object names.
    - Tick *Insert semicolons*, then Format Document → semicolons are added. Untick it →
      they're not.
    - With *Expand wildcards* on, `SELECT * FROM dbo.Products` expands.

## Done when

- Every scenario above passes, or is explicitly waived with a reason recorded in `tasks.md`
  and `doc/progress.md` (only scenario 26 may be waived).
- Automated suites are green, apart from the excused pre-existing failures.
- `doc/progress.md` has a "Spec 040" section.
- CLAUDE.md's "Latest merged work" and "Open follow-ups" are updated, and the stale
  `ThemeManager` guidance is corrected.
