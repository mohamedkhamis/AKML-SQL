# IPC Contract: `RequestStyleEditorSchema` (NEW)

**Feature**: `020-sqlprompt-visual-parity`
**Phase**: 1 — Design
**Status**: Design (no code yet)

The Format Styles editor needs the canonical descriptor of every format setting to build its tree, controls, and validation. Rather than duplicating the descriptor in the shell, the editor requests it from the engine. The engine owns `FormatSettingSchema` (built from `SqlPromptKeyMap` + AKML-native settings) and serves it on demand.

---

## Message numbers

| Direction | Type | Constant | Value |
|---|---|---|---|
| Shell → Engine | Request | `MessageTypes.RequestStyleEditorSchema` | **28** |
| Engine → Shell | Response | `MessageTypes.StyleEditorSchemaResult` | **128** |

Value 28 is the next free slot in the Shell→Engine "formatter family" range (10–19 are taken; 20–24 belong to snippets; 25–26 to code analysis; 27 to wildcard expansion; 28 is the next gap). Value 128 mirrors at the +100 offset that the engine→shell range uses for responses.

---

## Frame envelope

Standard `RpcMessage` with MessagePack payload:

```text
RpcMessage {
  MessageType  = 28 (request) or 128 (response)
  RequestId    = correlation id
  Payload      = MessagePack(StyleEditorSchemaRequest | StyleEditorSchemaResponse)
}
```

Max frame size: 16 MB (existing limit). The schema payload is small (≈ 30 KB).

---

## Request: `StyleEditorSchemaRequest`

```text
StyleEditorSchemaRequest {
  ClientSchemaVersion  : int?   // (optional) what version the shell last saw; engine may short-circuit if unchanged
  IncludeUnsupported   : bool   // default true — include settings flagged Status=Unsupported so the editor shows them in the "Settings not yet supported" panel
}
```

Engine handler: `StyleEditorSchemaRequestHandler` in `src/AkmlSql.Engine/Server/`.

---

## Response: `StyleEditorSchemaResponse`

```text
StyleEditorSchemaResponse {
  SchemaVersion : int
  Groups        : FormatSettingGroup[]
  Settings      : FormatSetting[]
}

FormatSettingGroup {
  Id          : string   // "global.casing"
  DisplayName : string   // "Casing"
  ParentId    : string?  // "global"
  Order       : int      // for stable tree rendering
}

FormatSetting {
  Id              : string  // "casing.reservedKeywords"
  GroupId         : string  // "global.casing"
  DisplayName     : string  // "Reserved keywords"
  Type            : enum    // Bool | Enum | Int | IntRange
  Default         : any     // typed per Type
  AllowedEnumValues : string[]?  // if Type=Enum
  Min             : int?    // if Type=Int|IntRange
  Max             : int?    // if Type=Int|IntRange
  SqlPromptKey    : string? // null for AKML-only settings
  Status          : enum    // Implemented | GapToImplement | Unsupported
  Description     : string?
}
```

If `ClientSchemaVersion` was supplied and matches the engine's `SchemaVersion`, the engine responds with empty `Groups` / `Settings` arrays — the shell uses its cached copy.

---

## Error responses

Reuse standard `MessageTypes.Error = 106` envelope:

```text
Error {
  RequestId : int
  Code      : string  // "ENGINE_BUSY" | "INTERNAL"
  Message   : string
}
```

`ENGINE_BUSY` should be exceptional — schema lookup is in-memory after first build.

---

## Latency target

Engine-side: ≤ 5 ms (one in-memory dictionary lookup). End-to-end including IPC: ≤ 30 ms p95. Not on a hot path (editor opens infrequently).

---

## Test coverage

| Test | What it validates |
|---|---|
| `StyleEditorSchemaResponseTests` | Every `FormatSetting` has a `GroupId` that resolves to a known `FormatSettingGroup` |
| `StyleEditorSchemaResponseTests` | Every `FormatSetting` with `SqlPromptKey != null` is present in `SqlPromptKeyMap` |
| `StyleEditorSchemaResponseTests` | `SchemaVersion` is monotonically increasing across commits (the test snapshots the schema bytes and fails if it changes without a bump) |
| `StyleEditorSchemaHandlerTests` | Short-circuit path returns empty arrays when client version matches |
| `StyleEditorSchemaHandlerTests` | Full payload returned when `ClientSchemaVersion` is null |

---

## Out of scope for this contract

- The actual setting controls (renderers in `SettingControlsPanel`) — those are UI code, not contract.
- The `.sqlpromptstyle` JSON shape — covered by `ipc-profile-import-sqlprompt.md`.
- Preview rendering — covered by `ipc-format-preview-debounce.md`.
