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

## `ai` Section

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `enabled` | bool | false | Master switch for AI assistance features. **Spec 037**: derived on every load — true exactly when at least one agent in `agents` is usable; the value on disk is rewritten by normalisation |
| `provider` | string | "" | Canonical provider id: `anthropic`, `openai`, `azure`, `gemini`, `kimi`, `ollama`, `lmstudio`, `custom`. Legacy spellings (`AzureOpenAI`, `LMStudio`) are normalised on load (spec 036). **Spec 037**: this and the other flat connection fields below are a derived mirror of the active agent — see *Multiple agents* |
| `model` | string | "" | Model identifier (e.g. `gpt-4o`, `claude-sonnet-4-6`, `kimi-latest`) |
| `apiKey` | string | "" | **DPAPI-wrapped at rest** (`dpapi:<base64>`, spec 036 FR-008) — the Options page wraps on save via `ApiKeyProtector` (entropy `AkmlSql-ApiKey-v1`). Legacy plaintext values still read correctly and are upgraded on the next save. The key is never written to logs |
| `endpoint` | string | "" | Service endpoint; required for `azure`, defaulted for `kimi` (`https://api.moonshot.ai/v1`; use `https://api.moonshot.cn/v1` for the mainland-China service) and `ollama` |
| `maxTokens` | int | 4096 | Maximum response tokens |
| `temperature` | double | 0.2 | Sampling temperature |
| `timeout` | int | 30 | Request timeout in seconds; also bounds the Options "Test connection" check |
| `retries` | int | 2 | Provider retry count |
| `schemaContextMaxObjects` | int | 500 | **Spec 036 FR-026** — the explicit schema-context budget. Every schema-aware AI request receives the full object inventory at name level up to this count; prompt-named objects are promoted to full detail additionally. When a database exceeds the budget the context is marked truncated and both the model and the user are told so |
| `privacyMode` | string | "schemaOnly" | `schemaOnly` (metadata only) / `full` (includes query text) / `anonymous` (identifiers hashed — the assistant cannot see real object names; the chat panel says so and names this setting) / `offline` / `disabled` |
| `offlineProvider` / `offlineModel` / `offlineEndpoint` | string | "" | Optional offline fallback profile |
| `privacyConsentRequired` | bool | true | When true, cloud providers require the in-product consent before use |

Schema information sent to a provider never includes table data rows — metadata only
(FR-032). The chat panel and all AI commands bind to the active editor's connection; when no
editor is connected the assistant says so instead of answering from an empty schema (FR-028).

### Multiple agents (spec 037)

`ai.agents` is the truth for which provider serves AI requests. The flat
`provider`/`model`/`apiKey`/`endpoint`/`maxTokens`/`temperature`/`timeout`/`retries` above are a
**derived mirror of the active agent**, rewritten by `AiAgentResolver.MirrorActiveAgent` on every
`ConfigManager.Load` (inside `Normalize`) and every `ConfigManager.Save`. The mirror keeps the
file downgrade-safe: an older build reads the flat fields and ignores `ai.agents`, so it uses the
same provider the new build would. When `agents` is empty the flat fields are the whole
configuration and are left untouched — that is the pre-migration shape the V14 migration rescues
on the next load.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `agents` | array | `[]` | 0–20 agent objects (V12); list order is display order |
| `activeAgentId` | string | "" | `id` of the active agent. A dangling id is repaired on load to the first usable agent, or "" when there is none (V13) |
| `featureAgents` | object | `{}` | Which agent serves each feature: `chat`, `textToSql`, `explain`, `fix`, `optimize`, `indexSuggestions`, `ghostText` — each an agent `id`, or `""` = follow the active agent. An assignment naming a missing or disabled agent is cleared on load (V16) |
| `fallbackOrder` | array | `[]` | Agent `id`s tried in order when the selected agent fails; the offline provider (`offlineProvider` above) is still tried last. Dangling, duplicate and self references are pruned on load (V17) |

Each `agents[]` entry:

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `id` | string | (generated) | 32 lowercase hex (`Guid.NewGuid().ToString("N")`), assigned at creation and immutable. Every reference — `activeAgentId`, `featureAgents.*`, `fallbackOrder[]` — is by id, never by name |
| `name` | string | — | 1–40 chars after trim, unique case/trim-insensitively across the list |
| `provider` | string | — | Canonical provider id, same vocabulary as the flat `provider` above |
| `model` | string | — | Model identifier (free text) |
| `apiKey` | string | "" | Same `dpapi:` wrapping as the flat key. Migration copies the flat value **verbatim** — never unwrapped, never re-wrapped |
| `endpoint` | string | "" | Absolute URL; required for `azure` and `custom` |
| `maxTokens` | int | 4096 | 256–32768 |
| `temperature` | double | 0.2 | 0.0–2.0 |
| `timeout` | int | 30 | 5–300 seconds |
| `retries` | int | 2 | 0–5 |
| `enabled` | bool | true | Disabled agents are hidden from the chat picker and treated as absent by assignments and the fallback order |
| `createdUtc` | string | — | ISO 8601 UTC creation time; informational only |
| `health` | object? | null | Last recorded health check; `null` = never tested. Advisory only — it never gates a request |

