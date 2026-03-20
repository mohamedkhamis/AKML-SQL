# Import Mapping Contract

**Version**: 1.0 | **Branch**: `004-snippet-manager`

## Supported Import Formats

| Format | Extension | Detection | Parser |
|---|---|---|---|
| SQL Prompt XML | `.sqlpromptsnippet` | `<?xml` or `<CodeSnippets` | SqlPromptXmlImporter |
| SQL Prompt JSON | `.json` (in SQL Prompt folder) | Content starts with `{` | SqlPromptJsonImporter |
| SSMS Native | `.snippet` | VS CodeSnippet XML schema | SsmsSnippetImporter |
| AKML SQL | `.akmlsnippet` | Direct load | No conversion needed |

## SQL Prompt XML → AKML SQL Mapping

### Metadata

| Source XML Path | AKML SQL Field | Mapping |
|---|---|---|
| `Header/Title` | `metadata.name` | Direct |
| `Header/Shortcut` | `metadata.shortcode` | Direct. If empty, derive from filename |
| `Header/Description` | `metadata.description` | Direct |
| `Header/Author` | `metadata.author` | Direct. Default "Imported" if empty |
| (none) | `metadata.id` | Generate new UUID |
| (none) | `metadata.version` | `"1.0"` |
| (none) | `metadata.created` | Import timestamp |
| (none) | `metadata.modified` | Import timestamp |
| `Header/SnippetTypes/SnippetType` | `metadata.surroundsWith` | `true` if `SurroundsWith`, else `false` |
| (none) | `metadata.category` | Auto-detect from keywords, default `"Custom"` |
| (none) | `metadata.tags` | Extract from body keywords or empty |
| (none) | `metadata.context` | `["global"]` |

### Variables

| Source XML Path | AKML SQL Field | Mapping |
|---|---|---|
| `Snippet/Declarations/Literal/ID` | `variables[].name` | Direct |
| `Snippet/Declarations/Literal/Default` | `variables[].default` | Direct |
| `Snippet/Declarations/Literal/ToolTip` | `variables[].tooltip` | Direct |
| (none) | `variables[].schemaAware` | Not set (schema-awareness is AKML-specific) |

### Body

| Source | AKML SQL | Mapping |
|---|---|---|
| `Snippet/Code` CDATA content | `body` (array of lines) | Split on newlines |

### Variable Renaming

| SQL Prompt Variable | AKML SQL Variable | Action |
|---|---|---|
| `$CURSOR$` | `$CURSOR$` | No change |
| `$SELECTEDTEXT$` | `$SELECTEDTEXT$` | No change |
| `$DATE$` | `$DATE$` | No change (strip format string if present: `$DATE(format)$` → `$DATE$`) |
| `$TIME$` | `$TIME$` | No change (strip format string) |
| `$USER$` | `$USER$` | No change |
| `$DBNAME$` | `$DATABASE$` | **Rename** |
| `$PASTE$` | `$CLIPBOARD$` | **Rename** |
| `$GUID$` | `$GUID$` | No change |
| `$MACHINE$` | `$MACHINE$` | No change |
| `$SELECTIONSTART$` | (dropped) | SQL Prompt-specific, rarely used |
| `$SELECTIONEND$` | (dropped) | SQL Prompt-specific, rarely used |
| Custom `$name$` | Custom `$name$` | Preserve name |

## SQL Prompt JSON → AKML SQL Mapping

Same variable renaming as XML. Additional mappings:

| Source JSON Field | AKML SQL Field | Mapping |
|---|---|---|
| `name` | `metadata.name` | Direct |
| `prefix` | `metadata.shortcode` | Direct |
| `description` | `metadata.description` | Direct |
| `body` (string with `\n`) | `body` (array of lines) | Split on `\n` |
| `placeholders[].name` | `variables[].name` | Direct |
| `placeholders[].defaultValue` | `variables[].default` | Direct |

## SSMS Native → AKML SQL Mapping

Same metadata mapping as SQL Prompt XML (shared schema). Variable renaming differs:

| SSMS Variable | AKML SQL Variable | Action |
|---|---|---|
| `$end$` | `$CURSOR$` | **Rename** |
| `$selected$` | `$SELECTEDTEXT$` | **Rename** |
| Custom `$LiteralID$` | Custom `$LiteralID$` | Preserve |

**Note**: SSMS native snippets often have no shortcode. The importer derives one from the filename or prompts the user.

## Auto-Detection of SQL Prompt Folder

The importer checks these paths in order:
1. `%LocalAppData%\Red Gate\SQL Prompt 11\Snippets\`
2. `%LocalAppData%\Red Gate\SQL Prompt 10\Snippets\`
3. `%LocalAppData%\Red Gate\SQL Prompt *\Snippets\` (glob for any version)

If found, offer one-click migration of all `.sqlpromptsnippet` and `.json` files.

## Import Report

After import, display:
- Total files processed
- Successfully imported count
- Failed count with reasons (parse error, duplicate shortcode, etc.)
- Suggestion: "Open Snippet Manager to add schema-aware placeholders"
