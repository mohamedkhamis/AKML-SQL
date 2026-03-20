# Named Pipe Protocol Contract

**Version**: 1.0 | **Branch**: `002-core-intellisense-engine`

## Transport

- **Mechanism**: Windows Named Pipes (`System.IO.Pipes`)
- **Pipe name**: `akmlsql-engine-{userSid}-{parentPid}`
- **Direction**: Bidirectional (InOut)
- **Transmission mode**: Byte (not Message)
- **Buffer size**: 64 KB (in and out)
- **Security**: ACL restricted to current user SID, NETWORK SID denied

## Framing

Every message is framed as:

```
[4 bytes: payload length (big-endian int32)][N bytes: MessagePack payload]
```

- Maximum message size: 16 MB (guard against malformed frames)
- All payloads serialized with MessagePack-CSharp 2.x using `[MessagePackObject]` with integer `[Key]` attributes

## Envelope

```
[MessagePackObject]
RpcMessage:
  [Key(0)] MessageType: int    // Discriminator (see table below)
  [Key(1)] RequestId: int      // Correlates request/response pairs (0 for notifications)
  [Key(2)] Payload: byte[]     // Inner MessagePack-serialized message body
```

## Message Types

### Shell → Engine

| MessageType | Name | Payload Type | Description |
|---|---|---|---|
| 1 | ConnectionChanged | ConnectionInfo | New/changed database connection |
| 2 | DocumentChanged | DocumentChange | Editor content changed |
| 3 | RequestCompletion | CompletionRequest | Completion triggered |
| 4 | RequestSignatureHelp | SignatureRequest | Parameter help triggered |
| 5 | RequestQuickInfo | QuickInfoRequest | Hover/shortcut tooltip |
| 6 | SchemaRefreshRequest | RefreshRequest | Manual cache refresh |
| 7 | Ping | (empty) | Health check |
| 8 | Shutdown | (empty) | Graceful shutdown |

### Engine → Shell

| MessageType | Name | Payload Type | Description |
|---|---|---|---|
| 101 | CompletionResult | CompletionResponse | Ranked suggestions |
| 102 | SignatureHelpResult | SignatureResponse | Parameter signatures |
| 103 | QuickInfoResult | QuickInfoResponse | Object metadata |
| 104 | SchemaRefreshComplete | RefreshResponse | Cache refresh done |
| 105 | Pong | EngineStatusInfo | Health check response |
| 106 | Error | ErrorInfo | Request processing error |

## Payload Schemas

### ConnectionInfo (MessageType 1)

```
SessionId: string
ConnectionString: string
ServerVersion: int          // 13=2016, 14=2017, 15=2019, 16=2022, 17=2025
EngineEdition: int          // 1-4=on-prem, 5=Azure SQL DB, 8=Azure MI
DatabaseName: string
```

### DocumentChange (MessageType 2)

```
SessionId: string
ChangeType: int             // 0=Full, 1=Incremental
FullText: string            // Set when ChangeType=Full
Changes: Change[]           // Set when ChangeType=Incremental
  Change:
    Offset: int
    OldLength: int
    NewText: string
```

### CompletionRequest (MessageType 3)

```
SessionId: string
CursorOffset: int
TriggerKind: int            // 0=Auto, 1=Manual, 2=AfterDot
```

### CompletionResponse (MessageType 101)

```
Items: CompletionItem[]
  CompletionItem:
    DisplayText: string
    InsertText: string
    ObjectType: int         // 0=Table, 1=View, 2=Column, 3=Keyword, 4=Snippet,
                            // 5=Function, 6=Procedure, 7=Schema, 8=Database,
                            // 9=Variable, 10=Alias, 11=Parameter
    SecondaryText: string
    SourceObject: string
    SortPriority: int
IsIncomplete: bool
```

### SignatureRequest (MessageType 4)

```
SessionId: string
CursorOffset: int
```

### SignatureResponse (MessageType 102)

```
FunctionName: string
Overloads: SignatureOverload[]
  SignatureOverload:
    Label: string
    Documentation: string
    Parameters: ParameterInfo[]
      ParameterInfo:
        Name: string
        Type: string
        Documentation: string
        IsOptional: bool
ActiveOverload: int
ActiveParameter: int
```

### QuickInfoRequest (MessageType 5)

```
SessionId: string
CursorOffset: int
```

### QuickInfoResponse (MessageType 103)

```
ObjectType: string
Header: string
Details: KeyValuePair[]     // Key: string, Value: string
Description: string         // nullable
```

### RefreshRequest (MessageType 6)

```
SessionId: string
```

### RefreshResponse (MessageType 104)

```
Success: bool
ObjectCount: int
ErrorMessage: string        // nullable
```

### EngineStatusInfo (MessageType 105)

```
MemoryUsageMB: int
CachedDatabases: int
ActiveSessions: int
UptimeSeconds: long
```

### ErrorInfo (MessageType 106)

```
Code: int                   // Application-defined error code
Message: string
```

## Concurrency

- Writes are serialized via `SemaphoreSlim` (one writer at a time)
- Reads are processed by a single reader loop per connection
- Multiple concurrent requests are multiplexed via `RequestId` correlation
- `TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously)` prevents deadlocks

## Lifecycle

1. Shell launches engine process: `AkmlSql.Engine.exe --pipe {name} --parent-pid {pid}`
2. Engine creates `NamedPipeServerStream` and waits for connection
3. Shell connects with `NamedPipeClientStream` (retry up to 10 times, 200ms apart)
4. Shell sends `ConnectionChanged` for active query window
5. Normal request/response flow
6. On pipe break: shell fails all pending requests, waits 500ms, relaunches engine
7. On IDE shutdown: shell sends `Shutdown`, engine exits gracefully
8. Engine monitors parent PID — self-terminates if parent exits
