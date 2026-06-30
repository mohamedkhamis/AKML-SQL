/* =====================================================================================
   AKML SQL — Spec 030 manual test script
   =====================================================================================
   HOW TO USE
     1. Open this file in SSMS 22 (or VS 2026) on a TEST database you can create objects in.
     2. Run the [SETUP] block once (F5 over that block only).
     3. Refresh the AKML schema cache:  Ctrl+Shift+D   (or reconnect the window).
        — completion + INSERT→UPDATE need the new objects in the cache.
     4. Work top-to-bottom through TEST 1..10. Each test has: WHERE to click, WHAT to do,
        and the EXPECTED result. Use the provided snippet right under each test.
     5. Run the [CLEANUP] block at the end to drop the test objects.

   Commands live on the  "AKML SQL"  top-level menu, and all are also in the
   Command Palette (Ctrl+Shift+P → type the name).

   DEPLOY NOTE: the currently-installed build covers through T067. TEST 2 (Toggle Code
   Analysis), TEST 4 (Find Invalid Objects) and TEST 9 (Disable Formatting) ship in a
   LATER commit — they will only work after the next redeploy. The rest work now.
   ===================================================================================== */


/* ============================ [SETUP] — run this block once ============================ */
IF OBJECT_ID('dbo.akmltest_OrderView','V')      IS NOT NULL DROP VIEW  dbo.akmltest_OrderView;
IF OBJECT_ID('dbo.akmltest_BrokenView','V')     IS NOT NULL DROP VIEW  dbo.akmltest_BrokenView;
IF OBJECT_ID('dbo.akmltest_GetCustomer','P')    IS NOT NULL DROP PROC  dbo.akmltest_GetCustomer;
IF OBJECT_ID('dbo.akmltest_Orders','U')         IS NOT NULL DROP TABLE dbo.akmltest_Orders;
IF OBJECT_ID('dbo.akmltest_Customers','U')      IS NOT NULL DROP TABLE dbo.akmltest_Customers;
IF OBJECT_ID('dbo.akmltest_Temp','U')           IS NOT NULL DROP TABLE dbo.akmltest_Temp;
GO

CREATE TABLE dbo.akmltest_Customers
(
    CustomerId  INT          NOT NULL IDENTITY(1,1) CONSTRAINT PK_akmltest_Customers PRIMARY KEY,
    FullName    NVARCHAR(100) NOT NULL,
    City        NVARCHAR(50)  NULL,
    CreatedOn   DATETIME      NOT NULL CONSTRAINT DF_akmltest_Customers_CreatedOn DEFAULT (GETDATE())
);
GO

CREATE TABLE dbo.akmltest_Orders
(
    OrderId     INT NOT NULL IDENTITY(1,1) CONSTRAINT PK_akmltest_Orders PRIMARY KEY,
    CustomerId  INT NOT NULL,
    Amount      DECIMAL(10,2) NOT NULL
);
GO

-- A simple single-statement proc — used by "Inline Stored Procedure" and "Script as ALTER".
CREATE PROCEDURE dbo.akmltest_GetCustomer
    @id INT
AS
    SELECT CustomerId, FullName, City FROM dbo.akmltest_Customers WHERE CustomerId = @id;
GO

-- A valid view — used by "Script as ALTER".
CREATE VIEW dbo.akmltest_OrderView
AS
    SELECT o.OrderId, o.Amount, c.FullName
    FROM dbo.akmltest_Orders o
    JOIN dbo.akmltest_Customers c ON c.CustomerId = o.CustomerId;
GO

-- A temp helper table + a view on it, then DROP the table so the view becomes INVALID
-- (used by "Find Invalid Objects").
CREATE TABLE dbo.akmltest_Temp (Id INT NOT NULL, Note NVARCHAR(50) NULL);
GO
CREATE VIEW dbo.akmltest_BrokenView AS SELECT Id, Note FROM dbo.akmltest_Temp;
GO
DROP TABLE dbo.akmltest_Temp;   -- akmltest_BrokenView now references a missing table.
GO
/* >>> After SETUP: press Ctrl+Shift+D to refresh the schema cache before continuing. <<< */



/* ============================ TEST 1 — bug #2: schema qualify on Ctrl+Space ============================
   WHAT:  In the FROM clause, click on the table name, press Ctrl+Space and re-select it.
   EXPECT (no join):  it inserts the schema-qualified name  ->  dbo.akmltest_Customers
   Then add a JOIN (second snippet) and re-select a table.
   EXPECT (with join): it inserts the BARE name  ->  akmltest_Customers   (no dbo. prefix)
   ------------------------------------------------------------------------------------------------------ */

-- 1a) No join — click "akmltest_Customers", Ctrl+Space, pick it again -> expect dbo.akmltest_Customers
SELECT * FROM akmltest_Customers WHERE City IS NOT NULL;

-- 1b) With a join — click either table name, Ctrl+Space, re-pick -> expect the BARE name (no dbo.)
SELECT c.FullName, o.Amount
FROM akmltest_Customers c
JOIN akmltest_Orders o ON o.CustomerId = c.CustomerId;



/* ============================ TEST 2 — Toggle Code Analysis (T056) ====================================
   WHAT:  The snippet below has two obvious analysis violations (SELECT * and = NULL), so you should
          see squiggles. Now run:  AKML SQL menu -> Toggle Code Analysis   (menu shows a check mark).
   EXPECT: OFF -> squiggles disappear immediately.   ON -> squiggles come back.
   ------------------------------------------------------------------------------------------------------ */
SELECT * FROM dbo.akmltest_Customers WHERE City = NULL;   -- PE001 (SELECT *) + BP004 (= NULL)



