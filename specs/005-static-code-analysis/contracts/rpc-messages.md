# Contract: RPC Message Types — Code Analysis

**Branch**: `005-static-code-analysis` | **Date**: 2026-03-22

This document defines the new IPC message types added to the existing named-pipe RPC protocol between the shell extension and the Engine process. All messages use MessagePack serialization, consistent with the existing protocol.

---

## New Message Type Constants

Added to `src/AkmlSql.Core/Ipc/MessageTypes.cs`:

```
Shell → Engine:
  RequestAnalyze         = 25    // Analyze document and return all diagnostics
  AnalysisSettingsChanged = 26   // Notify Engine of updated CaSettings (Engine reloads)

Engine → Shell:
  AnalysisResult         = 125   // Response to RequestAnalyze
```

---

## RequestAnalyze (25)

**Direction**: Shell → Engine
**Payload type**: `CodeAnalysisRequest`
**Response**: `AnalysisResult (125)`

```
CodeAnalysisRequest {
  0: SessionId         string    // Matches an existing session from ConnectionChanged
  1: RequestId         string    // UUID; echoed in response for correlation
  2: DocumentText      string    // Full current document text
  3: DocumentVersion   int       // Monotonically increasing; Engine discards if older than last processed
}
```

**Behavior**:
- Engine MUST respond with `AnalysisResult` even if no issues are found (empty `Issues` array)
- If a newer `RequestAnalyze` for the same `SessionId` arrives before the previous one completes, the Engine cancels the in-progress analysis and responds to the newer request
- If `SessionId` is unknown (no prior `ConnectionChanged`), Engine analyzes with no schema cache and defaults to TSql160 parser

---

## AnalysisResult (125)

**Direction**: Engine → Shell
**Payload type**: `CodeAnalysisResponse`

```
CodeAnalysisResponse {
  0: RequestId         string          // Echoed from CodeAnalysisRequest
  1: Issues            CodeIssueInfo[] // All active diagnostics; empty array if none
  2: AnalyzedVersion   int             // DocumentVersion that was analyzed
}

CodeIssueInfo {
  0: RuleId            string          // e.g. "PE001"
  1: Severity          int             // 0=Hint, 1=Information, 2=Warning, 3=Error
  2: Message           string          // e.g. "Avoid SELECT * in stored procedures"
  3: StartOffset       int             // Byte offset (0-based) into DocumentText
  4: EndOffset         int             // Byte offset (exclusive) into DocumentText
  5: Line              int             // 1-based line number
  6: Column            int             // 1-based column number
  7: FixActions        FixActionInfo[] // May be empty
}

FixActionInfo {
  0: Label             string          // e.g. "Replace with IS NULL"
  1: FixType           int             // 0=Transform, 1=Insert, 2=Remove, 3=Suppress
  2: ReplacementStart  int             // Byte offset where replacement begins
  3: ReplacementEnd    int             // Byte offset where replacement ends
  4: ReplacementText   string          // New text to write at [Start, End)
  5: SuppressRuleId    string?         // Rule to suppress (FixType=3 only)
  6: SuppressScopeCode int?            // 0=Line, 1=File, 2=Global (FixType=3 only)
}
```

**Shell behavior on receipt**:
- If `AnalyzedVersion` < current document version, discard (stale response)
- Otherwise update `DiagnosticTagger` with new `Issues` array
- Push all `Severity >= Warning` issues to the Error List panel
- Previous issue set is fully replaced (not merged)

---

## AnalysisSettingsChanged (26)

**Direction**: Shell → Engine
**Payload type**: `null` (no payload)
**Response**: `null` (notification only — no response message)

**Behavior**:
- Shell fires this when the user saves new rule configuration in the Options dialog
- Engine flushes its CaSettings cache for all sessions; next `RequestAnalyze` reloads settings from disk
- Shell also fires this when a `.casettings` file on disk is modified (detected via FileSystemWatcher)

---

## Error Handling

All existing error handling conventions apply:
- If Engine fails to analyze (unhandled exception), it returns an `Error (106)` message with the error text
- Shell logs the error and clears existing squiggles (fail-open: no false squiggles)
- Engine never crashes the process due to analysis failure; exceptions are caught per-rule
