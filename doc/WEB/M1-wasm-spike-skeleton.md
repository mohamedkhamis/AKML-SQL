# M1 — ScriptDom-in-WASM Spike & Blazor Project Skeleton

**Status**: Draft
**Phase**: M1 (decision gate)
**Estimated effort**: 1 week
**Branch prefix**: `m1-wasm-spike`
**Depends on**: M0 merged

---

## 1. Executive summary

Before committing to the "thick browser, thin server" architecture for M2 onwards, this milestone proves two things:

1. **`Microsoft.SqlServer.TransactSql.ScriptDom` can run inside a Blazor WebAssembly runtime.** ScriptDom is the foundation of parsing, formatting, and analysis. If it cannot be loaded under the `browser-wasm` runtime identifier — for example because it has native dependencies, blocked APIs, or trim-incompatible reflection — the architecture must pivot: parsing moves to the local agent, and the in-process WASM path becomes a thin UI shell.
2. **`AkmlSql.Core` and `AkmlSql.Formatting` can be referenced from a Blazor WASM project without rewrites.** Both are already `netstandard2.0`; the WASM runtime is supposed to consume them transparently. We need to verify this end-to-end before M2 starts.

The deliverable is a minimal Blazor WASM project (`AkmlSql.Web`) that loads a `.sql` file, parses it with `TSql170Parser`, runs the formatter pipeline, and renders the result. **No UI polish, no theme, no editor — just a textarea, a button, and an output panel.** If this works, M2 builds on top of it. If it does not, the spike output is a written decision document and a revised architecture for M2.

---

## 2. Why now

M0 lands a clean handler API. M2 needs to commit to a UI architecture. The single highest-risk technical assumption in the entire web-edition plan is "ScriptDom works in WASM." Deferring that test until M2 is well underway means weeks of wasted UI work if it fails. One week now, with an explicit decision gate, protects the whole rest of the plan.

---

## 3. Current state

