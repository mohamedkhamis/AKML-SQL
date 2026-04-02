# Formatter Pipeline Contract

**Version**: 1.0 | **Branch**: `003-sql-formatter`

## Overview

The formatter operates as a deterministic 6-stage pipeline. Each stage has a defined input, output, and responsibility. Stages are executed sequentially; there is no parallelism within a single format operation.

## Pre-Processing (before pipeline)

### Stage 0a: Noformat Region Scan

**Input**: Raw SQL text (full document)
**Output**: `List<NoformatRegion>` — sorted, non-overlapping offset ranges

Scans the raw token stream for `--noformat` / `--endnoformat` (line comments) and `/* noformat */` / `/* endnoformat */` (block comments). Case-insensitive matching. Runs before batch splitting.

Rules:
- Unmatched open tag → region extends to EOF
- Nested tags → first open to last close = single region
- Regions must not overlap after merging

### Stage 0b: SQLCMD Preprocessing

**Input**: Raw SQL text + noformat regions
**Output**: Cleaned SQL text + `Dictionary<int, SqlcmdDirective>` restoration map

Only processes text outside noformat regions:
- Line directives (`:setvar`, `:connect`, `:r`, etc.) → replaced with `--__SQLCMD_LINE_{N}__`
- Inline variables (`$(VarName)`) → replaced with `__SQLCMD_VAR_{N}__`

## Pipeline Stages

### Stage 1: Parse

**Input**: Cleaned SQL text
**Output**: `TSqlScript` AST + `IList<ParseError>` + `IList<TSqlParserToken>` (ScriptTokenStream)

Uses `TSqlParser.Create(SqlVersion.Sql170, initialQuotedIdentifiers: true)` by default. Parser version can be overridden based on connection's server version.

Error handling:
- Zero errors → proceed normally
- Errors present → identify successfully parsed fragments by `StartOffset`/`FragmentLength`; unparsed regions preserved verbatim

### Stage 2: Annotate

**Input**: AST + ScriptTokenStream + NoformatRegions
**Output**: Comment attachment map + per-token noformat flags

1. **Comment attachment**: Scan token stream, classify each comment token:
   - `Trailing`: same line as preceding semantic token
   - `Leading`: own line(s) before next semantic token
   - `Standalone`: multiple comment lines with no adjacent semantic token
2. **Noformat flags**: Mark each token that falls within a noformat region

### Stage 3: Layout

**Input**: Annotated AST + FormattingProfile (options)
**Output**: `List<LayoutNode>` — the layout tree

Walk the AST using a custom visitor. For each AST node, apply the relevant formatting rules based on the active profile's options. Decisions made per-token:
- **IndentLevel**: computed from AST depth and clause nesting
- **PrecedingBreak**: NewLine, EmptyLine, or None — determined by option rules
- **PrecedingSpaces**: space count before this token on the same line
- Noformat tokens: flagged, emit original text verbatim

Rule dispatch by AST node type:

| AST Node Type | Rule Set | Key Options |
|---|---|---|
| `SelectStatement` | DmlRules | `selectItemsOnNewLine`, `fromOnNewLine`, `whereOnNewLine`, `topOnSameLine`, `distinctOnSameLine`, `collapseShortStatements` |
| `InsertStatement` | DmlRules | `intoOnNewLine`, `valuesOnNewLine` |
| `UpdateStatement` | DmlRules | `setOnNewLine` |
| `DeleteStatement` | DmlRules | `deleteFromOnSameLine` |
| `MergeStatement` | DmlRules | `mergeWhenOnNewLine` |
| `QualifiedJoin` | JoinRules | `onNewLine`, `indentJoin`, `onConditionNewLine`, `alignJoinKeyword`, `joinTypeStyle` |
| `CreateTableStatement` | DdlRules | `createTableColumnsOnNewLine`, `alignDataTypes`, `alignConstraints` |
| `CreateProcedureStatement` | DdlRules | `firstParameterOnNewLine`, `parameterAlignment`, `asOnNewLine`, `beginOnNewLine` |
| `IfStatement` | ControlFlowRules | `beginOnNewLine`, `elseOnNewLine`, `elseAlignWithIf`, `collapseShortIfElse` |
| `CaseExpression` | ControlFlowRules | `whenOnNewLine`, `thenOnNewLine`, `endOnNewLine`, `indentWhen`, `alignThen`, `collapseShortCase` |
| `CommonTableExpression` | ControlFlowRules | `withOnNewLine`, `cteBodyIndent`, `commaBeforeCte`, `emptyLineBetweenCtes` |
| `BooleanExpression` | ExpressionRules | `booleanOperatorNewLine`, `betweenOnOneLine`, `inListStyle` |
| `FunctionCall` | ParenthesisRules | `openOnSameLine`, `closeOnNewLine`, `collapseShort`, `spaceInside` |

