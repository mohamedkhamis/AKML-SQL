# Profile Schema Contract

**Version**: 1.0 | **Branch**: `003-sql-formatter`

## Overview

Formatting profiles are stored as `.akmlstyle` files containing human-readable JSON. Each file defines a complete set of formatting options plus metadata. The schema is versioned for forward compatibility.

## File Format

- **Extension**: `.akmlstyle`
- **Encoding**: UTF-8 (no BOM)
- **Serialization**: JSON via System.Text.Json with source generators
- **Forward compatibility**: Unknown properties preserved via `[JsonExtensionData]`

## Storage Locations

| Location | Type | Access |
|---|---|---|
| `%AppData%/AKML SQL/profiles/` | Custom | Read/Write |
| `<install>/profiles/` | Built-in | Read-only |

## Schema

```json
{
  "metadata": {
    "id": "a3b7c9d1-e2f4-5678-9012-abcdef123456",
    "schemaVersion": 1,
    "name": "Profile Name",
    "description": "Optional description",
    "author": "Optional author",
    "version": "1.0",
    "created": "2026-03-20T10:00:00Z",
    "modified": "2026-03-20T10:00:00Z",
    "basedOn": "Default",
    "isBuiltIn": false
  },
  "whitespace": { ... },
  "casing": { ... },
  "lists": { ... },
  "parentheses": { ... },
  "dml": { ... },
  "join": { ... },
  "ddl": { ... },
  "controlFlow": { ... },
  "case": { ... },
  "cte": { ... },
  "expressions": { ... },
  "formatActions": { ... }
}
```

## Option Categories

### Category 1: Whitespace (`whitespace`)

| Key | Type | Default | Allowed |
|---|---|---|---|
| `tabStyle` | string | `"spaces"` | `"spaces"`, `"tabs"` |
| `tabSize` | int | `4` | 1–8 |
| `indentStyle` | string | `"block"` | `"block"`, `"hanging"`, `"alignedBlock"` |
| `maxLineWidth` | int | `120` | 80–200, 0 (unlimited) |
| `lineBreakBeforeClause` | bool | `true` | |
| `lineBreakAfterClause` | bool | `false` | |
| `lineBreakBeforeComma` | bool | `false` | |
| `lineBreakAfterComma` | bool | `true` | |
| `emptyLineBetweenStatements` | int | `1` | 0–3 |
| `emptyLineBeforeGO` | bool | `true` | |
| `emptyLineAfterGO` | bool | `true` | |
| `preserveEmptyLines` | bool | `true` | |
| `maxConsecutiveEmptyLines` | int | `2` | 1–5 |
| `trailingWhitespace` | string | `"remove"` | `"remove"`, `"preserve"` |
| `finalNewline` | string | `"ensure"` | `"ensure"`, `"remove"`, `"preserve"` |
| `spaceAfterComma` | bool | `true` | |
| `spaceAroundOperators` | bool | `true` | |
| `spaceAroundBooleanOperators` | bool | `true` | |
| `spaceInsideParentheses` | bool | `false` | |
| `spaceBeforeParentheses` | bool | `false` | |
| `lineBreakAfterSemicolon` | bool | `true` | |

### Category 2: Casing (`casing`)

| Key | Type | Default | Allowed |
|---|---|---|---|
| `reservedKeywords` | string | `"UPPERCASE"` | `"UPPERCASE"`, `"lowercase"`, `"PascalCase"`, `"camelCase"`, `"AsIs"` |
| `builtInFunctions` | string | `"UPPERCASE"` | same |
| `builtInDataTypes` | string | `"lowercase"` | same |
| `systemObjects` | string | `"lowercase"` | same |
| `globalVariables` | string | `"lowercase"` | same |
| `localVariables` | string | `"AsIs"` | same |
| `identifiers` | string | `"AsIs"` | same |
| `syncWithDatabase` | bool | `false` | |
| `camelCaseDictionary` | bool | `true` | |
| `applyOnTyping` | bool | `true` | |