Nothing exists. The solution has no Blazor project. `AkmlSql.Core` and `AkmlSql.Formatting` target `netstandard2.0` (+ `net10.0` for Core's updater path). ScriptDom is referenced via the `Microsoft.SqlServer.TransactSql.ScriptDom` NuGet package.

The package historically had Windows-only / .NET Framework variants. Recent versions support `.NET Standard 2.0` and `.NET 6+`. The unknown is specifically the `browser-wasm` RID under .NET 8/9 — whether the package's assembly trims clean, whether any P/Invoke into native bits is reachable, whether the parser's reflection survives trimming.

---

## 4. Proposed work

### 4.1 Project layout (additions only)

```
src/
  AkmlSql.Web/                   ← NEW; Blazor WASM standalone (.NET 8 or 9)
    Pages/
      Index.razor                ← textarea + button + output panel
    Program.cs
    AkmlSql.Web.csproj
```

`AkmlSql.Web.csproj` references `AkmlSql.Core` and `AkmlSql.Formatting`. No new shared library yet — that comes in M2 once the API surface is known.

### 4.2 Spike script

The Index.razor page does exactly four things:

1. Load a `.sql` file via `<InputFile>` (Blazor's built-in component).
2. On button click, call `new TSql170Parser(...).Parse(...)`.
3. If parse succeeded, run `FormatterPipeline.Format(ast, defaultProfile)`.
4. Render the formatted SQL in a `<pre>` block. If anything threw, render the exception text.

That's it. No styling, no theme, no editor component, no IPC.

### 4.3 Investigation matrix

The spike must answer these questions in writing, with evidence:

| Question | How we'll know | Pass condition |
|----------|---------------|----------------|
| Does `Microsoft.SqlServer.TransactSql.ScriptDom` load in `browser-wasm`? | Run; observe no `BadImageFormatException` or `TypeLoadException` | Parse succeeds on a 10-line `SELECT` |
| Does the formatter pipeline run end-to-end? | Run; observe output panel | Output matches what the engine produces for the same input |
| What is the WASM bundle size with ScriptDom included? | `dotnet publish -c Release` and measure `_framework/` total | ≤ 25 MB compressed (negotiable; record actual) |
| What is cold-load time on a representative dev machine? | DevTools timeline | ≤ 8 seconds on first load (negotiable; record actual) |
| Does AOT compilation improve performance enough to justify the build time? | Publish twice, with/without AOT; measure parse time | Decide based on measured numbers |
| Do trim warnings exist? | Build log | Document them; suppress only with evidence they're safe |
| Are there missing-API runtime errors? | Console errors during parse/format | None for the SELECT case; document any for richer SQL |

### 4.4 The decision gate

At end of week 1, write a short decision document (`docs/m1-wasm-decision.md`) recording:

- Pass / fail on each question above
- Bundle size + cold-load numbers
- A go/no-go recommendation for M2's architecture
- If no-go: the proposed pivot (parsing on local agent only; browser becomes thin)

---

## 5. Three possible outcomes

### Outcome A — clean pass

ScriptDom + formatter run in WASM, bundle size acceptable, cold load acceptable. **M2 proceeds as planned** with formatter + analyser entirely in-browser.

### Outcome B — works but heavy

Runs, but bundle is 50+ MB or cold load is 20+ seconds. **M2 still proceeds**, but with extra work to lazy-load ScriptDom on first use rather than at app start.

### Outcome C — does not work

ScriptDom throws at load time, or unfixable trim issues, or a P/Invoke into native bits. **M2 architecture pivots**: browser-side becomes a thin UI calling the local agent for everything. WebSocket transport (M3) moves earlier in the plan and becomes a hard dependency for M2.

The spike is sized so that all three outcomes resolve in ≤ 1 week.

---

## 6. Out of scope for M1

- Any UI work beyond the four-element spike page
- Theme system, design tokens, component library
- IndexedDB schema cache
- WebSocket transport
- IIS deployment
- AI integration

These start in M2 and beyond.

---

## 7. Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| ScriptDom doesn't run in WASM | Medium | Very high | The whole point of the spike — the risk is *not knowing*, which the spike resolves |
| Spike succeeds but only with .NET 8, not .NET 9 | Low | Low | Pick the working TFM; revisit later |
| Bundle size is acceptable but Egyptian / regional CDN latency makes load slow | Low | Medium | M4 (IIS hosting) means user serves from their own machine — latency irrelevant for local |
| Spike "kinda works" — passes basic SQL but fails on complex T-SQL | Medium | High | Test corpus must include richer SQL: stored procs, CTEs, window functions, MERGE |

---

## 8. Success metrics

- `AkmlSql.Web` project builds and publishes under `dotnet publish -c Release`
- Spike page loads in a browser (Edge/Chrome/Firefox — pick one, document others)
- A 50-line stored procedure parses and formats end-to-end with no exceptions
- Decision document `docs/m1-wasm-decision.md` written with go/no-go and evidence
- Bundle size, cold-load time, and AOT-vs-non-AOT measurements recorded

---

## 9. Open questions

1. **Which .NET version for the Blazor project?** — .NET 8 is the safe pick (LTS, mature WASM); .NET 9 may have better trim diagnostics. Decide on day 1 of the spike.
2. **AOT compile or interpreted?** — AOT gives ~2× perf but ~2× build time and ~2× bundle size. Spike measures both; M2 picks.
3. **Which browsers to support?** — Spike on Chrome/Edge; document Firefox + Safari separately. Mobile browsers are out of scope.

---

## 10. Definition of done

- [ ] `src/AkmlSql.Web/` exists and builds
- [ ] Spike page parses + formats a SELECT statement in the browser
- [ ] Spike page parses + formats a 50-line stored procedure
- [ ] Bundle size and cold-load numbers recorded
- [ ] `docs/m1-wasm-decision.md` written with go/no-go recommendation
- [ ] If go: M2 starts; if no-go: M2's PRD is revised before M2 starts
- [ ] Branch `m1-wasm-spike` merged to master via PR (even on no-go — the project skeleton stays)