/* ============================ TEST 3 — Manage Code Analysis Rules (T053) ==============================
   WHAT:  AKML SQL menu -> Manage Code Analysis Rules...
          Find rule BP004 (= NULL comparison) — UNCHECK Enabled, click Save.
   EXPECT: the "= NULL" squiggle on the snippet below disappears (BP004 disabled).
           Re-open the dialog, re-check BP004, Save -> the squiggle returns.
   (Also try changing a rule's Severity dropdown and Save — the squiggle colour/severity changes.)
   ------------------------------------------------------------------------------------------------------ */
SELECT FullName FROM dbo.akmltest_Customers WHERE City = NULL;   -- BP004 only



/* ============================ TEST 4 — Find Invalid Objects (T059) ====================================
   WHAT:  AKML SQL menu -> Find Invalid Objects   (no selection needed; uses the active connection).
   EXPECT: a results grid listing  dbo.akmltest_BrokenView  (Type = View), with
           Missing Dependency = akmltest_Temp  and an error about an unresolved reference.
   (If you see "No invalid objects found", make sure the SETUP block ran and you refreshed the cache.)
   ------------------------------------------------------------------------------------------------------ */
-- (nothing to type — just run the command)



/* ============================ TEST 5 — Inline EXEC (T064) =============================================
   WHAT:  Put the cursor on the EXEC line below, then:  AKML SQL menu -> Inline EXEC.
   EXPECT: a preview dialog showing the change; click Apply ->
           the line becomes:   SELECT 1 AS One
   ------------------------------------------------------------------------------------------------------ */
EXEC('SELECT 1 AS One');



/* ============================ TEST 6 — INSERT -> UPDATE (T065) ========================================
   WHAT:  Put the cursor inside the INSERT below, then:  AKML SQL menu -> Convert INSERT to UPDATE.
   EXPECT: preview shows an UPDATE with SET for the non-key columns and WHERE on the PK; Apply ->
           UPDATE dbo.akmltest_Customers SET FullName = N'Jane', City = N'Cairo' WHERE CustomerId = 1
   (Needs the table's PK in the cache — run SETUP + Ctrl+Shift+D first.)
   ------------------------------------------------------------------------------------------------------ */
INSERT INTO dbo.akmltest_Customers (CustomerId, FullName, City) VALUES (1, N'Jane', N'Cairo');



/* ============================ TEST 7 — Inline Stored Procedure (T063) =================================
   WHAT:  Put the cursor on the EXEC below, then:  AKML SQL menu -> Inline Stored Procedure.
   EXPECT: preview shows the proc body with @id replaced by 5; Apply ->
           SELECT CustomerId, FullName, City FROM dbo.akmltest_Customers WHERE CustomerId = 5
   (Needs a live connection — the engine fetches the proc body from the database.)
   ------------------------------------------------------------------------------------------------------ */
EXEC dbo.akmltest_GetCustomer @id = 5;



/* ============================ TEST 8 — Script as ALTER (T066 / T067) ==================================
   WHAT:  Put the cursor on the object name, then:  AKML SQL menu -> Script as ALTER.
   EXPECT: a NEW tab opens containing the object's definition with the leading CREATE rewritten to
           ALTER  (ALTER PROCEDURE dbo.akmltest_GetCustomer ...  /  ALTER VIEW dbo.akmltest_OrderView ...)
   ------------------------------------------------------------------------------------------------------ */
-- 8a) cursor on the proc name:
SELECT 'put the caret on this name ->', 'akmltest_GetCustomer';
-- 8b) cursor on the view name:
SELECT 'put the caret on this name ->', 'akmltest_OrderView';



/* ============================ TEST 9 — Disable Formatting for Selection (T068) ========================
   WHAT:  SELECT the messy block below (the two SELECT lines), then:
          AKML SQL menu -> Disable Formatting for Selection.
   EXPECT: the selection is wrapped:   -- AKML formatting off  ...  -- AKML formatting on
           Now run Format Document (Ctrl+K, Y): everything formats EXCEPT the wrapped region,
           which stays exactly as you left it.
   ------------------------------------------------------------------------------------------------------ */
select    OrderId,Amount   from dbo.akmltest_Orders   where Amount>0;
select CustomerId,FullName from dbo.akmltest_Customers where City is not null;



/* ============================ TEST 10 — Command Palette (all of the above) ============================
   WHAT:  Press Ctrl+Shift+P, type any of:  Inline EXEC, Convert INSERT, Inline Stored Procedure,
          Script as ALTER, Find Invalid Objects, Manage Code Analysis Rules, Toggle Code Analysis,
          Disable Formatting for Selection.
   EXPECT: each entry appears and runs the same action as its AKML SQL menu item.
   ------------------------------------------------------------------------------------------------------ */



/* ============================ [CLEANUP] — run when finished ============================ */
IF OBJECT_ID('dbo.akmltest_OrderView','V')   IS NOT NULL DROP VIEW  dbo.akmltest_OrderView;
IF OBJECT_ID('dbo.akmltest_BrokenView','V')  IS NOT NULL DROP VIEW  dbo.akmltest_BrokenView;
IF OBJECT_ID('dbo.akmltest_GetCustomer','P') IS NOT NULL DROP PROC  dbo.akmltest_GetCustomer;
IF OBJECT_ID('dbo.akmltest_Orders','U')      IS NOT NULL DROP TABLE dbo.akmltest_Orders;
IF OBJECT_ID('dbo.akmltest_Customers','U')   IS NOT NULL DROP TABLE dbo.akmltest_Customers;
IF OBJECT_ID('dbo.akmltest_Temp','U')        IS NOT NULL DROP TABLE dbo.akmltest_Temp;
GO
