# AKML SQL — Configuration Guide

The configuration file is stored at:

```
%AppData%\AKML SQL\config.json
```

It is created automatically on first run with all defaults. The file is written atomically (temp-file + rename) to prevent corruption.

---

## Full Schema

```jsonc
{
  "configVersion": 1,
  "autoUpdateEnabled": true,
  "telemetryEnabled": false,
  "logMinimumLevel": "Debug",
  "lastUpdateCheck": null,
  "installId": "00000000-0000-0000-0000-000000000000",
  "nativeIntelliSensePrompted": false,
  "disabledNativeIntelliSense": false,
  "installedTargets": [],

  "intelliSense": {
    "enabled": true,
    "autoTrigger": true,
    "triggerDelayMs": 100,
    "afterDot": true,
    "maxSuggestions": 50,
    "fuzzyMatch": true,
    "showDataTypes": true,
    "showNullability": true,
    "showPkFk": true,
    "autoAlias": true,
    "joinAssist": true,
    "keywordCase": "Upper",
    "disableNativeIntelliSense": true
  },

  "cache": {
    "autoRefresh": true,
    "refreshIntervalSeconds": 300,
    "detectDdl": true,
    "maxDatabases": 10,
    "lazyLoadColumns": true,
    "persistToDisk": true,
    "persistPath": ""
  },

  "formatter": {
    "enabled": true,
    "activeProfile": "Khamis Style",
    "formatOnPaste": false,
    "formatOnSave": false,
    "formatOnDelimiter": false,
    "shortcutKey": "Ctrl+K, Y",
    "showProfileInStatusBar": true,
    "confirmBulkFormat": true,
    "createBackups": true,
    "respectNoformat": true,
    "handleParseErrors": true,
    "semanticValidation": true
  },

  "snippets": {
    "enabled": true,
    "showInCompletion": true,
    "triggerKey": "Tab",
    "formatOnExpand": true,
    "personalFolder": "",
    "teamFolder": "",
    "contextFilter": true,
    "surroundShortcut": "Ctrl+K, Ctrl+S",
    "trackUsage": true
  },

  "codeAnalysis": {
    "enabled": true,
    "runOnType": true,
    "runOnSave": true,
    "autoFixOnFormat": false,
    "squiggleStyle": "underline",
    "showInErrorList": true
  },

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

## Top-Level Settings

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `configVersion` | int | 1 | Schema version (for future migrations) |
| `autoUpdateEnabled` | bool | true | Automatically check for updates on startup |
| `telemetryEnabled` | bool | false | Reserved for future telemetry opt-in |
| `logMinimumLevel` | string | "Debug" | Serilog minimum level: `Verbose`, `Debug`, `Information`, `Warning`, `Error`, `Fatal` |
| `lastUpdateCheck` | string? | null | ISO 8601 timestamp of the last update check |
| `installId` | string | (GUID) | Anonymous installation identifier |
| `nativeIntelliSensePrompted` | bool | false | Whether the native IntelliSense conflict dialog was shown |
| `disabledNativeIntelliSense` | bool | false | Whether AKML SQL disabled the native SSMS IntelliSense via registry |

---

## `intelliSense` Section

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `enabled` | bool | true | Master switch for all IntelliSense features |
| `autoTrigger` | bool | true | Show completion list automatically while typing |
| `triggerDelayMs` | int | 100 | Debounce delay before triggering auto-completion |
| `afterDot` | bool | true | Auto-trigger after typing `.` (table.column completion) |
| `maxSuggestions` | int | 50 | Maximum items in the completion list |
| `fuzzyMatch` | bool | true | Enable fuzzy/substring matching (not just prefix) |
| `showDataTypes` | bool | true | Show column data types in completion details |
| `showNullability` | bool | true | Show NOT NULL / NULL in column details |
| `showPkFk` | bool | true | Show PK/FK indicators in column details |
| `autoAlias` | bool | true | Suggest automatic table aliases |
| `joinAssist` | bool | true | Suggest JOIN conditions based on FK relationships |
| `keywordCase` | string | "Upper" | Keyword casing in completions: `Upper`, `Lower`, `PascalCase`, `AsIs` |
| `disableNativeIntelliSense` | bool | true | Whether to disable SSMS native IntelliSense to avoid conflicts |

---

## `cache` Section

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `autoRefresh` | bool | true | Periodically check for schema changes |
| `refreshIntervalSeconds` | int | 300 | How often to check (used by shell; engine uses 60s for periodic refresh) |
| `detectDdl` | bool | true | Trigger cache refresh when DDL (CREATE/ALTER/DROP) is executed |
| `maxDatabases` | int | 10 | Maximum number of databases to keep in memory; LRU eviction applies |
| `lazyLoadColumns` | bool | true | Load columns/FKs in Phase B (background) rather than blocking Phase A |
| `persistToDisk` | bool | true | Persist schema cache to disk across sessions |
| `persistPath` | string | "" | Override cache directory; empty = `%LocalAppData%\AKML SQL\cache` |

---

## `formatter` Section

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `enabled` | bool | true | Master switch for the formatter |
| `activeProfile` | string | "Khamis Style" | Name of the active formatting profile |
| `formatOnPaste` | bool | false | Auto-format SQL when pasting into the editor |
| `formatOnSave` | bool | false | Auto-format when saving a file |
| `formatOnDelimiter` | bool | false | Auto-format when typing `;` or `GO` |
| `shortcutKey` | string | "Ctrl+K, Y" | Keyboard shortcut for Format Document |
| `showProfileInStatusBar` | bool | true | Show active profile name in the VS status bar |
| `confirmBulkFormat` | bool | true | Ask for confirmation before bulk-formatting multiple files |
| `createBackups` | bool | true | Create `.bak` backup files before bulk format |
| `respectNoformat` | bool | true | Honor `-- noformat` / `-- endnoformat` region comments |
| `handleParseErrors` | bool | true | Skip files with parse errors in bulk format instead of aborting |
| `semanticValidation` | bool | true | Run semantic round-trip validation after formatting |

---

## `snippets` Section

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `enabled` | bool | true | Master switch for snippet features |
| `showInCompletion` | bool | true | Include snippets in the IntelliSense completion list |
| `triggerKey` | string | "Tab" | Key that expands a typed shortcode |
| `formatOnExpand` | bool | true | Format the expanded snippet body |
| `personalFolder` | string | "" | Override path for personal snippets; empty = `%AppData%\AKML SQL\snippets\personal` |
| `teamFolder` | string | "" | Optional path for shared team snippets |
| `contextFilter` | bool | true | Only show snippets appropriate for the current SQL clause |
| `surroundShortcut` | string | "Ctrl+K, Ctrl+S" | Shortcut to show surround-with snippet picker |
| `trackUsage` | bool | true | Track snippet usage counts |

---

## `codeAnalysis` Section

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `enabled` | bool | true | Master switch for static code analysis |
| `runOnType` | bool | true | Analyze after each keystroke (debounced) |
| `runOnSave` | bool | true | Analyze when saving a file |
| `autoFixOnFormat` | bool | false | Apply auto-fix actions when running Format Document |
| `squiggleStyle` | string | "underline" | Squiggle rendering style (`underline`, `dotted`, `solid`) |
| `showInErrorList` | bool | true | Show analysis issues in the VS Error List window |

---

## `refactoring` Section

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `previewBeforeApply` | bool | true | Show a diff preview before applying any refactoring |
| `createBackups` | bool | true | Create backup files before applying file-level refactoring |
| `formatAfterRefactor` | bool | true | Run the formatter on modified text after applying refactoring |
| `renameScope` | string | "currentScript" | Scope for Safe Rename: `currentScript` or `projectDirectory` |
| `includeCommentsInRename` | bool | true | Update object names found inside SQL comments |
| `includeStringLiteralsInRename` | bool | false | Update object names found inside string literals |

---

## Per-Project Settings (`.casettings`)

Individual rule overrides can be placed in a `.casettings` JSON file anywhere in the project directory hierarchy. The engine searches from the current file's directory upward.

```jsonc
{
  "rules": {
    "PE001": { "severity": "Warning", "enabled": true },
    "SE001": { "severity": "Error",   "enabled": true },
    "ST001": { "enabled": false }
  },
  "globalSuppressions": [
    { "ruleId": "NM002", "reason": "Legacy naming convention" }
  ]
}
```

### Rule severity values
`"None"` | `"Info"` | `"Warning"` | `"Error"`

### Inline suppressions

```sql
-- akml-disable PE001
SELECT * FROM dbo.Orders   -- suppressed
-- akml-enable PE001
```

Or single-line:
```sql
SELECT * FROM dbo.Orders  -- akml-disable-line PE001
```

Omit the `-- akml-enable` and the suppression runs to the end of the file, which is how the
"Disable … in this script" quick fix works. Rule ids are comma-separated (`-- akml-disable PE001,
BP004`); omitting them entirely suppresses every rule; trailing text is treated as a note. The
directives are case-insensitive, work inside `/* … */`, and the original `-- noqa:` /
`-- noqa-begin` / `-- noqa-end` forms are still honoured.

### Session-only suppressions

The quick-fix menu also offers **Disable … for this session**, which is held in the engine process
and written nowhere. It ends when the IDE closes, or from the **Restore** button in
**Manage Code Analysis Rules**. Because it is not persisted there is no `config.json` key for it.

---

## Persistence Markers (Spec 020)

Spec 020 (SQL Prompt visual parity) introduced two state files alongside `config.json`:

| File | Purpose |
|---|---|
| `%AppData%/AKML SQL/themeMigration.v1.json` | First-launch marker written by `ThemeMigrationManager` (FR-030). Records `migratedAt` timestamp, whether `legacyColorOverrides` were detected in `config.json`, and the migration schema version. Idempotent — presence of the file short-circuits future runs. |
| `%AppData%/AKML SQL/editor/preview-sample.sql` | User-pasted custom sample SQL for the Format Styles editor's live preview pane (T069). Atomic temp-file + rename writes. If absent, the editor falls back to its built-in `DefaultSampleSql` constant. |

Both files are written defensively — failures are caught and logged at Debug level; they never block extension startup or editor interaction.

## Formatting Profiles (`.akmlstyle`)

Profiles are stored in:
```
%AppData%\AKML SQL\profiles\{name}.akmlstyle
```

Built-in profiles (read-only) are embedded in the extension. The profile format is a JSON file with a `metadata` block plus formatting option sections. See [formatting.md](formatting.md) for the full profile schema.

---

## Log Configuration

Logs are written to:
```
%AppData%\AKML SQL\logs\akmlsql-YYYYMMDD.log
```

- Rolling interval: daily
- Max file size: 5 MB (rolls on size limit)
- Retained files: 10
- Format: `{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}`

To change the log level without editing JSON directly, set `logMinimumLevel` in `config.json`:
```json
{ "logMinimumLevel": "Information" }
```
