# Web Edition Autocomplete + Formatting Validation Campaign — 2026-07-16

Automated end-to-end test campaign against the AKML-SQL **web edition** (SSMS-like Blazor WASM app), focused on autocomplete, with formatting, execution, and CRUD coverage. **1,370 autocomplete cases + 100 formatting cases + ~120 keystroke/UI scenarios**, all run through the real product stack: Playwright browser → CodeMirror → Blazor WASM → WebSocket bridge → engine → live SQL Server.

## Environment

| Item | Value |
|------|-------|
| Web build | Fresh `dotnet publish` of branch `030-closure-followups` @ `dcdd667` **+ uncommitted working-tree edits** (031 formatter/profile files, `akml-editor.js` stale-doc guard), deployed to IIS `AkmlSqlWeb` (port 8083) |
| Engine build | Same source snapshot, full self-contained publish → `C:\Program Files (x86)\AKML SQL\Engine`, running as service `AkmlSqlWebEngine` (LocalSystem), bridge on `127.0.0.1:47291` |
| Engine version at runtime | `1.26.0716.1136+dcdd667…` (verified in status bar) |
| Database | `Northwind_AutoTest` — backup/restore clone of local Northwind (slim W3Schools variant), enriched with: seeded `OrderDetails` (300 rows), `Sales` schema + `Sales.Invoices`, views `vw_CustomerOrders` / `vw_ProductCatalog`, procs `usp_GetCustomerOrders(@CustomerID,@FromDate,@ToDate)` / `usp_UpdateProductPrice(@ProductID,@NewPrice)` / `Sales.usp_MarkInvoicePaid(@InvoiceID)`, functions `fn_OrderItemCount` (scalar) / `fn_OrdersByCustomer` (TVF) |
| Auth | Windows auth; `NT AUTHORITY\SYSTEM` granted `db_owner` on the sandbox (the LocalSystem engine can only list/access DBs it has grants for) |
| Harness | In-page JS driver recovering the live CodeMirror `EditorView` via the shared ESM bundle; explicit completion = `startCompletion` (Ctrl+Space semantics); popup awaited via CM's own `completionStatus` state (race-free); plus real-keyboard Playwright scenarios for trigger/acceptance/execution flows |

Method notes that matter when reading failures:

- The engine truncates completion lists at **50 items** (`CompletionEngine._maxSuggestions`), priority-sorted before truncation. Failures with `atCap` (n=50) are ambiguous — the expected item may exist below the cap. 71 of 334 failures are at-cap.
- The bulk battery used **explicit** trigger; a separate keystroke pass tested the **typing** trigger path (results differ — see findings 1–2).
- Full per-case results: `.playwright-mcp/results-completion.json`, `.playwright-mcp/results-formatting.json` (repo root; delete after triage). Corpus (22 JSON files, 1,470 cases): session scratchpad `corpus/` dir.

## Results overview

### Autocomplete battery (explicit trigger, end-to-end)

| Family | Passed | Notes |
|--------|--------|-------|
| select-columns | 117/120 | solid |
| from-tables | 95/100 | solid |
| join-on | 88/90 | solid (FK-aware ON completion works) |
| where-having | 82/90 | built-ins after operators missing |
| insert | **42/80** | column-list scoping to INSERT target broken |
| update | 69/90 | SET/WHERE zero-item cluster |
| delete | **48/70** | WHERE scoping zero-item cluster |
| exec-procs | **15/60** | proc names + @params largely absent |
| functions | 47/60 | built-ins missing in expression positions |
| cte | **40/70** | CTE column resolution fails in many shapes |
| temp-tables | 41/60 | #temp names/columns often absent |
| subqueries | **15/70** | inner-scope resolution broken (worst family) |
| multi-statement | 74/90 | isolation mostly works; `;`-then-write-again works |
| schema-qualified | 58/60 | solid |
| brackets-quoted | **25/40** | `[dbo].[Cust…` paths often return nothing |
| comments-strings | **50/50** | perfect — completion correctly suppressed |
| keywords | **29/50** | ORDER/OUTER/APPLY/BY context sets missing |
| casing-prefix | 32/40 | case-insensitive matching mostly fine |
| negative-controls | 35/40 | good |
| star-and-misc | 34/40 | at-cap leaks |
| **Total** | **1,036/1,370 (75.6%)** | 334 failures: 95 zero-item, 71 at-cap, rest scoping/membership |