**Collapse evaluation**: Short constructs (below threshold) are collapsed to fewer lines. The `CollapseEvaluator` measures the formatted length of a node's subtree and collapses if below the category-specific threshold.

### Stage 4: Casing

**Input**: Layout tree + FormattingProfile (casing options) + DatabaseCache (optional)
**Output**: Layout tree with `FormattedText` updated

Apply casing rules to each token based on its type:

| Token Category | Option Key | Default |
|---|---|---|
| Reserved keywords | `casing.reservedKeywords` | UPPERCASE |
| Built-in functions | `casing.builtInFunctions` | UPPERCASE |
| Data types | `casing.builtInDataTypes` | lowercase |
| System objects | `casing.systemObjects` | lowercase |
| Global variables | `casing.globalVariables` | lowercase |
| Local variables | `casing.localVariables` | AsIs |
| Identifiers | `casing.identifiers` | AsIs |

If `casing.syncWithDatabase = true` and a DatabaseCache is available, identifier casing is looked up from the cache. Cache miss → fall back to `casing.identifiers` rule.

If `casing.camelCaseDictionary = true`, compound identifiers are split using word boundary detection (e.g., `customerorderid` → `CustomerOrderId`).

### Stage 5: Emit

**Input**: Layout tree (with formatting decisions and cased text)
**Output**: Formatted SQL string

Serialize the layout tree to a flat string:
1. For each `LayoutNode` in order:
   - If `IsInNoformatRegion`: emit `OriginalText`
   - Else: emit `PrecedingBreak` (newline + indent spaces) or `PrecedingSpaces`, then `FormattedText`
   - If `TrailingComment` attached: emit space + comment text
2. Handle inter-node comments (leading comments for the next node)
3. Final cleanup: trailing whitespace removal, final newline per profile

### Stage 6: Validate

**Input**: Original SQL text + Formatted SQL text
**Output**: `bool` (pass/fail) + optional diagnostics

1. Parse both original and formatted text with the same parser version
2. Generate normalized script from both ASTs using `SqlScriptGenerator` with default options
3. Compare normalized outputs — must be identical
4. If validation fails: return original text unchanged + warning diagnostic

This stage can be disabled via `formatter.semanticValidation = false` for performance.

## Post-Processing

### SQLCMD Restoration

**Input**: Formatted text + SQLCMD restoration map
**Output**: Final formatted text with SQLCMD directives restored

Replace sentinel comments/identifiers with original SQLCMD directive text.

## Error Handling

| Error | Behavior |
|---|---|
| Parse error (partial) | Format valid fragments, preserve invalid regions verbatim |
| Parse error (complete failure) | Return original text unchanged |
| Semantic validation failure | Return original text unchanged + warning |
| Profile load error | Fall back to Default built-in profile |
| Engine crash | Shell preserves original document text (no change applied) |
| Noformat region detection error | Conservative: treat entire region as noformat |

## Performance Budget

| Stage | Budget (1K lines) | Budget (10K lines) |
|---|---|---|
| Pre-processing (noformat + SQLCMD) | <5ms | <20ms |
| Stage 1: Parse | <30ms | <100ms |
| Stage 2: Annotate | <5ms | <20ms |
| Stage 3: Layout | <50ms | <150ms |
| Stage 4: Casing | <10ms | <30ms |
| Stage 5: Emit | <10ms | <30ms |
| Stage 6: Validate | <30ms | <100ms |
| Post-processing (SQLCMD restore) | <2ms | <10ms |
| **Total** | **<142ms** | **<460ms** |