### Category 3: Lists (`lists`)

| Key | Type | Default | Allowed |
|---|---|---|---|
| `commaPosition` | string | `"trailing"` | `"trailing"`, `"leading"` |
| `alignItemsAcrossClauses` | bool | `true` | |
| `alignAliases` | bool | `true` | |
| `oneItemPerLine` | bool | `true` | |
| `collapseShortLists` | bool | `true` | |
| `collapseThreshold` | int | `60` | 30–200 (characters) |
| `indentListItems` | bool | `true` | |
| `alignDataTypesInDDL` | bool | `true` | |
| `alignValuesInInsert` | bool | `true` | |
| `spaceAfterListComma` | bool | `true` | |

### Category 4: Parentheses (`parentheses`)

| Key | Type | Default | Allowed |
|---|---|---|---|
| `openOnSameLine` | bool | `true` | |
| `closeOnNewLine` | string | `"false"` | `"true"`, `"false"`, `"auto"` |
| `collapseShort` | bool | `true` | |
| `collapseThreshold` | int | `40` | 20–120 |
| `indentContents` | bool | `true` | |
| `spaceInside` | bool | `false` | |
| `removeRedundant` | bool | `false` | |
| `createTableColumns` | string | `"newLine"` | `"sameLine"`, `"newLine"` |
| `procedureParameters` | string | `"newLine"` | `"sameLine"`, `"newLine"` |
| `subqueryStyle` | string | `"indent"` | `"indent"`, `"alignWithClause"` |

### Category 5: DML (`dml`)

| Key | Type | Default | Allowed |
|---|---|---|---|
| `selectItemsOnNewLine` | bool | `true` | |
| `selectStarOnSameLine` | bool | `true` | |
| `fromOnNewLine` | bool | `true` | |
| `whereOnNewLine` | bool | `true` | |
| `andOrNewLine` | string | `"before"` | `"before"`, `"after"`, `"sameLine"` |
| `andOrIndent` | string | `"alignWithWhere"` | `"alignWithWhere"`, `"indent"`, `"noIndent"` |
| `groupByOnNewLine` | bool | `true` | |
| `havingOnNewLine` | bool | `true` | |
| `orderByOnNewLine` | bool | `true` | |
| `topOnSameLine` | bool | `true` | |
| `distinctOnSameLine` | bool | `true` | |
| `intoOnNewLine` | bool | `true` | |
| `valuesOnNewLine` | bool | `true` | |
| `setOnNewLine` | bool | `true` | |
| `deleteFromOnSameLine` | bool | `true` | |
| `mergeWhenOnNewLine` | bool | `true` | |
| `collapseShortStatements` | bool | `true` | |
| `collapseThreshold` | int | `80` | 40–200 |
| `collapseShortSubqueries` | bool | `true` | |
| `subqueryCollapseThreshold` | int | `60` | 30–150 |

### Category 6: JOIN (`join`)

| Key | Type | Default | Allowed |
|---|---|---|---|
| `onNewLine` | bool | `true` | |
| `indentJoin` | bool | `false` | |
| `onConditionNewLine` | bool | `true` | |
| `onConditionIndent` | string | `"indent"` | `"indent"`, `"alignWithJoin"`, `"alignWithTable"` |
| `multipleOnConditions` | string | `"newLine"` | `"newLine"`, `"sameLine"` |
| `emptyLineBeforeJoin` | bool | `false` | |
| `alignJoinKeyword` | string | `"right"` | `"right"`, `"left"`, `"indent"` |
| `joinTypeStyle` | string | `"explicit"` | `"explicit"`, `"asIs"` |
| `crossApplyNewLine` | bool | `true` | |

### Category 7: DDL (`ddl`)

