# IPC Contract: ProfileGet (34/134) and ProfileRename (35/135)

Frame format, transport, and error envelope are unchanged (see `docs/ipc-api.md`): `[4-byte length][4-byte XOR CRC][MessagePack(RpcMessage)]`, `RpcMessage { MessageType, RequestId, Payload }`. Both are short-running shell→engine requests dispatched on the serial loop (NOT in the concurrent AI band 70–78). Shell timeout convention: 5000 ms (action-class, matching duplicate/export/import).

## ProfileGet — request 34, response 134

Purpose: return the stored `.akmlstyle` **file text verbatim** for one profile, plus its effective read-only status. The only profile-read IPC (List returns metadata only).

### Request payload — `ProfileGetRequest`

| Key | Field | Type | Contract |
|---|---|---|---|
| 0 | `Name` | string | Display name; resolved custom-first then built-in, OrdinalIgnoreCase (same semantics as `ProfileManager.Load`). |

### Response payload — `ProfileGetResponse`

| Key | Field | Type | Contract |
|---|---|---|---|
| 0 | `Success` | bool | `false` when the name resolves to no file. Nothing is created or modified — ever. |
| 1 | `ErrorMessage` | string? | Populated iff `Success == false`. |
| 2 | `Name` | string? | Resolved display name. |
| 3 | `ProfileJson` | string? | Raw file bytes decoded as UTF-8, **not** re-serialized. Guarantees: `metadata.modified` unaltered; unknown nested fields intact; suitable as the client's merge base. |
| 4 | `IsBuiltIn` | bool | `true` iff resolved from the built-in directory **and** no custom shadow exists (the `Save`-guard rule). The JSON's own `isBuiltIn` field is not consulted. |

Errors: unknown name → `Success=false, "Profile '<name>' was not found."`. I/O failures → `Success=false` + exception message (handler catch-all pattern).

## ProfileRename — request 35, response 135

Purpose: atomically rename a **custom** profile on the engine side, keeping file name, JSON `metadata.name`, and the `.source.json` sidecar consistent.

### Request payload — `ProfileRenameRequest`

| Key | Field | Type | Contract |
|---|---|---|---|
| 0 | `OldName` | string | Must resolve to a custom profile. Built-in (unshadowed) → failure. |
| 1 | `NewName` | string | Sanitized via `ProfileManager.SanitizeFileName`. Collision vs any existing custom or built-in name (OrdinalIgnoreCase) → failure — **except** the case-only rename of the same profile, which is allowed. |

### Response payload — `ProfileRenameResponse`

| Key | Field | Type | Contract |
|---|---|---|---|
| 0 | `Success` | bool | |
| 1 | `ErrorMessage` | string? | Populated iff failure. |
| 2 | `NewName` | string? | Final sanitized name actually persisted. |

### Engine-side transaction (order is the contract)

1. Read the custom file's raw text.
2. Rewrite `metadata.name` = NewName and `metadata.modified` = UtcNow in the JSON.
3. Atomic write to the new filename (temp file + move, the `ProfileManager.Save` pattern).
4. Delete the old file.
5. If `<OldName>.source.json` exists, move it to `<NewName>.source.json` (lossless re-import preservation).

A failure at step 3 leaves the original untouched. A failure at steps 4–5 leaves both names present (recoverable; reported as failure with detail).

### What ProfileRename does NOT do

It never touches `config.json` / `AppSettings.Formatter.ActiveProfile` — the active-style pointer is shell-owned. **Client obligation**: after a successful rename of the style currently named by `Formatter.ActiveProfile`, the shell must update that setting via `ConfigManager` (and refresh the status bar), or formatting silently falls back to defaults.

## Compatibility

- Additive only: two new request/response type ids; no existing DTO or key layout changes.
- Old engine + new shell: 34/35 yield the engine's unknown-type error response → the shell surfaces "not supported by this engine version" in the status bar and leaves the editor read-only-degraded (no save/rename affordances functional).
- New engine + old shell: unaffected (old shell never sends 34/35).

## Related hardening (same spec, existing messages — behavior fixes, not shape changes)

- `ProfileDelete` (16): response `Success` now reflects `ProfileManager.Delete`'s bool (previously always `true`, even when nothing existed).
- `ProfileSave` (15): request rejected (`Success=false`) when `ProfileJson` exceeds 1 MB, mirroring the import cap.
