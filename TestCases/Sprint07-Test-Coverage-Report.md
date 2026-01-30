# Sprint 7 Test Coverage Report

**Generated:** 2026-01-30
**Sprint:** 7 - Refactoring Tools
**Total Test Cases:** 29

---

## Summary

| Category | Test Cases | Implemented | Automated Tests |
|----------|------------|-------------|-----------------|
| Story 7.1: Rename Alias | 9 | 9 | 9 |
| Story 7.2: Extract to CTE | 4 | 4 | 4 |
| Story 7.3: Qualify Columns | 3 | 3 | 3 |
| Story 7.4: Expand SELECT * | 4 | 4 | 4 |
| Story 7.5: Add Table Alias | 4 | 4 | 4 |
| Story 7.6: Get Suggestions | 5 | 5 | 5 |
| **TOTAL** | **29** | **29** | **29** |

---

## Story 7.1: Rename Alias

### TC-7.1.01: Rename Valid Alias
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:25-62`
- **Verification:** All occurrences of alias renamed in SELECT, FROM, WHERE
- **Automated Test:** Yes - `RefactoringServiceTests.RenameAlias_ValidAlias_RenamesAllOccurrences`

### TC-7.1.02: Rename Only Specified Alias
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:AliasRenameVisitor`
- **Verification:** Other aliases in same query remain unchanged
- **Automated Test:** Yes - `RefactoringServiceTests.RenameAlias_MultipleTableAliases_RenamesOnlySpecified`

### TC-7.1.03: Empty SQL Validation
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:26-27`
- **Verification:** Empty SQL returns failure
- **Automated Test:** Yes - `RefactoringServiceTests.RenameAlias_EmptySql_ReturnsFailure`

### TC-7.1.04: Empty Old Alias Validation
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:29-30`
- **Verification:** Empty old alias returns failure
- **Automated Test:** Yes - `RefactoringServiceTests.RenameAlias_EmptyOldAlias_ReturnsFailure`

### TC-7.1.05: Same Old and New Alias Validation
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:35-36`
- **Verification:** Same alias names return failure
- **Automated Test:** Yes - `RefactoringServiceTests.RenameAlias_SameOldAndNew_ReturnsFailure`

### TC-7.1.06: Alias Not Found
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:48-49`
- **Verification:** Non-existent alias returns failure
- **Automated Test:** Yes - `RefactoringServiceTests.RenameAlias_AliasNotFound_ReturnsFailure`

### TC-7.1.07: Invalid SQL Handling
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:43-44`
- **Verification:** Parse errors return failure
- **Automated Test:** Yes - `RefactoringServiceTests.RenameAlias_InvalidSql_ReturnsFailure`

### TC-7.1.08: Case Insensitive Alias Match
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:AliasRenameVisitor` (StringComparison.OrdinalIgnoreCase)
- **Verification:** Alias matching is case-insensitive
- **Automated Test:** Yes - `RefactoringServiceTests.RenameAlias_CaseInsensitive_RenamesAlias`

### TC-7.1.09: Complex Query with Subquery
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:AliasRenameVisitor`
- **Verification:** Nested scopes handled correctly
- **Automated Test:** Yes - `RefactoringServiceTests.RenameAlias_ComplexQuery_RenamesCorrectly`

---

## Story 7.2: Extract to CTE

### TC-7.2.01: Extract Valid Subquery to CTE
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:67-129`
- **Verification:** Subquery extracted to WITH clause
- **Automated Test:** Yes - `RefactoringServiceTests.ExtractToCte_ValidSubquery_ExtractsCorrectly`

### TC-7.2.02: Empty SQL Validation
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:68-69`
- **Verification:** Empty SQL returns failure
- **Automated Test:** Yes - `RefactoringServiceTests.ExtractToCte_EmptySql_ReturnsFailure`

### TC-7.2.03: Empty CTE Name Validation
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:71-72`
- **Verification:** Empty CTE name returns failure
- **Automated Test:** Yes - `RefactoringServiceTests.ExtractToCte_EmptyCteName_ReturnsFailure`

### TC-7.2.04: Invalid Offsets Validation
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:74-75`
- **Verification:** Invalid offset range returns failure
- **Automated Test:** Yes - `RefactoringServiceTests.ExtractToCte_InvalidOffsets_ReturnsFailure`

---

## Story 7.3: Qualify Column Names

### TC-7.3.01: Qualify Unqualified Columns
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:134-172`
- **Verification:** Single-part column names qualified with alias
- **Automated Test:** Yes - `RefactoringServiceTests.QualifyColumnNames_SingleTable_QualifiesColumns`

### TC-7.3.02: Already Qualified Columns
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:ColumnQualifierVisitor`
- **Verification:** Already qualified columns unchanged
- **Automated Test:** Yes - `RefactoringServiceTests.QualifyColumnNames_AlreadyQualified_NoChanges`

### TC-7.3.03: Empty SQL Validation
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:135-136`
- **Verification:** Empty SQL returns failure
- **Automated Test:** Yes - `RefactoringServiceTests.QualifyColumnNames_EmptySql_ReturnsFailure`

---

## Story 7.4: Expand SELECT *

