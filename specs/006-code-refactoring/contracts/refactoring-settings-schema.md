# Refactoring Settings Schema

**Branch**: `006-code-refactoring` | **Date**: 2026-03-23

Refactoring settings are stored as a `refactoring` key in the existing `%AppData%/AKML SQL/config.json` file, following the same pattern as `codeAnalysis` settings added in Phase 5.

---

## Schema

```json
{
  "refactoring": {
    "previewBeforeApply": true,
    "createBackups": true,
    "formatAfterRefactor": true,
    "renameScope": "currentScript",
    "includeCommentsInRename": true,
    "includeStringLiteralsInRename": false
  }
}
```

---

## Field Reference

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `previewBeforeApply` | boolean | `true` | Show the preview dialog for all heavyweight operations before applying |
| `createBackups` | boolean | `true` | Write a `.refactor-backup` copy of each file before cross-file modification |
| `formatAfterRefactor` | boolean | `true` | Run the active formatter profile on the current document after each refactoring |
| `renameScope` | string | `"currentScript"` | Default scope for Safe Rename: `"currentScript"` or `"projectDirectory"` |
| `includeCommentsInRename` | boolean | `true` | Include `-- comment` text in identifier search during rename |
| `includeStringLiteralsInRename` | boolean | `false` | Include string literal content in identifier search (risky — off by default) |

---

## Validation Rules

- `renameScope` MUST be one of: `"currentScript"`, `"projectDirectory"`. Other values are ignored and the default (`"currentScript"`) is used.
- All boolean fields default to their specified default when absent or null.
- The entire `refactoring` section is optional; all defaults apply when absent.

---

## Full config.json example with refactoring section

```json
{
  "autoUpdateEnabled": true,
  "telemetryEnabled": true,
  "intelliSense": { ... },
  "cache": { ... },
  "formatter": { ... },
  "snippets": { ... },
  "codeAnalysis": { ... },
  "refactoring": {
    "previewBeforeApply": true,
    "createBackups": true,
    "formatAfterRefactor": true,
    "renameScope": "currentScript",
    "includeCommentsInRename": true,
    "includeStringLiteralsInRename": false
  }
}
```
