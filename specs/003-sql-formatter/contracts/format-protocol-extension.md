# Format Protocol Extension Contract

**Version**: 1.0 | **Branch**: `003-sql-formatter`

## Overview

These messages extend the Phase 2 named pipe protocol (defined in `specs/002-core-intellisense-engine/contracts/named-pipe-protocol.md`) for formatter operations. Same transport (named pipes), framing (4-byte length prefix), and envelope (`RpcMessage` with MessagePack serialization).

## New Message Types

### Shell → Engine

| MessageType | Name | Payload Type | Description |
|---|---|---|---|
| 10 | FormatDocument | FormatRequest | Format entire document text |
| 11 | FormatSelection | FormatSelectionRequest | Format selected text range |
| 12 | FormatPreview | FormatPreviewRequest | Live preview during profile editing |
| 13 | FormatAction | FormatActionRequest | Execute standalone action (casing, semicolons, etc.) |
| 14 | ProfileList | ProfileListRequest | List available profiles |
| 15 | ProfileSave | ProfileSaveRequest | Save/update a profile |
| 16 | ProfileDelete | ProfileDeleteRequest | Delete a custom profile |
| 17 | ProfileImport | ProfileImportRequest | Import .sqlpromptstyle file |
| 18 | BulkFormat | BulkFormatRequest | Bulk format files |
| 19 | BulkFormatCancel | BulkFormatCancelRequest | Cancel in-progress bulk format |

### Engine → Shell

| MessageType | Name | Payload Type | Description |
|---|---|---|---|
| 110 | FormatResult | FormatResponse | Formatted text or error |
| 111 | FormatSelectionResult | FormatSelectionResponse | Formatted selection |
| 112 | FormatPreviewResult | FormatPreviewResponse | Preview of formatted text |
| 113 | FormatActionResult | FormatActionResponse | Action result |
| 114 | ProfileListResult | ProfileListResponse | Array of profile metadata |
| 115 | ProfileSaveResult | ProfileSaveResponse | Save confirmation |
| 116 | ProfileDeleteResult | ProfileDeleteResponse | Delete confirmation |
| 117 | ProfileImportResult | ProfileImportResponse | Import result with mapping report |
| 118 | BulkFormatProgress | BulkFormatProgressInfo | Progress updates |
| 119 | BulkFormatResult | BulkFormatReportResponse | Final bulk format report |

## Payload Schemas

### FormatRequest (MessageType 10)

```
SessionId: string
Text: string                    // Full document text
ProfileName: string             // Active profile name (or null for default)
ProfileOverrides: byte[]        // Optional: MessagePack-serialized partial profile overrides
IncludeActions: int[]           // Action types to include (from FormatActionType enum)
```

### FormatSelectionRequest (MessageType 11)

```
SessionId: string
Text: string                    // Full document text
SelectionStart: int             // Character offset of selection start
SelectionEnd: int               // Character offset of selection end
ProfileName: string
```

### FormatResponse (MessageType 110)

```
Success: bool
FormattedText: string           // Formatted text (or original on failure)
WasModified: bool               // true if output differs from input
ValidationPassed: bool          // true if semantic equivalence confirmed
ElapsedMs: long
Diagnostics: FormatDiagnosticInfo[]
  FormatDiagnosticInfo:
    Severity: int               // 0=Info, 1=Warning, 2=Error
    Message: string
    Offset: int                 // -1 if not applicable
    Line: int                   // -1 if not applicable
```

### FormatSelectionResponse (MessageType 111)

```
Success: bool
FormattedText: string           // Just the formatted selection text
OriginalStart: int              // Start offset of the region that was actually formatted
OriginalEnd: int                // End offset (may differ from requested selection if expanded)
WasModified: bool
ValidationPassed: bool
ElapsedMs: long
```

### FormatPreviewRequest (MessageType 12)

```
SessionId: string
SampleText: string              // SQL text to preview
ProfileJson: string             // Full profile JSON (not saved yet)
```

### FormatPreviewResponse (MessageType 112)