### Formatting battery (in-browser FormatterPipeline — active profile turned out to be `builtin.default`, see finding J3: the web edition has no built-in Khamis Style)

**99/100 passed** (no exceptions, output non-empty, literals + comments preserved, idempotent) — one idempotency failure (finding 7).

### Execution / CRUD / UI scenarios

All core SSMS-like flows **work**: SELECT grids + row counts, UPDATE/DELETE/INSERT with `(N row(s) affected)`, multi-statement batches with result-set tabs + Messages tab, `EXEC` procedures, error surfacing (`Invalid object name`), `BEGIN TRAN…ROLLBACK`, mid-document `;` then continue-typing (completion works on the new statement), CRUD grid inline edit → Apply (DB-verified), wildcard `*` + Tab expansion to real column list, signature help on `(`, quick keyboard flows (Enter-accept, arrow navigation, Escape-close, mouse-accept).

### Runtime log analysis

Engine log (`C:\Windows\System32\config\systemprofile\AppData\Roaming\AKML SQL\logs\akmlsql-20260716.log`): **zero ERR/WRN/FTL lines across the entire ~1,500-request battery**. No crashes, no exceptions — every failure below is behavioral, not a fault. Clause detection distribution across the battery (from engine DBG lines): Select 459, From 262, Where 221, **Unknown 111**, JoinOn 108, UpdateSet 67, InsertColumns 62, JoinTable 32, OrderBy 27, GroupBy 19, UpdateTable 17, Having 14, **Exec 7**, InsertValues 7, **Delete 2**, Set 2, Declare 1, With 1. The tiny Exec/Delete counts and 111 Unknowns line up with the failure clusters.

## Confirmed findings

Severity scale: **CRITICAL** = daily-use autocomplete broken vs SSMS expectations · **HIGH** = frequent scenario broken · **MEDIUM** = noticeable gap/misleading UX · **LOW** = cosmetic or edge.

### 1. CRITICAL — Typing `.` after an alias/schema does not trigger completion (web editor)

Typing `o.` / `dbo.` / `c.` produces **no popup**; Ctrl+Space at the same caret returns perfectly scoped columns. 48 of 101 keystroke scenarios failed on exactly this. SSMS/SQL Prompt auto-trigger on dot — this is the single most common completion gesture in SQL editing.
Repro: type `SELECT o` → `.` in a doc with `FROM dbo.Orders o`. Nothing appears; Ctrl+Space shows `OrderID, CustomerID…`.
Where: `src/AkmlSql.Web/wwwroot/js/akml-editor.js` — `completionSource` gate accepts only a word-prefix match, `POST_KEYWORD_TRIGGER` (keyword+space regex), or `context.explicit`; a trailing `.` matches none of them.

### 2. HIGH — Space after DML keywords doesn't trigger (web editor)

`UPDATE ␣`, `INSERT INTO ␣`, `DELETE FROM ␣`, `EXEC ␣` do not auto-open the table/proc list; `WHERE ␣` / `FROM ␣` / `AND ␣` do (control-tested). `POST_KEYWORD_TRIGGER` regex covers `where|and|or|from|join|on|set|having|select|group by|order by|by|when|then|else` — no DML verbs.

### 3. HIGH — Tab does not accept the selected completion; it indents (web editor)

With the popup open and an item visibly selected, Tab inserts indentation (`  SELECT Cust…`). Enter, arrows, Escape and mouse-click all behave correctly (verified with real focus + DOM-visible popup + screenshot). Tab is bound by the wildcard-expansion/indent handler with higher precedence than CM6's `acceptCompletion`. SSMS/VS/SQL Prompt users hit Tab by muscle memory.

### 4. HIGH — Ctrl+Enter does not execute; no working keyboard execute exists

The `Mod-Enter → runExecute` binding (`akml-editor.js` ~570) does not fire (verified twice: grid provably unchanged after Ctrl+Enter, while the Execute button works). F5 is deliberately unbound (browser refresh). Net effect: **queries can only be executed by mouse**.

