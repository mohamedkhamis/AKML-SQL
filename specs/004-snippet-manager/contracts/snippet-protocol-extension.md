# Snippet Protocol Extension Contract

**Version**: 1.0 | **Branch**: `004-snippet-manager`

## Overview

These messages extend the Phase 2 named pipe protocol for snippet operations. Same transport, framing, and envelope as Phase 2.

## New Message Types

### Shell → Engine

| MessageType | Name | Payload Type | Description |
|---|---|---|---|
| 20 | SnippetExpand | SnippetExpandRequest | Expand snippet by shortcode |
| 21 | SnippetList | SnippetListRequest | List/search snippets |
| 22 | SnippetSave | SnippetSaveRequest | Save/update a snippet |
| 23 | SnippetDelete | SnippetDeleteRequest | Delete a snippet |
| 24 | SnippetImport | SnippetImportRequest | Import from external format |

### Engine → Shell

| MessageType | Name | Payload Type | Description |
|---|---|---|---|
| 120 | SnippetExpandResult | SnippetExpandResponse | Expanded text + placeholder info |
| 121 | SnippetListResult | SnippetListResponse | Snippet metadata array |
| 122 | SnippetSaveResult | SnippetSaveResponse | Save confirmation |
| 123 | SnippetDeleteResult | SnippetDeleteResponse | Delete confirmation |
| 124 | SnippetImportResult | SnippetImportResponse | Import report |

## Modified Existing Messages

### CompletionRequest (MessageType 3) — Extended

Add field:
```
HasSelection: bool    // True if text is selected in editor (for surround-with filtering)
```

## Payload Schemas

### SnippetExpandRequest (MessageType 20)

```
SessionId: string
Shortcode: string           // Shortcode to expand
CursorOffset: int           // Position in document (for context)
SelectedText: string        // For surround-with (empty if no selection)
FormatOnExpand: bool        // Apply formatting profile
ProfileName: string         // Active profile name (if formatting)
```

### SnippetExpandResponse (MessageType 120)

```
Success: bool
ExpandedText: string        // Fully expanded text (built-in vars resolved)
Placeholders: PlaceholderInfo[]
  PlaceholderInfo:
    VariableName: string
    Offset: int             // Character offset in expanded text
    Length: int             // Length of default text
    DefaultText: string
    SchemaAwareType: string // Nullable
    GroupIndex: int         // Tab-stop order (0-based)
CursorOffset: int           // Position of $CURSOR$ (-1 if not present)
WasFormatted: bool
ErrorMessage: string        // Nullable
```

### SnippetListRequest (MessageType 21)

```
Query: string               // Search query (empty = list all)
Context: string             // Current clause context for filtering (nullable)
HasSelection: bool          // For surround-with filtering
SourceFilter: int           // 0=All, 1=Personal, 2=Team, 3=BuiltIn
CategoryFilter: string      // Nullable (e.g., "DML", "DDL")
```

### SnippetListResponse (MessageType 121)

```
Snippets: SnippetInfo[]
  SnippetInfo:
    Id: string
    Shortcode: string
    Name: string
    Description: string
    Category: string
    Source: int              // 1=Personal, 2=Team, 3=BuiltIn
    SurroundsWith: bool
    UsageCount: int
    Tags: string[]
```

### SnippetSaveRequest (MessageType 22)

```
SnippetJson: string         // Full .akmlsnippet JSON
IsNew: bool                 // true = create, false = update
```

### SnippetSaveResponse (MessageType 122)

```
Success: bool
ErrorMessage: string        // Nullable (e.g., "Shortcode conflict")
```

### SnippetDeleteRequest (MessageType 23)

```
SnippetId: string           // Snippet GUID to delete
```

### SnippetDeleteResponse (MessageType 123)

```
Success: bool
ErrorMessage: string        // Nullable (e.g., "Cannot delete built-in")
```

### SnippetImportRequest (MessageType 24)

```
FileContent: string         // Raw file content (XML or JSON)
SourceFormat: int           // 0=Auto-detect, 1=SqlPromptXml, 2=SqlPromptJson, 3=SsmsNative, 4=AkmlSnippet
NewSnippetName: string      // Optional override for imported snippet name
```

### SnippetImportResponse (MessageType 124)

```
Success: bool
ImportedCount: int
FailedCount: int
FailedDetails: string[]     // Error messages for failed imports
SnippetIds: string[]        // IDs of successfully imported snippets
```
