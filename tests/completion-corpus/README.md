# Completion corpus (spec 032)

The 2026-07-16 web autocomplete campaign corpus, imported verbatim (T001). 22 JSON files:

- `f01`–`f20`: **1,370 autocomplete cases** — `{ id, family, doc, expect: { mustContain, mustNotContain, minCount }, note }`. The `|` in `doc` marks the caret (removed before the engine call).
- `f21-formatting-a/b`: **100 formatting cases** (incl. `FMTA-006`, the idempotency repro used by spec 032 US8).

Consumed by `tests/AkmlSql.Engine.Tests/Completion/CorpusGateTests.cs`, which runs every autocomplete case through `CompletionEngine.GetCompletions` against the fake `Northwind_AutoTest` cache (`NorthwindAutoTestCacheFactory`) and asserts ratcheted per-family pass thresholds.

- `exclusions.json`: the 24 cases the campaign verifiers reclassified as **corpus mistakes** (fuzzy-by-design / compound-keyword-by-design). Reported, never failed. Excluded from the SC-001 denominator.
- At-cap cases (suggestion list hit the 50-item cap and the expected item was missing) **do fail** — correct scoping/ranking is expected to surface expected items above the cap (spec 032 edge case). They are tagged `atCap` in the gate's output for diagnosis.
- The original end-to-end (browser) results of the campaign run: `.playwright-mcp/results-completion.json` (removed after spec-032 acceptance).

Provenance: authored for the live `Northwind_AutoTest` sandbox (W3Schools Northwind + campaign enrichments: seeded OrderDetails, `Sales` schema + `Sales.Invoices`, views `vw_CustomerOrders`/`vw_ProductCatalog`, procs `usp_GetCustomerOrders`/`usp_UpdateProductPrice`/`Sales.usp_MarkInvoicePaid`, functions `fn_OrderItemCount`/`fn_OrdersByCustomer`). The fake cache must stay faithful to that shape.
