# Contract: `releases.json` — Download Feed

Checked-in static asset at `wwwroot/releases.json`, updated by the release process at release time (research.md R5). Rendered server-side by the download page; may later double as the `updates.akmlsql.com/manifest.json` feed (field names align with `src/AkmlSql.Core/Update/UpdateManifest.cs`).

## Schema (camelCase JSON)

```json
{
  "product": "AKML SQL",
  "generatedAt": "2026-08-27T00:00:00Z",
  "releases": [
    {
      "version": "1.26.0827.1830",
      "releasedAt": "2026-08-27",
      "supportedHosts": ["SSMS 22", "VS 2026"],
      "downloadUrl": "https://akmlsql.com/downloads/AKMLSQLSetup-1.26.0827.1830.exe",
      "sha256Hash": "<64 hex chars>",
      "releaseNotesUrl": "https://github.com/mohamedkhamis/AKML-SQL/releases/tag/v1.26.0827.1830",
      "notesSummary": "Format Styles window, autocomplete corpus gate at 97.5%.",
      "minimumOsVersion": "10.0"
    }
  ]
}
```

## Rules

- `releases` is ordered **newest first**; element 0 is "latest" (no stored flag).
- Required per release: `version`, `releasedAt`, `supportedHosts` (≥1), `downloadUrl`, `sha256Hash`. Optional: `releaseNotesUrl`, `notesSummary`, `minimumOsVersion`.
- `version` follows the repo's date-based build version `1.YY.MMDD.HHmm` (see `src/Directory.Build.props`).
- Installer artifacts are **not** committed to the repo; `downloadUrl` points at the host's downloads folder or a future GitHub Release asset.
- Older releases remain in the list indefinitely (FR-003).

## Failure behavior (spec edge case)

| Condition | Site behavior |
|-----------|---------------|
| File missing / unreadable | Friendly "download temporarily unavailable" message + link to repo `/releases/latest`; page still renders |
| Invalid JSON / schema violation | Skip invalid entries; render valid ones; if none valid → same fallback as missing |
| Empty `releases` array | Fallback message + repo link |

## Consumer contract with the updater

Field names `version`, `downloadUrl`, `releaseNotesUrl`, `sha256Hash`, `minimumOsVersion` are intentionally identical to `UpdateManifest.cs` (camelCase) so a single generator can emit both this file and the updater's single-release manifest later. The updater is **out of scope** for this feature; this is forward-compatibility only.