`health` carries `status` (`unknown` / `ready` / `needsKey` / `failed` — an unrecognised value
loads as `unknown`, V20), `checkedUtc`, `latencyMs` (round trip of the last **successful** check)
and `message` (≤ 500 chars, never contains a key).

Load-time migration and repairs all live in `AiAgentResolver.Normalize`, are idempotent, and never
throw: **V14** turns an empty `agents` list with a non-empty flat `provider` into exactly one agent
named for the provider's display name, enabled, never health-tested, and active, copying the key
verbatim; **V15** drops malformed entries (empty or duplicate id) with a log warning naming the
index; **V21** drops entries beyond the 20th. An agent counts as *usable* when it is enabled,
names a canonical provider, has a model, and carries every field its provider requires (key for
the cloud providers, endpoint for `azure`/`custom`).

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

## Site settings (spec 038)

The product site (`src/AkmlSql.Site`) has its own owner-editable settings, stored in a
`site_settings` table inside the **same** `analytics.db` the metrics use — not in `appsettings.json`.
Writing to the deployed config file would restart the application on every settings change, turning
a one-second toggle into a cold start.

Edit them at **`/admin/settings`**. Changes take effect on the next public request; no deploy, no
restart.

| Key | Values | Default | Effect |
|---|---|---|---|
| `release_visibility` | `LatestOnly`, `LatestN`, `All` | `LatestN` | How much release history `/download` advertises |
| `release_visibility_count` | 1–50 | `3` | Number shown when the mode is `LatestN` |
| `identifiable_retention_days` | 1–3650 | `365` | Days before `ip` and `visitor_id` are erased in place |

Out-of-range values are **rejected with a message naming the bound, never silently clamped** — a
clamp leaves the owner believing they saved something they did not.

If the settings table cannot be read at all, the site serves the documented defaults and reports the
failure in the logs, on `/health` (`settingsLoaded: false`) and in the portal. It is never fatal: the
download page must not break for a reason unrelated to releases.

**Visibility governs advertising, not reachability.** A release hidden from the page is still
downloadable by a link published earlier, and still counted. `/dl/{file}` has no reference to the
settings store at all, which is asserted structurally by a test.

### Visitor data and consent

After spec 038 the site stores, **only for visitors who explicitly accept**:

- the **full client IP address**, and
- a persistent first-party identifier in the `akml.vid` cookie.

For everyone else — declined, or not yet answered — both columns stay NULL and only the per-day
salted hash, the truncated network prefix (/24 or /48) and the derived country are written, exactly
as before. The gate is enforced in `AnalyticsStore`, not at the call site, so no caller can bypass it.

| Cookie | Purpose | Lifetime | Flags |
|---|---|---|---|
| `akml.consent` | Remembers the choice; set whichever way the visitor answers | 365 days | HttpOnly, Secure, SameSite=Lax |
| `akml.vid` | Persistent individual id; **only** set on acceptance | 365 days | HttpOnly, Secure, SameSite=Lax |
| `akml.admin` | Owner's portal sign-in session; never set for visitors | 8 hours | HttpOnly, Secure, SameSite=Lax |

The consent request is shown to **every** visitor with no recorded choice, regardless of country, and
is **non-blocking** — a visitor can ignore it and complete the whole download path. Silence leaves
the choice unresolved and is never treated as consent; an explicit dismissal records a refusal.

Two retention boundaries apply:

- `identifiable_retention_days` (site setting, default 365) — `ip` and `visitor_id` are nulled **in
  place**, keeping the row, so country and version totals for old periods still reconcile.
- `Analytics:RetentionDays` (`appsettings.json`, default 400) — whole rows deleted.

Both run in the post-start maintenance service, never inline at startup.

### Geo database

Country is resolved **at write time** from `GeoLite2-Country.mmdb`. Without that file every country
reads "Unknown", and installing it later **cannot backfill** existing rows. Obtain a MaxMind licence
key and run `scripts/update-geoip.ps1`.