### 5. MEDIUM — Page reload silently drops the SQL connection while the pill still says "Live"

After F5/reload (even with a saved connection), the bridge auto-reconnects (`Live` — "Live IntelliSense available.") but the SQL session is gone (`Connect` button state). Completions silently degrade to keywords+snippets with no visual cue; `SessionId=""` requests hit the engine with an empty session. Either auto-restore the saved/last SQL connection on boot, or make the pill/status reflect "no SQL connection".

### 6. LOW — Saved connection restores server/name but displays the wrong database until ⟳ refresh

Selecting a saved connection shows `master` in the Database dropdown (its option list is not repopulated with the saved DB); the *bound* value is correct, so Connect actually targets the saved DB — the display is misleading. Also: the DB dropdown lists only databases the **engine's service account** can access, with no hint when others are filtered out (for the LocalSystem web service this surprised even this campaign — required a `db_owner` grant for the sandbox to appear).

### 7. MEDIUM — Formatter idempotency: JOIN line-break oscillates inside CTE bodies (FMTA-006)

Chained-CTE input: first format renders `FROM dbo.Suppliers s` + newline + `INNER JOIN   L2 l ON …` (note stray triple space); formatting the **output** collapses the JOIN back onto the FROM line (then stable). 99/100 formatting cases were idempotent; this one layout rule oscillates.
Repro input in `corpus/f21-formatting-a.json` (`FMTA-006`); observed diff at line 13 of the output.

## Root-caused engine findings (multi-agent analysis, every finding adversarially verified against source)

A 28-agent workflow root-caused all 13 failure clusters; each claim below was independently re-checked by a verifier agent that read the cited code and re-tested the mechanism against observed failures (all verdicts CONFIRMED). **Almost all engine-side bugs also affect the desktop (SSMS/VS) edition — same engine.**

### A. Cross-cutting scope-resolution bugs (explain the worst families)

| # | Sev | Bug | Where | Mechanism |
|---|-----|-----|-------|-----------|
| A1 | **CRITICAL** | Subquery/CTE-body scope is discarded: token-based alias fallback skips every token at paren depth > 0, and the parse-repair helper only appends dummy tokens at the document END (never at the caret), so any broken-at-caret doc inside `(...)` loses its own FROM tables | `TokenBasedAliasExtractor.cs:66`, `SuffixCompletionHelper.cs:8-88` | Explains most of subqueries 55/70 and CTE-body failures. Fix: cursor-scope-aware extraction (innermost paren span containing caret) + cursor-position dummy insertion |
| A2 | **CRITICAL** | Aliased DML poisons the alias map: `UPDATE o SET … FROM Orders o` / `DELETE o FROM Orders o` registers the ALIAS as a table (`dbo.o`), and first-occurrence-wins blocks the real FROM mapping → `o.` yields 0 items | `TokenBasedAliasExtractor.cs:147-157` | Explains the UPDATE/DELETE zero-item clusters (UPD-045…58, DEL-031…44, MULTI-045/046/077/079/080). Fix: two-pass extraction, FROM/JOIN wins |
| A3 | HIGH | AST alias resolution only understands `QuerySpecification` — UPDATE/DELETE statements (FROM hangs off Update/DeleteSpecification) always fall back to the buggy token path even when they parse cleanly | `AliasResolver.cs:91-92,145-160` | Fix: extend CursorScopeFinder to Update/Delete/MergeSpecification |
| A4 | HIGH | Correlated subqueries lose outer aliases (innermost-QuerySpecification-only), derived tables resolve to a `(derived:alias)` placeholder with zero columns | `AliasResolver.cs:97-124` | Fix: merge ancestor scopes (inner wins on conflict); enumerate derived-table projections like CTE bodies |
| A5 | MEDIUM | Set-operator branches leak into each other (UNION/INTERSECT/EXCEPT are not statement boundaries in the token fallback) | `TokenBasedAliasExtractor.cs:29-55` | Fix: treat depth-0 set-operator tokens as scope boundaries relative to the caret |
| A6 | MEDIUM | Three-part names unsupported by the token fallback (consumes exactly one dot): `db.dbo.Orders o` registers bogus alias `dbo→db.dbo` and drops real aliases; DotPrefix keeps only ONE identifier so the `db.` part is silently ignored (`BogusDb.dbo.` serves local objects) | `TokenBasedAliasExtractor.cs:83-104`, `CursorContextAnalyzer.cs:176-201` | Fix: multi-part identifier chain consumption in both |

