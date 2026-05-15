# M2 — Blazor WASM Standalone: Formatter & Analyser MVP

**Status**: Draft
**Phase**: M2 (first usable web surface)
**Estimated effort**: 3–4 weeks
**Branch prefix**: `m2-web-formatter-analyser`
**Depends on**: M0 merged, M1 decision = go

---

## 1. Executive summary

M2 ships the first version of the web edition that an actual user can use. Scope is intentionally narrow: a browser-based SQL editor where the user can paste or open a `.sql` file, format it, and run static analysis. No live schema, no IntelliSense beyond what works without a database connection, no AI. The point is to prove the architecture end-to-end with the simplest features that depend only on `AkmlSql.Core` + `AkmlSql.Formatting` + `AkmlSql.Analyzer` — all of which already run in `netstandard2.0` and (per M1) work in WASM.

Three things land in M2:

1. A real Blazor WASM application with a real editor component (Monaco or CodeMirror), routing, and a theme system aligned with the existing WPF theme tokens.
2. The formatter pipeline running in-browser with full profile support — including loading `.akmlstyle` and `.sqlpromptstylev2` files from the user's machine via `<InputFile>` and saving back via download.
3. The 130+ analysis rules running in-browser with results rendered as a problems list, click-to-jump-to-line behaviour, and per-rule severity colouring.

At the end of M2, the web edition is **a self-contained, offline-capable, browser-based SQL formatter and linter** — already useful on its own, even before live schema and IntelliSense arrive in M3/M5.

---

## 2. Why now

M1 has just answered the viability question. M2 is the smallest scope that produces a shippable web surface without depending on M3 (WebSocket transport) or M4 (IIS deployment). Crucially, M2 has no dependency on a running engine process — the user can open the WASM bundle from any static host (including `file://` if needed) and it works. This lets us validate the architecture and gather feedback before adding the live-schema complexity.

---

## 3. Current state

End of M1: a four-element spike page that proves the technology works. No real UI, no theme, no editor, no analysis, no profiles.

---

## 4. Proposed architecture

### 4.1 Project additions

```
src/
  AkmlSql.Web/                    ← Blazor WASM standalone (from M1, expanded)
    Pages/
      Editor.razor                ← main editor + format + analyse view
      Settings.razor              ← format profile picker + analysis settings
      About.razor                 ← version, license, links
    Shared/
      MainLayout.razor
      NavMenu.razor
      EditorComponent.razor       ← Monaco or CodeMirror wrapper
      ProblemsListComponent.razor ← analysis results
      ProfilePickerComponent.razor
    Services/
      FormatterService.cs         ← thin wrapper around FormatterPipeline
      AnalyserService.cs          ← thin wrapper around AnalysisEngine + RuleRegistry
      ProfileStore.cs             ← in-memory + file-import/export
      ThemeService.cs             ← reads OS preference + user override
    wwwroot/
      js/
        editor-interop.js         ← Monaco/CodeMirror JS-interop shim
      css/
        themes/                   ← light, dark, high-contrast CSS variable sets
    Program.cs
    AkmlSql.Web.csproj
  AkmlSql.Web.Shared/             ← NEW; lib for code that will be reused across
                                    standalone WASM and the future SaaS surface
    Models/                       ← Blazor-side DTOs (mirror engine MessagePack types)
    Theme/                        ← theme token mapping ↔ CSS variables
    LocalStorage/                 ← profile/history persistence abstractions
```

### 4.2 Why an editor component is the central decision

A textarea will not do for M2. SQL needs syntax highlighting, line numbers, bracket matching, and click-to-jump-from-problems-list. Two viable options:

