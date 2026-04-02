# AKML-SQL Unit Test Coverage Plan

**Date:** 2026-03-22
**Author:** AI-assisted analysis
**Goal:** Achieve ≥ 90% line coverage across Core, Formatting, and Engine test projects

---

## Executive Summary

The codebase has 40 test classes covering ~32% of source classes. This plan covers every remaining
untested class, grouped by test file to create, with the business reason each class matters and the
concrete scenarios that should be tested.

**Priority tiers:**

| Tier | Criteria | Action |
|------|----------|--------|
| P0 | Pure-logic, no I/O, high line density | Implement immediately |
| P1 | Needs TSqlParser (in-process, no DB) | Implement with TSql170Parser fixture |
| P2 | Needs file I/O (temp dirs, easy to isolate) | Implement with TempDirectory fixture |
| P3 | Needs SQL Server / named pipes | Integration tests only, skip unit |

---

## Module 1 — AkmlSql.Formatting.Tests

### 1.1 Rules (P0 — no dependencies)

#### File: `tests/AkmlSql.Formatting.Tests/Rules/CasingRulesTests.cs`

**Business purpose:** Enforces keyword/identifier casing style so all SQL in an organization looks
consistent. Example: a rule that keywords must be UPPERCASE prevents mixed-case `select` vs `SELECT`
across a 50-developer team.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Apply_Keywords_Uppercase_ConvertsSelectToUpper` | LayoutNode with `Select` token type, casing = UPPERCASE | `FormattedText == "SELECT"` |
| `Apply_Keywords_Lowercase_ConvertsSelectToLower` | casing = lowercase | `FormattedText == "select"` |
| `Apply_Keywords_AsIs_LeavesTextUnchanged` | casing = AsIs | text unchanged |
| `Apply_Identifiers_Uppercase_ConvertsIdentifier` | `Identifier` token, identifier casing = UPPERCASE | uppercased |
| `Apply_GlobalVariables_Uppercase_ConvertsRowcount` | `@@ROWCOUNT` token, globals casing = UPPERCASE | `"@@ROWCOUNT"` |
| `Apply_LocalVariables_Lowercase_ConvertsVar` | `@myVar` token | `"@myvar"` |
| `Apply_DataTypes_Uppercase_ConvertsNvarchar` | `nvarchar` token marked as DataType | `"NVARCHAR"` |
| `Apply_BuiltinFunctions_PascalCase_ConvertsGetdate` | `getdate` token | `"GetDate"` |
| `Apply_NoformatRegion_Skipped` | any token with `IsInNoformatRegion = true` | text unchanged |
| `Apply_EmptyList_NoThrow` | empty `List<LayoutNode>` | no exception |
| `Apply_SystemObjects_Casing_SysObjects` | `sys.objects` identifier | correct casing per profile |

---

#### File: `tests/AkmlSql.Formatting.Tests/Rules/ControlFlowRulesTests.cs`

**Business purpose:** Formats `BEGIN`/`END`, `IF`/`ELSE`, `TRY`/`CATCH`, and `WHILE` blocks. Correct
indentation of these blocks is the most visible quality signal for SQL formatting tools — badly
indented IF/ELSE is the first thing users complain about.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Apply_BeginOnNewLine_True_AddsBreakBeforeBegin` | BEGIN token, `BeginOnNewLine = true` | `PrecedingBreak == NewLine` |
| `Apply_BeginOnNewLine_False_KeepsInline` | BEGIN token, `BeginOnNewLine = false` | `PrecedingBreak == None` |
| `Apply_EndOnNewLine_AddsBreakBeforeEnd` | END token | `PrecedingBreak == NewLine` |
| `Apply_ElseOnNewLine_True_AddsBreak` | ELSE token | `PrecedingBreak == NewLine` |
| `Apply_ElseOnNewLine_False_KeepsInline` | ELSE token | break unchanged |
| `Apply_IndentBodyOfIf_IncreasesIndent` | nodes inside IF block | `IndentLevel > 0` |
| `Apply_TryCatch_IndentsBody` | nodes inside TRY/CATCH | properly indented |
| `Apply_WhileLoop_IndentsBody` | nodes inside WHILE | properly indented |
| `Apply_EmptyList_NoThrow` | empty input | no exception |
| `Apply_NoformatRegion_Skipped` | tokens marked noformat | unchanged |

---

#### File: `tests/AkmlSql.Formatting.Tests/Rules/DdlRulesTests.cs`