| Key | Type | Default | Allowed |
|---|---|---|---|
| `createTableColumnsOnNewLine` | bool | `true` | |
| `alignDataTypes` | bool | `true` | |
| `alignConstraints` | bool | `true` | |
| `constraintsOnNewLine` | bool | `false` | |
| `inlineConstraintStyle` | string | `"sameLine"` | `"sameLine"`, `"newLine"` |
| `tableConstraintsSeparate` | bool | `true` | |
| `firstParameterOnNewLine` | string | `"auto"` | `"always"`, `"never"`, `"auto"` |
| `parameterAlignment` | string | `"aligned"` | `"aligned"`, `"indented"`, `"hanging"` |
| `alignParameterDataTypes` | bool | `true` | |
| `alignParameterDefaults` | bool | `true` | |
| `asOnNewLine` | bool | `true` | |
| `beginOnNewLine` | bool | `true` | |
| `collapseShortDDL` | bool | `true` | |
| `collapseThreshold` | int | `60` | 30–150 |

### Category 8: Control Flow, CASE, CTEs & Expressions

#### `controlFlow`

| Key | Type | Default | Allowed |
|---|---|---|---|
| `beginOnNewLine` | bool | `true` | |
| `endOnNewLine` | bool | `true` | |
| `indentBetweenBeginEnd` | bool | `true` | |
| `collapseShortIfElse` | bool | `true` | |
| `collapseThreshold` | int | `60` | 30–150 |
| `elseOnNewLine` | bool | `true` | |
| `elseAlignWithIf` | bool | `true` | |
| `tryCatchOnNewLine` | bool | `true` | |

#### `case`

| Key | Type | Default | Allowed |
|---|---|---|---|
| `whenOnNewLine` | bool | `true` | |
| `thenOnNewLine` | bool | `false` | |
| `elseOnNewLine` | bool | `true` | |
| `endOnNewLine` | bool | `true` | |
| `indentWhen` | bool | `true` | |
| `alignThen` | bool | `true` | |
| `collapseShortCase` | bool | `true` | |
| `collapseThreshold` | int | `60` | 30–120 |

#### `cte`

| Key | Type | Default | Allowed |
|---|---|---|---|
| `withOnNewLine` | bool | `true` | |
| `cteBodyIndent` | bool | `true` | |
| `commaBeforeCte` | bool | `false` | |
| `emptyLineBetweenCtes` | bool | `true` | |

#### `expressions`

| Key | Type | Default | Allowed |
|---|---|---|---|
| `booleanOperatorNewLine` | string | `"before"` | `"before"`, `"after"`, `"sameLine"` |
| `betweenOnOneLine` | bool | `true` | |
| `inListStyle` | string | `"multiLine"` | `"multiLine"`, `"singleLine"`, `"auto"` |
| `inListThreshold` | int | `60` | 30–200 |
| `existsSubqueryIndent` | string | `"indent"` | `"indent"`, `"alignWithExists"` |

### Format Actions (`formatActions`)

| Key | Type | Default |
|---|---|---|
| `applyLayout` | bool | `true` |
| `applyCasing` | bool | `true` |
| `insertSemicolons` | bool | `false` |
| `expandWildcards` | bool | `false` |
| `qualifyObjectNames` | bool | `false` |
| `addAsKeyword` | bool | `true` |
| `addSquareBrackets` | bool | `false` |

## SQL Prompt Import Mapping

When importing a `.sqlpromptstyle` file, options are mapped using a best-effort strategy:

- Direct 1:1 mappings for common options (keyword casing, indentation, comma position)
- Close approximations for options with slightly different semantics
- Unmapped options default to the Default profile value
- Import summary reports exactly which options were mapped, approximated, or unmapped

The `SqlPromptImporter` maintains a static mapping table of SQL Prompt option names to AKML SQL option paths.

## Schema Migration

When `metadata.schemaVersion` is:
- **Equal to current**: Load normally
- **Less than current**: Apply migrations (rename fields, add defaults for new fields)
- **Greater than current**: Load with best-effort, log warning, preserve unknown fields via `JsonExtensionData`

This ensures profiles created by newer versions are not corrupted when opened in older versions.
