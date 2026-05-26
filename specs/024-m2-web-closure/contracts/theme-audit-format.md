# Contract: `M2-THEME-PARITY-AUDIT.md` document schema

**Owner**: User Story 1 (FR-001–FR-005)
**Location**: `specs/021-web-edition/M2-THEME-PARITY-AUDIT.md` (replaces the existing placeholder).

The audit is a single markdown file with seven sections in this order. A second reviewer must be able to verify completeness from this contract alone, without reading the source spec.

---

## Section order (mandatory)

```markdown
# M2 — Theme Parity Audit

- Date: <YYYY-MM-DD>
- Capturer: <maintainer name>
- Master commit: <full SHA>
- IDE plugin build version: <e.g. 1.26.0525.1538>
- Web edition build version: <e.g. 1.26.0525.1538>

## 1 — Host environment

| Item | Value |
|------|-------|
| OS | Windows 11 Pro 10.0.<build> |
| DPI scaling | 100% / 125% / 150% / 200% |
| Font smoothing | ClearType on / off |
| Monitor | <model + native resolution> |

## 2 — Theme matrix

| Theme | WPF screenshot | Web screenshot |
|-------|----------------|----------------|
| Light | ![light-wpf](screenshots/light-wpf.png) | ![light-web](screenshots/light-web.png) |
| Dark | ![dark-wpf](screenshots/dark-wpf.png) | ![dark-web](screenshots/dark-web.png) |
| HighContrast | ![hc-wpf](screenshots/high-contrast-wpf.png) | ![hc-web](screenshots/high-contrast-web.png) |

## 3 — Deltas

| # | Theme | Surface element | IDE rendering | Web rendering | Disposition |
|---|-------|-----------------|---------------|---------------|-------------|
| 1 | Dark | Editor gutter background | `#1e1e1e` | `#1f1f1f` | Closed (top-5) |
| 2 | Light | Problems list severity dot | 8 px filled circle | 6 px outline | Closed (top-5) |
| ... | ... | ... | ... | ... | ... |

If no deltas observed: `**No visible deltas. Audit passes on first review.**` (and no §4/§5 follow).

## 4 — Closed deltas

For each "Closed (top-5)" row in §3, one subsection:

### Delta #1 — Dark editor gutter

- File: `src/AkmlSql.Web/wwwroot/css/themes/dark.css`
- Before:

  ```css
  .akml-editor-gutter { background: #1f1f1f; }
  ```

- After:

  ```css
  .akml-editor-gutter { background: #1e1e1e; }
  ```

## 5 — Filed follow-ups

For each delta beyond the top-5:

- **Delta #6 — Light theme tab close button hover** — Filed as `024-followup-tab-close-hover` for a future polish pass. Rationale: low user impact (hover-only), out-of-scope for the M2 quality bar.

## 6 — Procedure (reproducible)

Step-by-step the next reviewer follows to re-capture:

1. Boot the workstation; confirm OS theme is `Light`.
2. Launch SSMS 22 with the AKML SQL extension installed; open `tests/format-parity/corpus/03-stored-proc.sql`.
3. Launch `dotnet run --project src/AkmlSql.Web -c Release`; open `http://localhost:5XXX/` in Chromium; paste the same file content.
4. Side-by-side: Win+Left for SSMS, Win+Right for Chromium; capture editor region only (exclude title bar).
5. Save as `screenshots/light-wpf.png` and `screenshots/light-web.png`.
6. Repeat for Dark and HighContrast (Settings → Personalization → Colors).

## 7 — Verdict

`AUDIT PASSES` — every delta in §3 has a disposition; the top-5 are closed in §4; the rest are filed in §5.
```

---

## Validation checklist (what a reviewer asserts)

- [ ] All six sections present in order
- [ ] §2 theme matrix has all six screenshot links resolvable (no broken images)
- [ ] §3 deltas table has at least one row OR the "no visible deltas" note
- [ ] Every §3 disposition is one of: `Closed (top-5)`, `Accepted with reason: <text>`, `Filed as follow-up: <name>`
- [ ] §4 has one subsection per `Closed (top-5)` row, with the actual `before`/`after` CSS snippet
- [ ] §5 has one bullet per `Filed as follow-up` row
- [ ] §6 procedure is concrete enough that a second person could reproduce it
- [ ] §7 verdict is `AUDIT PASSES` only when every other section is complete