### B. Clause-detection dead code (dedicated token types never handled)

`CursorContextAnalyzer.DetermineClauseType` walks tokens backward but several ScriptDom **dedicated token types** match no switch case (the Identifier-text arms meant to catch them are dead code), so the walk falls through to FROM/Unknown:

| # | Sev | Bug | Evidence |
|---|-----|-----|----------|
| B1 | **HIGH** | `EXEC` tokenizes as `TSqlTokenType.Exec` but only `TSqlTokenType.Execute` is handled → clause=Exec fired just 7× in the whole battery; proc-name completion after `EXEC ` mostly dead. **One-line fix** (`case TSqlTokenType.Exec:` at `CursorContextAnalyzer.cs:336`) | exec-procs 45/60 failed |
| B2 | HIGH | `GROUP`/`ORDER` (dedicated `TSqlTokenType.Group/Order`) unhandled → `ORDER \|` misdetected as From: `BY` never offered, tables+HAVING wrongly offered | KW-023 |
| B3 | HIGH | No join-qualifier context: after `LEFT/INNER/CROSS \|` the engine offers tables+ON instead of `JOIN`/`OUTER`/`APPLY` | KW-026…030 |
| B4 | MEDIUM | `UNION/INTERSECT/EXCEPT` dedicated tokens unhandled → after `UNION \|` neither SELECT nor ALL offered (+ branch table leaks, A5) | KW cluster |
| B5 | MEDIUM | `ClauseType.Delete` has no keyword mapping → `DELETE \|` offers SET/DECLARE (GeneralKeywords), never FROM | Delete clause fired 2× in battery |
| B6 | MEDIUM | CASE expression states untracked → THEN/ELSE never offered inside CASE | KW cluster |
| B7 | MEDIUM | `UPDATE TOP (5) dbo.Orders SET \|` misclassified as SET-options (ANSI_NULLS list) — the `)` breaks the SET↔UPDATE back-scan; also `UPDATE TOP (10) \|` offers no tables (`)` mistaken for completed target) | `CursorContextAnalyzer.cs:320-335`, `ObjectProvider.cs:156-165` |

### C. INSERT / EXEC / variables — missing providers & wiring

| # | Sev | Bug | Where |
|---|-----|-----|-------|
| C1 | **HIGH** | INSERT target table is never injected into scope → `INSERT INTO Customers (\|` offers a generic object list instead of Customers columns (38/80 family failures). The ALTER TABLE path already does exactly this injection — INSERT was never given it | `CursorContextAnalyzer.cs:424` (contrast `:498-501`) |
| C2 | HIGH | `INSERT INTO \|` (table position) and `INSERT INTO t (\|` (column position) are the same ClauseType → procs/functions offered as INSERT targets; `INTO` missing from the AfterInsert keyword set | `ObjectProvider.cs:497`, `KeywordDictionary.cs:707-711` |
| C3 | **HIGH** | No stored-proc **parameter** provider exists at all — Phase B loads parameters into the cache and SignatureProvider reads them, but nothing emits `@param` completion items | `CompletionEngine.cs:119-127` |
| C4 | MEDIUM | `@`-prefixed carets have empty PartialText (`TSqlTokenType.Variable` excluded from extraction) and `VariableTracker` is dead code (zero callers) → declared `@vars` never complete | `CursorContextAnalyzer.cs:206-211`, `VariableTracker.cs:17` |
| C5 | MEDIUM | Web-only (latent): CM6 replace-span regex `/[\w]+/` excludes `@`/`#` → once param completion exists, accepting `@CustomerID` over `@C` would produce `@@CustomerID` | `akml-editor.js:140` |

### D. Built-in functions never surfaced

