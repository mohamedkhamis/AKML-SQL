# Contract: Style Editor Schema v2 (rides existing msg 28 — no wire changes)

The schema continues to travel as a JSON string in `StyleEditorSchemaResponse.SchemaJson` (`[Key(1)]`); request/response MessagePack layouts are untouched. This contract defines the **JSON body** an engine at v2 emits and what shells may rely on.

## Versioning

- `schemaVersion`: `2`. Engines short-circuit `Cached=true` when `ClientSchemaVersion == 2`; a shell that cached v1 receives the full v2 body on next request (automatic invalidation).
- A shell talking to a v1 engine (mixed-version window during upgrade) receives the v1 body: `parentId`/`allowedEnumValues`/`min`/`max`/`description` absent or null. **Shells MUST treat all v2 fields as optional** and degrade: flat tree, free-text enum boxes, unclamped ints, no descriptions.

## Group rows (all 18 — categories are values, not rows)

```json
{ "id": "whitespace", "displayName": "Whitespace", "parentId": "global", "order": 1 }
```

- `parentId` ∈ `"global" | "statements" | "clauses" | "expressions" | "other"` on **every** group row at v2.
- No group row is emitted for a category itself (a v1 shell rendering rows flatly must not see empty category nodes).
- Shell maps category id → display name (`global`→Global, `statements`→Statements, `clauses`→Clauses, `expressions`→Expressions, `other`→Other); unknown or missing `parentId` → render under Other.
- Group ids are frozen (v1 set); `order` semantics unchanged.

## Setting rows

```json
{
  "id": "casing.reservedKeywords",
  "groupId": "casing",
  "displayName": "Reserved keywords",
  "type": "Enum",
  "default": "UPPERCASE",
  "allowedEnumValues": ["UPPERCASE", "lowercase", "PascalCase", "AsIs"],
  "min": null,
  "max": null,
  "sqlPromptKey": "KeywordCasing",
  "status": "Implemented",
  "description": "Casing applied to reserved keywords (SELECT, FROM, WHERE...)."
}
```

Guarantees at v2:

- `description`: non-empty on **every** setting.
- `type == "Enum"` ⇒ `allowedEnumValues` non-empty and contains `default`. Values are the **exact stored spellings** for the profile JSON (mixed case preserved: `"UPPERCASE"`, `"AsIs"`, `"trailing"`). Clients persist the selected entry verbatim.
- `type == "Int"` with a declared range ⇒ `min ≤ default ≤ max`; clients reject out-of-range input before preview/save. Absent `min`/`max` ⇒ unclamped.
- Setting-id format `"{groupId}.{jsonName}"` is frozen; existing ids are byte-identical to v1 (SqlPromptKey mapping keys on them).
- **New at v2**: the previously-opaque `insertStatements` sub-objects are flattened into 6 multi-segment ids — `insertStatements.columns.parenthesisStyle`, `insertStatements.columns.indentContents`, `insertStatements.columns.placeSubsequentItemsOnNewLines`, and the same three under `insertStatements.values.` — each a normal typed setting row (`groupId: "insertStatements"`). The two `"Other"`-typed blob rows (`insertStatements.columns`, `insertStatements.values`) are no longer emitted. Clients writing profile JSON must nest by **all** dot segments after the group id.
- `sqlPromptKey`/`status` semantics unchanged (`Implemented` / `AkmlOnly`; `Unsupported` remains reserved).

## Consumer obligations (shell window)

- Build the 2-level tree from `parentId`; categories expanded by default.
- Enum → themed ComboBox of `allowedEnumValues` (plain-string items); Int → validated numeric box honoring `min`/`max`; Bool → CheckBox (unchanged); `Other` → read-only (unchanged).
- Show `description` with the setting; keep the existing SqlPromptKey/status badge line.
- Cache invalidation: store `(schemaVersion, schemaJson)` as today; send `ClientSchemaVersion` on every request.