```
FormattedText: string
ElapsedMs: long
```

### FormatActionRequest (MessageType 13)

```
SessionId: string
Text: string
ActionType: int                 // FormatActionType enum
ProfileName: string             // For casing action: which profile's casing rules
```

**FormatActionType enum**:

| Value | Name | Description |
|---|---|---|
| 0 | CasingOnly | Apply casing rules without layout changes |
| 1 | InsertSemicolons | Add missing statement terminators |
| 2 | RemoveSemicolons | Remove statement terminators |
| 3 | ExpandWildcards | Replace SELECT * with column list |
| 4 | QualifyObjectNames | Add schema prefixes |
| 5 | AddSquareBrackets | Add [brackets] to identifiers |
| 6 | RemoveSquareBrackets | Remove [brackets] from identifiers |
| 7 | AddAsKeyword | Add AS on aliases |
| 8 | RemoveAsKeyword | Remove AS from aliases |

### FormatActionResponse (MessageType 113)

```
Success: bool
FormattedText: string
WasModified: bool
ElapsedMs: long
ErrorMessage: string            // nullable
```

### ProfileListRequest (MessageType 14)

```
(empty — no parameters)
```

### ProfileListResponse (MessageType 114)

```
Profiles: ProfileInfo[]
  ProfileInfo:
    Name: string
    Description: string
    Author: string
    IsBuiltIn: bool
    IsActive: bool
    BasedOn: string             // nullable
    Modified: string            // ISO 8601 datetime
```

### ProfileSaveRequest (MessageType 15)

```
ProfileJson: string             // Full profile JSON
IsNew: bool                     // true = create, false = update
```

### ProfileSaveResponse (MessageType 115)

```
Success: bool
ErrorMessage: string            // nullable (e.g., "Name already exists")
```

### ProfileDeleteRequest (MessageType 16)

```
ProfileName: string
```

### ProfileDeleteResponse (MessageType 116)

```
Success: bool
ErrorMessage: string            // nullable (e.g., "Cannot delete built-in profile")
```

### ProfileImportRequest (MessageType 17)

```
FileContent: string             // Raw .sqlpromptstyle JSON content
SourceFormat: int               // 0 = SqlPromptStyle, 1 = AkmlStyle (for re-import)
NewProfileName: string          // Name for the imported profile
```

### ProfileImportResponse (MessageType 117)

```
Success: bool
ProfileName: string             // Final profile name
MappedOptionsCount: int         // Options successfully mapped
UnmappedOptionsCount: int       // Options that could not be mapped
UnmappedOptions: string[]       // Names of unmapped options
ErrorMessage: string            // nullable
```

### BulkFormatRequest (MessageType 18)

```
FilePaths: string[]             // Absolute paths to format
ProfileName: string
Mode: int                       // 0=Format, 1=Preview (dry run)
CreateBackups: bool
SkipParseErrors: bool
RespectNoformat: bool
```

### BulkFormatProgressInfo (MessageType 118)

```
TotalFiles: int
CompletedFiles: int
CurrentFile: string
Status: string                  // "formatting", "skipped", "error"
```

### BulkFormatReportResponse (MessageType 119)

```
TotalFiles: int
FormattedCount: int
AlreadyFormattedCount: int
ParseErrorCount: int
SkippedCount: int
TotalLinesChanged: int
ElapsedMs: long
Details: FileResult[]
  FileResult:
    FilePath: string
    Status: int                 // 0=Formatted, 1=AlreadyFormatted, 2=ParseError, 3=Skipped, 4=Error
    LinesChanged: int
    ErrorMessage: string        // nullable
```

### BulkFormatCancelRequest (MessageType 19)

```
(empty — cancels the in-progress bulk format)
```

## Concurrency Notes

- Format requests are serialized per session (one format at a time per connection)
- Bulk format operations run on a background thread; progress is pushed via notification messages
- Profile operations (list, save, delete, import) are lightweight and processed inline
- Preview requests during profile editing should be debounced on the shell side (100ms)