The ~130-entry `KeywordDictionary.ScalarFunctions` catalog (GETDATE, DATEADD, ISNULL, …) is referenced **only** by `GetAllKeywords` — no provider ever emits it per-clause. Expression positions (`WHERE OrderDate >= |`, `SET Price = |`, VALUES slots) offer no built-ins at all; `InsertValues` has no keyword mapping (falls to GeneralKeywords). JOIN ON positions additionally exclude scalar UDFs (`fn_OrderItemCount`) from schema-qualified completion. — `KeywordDictionary.cs:156-223,559,713`, `KeywordProvider.cs:73`, `ObjectProvider.cs:491-499`. Explains the functions/where-having failures.

### E. CTE resolution (six confirmed root causes)

1. Alias over a CTE never resolves to CTE columns (`FROM cte x` … `x.|` — alias maps to `dbo.cte`, CTE branch only matches the raw CTE name) — `ColumnProvider.cs:383,175`.
2. Caret inside CTE body/subquery loses its own scope (= A1) — `TokenBasedAliasExtractor.cs:66`.
3. CTEs leak across `;` statement boundaries (batch-scoped, not statement-scoped; AliasResolver got per-statement scoping, CteResolver never did) — `CteResolver.cs:113-139`.
4. CTE with `SELECT *` body exposes zero columns even though its source tables are tracked in `AvailableCteSources` (never used as fallback) — `CteResolver.cs:218`, `ColumnProvider.cs:383-397`.
5. Recursive CTE self-reference invisible inside its own body (blanket "can't reference itself" exclusion) — `CteResolver.cs:128-136`.
6. Caret inside a *later* CTE body: prefix-parse dies on unbalanced parens; token fallback discards explicit column lists (`WITH x (OID, CID)`) — `TokenBasedCteExtractor.cs:60-68`.

### F. Temp tables

1. Temp-table **names** are never suggested anywhere — `AvailableTempTables` is consumed only by ColumnProvider; ObjectProvider has a CTE-names branch but no temp-table branch — `ObjectProvider.cs:172-187`.
2. TempTableTracker drops ALL definitions whenever the statement being typed doesn't parse (batch-containment gate uses the shrunken parsed extent) — `TempTableTracker.cs:28-31`.
3. `SELECT * INTO #t` records an empty column list (star never expanded despite cache access downstream) — `TempTableTracker.cs:135-137`.
4. Aliased DML over temp tables → 0 items (= A2).

### G. Bracketed/quoted identifiers

1. An unterminated `[` or `"` at the caret fuses the rest of the statement into one token, destroying the stream (zero/garbage completions); no cursor-local neutralization — `CompletionEngine.cs:155`.
2. PartialText keeps the opening delimiter (`[Cust`) which FuzzyMatcher can never match → filters everything to zero. **One-line fix** (`TrimStart('[', '"')`) — `CursorContextAnalyzer.cs:210`.
3. `"dbo"."|` loses dot-scoping — double-quoted names tokenize as `AsciiStringOrQuotedIdentifier`, which the DotPrefix extraction doesn't accept — `CursorContextAnalyzer.cs:183,196`.
4. `JOIN [Sales].[|` — JoinProvider ignores the typed schema qualifier and suggests FK joins from other schemas that would insert broken SQL — `JoinProvider.cs:40-57`.

### H. Ranking/filter fidelity

- Fuzzy filter scores the full `Table.Column` display label, so table-name matches flood ORDER BY/GROUP BY suggestions with unrelated columns; fix = dedicated `FilterText` — `CompletionEngine.cs:381`, `ColumnProvider.cs:287-303`.
- IDENTITY/computed columns offered as UPDATE SET assignment targets — `ColumnProvider.cs:243-304`.
- `CROSS APPLY fn_|` returns zero (APPLY tokenizes as Identifier and trips the "after table target" suppression) — `ObjectProvider.cs:156-165`.
- Parse-repair helper matches `EndsWith("OR")` on identifier tails (`…dbo.Or` treated as the OR operator) — `SuffixCompletionHelper.cs:48-51`.

### I. Web editor (browser-side; found by keystroke pass, root-caused to the line)

