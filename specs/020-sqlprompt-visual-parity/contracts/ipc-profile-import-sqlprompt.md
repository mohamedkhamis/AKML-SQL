# IPC Contract: `ProfileImport` extended for `.sqlpromptstyle` (EXTENDED)

**Feature**: `020-sqlprompt-visual-parity`
**Phase**: 1 — Design
**Status**: Design — extends the existing message; no breaking changes

The existing `ProfileImport (17)` IPC currently imports AKML-native `.akmlstyle` files. We extend the engine-side handler (not the wire format) to detect `.sqlpromptstyle` files and route through `SqlPromptStyleImporter`. The response envelope gains two optional fields so the editor can surface "Settings not yet supported" and "Pass-through unknown keys".

---

## Message numbers (unchanged)

| Direction | Type | Constant | Value |
|---|---|---|---|
| Shell → Engine | Request | `MessageTypes.ProfileImport` | **17** |
| Engine → Shell | Response | `MessageTypes.ProfileImportResult` | **117** |

---

## Request: `ProfileImportRequest` (unchanged shape; new behaviour)

```text
ProfileImportRequest {
  SourcePath    : string   // absolute path; engine canonicalises via Path.GetFullPath
  TargetName    : string?  // optional new name; if null, use metadata.name from the file
  Overwrite     : bool     // when a style with the same name already exists
}
```

**New behaviour**: the engine inspects the file extension after canonicalising the path:

- `.akmlstyle` → existing `AkmlStyleImporter` (unchanged)
- `.sqlpromptstyle` → new `SqlPromptStyleImporter`
- anything else → `Error` with code `UNSUPPORTED_EXTENSION`

The shell does **not** branch on extension. It only sends a path; the engine decides.

---

## Response: `ProfileImportResponse` (extended fields)

Existing fields stay; two new optional fields added at the end (MessagePack handles missing fields as nulls / empty lists, so existing shell builds keep working):

```text
ProfileImportResponse {
  // existing
  Success            : bool
  ImportedProfileId  : Guid?
  ImportedProfileName: string?
  Error              : string?

  // NEW (this feature)
  UnsupportedSettings : string[]?  // SQL Prompt keys present in the file that AKML does not support
  PassthroughKeys     : string[]?  // unknown SQL Prompt keys preserved verbatim for round-trip
  Kind                : string?    // "Native" | "SqlPromptImported"  — lets the editor decorate the style with the import badge
}
```

`UnsupportedSettings` and `PassthroughKeys` are always populated for `.sqlpromptstyle` imports (possibly empty). They are always null for `.akmlstyle` imports.

---

## Error codes

| Code | When | Recovery |
|---|---|---|
| `FILE_NOT_FOUND` | Path resolves but file absent | Shell shows file picker again |
| `FILE_TOO_LARGE` | > 1 MB | Reject; no partial state |
| `INVALID_PATH` | `Path.GetFullPath` rejects | Reject |
| `UNSUPPORTED_EXTENSION` | Neither `.akmlstyle` nor `.sqlpromptstyle` | Reject; suggest the two supported extensions |
| `MALFORMED_JSON` | JSON parse failure | Error message includes line / column |
| `SCHEMA_VIOLATION` | Required field missing or wrong type | Error message names the failed JSON path (e.g. `casing.reservedKeywords: expected string, got bool`) |
| `NAME_COLLISION` | Style with same name exists and `Overwrite=false` | Shell prompts user to overwrite or rename |

No partial state is ever persisted — the importer writes the new `.akmlstyle` (storage format remains AKML's own, even for imported SQL Prompt styles, because they share the same `FormatProfile` POCO) and the `.sqlpromptstyle` source copy in `imported/` atomically (temp + rename) after a successful full parse.

---

## Round-trip pairing

This message pairs with `ProfileSave (15)` / `ProfileSaveResult (115)`. After import + edit, the user can export back to `.sqlpromptstyle`; `ProfileSave` already handles "save as", and the engine inspects the target path's extension the same way to choose the exporter (`SqlPromptStyleExporter` for `.sqlpromptstyle`).

The `PassthroughUnknownKeys` dictionary on `FormatProfile` is the bridge — it survives the in-memory edit cycle and is written back at the same JSON paths on export.

---

## Security

- `SourcePath` MUST be canonicalised via `Path.GetFullPath` before any `File.OpenRead`. CLAUDE.md "Path validation" rule.
- File size cap is 1 MB (compare with snippet JSON cap, same envelope).
- The importer NEVER reads or executes any path-like values inside the JSON. Settings only.

---

## Test coverage

| Test | What it validates |
|---|---|
| `SqlPromptStyleImporterTests.Import_RealWorldStyleFile_Succeeds` | Imports one of each canonical Redgate built-in style (`Compact`, `Indented`, `AlignedLeftBracket`) |
| `SqlPromptStyleImporterTests.Import_UnknownKey_PreservedInPassthrough` | Adds a synthetic `joins.futureFeature: "foo"` key; importer succeeds; key reappears at export |
| `SqlPromptStyleImporterTests.Import_UnsupportedKey_SurfaceInResponse` | Imports a file with `casing.useObjectDefinitionCase`; response lists it in `UnsupportedSettings` |
| `SqlPromptStyleImporterTests.Import_MalformedJson_NoPartialState` | Corrupt JSON; importer returns error; no file appears in `styles/` |
| `SqlPromptStyleImporterTests.Import_OversizeFile_Rejected` | 2 MB file; rejected with `FILE_TOO_LARGE` |
| `SqlPromptStyleImporterTests.Import_PathTraversal_Rejected` | `..\..\..\Windows\system32\…`-style path; rejected with `INVALID_PATH` |
| `SqlPromptStyleExporterTests.RoundTrip_PreservesUnknownKeys` | Import → edit → export; diff at unknown JSON paths is zero |