**Business purpose:** Formats `CREATE TABLE`, `ALTER TABLE`, and other DDL statements. In large
database projects, DDL scripts are the source of truth for schema — correct column alignment
(`VARCHAR(50)` vs `NVARCHAR(MAX)`) in CREATE TABLE makes scripts readable and diff-friendly.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Apply_EachColumnOnNewLine_True_BreaksColumns` | CREATE TABLE node list, `EachColumnOnNewLine = true` | each column node has `PrecedingBreak == NewLine` |
| `Apply_EachColumnOnNewLine_False_InlineColumns` | `EachColumnOnNewLine = false` | columns inline |
| `Apply_AlignDataTypes_True_AddsAlignmentSpaces` | columns with varying name lengths | data type nodes padded to same column |
| `Apply_AlignDataTypes_False_NoExtraSpaces` | same | no extra padding |
| `Apply_CollapseShortDdl_True_ShortTableCollapsed` | 1-column table below threshold | `PrecedingBreak == None` on all |
| `Apply_CollapseShortDdl_False_NotCollapsed` | same | breaks preserved |
| `Apply_AsOnNewLine_True_MovesProcBodyToNewLine` | procedure body after AS | `PrecedingBreak == NewLine` after AS |
| `Apply_AsOnNewLine_False_AsInline` | same | AS stays inline |
| `Apply_ConstraintOnNewLine_True_BreaksBefore` | PRIMARY KEY constraint | break before CONSTRAINT |
| `Apply_EmptyList_NoThrow` | empty input | no exception |

---

#### File: `tests/AkmlSql.Formatting.Tests/Rules/JoinRulesTests.cs`

**Business purpose:** Controls JOIN formatting (empty lines before JOINs, JOIN keyword alignment).
Queries with 8+ JOINs are extremely common in reporting — visual separation between JOINs is
critical for readability.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Apply_EmptyLineBeforeJoin_True_PromotesToEmptyLine` | JOIN node with existing `NewLine` break, `EmptyLineBeforeJoin = true` | `PrecedingBreak == EmptyLine` |
| `Apply_EmptyLineBeforeJoin_False_KeepsNewLine` | same, `EmptyLineBeforeJoin = false` | `PrecedingBreak == NewLine` |
| `Apply_IndentJoin_True_IncreasesIndentOnJoin` | `IndentJoin = true` | JOIN node's `IndentLevel > 0` |
| `Apply_IndentJoin_False_NoIndent` | `IndentJoin = false` | IndentLevel unchanged |
| `Apply_AlignJoinKeyword_True_PadsKeywords` | LEFT JOIN vs INNER JOIN vs JOIN — `AlignJoinKeyword = true` | all JOIN tokens aligned to same column |
| `Apply_InnerJoinKeyword_Formatted` | INNER JOIN pair | correct handling of two-token join type |
| `Apply_LeftOuterJoin_ThreeTokens_Handled` | LEFT OUTER JOIN | all three tokens handled without null ref |
| `Apply_NoJoinsInList_NoThrow` | node list with no JOIN tokens | no exception |
| `Apply_EmptyList_NoThrow` | empty input | no exception |

---

#### File: `tests/AkmlSql.Formatting.Tests/Rules/ParenthesisRulesTests.cs`