### TC-7.4.01: Expand SELECT * to Columns
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:177-210`
- **Verification:** * replaced with explicit column list
- **Automated Test:** Yes - `RefactoringServiceTests.ExpandSelectStar_ValidStar_ExpandsColumns`

### TC-7.4.02: Expand Table-Qualified Star
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:191-198`
- **Verification:** t.* replaced with column list
- **Automated Test:** Yes - `RefactoringServiceTests.ExpandSelectStar_TableQualifiedStar_ExpandsColumns`

### TC-7.4.03: Empty Columns Validation
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:183-184`
- **Verification:** Empty column list returns failure
- **Automated Test:** Yes - `RefactoringServiceTests.ExpandSelectStar_EmptyColumns_ReturnsFailure`

### TC-7.4.04: Invalid Offset Validation
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:186-187`
- **Verification:** Invalid offset returns failure
- **Automated Test:** Yes - `RefactoringServiceTests.ExpandSelectStar_InvalidOffset_ReturnsFailure`

---

## Story 7.5: Add Table Alias

### TC-7.5.01: Add Alias to Table Without Alias
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:258-297`
- **Verification:** Alias added after table name
- **Automated Test:** Yes - `RefactoringServiceTests.AddTableAlias_TableWithoutAlias_AddsAlias`

### TC-7.5.02: Table Already Has Alias
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:284-285`
- **Verification:** Returns failure when alias exists
- **Automated Test:** Yes - `RefactoringServiceTests.AddTableAlias_TableAlreadyHasAlias_ReturnsFailure`

### TC-7.5.03: Empty Alias Validation
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:262-263`
- **Verification:** Empty alias returns failure
- **Automated Test:** Yes - `RefactoringServiceTests.AddTableAlias_EmptyAlias_ReturnsFailure`

### TC-7.5.04: Invalid Offset Validation
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:281-282`
- **Verification:** Invalid offset returns failure
- **Automated Test:** Yes - `RefactoringServiceTests.AddTableAlias_InvalidOffset_ReturnsFailure`

---

## Story 7.6: Get Refactoring Suggestions

### TC-7.6.01: Suggest Expand SELECT *
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:RefactoringSuggestionVisitor`
- **Verification:** SelectStarExpression suggests expand
- **Automated Test:** Yes - `RefactoringServiceTests.GetSuggestions_AtSelectStar_SuggestsExpand`

### TC-7.6.02: Suggest Add Alias
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:RefactoringSuggestionVisitor`
- **Verification:** Table without alias suggests add alias
- **Automated Test:** Yes - `RefactoringServiceTests.GetSuggestions_AtTableWithoutAlias_SuggestsAddAlias`

### TC-7.6.03: Suggest Rename Alias
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:RefactoringSuggestionVisitor`
- **Verification:** Table with alias suggests rename
- **Automated Test:** Yes - `RefactoringServiceTests.GetSuggestions_AtTableWithAlias_SuggestsRename`

### TC-7.6.04: Empty SQL Returns Empty List
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:302-303`
- **Verification:** Empty SQL returns empty suggestions
- **Automated Test:** Yes - `RefactoringServiceTests.GetSuggestions_EmptySql_ReturnsEmptyList`

### TC-7.6.05: Invalid SQL Returns Empty List
- **Status:** ✅ IMPLEMENTED
- **File:** `RefactoringService.cs:311-314`
- **Verification:** Invalid SQL returns empty suggestions
- **Automated Test:** Yes - `RefactoringServiceTests.GetSuggestions_InvalidSql_ReturnsEmptyList`

---

## Files Created/Modified

### New Files (Sprint 7)
| File | Lines | Purpose |
|------|-------|---------|
| `Core/Services/RefactoringService.cs` | 530 | SQL refactoring operations service |
| `Core.Tests/Services/RefactoringServiceTests.cs` | 320 | 29 unit tests for refactoring |

### Modified Files (Sprint 7)
| File | Purpose |
|------|---------|
| `Core/Program.cs` | Registered RefactoringService |

---

## Test Results Summary

```
Total Automated Tests: 136
  - AKML.SQL.Shared.Tests: 27 passed
  - AKML.SQL.Core.Tests: 109 passed
    - Trie tests: 20 passed
    - SqlContextAnalyzer tests: 26 passed
    - CompletionService tests: 12 passed
    - SqlParserService tests: 22 passed
    - FormatStyleService tests: 26 passed
    - RefactoringService tests: 29 passed (all new)

Sprint 7 New Tests: 29 passed
```

---

## Refactoring Operations Summary

### RenameAlias
- Renames table alias throughout the SQL statement
- Updates alias definition and all column references
- Case-insensitive matching
- Handles complex queries with subqueries

### ExtractToCte
- Extracts subquery to Common Table Expression
- Creates WITH clause or adds to existing
- Preserves query semantics

### QualifyColumnNames
- Adds table alias prefix to unqualified columns
- Only modifies single-part column references
- Requires single table context for unambiguous resolution

### ExpandSelectStar
- Replaces * with explicit column list
- Handles table-qualified stars (t.*)
- Requires column names to be provided

### AddTableAlias
- Adds alias to table reference
- Validates table doesn't already have alias
- Inserts alias after table name

### GetSuggestions
- Context-sensitive refactoring suggestions
- Suggests based on cursor position
- Returns relevant operations for current element