| Editor | Pros | Cons |
|--------|------|------|
| **Monaco Editor** (VS Code's editor) | Same engine SSMS/VS users know; rich API; mature TypeScript types | Larger (~2 MB); JS interop only; SQL grammar is OK but not amazing |
| **CodeMirror 6** | Smaller (~500 KB); modern; good SQL grammar via `@codemirror/lang-sql` | Different API style; less familiar to SQL Server users |

Decision deferred to milestone M2.1 with a one-day comparison spike. The architecture treats the editor as a swappable component behind `EditorComponent.razor`; either choice fits.

### 4.3 Theme system alignment

The WPF theme system (spec 016) exposes `ThemeTokens` / `ThemeRegistry` / `HostThemeWatcher` with 25+ brush tokens across Surface / Text / Border / Accent / Status / Editor / Chat / IconBadge / TabColor / History families. The web edition mirrors this with **CSS custom properties** sharing the same token names:

```
WPF                          Web (CSS variable)
ThemeTokens.SurfaceBase  →   --akml-surface-base
ThemeTokens.AccentBrush  →   --akml-accent
ThemeTokens.TextPrimary  →   --akml-text-primary
... etc
```

A single source-of-truth JSON file (`docs/theme-tokens.json`, added in M2) defines all tokens for Light / Dark / HighContrast, and is consumed by both the WPF `ThemeRegistry` and a CSS generator script that emits `themes/light.css`, `themes/dark.css`, `themes/high-contrast.css`. This keeps the two surfaces aligned without manual sync.

### 4.4 Profile system in the browser

The WPF surface stores profiles in `%AppData%/AKML SQL/profiles/*.akmlstyle`. The browser cannot read `%AppData%`. Two paths:

| Profile source | Mechanism |
|----------------|-----------|
| Built-in profiles | Embedded in the WASM bundle as JSON resources |
| User profiles | `<InputFile>` to import; `Blob` + download link to export; `IndexedDB` for persistent storage between sessions |
| SQL Prompt `.sqlpromptstylev2` | Same XML round-trip path used in the WPF surface; runs in WASM as it's pure C# |

User profiles persist in IndexedDB so the user does not have to re-import on every visit. A "Reset to built-in" button clears them.

---

## 5. Feature scope

| Feature | In M2 |
|---------|-------|
| Open `.sql` file via `<InputFile>` | Yes |
| Paste SQL into editor | Yes |
| Syntax highlighting (Monaco or CodeMirror) | Yes |
| Format command (Ctrl+K, Ctrl+F) | Yes |
| Profile picker | Yes |
| Import `.akmlstyle` | Yes |
| Import `.sqlpromptstylev2` | Yes |
| Export current profile | Yes |
| Run analysis | Yes (manual button + on-format) |
| Problems list with severity icons | Yes |
| Click problem → jump to line | Yes |
| Per-rule severity override (in-session) | Yes |
| Per-rule severity override (persisted) | Yes (IndexedDB) |
| Inline suppression hints | Yes |
| Theme: Light / Dark / High-contrast | Yes |
| Theme follows OS preference | Yes |
| Multi-file tabs | **No** — deferred |
| Live IntelliSense from a DB | **No** — M3 |
| Snippets | **No** — M5 |
| Refactoring | **No** — M5 |
| AI | **No** — M6 |
| Git integration | **No** — out of scope |

---

## 6. Milestones

### M2.1 — Editor choice + skeleton (week 1, days 1–3)

One-day comparison spike between Monaco and CodeMirror. Pick one. Build `EditorComponent.razor` as a swappable wrapper. Layout: top nav, main editor, side problems panel, footer status bar.

### M2.2 — Theme system + token JSON source (week 1, days 4–5)

Add `docs/theme-tokens.json`. Write a small CSS generator (Node or .NET) that emits `themes/*.css`. Verify the WPF surface and the web surface match visually on a side-by-side comparison screenshot.

### M2.3 — Formatter integration (week 2)

Wire `FormatterService` to call `FormatterPipeline` directly (`InProcessTransport` from M0). Profile picker dropdown. Built-in profiles embedded. Import/export of `.akmlstyle` and `.sqlpromptstylev2`. Format-on-save (Ctrl+S) and Format-document (Ctrl+K Ctrl+F) keybindings.

### M2.4 — Analysis integration (week 3)

Wire `AnalyserService` to call `AnalysisEngine` + `RuleRegistry`. Problems panel: filterable by severity, sortable by line, click-to-jump. Per-rule severity overrides stored in IndexedDB. Inline suppression hints (read-only display — actual suppression editing is M5).

### M2.5 — Polish + theme parity audit (week 4)

Side-by-side screenshots of WPF and web surfaces in Light/Dark/HighContrast. Document any deltas. Address top 5 visual gaps. Write quickstart doc.

---

## 7. Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Monaco/CodeMirror JS interop is slow for large files | Medium | Medium | Test with a 10 KLOC SQL file in M2.1 spike; if slow, virtualise the editor |
| Theme parity drifts during development | High | Medium | Single JSON source + auto-generated CSS; visual regression check in M2.5 |
| Bundle size grows past M1's target | Medium | Medium | M2.5 measures + decides whether to lazy-load analysis rules |
| Browser memory pressure on large files | Medium | Medium | Per-document size limit (10 MB, same as engine `MaxDocumentSizeChars`); show warning past 1 MB |
| User imports a `.sqlpromptstylev2` with an unsupported setting | Medium | Low | FR-023 affordance from spec 020 already handles this — port directly |

---

## 8. Success metrics

- A user can open the published WASM app, paste a 100-line stored procedure, format it, and see analysis results in under 5 seconds total interaction time
- Built-in profiles match the WPF surface byte-for-byte on the same input
- All 130+ analysis rules execute in WASM
- Theme tokens generated from `theme-tokens.json` match the WPF tokens on visual inspection
- Bundle size + cold load within targets set by M1 decision document
- Quickstart doc allows a new user to be productive in under 60 seconds

---

## 9. Out of scope (deferred)

- Multi-file tabs — needs design work; defer to post-M6
- Live IntelliSense — needs M3 WebSocket transport
- Snippets, refactoring — M5
- AI — M6
- Git, version control — separate planning cycle
- Multi-user state — SaaS, separate planning cycle
- Mobile-friendly responsive layout — desktop-first; mobile is a stretch goal

---

## 10. Open questions

1. **Monaco vs CodeMirror** — resolved in M2.1
2. **AOT or interpreted** — based on M1 numbers; M2.1 confirms
3. **Default profile in browser** — same as WPF default? Or a web-specific default optimised for readability in `<pre>` blocks?
4. **Persistence scope of per-rule overrides** — IndexedDB per-origin, so localhost vs LAN-IP host have separate stores. Is that the right behaviour? Probably yes — it matches "independent surfaces" decision.

---

## 11. Definition of done

- [ ] `AkmlSql.Web` is a real Blazor WASM app with editor, problems panel, profile picker
- [ ] Format and analyse commands work on a 100-line stored procedure
- [ ] Light, Dark, HighContrast themes work and follow OS preference
- [ ] Theme tokens generated from a shared JSON source
- [ ] `.akmlstyle` and `.sqlpromptstylev2` import/export work
- [ ] Per-rule severity overrides persist across sessions
- [ ] Quickstart doc published
- [ ] Visual parity audit screenshot set committed
- [ ] Branch `m2-web-formatter-analyser` merged to master via PR
