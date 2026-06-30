# Spec 030 — manual test checklist

Pairs with [`spec-030-manual-tests.sql`](spec-030-manual-tests.sql). Open that file in SSMS 22 / VS 2026 on a **test** database, run the `[SETUP]` block, press **Ctrl+Shift+D** to refresh the schema cache, then work through the tests. All commands are on the **AKML SQL** menu and in the **Command Palette** (`Ctrl+Shift+P`).

> **Deployed build = through T067.** Tests marked ⏳ need the next redeploy (commits after T067).

| # | Feature | Where | Quick steps | Expected | ✅/❌ |
|---|---------|-------|-------------|----------|------|
| 1 | **bug #2** — qualify on Ctrl+Space | editor | FROM table → Ctrl+Space → re-pick | no join → `dbo.akmltest_Customers`; with join → bare name | |
| 2 ⏳ | **Toggle Code Analysis** (T056) | AKML SQL menu | run it (menu shows ✓) | OFF → squiggles vanish; ON → return | |
| 3 | **Manage Rules** (T053) | AKML SQL → Manage Code Analysis Rules… | uncheck **BP004** → Save | `= NULL` squiggle goes; re-check → returns; severity dropdown changes it | |
| 4 ⏳ | **Find Invalid Objects** (T059) | AKML SQL menu | run it | grid lists `dbo.akmltest_BrokenView` (missing `akmltest_Temp`) | |
| 5 | **Inline EXEC** (T064) | caret on `EXEC('…')` → AKML SQL menu | Apply in preview | `EXEC('SELECT 1 AS One')` → `SELECT 1 AS One` | |
| 6 | **INSERT → UPDATE** (T065) | caret in INSERT → AKML SQL menu | Apply | single-row INSERT → `UPDATE … SET … WHERE CustomerId = 1` | |
| 7 | **Inline Stored Procedure** (T063) | caret on `EXEC dbo.akmltest_GetCustomer @id = 5` | Apply | body inlined, `@id` → `5` | |
| 8 | **Script as ALTER** (T066/T067) | caret on proc/view name → AKML SQL menu | — | new tab with `ALTER PROCEDURE…` / `ALTER VIEW…` | |
| 9 ⏳ | **Disable Formatting for Selection** (T068) | select messy SQL → AKML SQL menu | then Format Document (Ctrl+K, Y) | selection wrapped in `-- AKML formatting off/on`; that region stays unformatted | |
| 10 | **Command Palette** | `Ctrl+Shift+P` | type any command name | same actions run from the palette | |

**Notes / failures (copy a failing test's input + what happened, like the bug #1/#2 reports):**

-