1. Dot-trigger missing: the `completionSource` gate (`akml-editor.js:152`) has no member-access arm (CM6 *does* invoke the source after `.`; the gate rejects it). Fix: add a `DOT_MEMBER_TRIGGER` regex arm.
2. `POST_KEYWORD_TRIGGER` (line 92) lacks `update|insert|into|delete|exec(ute)?`.
3. Tab-accept: CM6's completion keymap deliberately ships **no Tab binding**; Tab falls through to `indentWithTab` (line 584). Fix: insert `{ key: 'Tab', run: acceptCompletion }` before `indentWithTab`.
4. Ctrl+Enter dead: `defaultKeymap`'s own `Mod-Enter → insertBlankLine` (spread at line 581) shadows the later-registered `navKeymap` `Mod-Enter → runExecute` (line 570). Fix: bind runExecute ahead of the defaultKeymap spread.

### J. Formatter (FMTA-006 + spec-031 gap)

1. **Non-idempotent JOIN layout inside parenthesized bodies**: `ClauseTracker.Update` returns early inside parens, so `IsJoinModifier` never matches there — pass 1 breaks bare `JOIN` (written as `INNER JOIN` by the explicit-join rewrite) onto its own line, pass 2 sees the modifier-prefixed form and collapses it. Oscillation between passes — `LineBreakDecider.cs:84-103,195-210`, `LayoutEngine.cs:384-385`.
2. Stage-7 IdempotencyCheck is detect-only: it *found* the divergence, appended a Warning diagnostic — and the web editor silently drops diagnostics, shipping the divergent first pass. Fix: return the converged second pass + surface the warning.
3. **Spec-031 gap**: the web edition has no built-in Khamis Style at all — web `ProfileStore` synthesizes only `builtin.default` + `builtin.ansi` (active default `builtin.default`), so the campaign formatted with POCO defaults, not the intended product default — `IProfileStore.cs:48,125-166`.

## Corpus corrections (excluded from bug counts)

The verifiers reclassified **24 failing cases as corpus mistakes** — the engine behavior is deliberate:

- **Fuzzy matching by design** (5-level matcher, `FuzzyMatcher.cs`): non-contiguous subsequence matches are intentional; ranking still puts exact prefixes first. Excluded: SELCOL-050/113, WHERE-013/018/061/066, JOINON-029, CASE-005/008/009/012, TEMP-029, DEL-066, KW-007, EXEC-010/011/015/037.
- **Compound keyword items by design**: the engine offers `ORDER BY`/`GROUP BY`/`INNER JOIN` as single items (never bare ORDER/GROUP/INNER), and `IS ` → `NOT NULL`/`NULL`. Excluded: KW-020/022/024/025/040/042.

Adjusted totals: **~310 genuine failing cases** collapsing into the ~40 distinct root causes above; adjusted pass rate ≈ 77%.

## Suggested fix priority

1. `TSqlTokenType.Exec` one-liner (B1) + PartialText bracket-trim one-liner (G2) — biggest wins per line of code.
2. Web editor trigger/keys (I1–I4) — restores SSMS-parity feel in the web editor immediately.
3. TokenBasedAliasExtractor rewrite (A1/A2/A5/A6 + F4 share one file) + AliasResolver DML/correlation support (A3/A4) — unlocks subqueries, UPDATE/DELETE, CTE bodies at once.
4. INSERT target injection (C1/C2) and proc-param provider (C3/C4).
5. Built-in function surfacing (D), keyword context sets (B2–B6), CTE fixes (E), temp names (F1–F3).
6. Formatter idempotency + web Khamis built-in (J).

## Cleanup performed / owed

- `Northwind_AutoTest` — **kept** until findings are triaged (drop with `DROP DATABASE Northwind_AutoTest;` + remove the `NT AUTHORITY\SYSTEM` grant note).
- `C:\Program Files (x86)\AKML SQL\Web\test-corpus\` — static corpus copy served for the harness — **remove after triage**.
- `.playwright-mcp/results-*.json` + screenshots — raw results — remove after triage.
- Pre-deploy backups of the previous Web/Engine deployment: session scratchpad `deploy-backup/`.
- Shippers phone + Products price restored to original values after CRUD tests; OrderDetails lost 1 row (DELETE test) + gained/lost the temp Shipper row (INSERT/DELETE pair).