**Business purpose:** Handles parenthesis formatting for subqueries and complex expressions.
Incorrect parenthesis handling (e.g., collapsing a multi-line subquery) is a data-loss bug —
it can silently change query semantics if done wrong.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Apply_SpaceInsideParentheses_AddsSpace` | `(` node followed by content, `SpaceInsideParentheses = true` | space after `(` |
| `Apply_NoSpaceInsideParentheses_RemovesSpace` | `SpaceInsideParentheses = false` | 0 spaces after `(` |
| `Apply_CloseParenOnNewLine_True_BreaksBefore` | `)` in multi-line subquery, `CloseParenOnNewLine = true` | `)` has `PrecedingBreak == NewLine` |
| `Apply_CloseParenOnNewLine_False_NoBreak` | same, false | no break change |
| `Apply_CollapseShortParenthesized_True_ShortCollapsed` | 3-token parenthesized expr below threshold | inline |
| `Apply_CollapseShortParenthesized_False_NotCollapsed` | same, false | breaks preserved |
| `Apply_EmptyParens_NoThrow` | `()` with nothing inside | no exception |
| `Apply_NestedParens_NoThrow` | `((a+b))` | no exception, correct nesting |
| `Apply_EmptyList_NoThrow` | empty input | no exception |

---

### 1.2 Layout (P0)

#### File: `tests/AkmlSql.Formatting.Tests/Layout/AlignmentCalculatorTests.cs`

**Business purpose:** Aligns SELECT list aliases and data types into visual columns. This is a
premium feature — the "alignment" mode is what distinguishes professional SQL formatters from simple
indentation tools.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Calculate_AlignSelectAliases_True_AlignsAs` | SELECT list with varying-length column names, AS aliases | all AS tokens are at the same column position (measured by PrecedingSpaces sum) |
| `Calculate_AlignSelectAliases_False_NoChange` | same, false | spaces unchanged |
| `Calculate_AlignDataTypes_True_AlignsTypes` | parameter list with varying name lengths | data type tokens padded to same offset |
| `Calculate_SingleItem_NoChange` | single SELECT column | no crash, no spurious padding |
| `Calculate_MixedBreaks_OnlyInlineGroupsAligned` | some columns on new lines, some inline | only inline-group items aligned together |
| `Calculate_EmptyList_NoThrow` | empty input | no exception |
| `MeasureLineWidth_CorrectWidth` | specific node sequence | correct total character count |
| `MeasureRange_PartialNodes_CorrectWidth` | range [1..3] of 5-node list | correct width for the subset |

---

### 1.3 Pipeline (P0 / P1)

#### File: `tests/AkmlSql.Formatting.Tests/Pipeline/TextEmitterTests.cs`

**Business purpose:** Converts the final `List<LayoutNode>` into a string. This is the last step
in the pipeline — every byte of output passes through here. A bug here affects 100% of formatted
SQL.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Emit_SingleNode_ProducesText` | `[Node("SELECT")]` | `"SELECT"` |
| `Emit_TwoNodes_SpacesBetween` | `[Node("SELECT"), Node("1", spaces:1)]` | `"SELECT 1"` |
| `Emit_NewLine_InsertsCRLF` | second node has `PrecedingBreak=NewLine` | contains `\r\n` or `\n` |
| `Emit_EmptyLine_InsertsTwoCRLF` | `PrecedingBreak=EmptyLine` | blank line in output |
| `Emit_IndentLevel_InsertsSpaces` | node with `IndentLevel=2`, 4-space indent profile | 8 leading spaces |
| `Emit_IndentLevel_InsertsTabs` | node with `IndentLevel=1`, tab indent profile | one tab |
| `Emit_ZeroSpaces_NoSpace` | `PrecedingSpaces=0, PrecedingBreak=None` | tokens concatenated |
| `Emit_EmptyText_NodeSkipped` | node with `FormattedText=""` | not emitted |
| `Emit_TrailingNewline_Added` | profile with `FinalNewline="ensure"` | output ends with `\n` |
| `Emit_TrailingNewline_Removed` | profile with `FinalNewline="remove"` | no trailing `\n` |
| `Emit_EmptyList_ReturnsEmpty` | empty node list | `""` |

---

#### File: `tests/AkmlSql.Formatting.Tests/Pipeline/AstAnnotatorTests.cs`

**Business purpose:** Classifies SQL comments as "leading" (before a statement) or "trailing"
(after a token on the same line). Misclassification causes comment deletion or displacement —
a data-loss bug for developers who document their queries inline.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `AttachComments_TrailingComment_MarkedTrailing` | `-- comment` token on same line as previous code | `AttachmentType == Trailing` |
| `AttachComments_LeadingComment_MarkedLeading` | `-- comment` on its own line before a statement | `AttachmentType == Leading` |
| `AttachComments_BlockComment_Detected` | `/* block */` token | returned in attachments |
| `AttachComments_NoComments_ReturnsEmpty` | token stream with no comment tokens | empty list |
| `AttachComments_MultipleComments_AllReturned` | 3 comments | 3 entries in result |

(Requires TSql170Parser to produce `IList<TSqlParserToken>` — see Parser fixture in §1.4)

---

#### File: `tests/AkmlSql.Formatting.Tests/Pipeline/BulkFormatterTests.cs`

**Business purpose:** Formats entire directories of `.sql` files in parallel. Bulk format is used
before committing to source control — a crash or incorrect output here breaks every developer's
pre-commit hook.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `FormatFilesAsync_SingleFile_FormattedCorrectly` | one `.sql` file in temp dir | `Status == Formatted`, file content changed |
| `FormatFilesAsync_AlreadyFormatted_SkippedStatus` | file already in canonical form | `Status == AlreadyFormatted` |
| `FormatFilesAsync_ParseError_ErrorStatus` | file with invalid SQL | `Status == ParseError`, file unchanged |
| `FormatFilesAsync_DryRun_FileNotModified` | `DryRun = true` | file content unchanged after run |
| `FormatFilesAsync_CreateBackups_BackupCreated` | `CreateBackups = true` | `.bak` file created alongside |
| `FormatFilesAsync_EmptyList_NoThrow` | no files | `TotalFiles == 0`, no exception |
| `FormatFilesAsync_CancellationToken_StopsEarly` | cancel after first file | task completes cleanly |
| `FormatFilesAsync_Report_CountsCorrect` | 3 files: 1 formatted, 1 skipped, 1 error | report counts match |

(Requires temp directory — P2 tier)

---

#### File: `tests/AkmlSql.Formatting.Tests/Pipeline/SelectionFormatterTests.cs`

**Business purpose:** Formats only the selected text in the editor, leaving surrounding SQL
untouched. Used when a developer selects one sub-query to clean up. An off-by-one in the
selection boundaries produces malformed SQL.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `FormatSelection_FullText_EquivalentToFullFormat` | selection = entire text | same result as `FormatterPipeline.Format()` |
| `FormatSelection_Partial_OnlySelectedChanged` | select inner SELECT in a multi-query script | text before/after selection unchanged |
| `FormatSelection_InvalidBounds_ReturnsOriginal` | `selectionStart > selectionEnd` | `WasModified == false` |
| `FormatSelection_EmptySelection_ReturnsOriginal` | `selectionStart == selectionEnd` | `WasModified == false` |
| `FormatSelection_SimpleQuery_FormattedCorrectly` | `select 1` selected | uppercased/indented |

---

### 1.4 Actions (P1 — needs TSqlParser)

#### File: `tests/AkmlSql.Formatting.Tests/Actions/CasingOnlyActionTests.cs`

**Business purpose:** Applies only casing changes without full reformatting. Used by "Quick Fix"
menu: "Capitalize keywords" without changing indentation or line breaks.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Execute_Keywords_Uppercased` | `select 1` | `SELECT 1` |
| `Execute_Identifiers_Unchanged_WhenIdentifierCasingAsIs` | profile with AsIs for identifiers | identifiers unchanged |
| `Execute_EmptySql_ReturnsEmpty` | `""` | `""` |

---

#### File: `tests/AkmlSql.Formatting.Tests/Actions/ToggleBracketsActionTests.cs`

**Business purpose:** Adds or removes square bracket quoting from identifiers (`MyTable` ↔
`[MyTable]`). Required for identifiers that conflict with reserved keywords.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Execute_AddBrackets_WrapsIdentifiers` | `SELECT col FROM tbl` | `SELECT [col] FROM [tbl]` |
| `Execute_RemoveBrackets_UnwrapsIdentifiers` | `SELECT [col] FROM [tbl]` | `SELECT col FROM tbl` |
| `Execute_AlreadyBracketed_Idempotent` | bracketed identifiers with add action | no double-bracket `[[col]]` |
| `Execute_EmptySql_ReturnsEmpty` | `""` | `""` |
| `Execute_Keywords_NotBracketed` | `SELECT` keyword | remains unbracketed |

---

#### File: `tests/AkmlSql.Formatting.Tests/Actions/InsertSemicolonsActionTests.cs`

**Business purpose:** Adds missing statement terminators. Semicolons are required by modern T-SQL
best practices and are mandatory before CTE declarations.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Execute_AddsSemicolon_AtEndOfStatement` | `SELECT 1` | `SELECT 1;` |
| `Execute_NoDoubleAdd_WhenAlreadyPresent` | `SELECT 1;` | unchanged |
| `Execute_MultipleStatements_AllTerminated` | `SELECT 1 SELECT 2` | both get `;` |
| `Execute_EmptySql_ReturnsEmpty` | `""` | `""` |

---

#### File: `tests/AkmlSql.Formatting.Tests/Actions/RemoveSemicolonsActionTests.cs`

**Business purpose:** Removes statement terminators (some shops prefer no trailing semicolons for
SSMS compatibility with old migration scripts).

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Execute_RemovesSemicolon` | `SELECT 1;` | `SELECT 1` |
| `Execute_NoChange_WhenNone` | `SELECT 1` | unchanged |

---

#### File: `tests/AkmlSql.Formatting.Tests/Actions/ToggleAsKeywordActionTests.cs`

**Business purpose:** Adds/removes the `AS` keyword in column aliases (`col alias` vs `col AS alias`).

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Execute_AddAs_InsertsKeyword` | `SELECT col alias` | `SELECT col AS alias` |
| `Execute_RemoveAs_RemovesKeyword` | `SELECT col AS alias` | `SELECT col alias` |

---

### 1.5 Profiles (P0)

#### File: `tests/AkmlSql.Formatting.Tests/Profiles/SqlPromptImporterTests.cs`

**Business purpose:** Migrates existing SQL Prompt or MSSQL Format settings so users can switch
to AKML-SQL without manually reconfiguring 40+ options. A broken importer blocks onboarding for
teams already invested in SQL Prompt.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `ImportRedgate_ValidXml_SetsKeywordCasing` | minimal SQL Prompt XML with keyword casing setting | `profile.Casing.Keywords` is set |
| `ImportRedgate_EmptyXml_ReturnsDefaultProfile` | empty/minimal XML | no exception, default profile |
| `ImportRedgate_InvalidXml_ThrowsOrReturnsDefault` | malformed XML | either FormatException or default profile |
| `ImportMSSQLFormat_ValidJson_SetsOptions` | minimal JSON with options | at least one option mapped |
| `ImportMSSQLFormat_EmptyJson_ReturnsDefault` | `{}` | no exception |

---

---

## Module 2 — AkmlSql.Engine.Tests

### 2.1 Parser (P1 — needs TSql170Parser, no DB)

All parser tests share one static helper:

```csharp
private static TSqlScript ParseSql(string sql)
{
    var parser = new TSql170Parser(false);
    var reader = new StringReader(sql);
    var script = parser.Parse(reader, out IList<ParseError> errors) as TSqlScript;
    return script!;
}
```

#### File: `tests/AkmlSql.Engine.Tests/Parser/TsqlParserServiceTests.cs`

**Business purpose:** Wraps `TSql170Parser` and caches parser instances per server version. Every
IntelliSense feature — completion, signature help, hover info — depends on this service producing
a valid AST. A regression here disables all IntelliSense for all users.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `GetTokenStream_SimpleSelect_ReturnsTokens` | `SELECT 1` | non-empty token list |
| `GetTokenStream_EmptySql_ReturnsTokensOrEmpty` | `""` | no exception |
| `Parse_ValidSql_ReturnsScript` | `SELECT 1;` | `TSqlScript` not null, no parse errors |
| `Parse_InvalidSql_ReturnsNullOrErrors` | `SELECT FROM` | errors list populated |
| `ParseWithSuffix_IncompleteFragment_ParsesSuccessfully` | `SELECT ` (no FROM) | script not null |
| `SplitBatches_SingleBatch_OneEntry` | `SELECT 1;` | 1 batch |
| `SplitBatches_TwoBatches_WithGO` | `SELECT 1;\nGO\nSELECT 2;` | 2 batches |
| `SplitBatches_Empty_ReturnsEmpty` | `""` | 0 batches |
| `SetServerVersion_ChangesParser` | set version 160, then 170 | no exception, parses correctly |

---

#### File: `tests/AkmlSql.Engine.Tests/Parser/CteResolverTests.cs`

**Business purpose:** Identifies all CTE names in a query so the completion engine can offer them
as completion candidates. Without this, users get no IntelliSense for their own CTEs — a common
complaint in SQL IntelliSense tools.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `ResolveCtes_SingleCte_Found` | `WITH cte AS (SELECT 1) SELECT * FROM cte` | result contains key `"cte"` |
| `ResolveCtes_MultipleCtes_AllFound` | `WITH a AS (...), b AS (...) SELECT ...` | both `"a"` and `"b"` in result |
| `ResolveCtes_NoCtes_ReturnsEmpty` | `SELECT 1` | empty dictionary |
| `ResolveCtes_NestedCteReference_Resolved` | CTE that references another CTE | outer found |
| `ResolveCtes_CaseInsensitive_NormalizedName` | `WITH MyCtE AS (...)` | key is normalized (e.g., lowercase) |

---

#### File: `tests/AkmlSql.Engine.Tests/Parser/TempTableTrackerTests.cs`

**Business purpose:** Tracks `#temp` and `##global` temporary tables declared in a script to offer
column completion for them. Without this, developers get no column suggestions when querying their
own temp tables.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Track_CreateTempTable_Found` | `CREATE TABLE #t (id INT)` | result contains `#t` |
| `Track_SelectInto_Found` | `SELECT col INTO #t FROM src` | result contains `#t` |
| `Track_GlobalTemp_Found` | `CREATE TABLE ##g (id INT)` | result contains `##g` |
| `Track_NoTempTables_ReturnsEmpty` | `SELECT 1` | empty list |
| `Track_MultipleDeclarations_AllFound` | two CREATE TABLE #t statements | both found |

---

#### File: `tests/AkmlSql.Engine.Tests/Parser/VariableTrackerTests.cs`

**Business purpose:** Identifies `DECLARE @variable` statements so the completion engine can
suggest those variables. Without variable tracking, developers see no autocomplete for their
own declared variables.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Track_SingleDeclaration_Found` | `DECLARE @id INT` | dictionary contains `@id` |
| `Track_MultipleDeclarations_AllFound` | `DECLARE @a INT, @b VARCHAR(50)` | both keys present |
| `Track_SetStatement_NotAddedAsNew` | `SET @x = 1` (no DECLARE) | only previously declared in dict |
| `Track_NoVariables_ReturnsEmpty` | `SELECT 1` | empty dict |
| `Track_VariableType_Preserved` | `DECLARE @n NVARCHAR(100)` | type info recorded |

---

#### File: `tests/AkmlSql.Engine.Tests/Parser/CursorContextAnalyzerTests.cs`

**Business purpose:** Determines the SQL context at the cursor position (SELECT list, FROM clause,
WHERE clause, etc.). This context drives which completion provider is activated — wrong context
returns irrelevant suggestions.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `Analyze_SelectClause_ReturnsSelectContext` | `SELECT |` (cursor in SELECT list) | `ClauseType == "SELECT"` or equivalent |
| `Analyze_FromClause_ReturnsFromContext` | `SELECT 1 FROM |` | `ClauseType == "FROM"` |
| `Analyze_WhereClause_ReturnsWhereContext` | `SELECT 1 FROM t WHERE |` | WHERE context |
| `Analyze_InsideString_InStringTrue` | `SELECT '|'` | `InComment == false`, `InString == true` |
| `Analyze_InsideComment_InCommentTrue` | `SELECT -- |` | `InComment == true` |
| `Analyze_PartialWord_ExtractedCorrectly` | `SELECT MyTab|` | `PartialText == "MyTab"` |
| `Analyze_AtStart_ReturnsDefaultContext` | cursor at position 0 | no exception |

---

#### File: `tests/AkmlSql.Engine.Tests/Parser/AliasResolverTests.cs`

**Business purpose:** Maps table aliases (`t`, `o`) to their actual table names (`dbo.Orders`) so
the column completion engine knows which table to look up. Without alias resolution, column
completions are empty for aliased tables.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `ResolveAliases_SimpleAlias_Found` | `SELECT * FROM dbo.Orders o` | `o → dbo.Orders` in result |
| `ResolveAliases_MultipleAliases_AllFound` | `FROM t1, t2` | both aliases resolved |
| `ResolveAliases_NoAlias_TableNameUsedAsKey` | `FROM dbo.Orders` | `Orders → dbo.Orders` |
| `ResolveAliases_JoinAlias_Found` | `FROM t1 INNER JOIN t2 AS j` | `j` resolved |
| `ResolveAliases_SubqueryAlias_NotCrash` | `FROM (SELECT 1) sub` | no exception |
| `ResolveAliases_CteAlias_NotOverrideActual` | CTE named `cte`, then `FROM cte` | no conflict |

---

### 2.2 Schema (P0)

#### File: `tests/AkmlSql.Engine.Tests/Schema/DatabaseCacheTests.cs`

**Business purpose:** Stores schema objects (tables, views, procedures) fetched from SQL Server.
The cache is shared across sessions — thread safety and correctness of lookup are critical for
multi-document IntelliSense.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `FindObject_ExactMatch_ReturnsObject` | add `dbo.Orders`, find `dbo.Orders` | not null |
| `FindObject_CaseInsensitive_ReturnsObject` | find `DBO.ORDERS` | not null |
| `FindObject_NotFound_ReturnsNull` | find `dbo.NonExistent` | null |
| `GetAllObjects_ReturnsAllAdded` | add 3 objects | count == 3 |
| `GetObjectsInSchema_FiltersBySchema` | add dbo + hr objects, request dbo | only dbo objects |
| `GetSchemaNames_ReturnsDistinctSchemas` | add dbo.A, dbo.B, hr.C | `{"dbo", "hr"}` |
| `AreColumnsLoaded_FalseBeforeLoad` | fresh object | false |
| `GetForeignKeysForTable_ReturnsRelevant` | add FK for dbo.Orders | returns that FK |
| `GetForeignKeysForTable_NoFKs_ReturnsEmpty` | table with no FKs | empty list |
| `IsStale_FalseAfterConstruct` | new cache | `IsStale == false` |
| `ConcurrentRead_NoException` | parallel `GetAllObjects()` calls | no exception |

---

#### File: `tests/AkmlSql.Engine.Tests/Schema/SchemaCacheManagerTests.cs`

**Business purpose:** Manages one `DatabaseCache` per (server, database) pair. Ensuring a stale
cache is replaced atomically prevents race conditions where one session reads half-updated schema
data.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `GetCache_AfterAddCache_ReturnsSame` | `AddCache` then `GetCache` with same key | same instance |
| `GetCache_Missing_ReturnsNull` | key never added | null |
| `RemoveCache_Removes` | add then remove | subsequent `GetCache` returns null |
| `GetAll_ReturnsAllCaches` | add 2 caches | 2 entries |
| `AddCache_DuplicateKey_Overwrites` | add same key twice | second value returned |

---

### 2.3 Completion (P0 — no DB)

#### File: `tests/AkmlSql.Engine.Tests/Completion/CompletionEngineTests.cs`

**Business purpose:** Orchestrates all completion providers and returns ranked, deduplicated
completion items. This is the entry point for every keystroke-triggered completion — latency and
correctness here are the primary user-visible quality metrics.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `GetCompletions_NoProviders_ReturnsEmpty` | engine with no registered providers | empty items |
| `GetCompletions_SingleProvider_ReturnsItems` | one provider returning 3 items | 3 items |
| `GetCompletions_MultipleProviders_CombinesItems` | two providers, 2 items each | 4 items |
| `GetCompletions_FuzzyFiltered_ReturnsMatching` | partial text `"sel"` filters keywords | matching items returned |
| `GetCompletions_InComment_ReturnsEmpty` | cursor inside `-- comment` | empty (context = in-comment) |
| `GetCompletions_InString_ReturnsEmpty` | cursor inside string literal | empty |
| `SetMaxSuggestions_LimitsResults` | 2 providers × 100 items, max = 5 | ≤ 5 items |
| `RegisterProvider_NullProvider_ThrowsArgNull` | pass null | `ArgumentNullException` |
| `GetSignatureHelp_KnownFunction_ReturnsSignature` | `GETDATE(|` | signature help with GETDATE |
| `GetQuickInfo_KnownKeyword_ReturnsInfo` | `SELECT` hovered | quick info not null/empty |

---

#### File: `tests/AkmlSql.Engine.Tests/Completion/Providers/KeywordProviderTests.cs`

**Business purpose:** Supplies SQL keyword completions (`SELECT`, `FROM`, `WHERE`, etc.). Keywords
are the most frequently triggered completions — correctness and completeness here is table stakes.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `CanHandle_AnyContext_ReturnsTrue` | any CursorContext | true |
| `GetCompletions_NoFilter_ReturnsKeywords` | empty partial text | non-empty list |
| `GetCompletions_PartialSel_ReturnsSelectFamily` | `PartialText = "sel"` | contains SELECT |
| `GetCompletions_NoMatch_ReturnsEmpty` | `PartialText = "zzz"` | empty |
| `GetCompletions_InComment_Skipped` | `InComment = true` | `CanHandle` returns false |
| `GetCompletions_Keywords_CorrectObjectType` | any result | `ObjectType == Keyword` |

---

#### File: `tests/AkmlSql.Engine.Tests/Completion/Providers/VariableProviderTests.cs`

**Business purpose:** Suggests `@variable` completions declared earlier in the batch. Without this,
developers must remember every variable name exactly.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `CanHandle_AtSymbolPartial_ReturnsTrue` | `PartialText = "@my"` | true |
| `CanHandle_NoAtSymbol_ReturnsFalse` | `PartialText = "col"` | false |
| `GetCompletions_DeclaredVariable_InResults` | context has `@userId` in available variables | `@userId` in items |
| `GetCompletions_NoVariables_ReturnsEmpty` | no variables in context | empty |
| `GetCompletions_FilterByPartial_Correct` | `@user` partial, `@userId` and `@orderId` available | only `@userId` |

---

### 2.4 Server (P0 — pure in-memory)

#### File: `tests/AkmlSql.Engine.Tests/Server/SessionManagerTests.cs`

**Business purpose:** Tracks active IDE sessions (open documents + connection state). Each
connected query editor window is a session. Stale or missing sessions cause IntelliSense to offer
wrong-database completions.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `UpdateSession_NewSession_Stored` | `UpdateSession` with new sessionId | `GetSession(id) != null` |
| `UpdateSession_ExistingSession_Overwrites` | `UpdateSession` with same id, new connection | `ConnectionString` updated |
| `UpdateDocument_UpdatesDocumentText` | `UpdateDocument` with new text | `GetSession(id).DocumentText == newText` |
| `GetSession_Unknown_ReturnsNull` | unknown id | null |
| `RemoveSession_Removes` | add then remove | `GetSession` returns null |
| `SessionCount_CorrectAfterOperations` | add 3, remove 1 | `SessionCount == 2` |
| `UpdateSession_Thread_Safe` | 100 concurrent `UpdateSession` calls | `SessionCount == 100`, no exception |

---

### 2.5 Format Handler (P0 — no DB)

#### File: `tests/AkmlSql.Engine.Tests/Formatter/FormatRequestHandlerTests.cs`

**Business purpose:** Handles IPC format requests from the SSMS/VS extension. This is the server
side of the formatter — if it crashes or returns wrong data, the user sees an error toast and
their SQL is not formatted.

| Test Method | Scenario | What to assert |
|-------------|----------|----------------|
| `HandleFormat_ValidSql_Success` | `FormatRequest` with valid SQL | `Success == true`, `FormattedText != null` |
| `HandleFormat_InvalidSql_ReturnsDiagnostics` | `FormatRequest` with invalid SQL | `Success == false` or has diagnostics |
| `HandleFormat_EmptySql_ReturnsEmpty` | `Text = ""` | `FormattedText == ""`, `Success == true` |
| `HandleFormatSelection_ValidBounds_FormatsSubset` | selection over valid SQL range | `Success == true` |
| `HandleFormatSelection_InvalidBounds_Handled` | `SelectionStart > SelectionEnd` | no crash, `Success == false` |
| `HandleFormatPreview_ValidJson_Formats` | sample text + profile JSON | `FormattedText != null` |
| `HandleFormatAction_CasingOnly_AppliesCasing` | `ActionType == CasingOnly` | keywords uppercased |
| `HandleProfileList_ReturnsProfiles` | default profiles | non-empty list |
| `HandleBulkFormat_EmptyPaths_ReturnsZero` | `FilePaths = []` | `TotalFiles == 0` |

---

## Module 3 — AkmlSql.Core.Tests

The agent scan confirmed Core tests are comprehensive. No new test files are required beyond what
already exists. The IPC message classes (POCO DTOs) are tested via `IpcMessagesTests.cs`.

---

## Implementation Order (by coverage ROI)

| Step | File to Create | Tier | Est. New Lines Covered |
|------|---------------|------|----------------------|
| 1 | `Rules/CasingRulesTests.cs` | P0 | ~150 |
| 2 | `Rules/ControlFlowRulesTests.cs` | P0 | ~120 |
| 3 | `Rules/DdlRulesTests.cs` | P0 | ~110 |
| 4 | `Rules/JoinRulesTests.cs` | P0 | ~80 |
| 5 | `Rules/ParenthesisRulesTests.cs` | P0 | ~90 |
| 6 | `Layout/AlignmentCalculatorTests.cs` | P0 | ~100 |
| 7 | `Pipeline/TextEmitterTests.cs` | P0 | ~80 |
| 8 | `Schema/DatabaseCacheTests.cs` | P0 | ~120 |
| 9 | `Schema/SchemaCacheManagerTests.cs` | P0 | ~50 |
| 10 | `Server/SessionManagerTests.cs` | P0 | ~60 |
| 11 | `Formatter/FormatRequestHandlerTests.cs` | P0 | ~130 |
| 12 | `Completion/CompletionEngineTests.cs` | P0 | ~150 |
| 13 | `Completion/Providers/KeywordProviderTests.cs` | P0 | ~60 |
| 14 | `Completion/Providers/VariableProviderTests.cs` | P0 | ~50 |
| 15 | `Parser/TsqlParserServiceTests.cs` | P1 | ~80 |
| 16 | `Parser/CteResolverTests.cs` | P1 | ~70 |
| 17 | `Parser/TempTableTrackerTests.cs` | P1 | ~60 |
| 18 | `Parser/VariableTrackerTests.cs` | P1 | ~60 |
| 19 | `Parser/CursorContextAnalyzerTests.cs` | P1 | ~80 |
| 20 | `Parser/AliasResolverTests.cs` | P1 | ~70 |
| 21 | `Pipeline/AstAnnotatorTests.cs` | P1 | ~50 |
| 22 | `Pipeline/TextEmitterTests.cs` (already above) | — | — |
| 23 | `Pipeline/BulkFormatterTests.cs` | P2 | ~90 |
| 24 | `Pipeline/SelectionFormatterTests.cs` | P0 | ~60 |
| 25 | `Actions/CasingOnlyActionTests.cs` | P1 | ~40 |
| 26 | `Actions/ToggleBracketsActionTests.cs` | P1 | ~50 |
| 27 | `Actions/InsertSemicolonsActionTests.cs` | P1 | ~40 |
| 28 | `Actions/RemoveSemicolonsActionTests.cs` | P1 | ~30 |
| 29 | `Actions/ToggleAsKeywordActionTests.cs` | P1 | ~30 |
| 30 | `Profiles/SqlPromptImporterTests.cs` | P0 | ~80 |

**Estimated total new lines covered: ~2 200**

---

## Classes NOT to unit-test (P3 — I/O / external dependencies)

| Class | Reason |
|-------|--------|
| `PipeRpcServer` | Requires named pipe + client; use integration test |
| `SchemaMetadataService` | Requires live SQL Server connection |
| `ChangeDetector` | Requires live SQL Server connection (already has mock tests) |
| `SnippetLoader` | File I/O only; covered implicitly by `SnippetRequestHandler` integration tests |
| `LoggerFactory` | Already tested in Core.Tests |
| `ConfigManager` | Already tested in Core.Tests |
| `BulkFormatter` (file writing) | Bulk format of real files tested in P2 tier above |

---

## Shared Test Helpers to Extract

Create `tests/AkmlSql.Engine.Tests/Helpers/ParserFixture.cs`:

```csharp
internal static class ParserFixture
{
    public static TSqlScript Parse(string sql)
    {
        var parser = new TSql170Parser(false);
        using var reader = new StringReader(sql);
        var script = (TSqlScript)parser.Parse(reader, out _);
        return script;
    }

    public static IList<TSqlParserToken> Tokens(string sql)
    {
        var parser = new TSql170Parser(false);
        using var reader = new StringReader(sql);
        parser.Parse(reader, out _);
        return parser.GetTokenStream(); // or via ScriptTokenStream
    }
}
```

Create `tests/AkmlSql.Formatting.Tests/Helpers/NodeBuilder.cs`:

```csharp
internal static class NodeBuilder
{
    public static LayoutNode Node(
        string text,
        TSqlTokenType tokenType = TSqlTokenType.Identifier,
        BreakType breakType = BreakType.None,
        int spaces = 1,
        int indent = 0,
        bool inNoformat = false) => new()
    {
        FormattedText = text,
        TokenType = tokenType,
        PrecedingBreak = breakType,
        PrecedingSpaces = spaces,
        IndentLevel = indent,
        IsInNoformatRegion = inNoformat
    };
}
```

(Multiple test files can reference this instead of defining `Node()` locally in each class.)
