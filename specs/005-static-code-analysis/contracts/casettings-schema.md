# Contract: CAsettings File Format

**Branch**: `005-static-code-analysis` | **Date**: 2026-03-22

---

## File Name and Discovery

- File name: `.casettings` (no extension) or `akml.casettings.json`
- Discovery: Walk up from the open SQL file's directory to the drive root; first file found wins
- Global default: stored inside `%AppData%/AKML SQL/config.json` under the `codeAnalysis` key

---

## JSON Schema

```json
{
  "metadata": {
    "name": "Team Standard",
    "version": "1.0",
    "description": "Optional free-text description"
  },
  "rules": {
    "PE001": { "enabled": true,  "severity": "warning" },
    "PE008": { "enabled": false, "severity": "ignore"  },
    "BP004": { "enabled": true,  "severity": "error"   },
    "NM006": { "enabled": false, "severity": "ignore"  }
  },
  "globalSuppressions": [
    {
      "rule": "PE007",
      "reason": "Cursor usage is intentional in ETL procedures"
    }
  ]
}
```

---

## Field Definitions

### `metadata` *(optional)*

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | No | Human label for this settings set |
| `version` | string | No | Semver string; informational only |
| `description` | string | No | Free-text |

### `rules` *(optional)*

Dictionary keyed by rule ID (e.g. `"PE001"`). Only rules listed here override defaults; unlisted rules use their built-in defaults.

| Field | Type | Values | Description |
|-------|------|--------|-------------|
| `enabled` | bool | `true` / `false` | Whether the rule fires |
| `severity` | string | `"error"`, `"warning"`, `"information"`, `"hint"`, `"ignore"` | Effective severity; `"ignore"` is equivalent to `enabled: false` |

### `globalSuppressions` *(optional)*

Array of project-wide suppressions. These suppress a rule for the entire project without needing inline `-- noqa` comments.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `rule` | string | Yes | Rule ID to suppress, e.g. `"PE007"` |
| `reason` | string | Yes | Mandatory documentation string |

---

## Merge Precedence (lowest → highest priority)

1. Built-in rule defaults
2. Global settings (`config.json` → `codeAnalysis.rules`)
3. Project `.casettings` file (nearest ancestor directory)
4. Inline `-- noqa: RULEID` comments (per-line)
5. Inline `-- noqa-begin` / `-- noqa-end` blocks

---

## Inline Suppression Syntax

```sql
-- noqa: PE001                         -- suppress PE001 for the NEXT line only
SELECT * FROM dbo.Orders;

-- noqa: PE001, ST004                  -- suppress multiple rules for NEXT line
SELECT * FROM dbo.Orders

-- noqa                                -- suppress ALL rules for NEXT line
SELECT * FROM dbo.Orders

-- noqa-begin                          -- suppress ALL rules for this block
SELECT * FROM dbo.Orders
SELECT * FROM dbo.Customers
-- noqa-end
```

**Rules**:
- `-- noqa` comments are case-insensitive
- Whitespace around rule IDs and commas is ignored
- Unknown rule IDs in `-- noqa` generate an `Information` diagnostic themselves
- `-- noqa-begin` without a matching `-- noqa-end` suppresses to end-of-file (with a warning)

---

## SQL Prompt CAsettings Import

The importer reads SQL Prompt's XML format (`<CASetting>` elements) and produces an AKML JSON file.

**Mapped rules** (1:1 by ID):

| SQL Prompt Prefix | AKML Prefix | Notes |
|-------------------|-------------|-------|
| `BP001–BP018` | `BP001–BP018` | Direct map |
| `PE001–PE013` | `PE001–PE013` | Direct map |
| `DEP001–DEP010` | `DEP001–DEP010` | Direct map |
| `ST001–ST011` | `ST001–ST011` | Direct map |
| `EX001–EX006` | `EX001–EX006` | Direct map |

**Unmapped rules**: logged to output as "skipped — no AKML equivalent" and excluded from output.
