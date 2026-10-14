using System;
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    // ────────────────────────────────────────────────────────────────────────────────────────
    // Spec 030 — Phase 5 (web edition) query execution + results grid + inline CRUD IPC DTOs.
    //
    // ALL row data is shipped as SAFE pre-stringified text (string?[][]) — never object/object[][].
    // The repo configures only the implicit MessagePack StandardResolver, which cannot serialize an
    // `object` member and would lose SQL type fidelity even with Typeless. A null array element means
    // SQL NULL (distinct from empty string). Cell text is invariant-culture and round-trippable:
    //   • datetime/datetime2/datetimeoffset → ISO-8601 "o"
    //   • uniqueidentifier → "D" (canonical 36-char)
    //   • varbinary/binary/timestamp → Base64
    //   • bit → "0" / "1"
    //   • decimal/numeric/float/real/money → invariant ToString (shortest round-trippable on net10.0)
    // SqlScalarEncoder is the SINGLE round-trip source (read-format == write-parse).
    //
    // Every [MessagePackObject] uses contiguous [Key(0..n)] and non-null defaults. Additive evolution
    // only: APPEND new keys, never renumber (older payloads default missing tail keys to 0/null/empty).
    // ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Browser → Engine: run a SQL batch on the persistent per-session connection.</summary>
    [MessagePackObject]
    public sealed class ExecuteQueryRequest
    {
        /// <summary>The canonical web session id (ISqlConnectionService.SessionId).</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>The SQL text to execute (the live editor selection, or the whole document).</summary>
        [Key(1)]
        public string Sql { get; set; } = string.Empty;

        /// <summary>Advisory row cap; the engine RE-CLAMPS to its ceiling regardless of this value.</summary>
        [Key(2)]
        public int MaxRows { get; set; } = 1000;

        /// <summary>Advisory command timeout; the engine RE-CLAMPS to its ceiling regardless.</summary>
        [Key(3)]
        public int CommandTimeoutSeconds { get; set; } = 30;

        /// <summary>App-level GUID correlating an ExecuteCancel to this execute (the bridge's per-frame
        /// RequestId is internal and not visible to callers).</summary>
        [Key(4)]
        public string QueryId { get; set; } = string.Empty;

        /// <summary>When true, the reader is opened with KeyInfo so the grid can offer inline CRUD.</summary>
        [Key(5)]
        public bool IncludeProvenance { get; set; } = true;
    }

    /// <summary>Per-column provenance captured from the live reader's column schema (KeyInfo).</summary>
    [MessagePackObject]
    public sealed class ColumnProvenanceDto
    {
        [Key(0)]
        public int Ordinal { get; set; }

        /// <summary>The real underlying column name (NOT the SELECT alias). Null for expressions.</summary>
        [Key(1)]
        public string? BaseColumnName { get; set; }

        /// <summary>True when this column is part of the key the engine uses to identify the row.</summary>
        [Key(2)]
        public bool IsKey { get; set; }

        /// <summary>IDENTITY column — excluded from INSERT, shown read-only.</summary>
        [Key(3)]
        public bool IsAutoIncrement { get; set; }

        /// <summary>Computed/server-generated/expression — excluded from SET/INSERT.</summary>
        [Key(4)]
        public bool IsReadOnly { get; set; }

        /// <summary>Expression/aggregate column with no base column.</summary>
        [Key(5)]
        public bool IsExpression { get; set; }

        [Key(6)]
        public bool AllowDBNull { get; set; }

        /// <summary>SqlDbType as int (for typed SqlParameter construction on the write path).</summary>
        [Key(7)]
        public int ProviderType { get; set; }

        [Key(8)]
        public int? ColumnSize { get; set; }

        /// <summary>Numeric precision; null when not applicable (spec 041: SqlClient's 255 "not applicable"
        /// sentinel is sent as null, so a write never binds it).</summary>
        [Key(9)]
        public int? Precision { get; set; }

        /// <summary>Numeric scale; null when not applicable (the 255 sentinel, as for <see cref="Precision"/>).</summary>
        [Key(10)]
        public int? Scale { get; set; }

        /// <summary>True when the IsKey column cross-checks to the schema-cache declared PRIMARY KEY.</summary>
        [Key(11)]
        public bool IsTruePrimaryKey { get; set; }
    }

    /// <summary>One result set: column metadata + SAFE text rows + CRUD eligibility + base-table identity.</summary>
    [MessagePackObject]
    public sealed class ExecuteResultSet
    {
        [Key(0)]
        public string[] ColumnNames { get; set; } = System.Array.Empty<string>();

        /// <summary>
        /// The declared SQL type of each column (spec 041 R10): <c>nvarchar(50)</c>, <c>nvarchar(max)</c>,
        /// <c>decimal(10,2)</c>, <c>datetime2(3)</c>, <c>time(0)</c>; bare for types without a length or scale
        /// (<c>int</c>, <c>date</c>, <c>money</c>, <c>uniqueidentifier</c>, UDT names). Older engines send bare
        /// names for every type; <c>SqlTypeName.Parse</c> reads both forms.
        /// </summary>
        [Key(1)]
        public string[] ColumnSqlTypes { get; set; } = System.Array.Empty<string>();

        /// <summary>Per-column CLR hint so the client can parse the text back. See <see cref="ClrTypeHint"/>.</summary>
        [Key(2)]
        public int[] ClrTypeHints { get; set; } = System.Array.Empty<int>();

        /// <summary>Row-major cell text; a null element == SQL NULL.</summary>
        [Key(3)]
        public string?[][] Rows { get; set; } = System.Array.Empty<string?[]>();

        /// <summary>True when the row cap or byte budget stopped row collection early.</summary>
        [Key(4)]
        public bool Truncated { get; set; }

        /// <summary>Best-effort count of rows omitted due to truncation (0 if unknown).</summary>
        [Key(5)]
        public int RowsOmitted { get; set; }

        [Key(6)]
        public ColumnProvenanceDto[] Provenance { get; set; } = System.Array.Empty<ColumnProvenanceDto>();

        /// <summary>True when the CRUD-eligibility predicate passed (single base table + a key column).</summary>
        [Key(7)]
        public bool IsEditable { get; set; }

        [Key(8)]
        public string? BaseSchema { get; set; }

        [Key(9)]
        public string? BaseTable { get; set; }

        [Key(10)]
        public string? BaseCatalog { get; set; }

        /// <summary>Spec 041 — the batch (0-based, after GO splitting) that produced this set; 0 from older engines.</summary>
        [Key(11)]
        public int BatchIndex { get; set; }

        /// <summary>
        /// Spec 042 (FR-130): why a PostgreSQL set cannot be edited - it joins tables, is a view, has no primary key or leaves part of
        /// it out. Null when editable, and for SQL Server sets. The grid says it where it would otherwise only say "can't be edited".
        /// </summary>
        [Key(12)]
        public string? ReadOnlyReason { get; set; }
    }

    /// <summary>Engine → Browser: the full execute outcome (NEVER null — a missing frame hangs the bridge).</summary>
    [MessagePackObject]
    public sealed class ExecuteQueryResult
    {
        [Key(0)]
        public string QueryId { get; set; } = string.Empty;

        /// <summary>See <see cref="ExecuteStatus"/>. Ok when no batch reported an error; Error when any did
        /// (the script still ran on); TimedOut / Cancelled when the script stopped for that reason.</summary>
        [Key(1)]
        public int Status { get; set; }

        /// <summary>The first error's text, or null.</summary>
        [Key(2)]
        public string? ErrorMessage { get; set; }

        /// <summary>Every set the script returned (also when a later batch failed); possibly empty.</summary>
        [Key(3)]
        public ExecuteResultSet[] ResultSets { get; set; } = System.Array.Empty<ExecuteResultSet>();

        /// <summary>The messages as SSMS words them, one string each (<c>Msg 208, Level 16, State 1, Line 4</c> +
        /// newline + text; <c>(N rows affected)</c>; PRINT text; system lines). <see cref="MessageDetails"/>
        /// carries the same messages structured; this array serves older web bundles.</summary>
        [Key(4)]
        public string[] Messages { get; set; } = System.Array.Empty<string>();

        /// <summary>The whole script's elapsed time.</summary>
        [Key(5)]
        public long ElapsedMs { get; set; }

        /// <summary>The sum of the DML row counts; -1 when no statement reported one (spec 041).</summary>
        [Key(6)]
        public int TotalRowsAffected { get; set; }

        /// <summary>True when a prior #temp/SET/USE state was lost because the connection was reopened
        /// (the SSMS-like persistence guarantee was broken; surfaced, not silently swallowed).</summary>
        [Key(7)]
        public bool ConnectionWasReset { get; set; }

        /// <summary>Spec 041 (execute.v2) — every message in stream order; empty from older engines.</summary>
        [Key(8)]
        public ExecuteMessageDto[] MessageDetails { get; set; } = System.Array.Empty<ExecuteMessageDto>();

        /// <summary>Spec 041 (execute.v2) — one entry per batch (a <c>GO n</c> loop is one entry).</summary>
        [Key(9)]
        public ExecuteBatchDto[] Batches { get; set; } = System.Array.Empty<ExecuteBatchDto>();

        /// <summary>Spec 041 (execute.v2) — when the script ended, UTC Unix milliseconds; 0 from older engines.</summary>
        [Key(10)]
        public long CompletedAtUnixMs { get; set; }

        /// <summary>Spec 041 (execute.v2) — messages dropped beyond the engine's cap (10,000).</summary>
        [Key(11)]
        public int MessagesOmitted { get; set; }

        /// <summary>Spec 041 (session.database.v1) — the connection's database after the script, on every
        /// reply; null from older engines.</summary>
        [Key(12)]
        public string? CurrentDatabase { get; set; }

        /// <summary>Spec 042 (FR-016): the transaction state after the script (0 None, 1 Open, 2 Aborted); always 0 for SQL Server.</summary>
        [Key(13)]
        public int TransactionState { get; set; }
    }

    /// <summary>Spec 041 (R20) — one server or engine message of an execute, in stream order.</summary>
    [MessagePackObject]
    public sealed class ExecuteMessageDto
    {
        /// <summary>See <see cref="ExecuteMessageKind"/>.</summary>
        [Key(0)]
        public int Kind { get; set; }

        /// <summary>The message text as the server sent it (no "Msg …" header).</summary>
        [Key(1)]
        public string Text { get; set; } = string.Empty;

        /// <summary>The batch (0-based) the message belongs to.</summary>
        [Key(2)]
        public int BatchIndex { get; set; }

        /// <summary>The error number; 0 for PRINT and engine messages.</summary>
        [Key(3)]
        public int Number { get; set; }

        /// <summary>The severity (<c>SqlError.Class</c>).</summary>
        [Key(4)]
        public int Severity { get; set; }

        [Key(5)]
        public int State { get; set; }

        /// <summary>The line within the batch (1-based); 0 when the server gave none.</summary>
        [Key(6)]
        public int Line { get; set; }

        /// <summary>The procedure the error was raised in, or null.</summary>
        [Key(7)]
        public string? Procedure { get; set; }

        /// <summary>The result set a row count belongs to; -1 for none.</summary>
        [Key(8)]
        public int ResultSetIndex { get; set; } = -1;

        /// <summary>The statement's row count (RowCount messages); -1 for none.</summary>
        [Key(9)]
        public long RowCount { get; set; } = -1;

        /// <summary>Spec 042 (FR-014): the PostgreSQL SQLSTATE (<c>42P01</c>); null for SQL Server messages and for notices without one.</summary>
        [Key(10)]
        public string? SqlState { get; set; }

        /// <summary>Spec 042: the server's DETAIL text, or null.</summary>
        [Key(11)]
        public string? Detail { get; set; }

        /// <summary>Spec 042: the server's HINT text, or the engine's own hint (for example that <c>GO</c> is SQL Server-only), or null.</summary>
        [Key(12)]
        public string? Hint { get; set; }

        /// <summary>Spec 042: the 1-based column within <see cref="Line"/> of the error position; 0 when unknown.</summary>
        [Key(13)]
        public int Column { get; set; }
    }

    /// <summary>Spec 041 (R20) — one batch of an execute, after GO splitting.</summary>
    [MessagePackObject]
    public sealed class ExecuteBatchDto
    {
        [Key(0)]
        public int Index { get; set; }

        /// <summary>The 1-based line of the submitted SQL where the batch text starts; the batch is sent
        /// verbatim, so the server's line 1 is this line.</summary>
        [Key(1)]
        public int StartLine { get; set; } = 1;

        [Key(2)]
        public int StartOffset { get; set; }

        [Key(3)]
        public int EndOffset { get; set; }

        /// <summary>See <see cref="ExecuteStatus"/> (Skipped when the batch never ran).</summary>
        [Key(4)]
        public int Status { get; set; }

        [Key(5)]
        public long ElapsedMs { get; set; }

        /// <summary>How many times the batch ran (<c>GO n</c>), at least 1; clamped by the engine.</summary>
        [Key(6)]
        public int RepeatCount { get; set; } = 1;

        [Key(7)]
        public int ErrorCount { get; set; }
    }

    /// <summary>Values of <see cref="ExecuteMessageDto.Kind"/>.</summary>
    public static class ExecuteMessageKind
    {
        /// <summary>PRINT, or a server message of severity 10 or lower.</summary>
        public const int Info = 0;

        /// <summary>A statement's row count.</summary>
        public const int RowCount = 1;

        /// <summary>A server error (severity 11 or higher).</summary>
        public const int Error = 2;

        /// <summary>A line the engine adds (loop start and end, skipped batches, omitted messages).</summary>
        public const int System = 3;
    }

    /// <summary>Browser → Engine: cancel a (possibly queued) execute by QueryId. NOTIFICATION — no reply.</summary>
    [MessagePackObject]
    public sealed class ExecuteCancelRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public string QueryId { get; set; } = string.Empty;
    }

    /// <summary>One bound cell for a CRUD statement (SET value or WHERE key).</summary>
    [MessagePackObject]
    public sealed class CrudCellDto
    {
        /// <summary>The real underlying column name (provenance BaseColumnName).</summary>
        [Key(0)]
        public string BaseColumnName { get; set; } = string.Empty;

        /// <summary>SqlDbType as int.</summary>
        [Key(1)]
        public int ProviderType { get; set; }

        [Key(2)]
        public int? Size { get; set; }

        [Key(3)]
        public int? Precision { get; set; }

        [Key(4)]
        public int? Scale { get; set; }

        /// <summary>Invariant-culture cell text (same encoding as the read path); null == SQL NULL.</summary>
        [Key(5)]
        public string? Value { get; set; }

        /// <summary>Spec 042 (R11): the PostgreSQL type name (<c>format_type</c> text) used for the <c>$n::type</c> cast; null for SQL Server.</summary>
        [Key(6)]
        public string? TypeName { get; set; }
    }

    /// <summary>One row-level edit: Update, Insert, or Delete. See <see cref="CrudOp"/>.</summary>
    [MessagePackObject]
    public sealed class CrudEditDto
    {
        [Key(0)]
        public int Op { get; set; }

        /// <summary>Columns to write (UPDATE SET / INSERT VALUES). Empty for Delete.</summary>
        [Key(1)]
        public CrudCellDto[] SetCells { get; set; } = System.Array.Empty<CrudCellDto>();

        /// <summary>Key columns for the WHERE clause (UPDATE/DELETE). Empty for Insert.</summary>
        [Key(2)]
        public CrudCellDto[] KeyCells { get; set; } = System.Array.Empty<CrudCellDto>();
    }

    /// <summary>Browser → Engine: apply a batch of grid edits against one base table, one transaction.</summary>
    [MessagePackObject]
    public sealed class ApplyChangesRequest
    {
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        [Key(1)]
        public string? BaseCatalog { get; set; }

        [Key(2)]
        public string BaseSchema { get; set; } = string.Empty;

        [Key(3)]
        public string BaseTable { get; set; } = string.Empty;

        [Key(4)]
        public CrudEditDto[] Edits { get; set; } = System.Array.Empty<CrudEditDto>();

        /// <summary>
        /// Spec 042 (FR-133, R11): the result set's base columns, in result order. A PostgreSQL UPDATE or INSERT returns exactly these
        /// from the written row (<c>RETURNING a, b, c</c>), so the grid shows what the server stored - defaults, generated keys, what
        /// a trigger changed - and reads back nothing else. SQL Server ignores it. Null or empty returns nothing.
        /// </summary>
        [Key(5)]
        public string[]? ReturnColumns { get; set; }
    }

    /// <summary>Per-edit result so the grid can flag the exact row that failed.</summary>
    [MessagePackObject]
    public sealed class CrudEditResult
    {
        [Key(0)]
        public int Index { get; set; }

        [Key(1)]
        public bool Ok { get; set; }

        [Key(2)]
        public string? Error { get; set; }

        [Key(3)]
        public int RowsAffected { get; set; }

        /// <summary>SCOPE_IDENTITY() for an INSERT into an identity table, as invariant text; else null.</summary>
        [Key(4)]
        public string? NewIdentity { get; set; }

        /// <summary>
        /// Spec 042 (FR-133): the written row as the server holds it, in the order of <see cref="ApplyChangesRequest.ReturnColumns"/>
        /// (PostgreSQL <c>RETURNING</c>). Null for a delete, for SQL Server, and when no columns were asked for.
        /// </summary>
        [Key(5)]
        public string?[]? Row { get; set; }
    }

    /// <summary>Engine → Browser: outcome of an ApplyChanges batch (NEVER null).</summary>
    [MessagePackObject]
    public sealed class ApplyChangesResult
    {
        [Key(0)]
        public int Status { get; set; }

        [Key(1)]
        public string? ErrorMessage { get; set; }

        [Key(2)]
        public CrudEditResult[] Results { get; set; } = System.Array.Empty<CrudEditResult>();

        /// <summary>True when the persistent connection was found broken and silently reopened during
        /// the apply (any #temp/SET/transaction state the edits assumed was lost). Surfaced, not swallowed.</summary>
        [Key(3)]
        public bool ConnectionWasReset { get; set; }
    }

    /// <summary>Status codes for <see cref="ExecuteQueryResult.Status"/> / <see cref="ApplyChangesResult.Status"/>.</summary>
    public static class ExecuteStatus
    {
        public const int Ok = 0;
        public const int Error = 1;
        public const int Cancelled = 2;
        public const int TimedOut = 3;
        public const int NoConnection = 4;

        /// <summary>Spec 041 — a batch that never ran (the connection closed, or an earlier batch timed out).</summary>
        public const int Skipped = 5;

        /// <summary>Spec 041 — client-only: the bridge dropped before the reply came.</summary>
        public const int Disconnected = 6;

        /// <summary>Spec 041 — client-only: no reply within the deadline.</summary>
        public const int NoReply = 7;
    }

    /// <summary>
    /// The time limits of one execute, shared by the engine (which enforces them) and the web (which waits
    /// for the reply on the same figure), so the two always agree.
    /// </summary>
    public static class ExecuteLimits
    {
        /// <summary>The longest one command may run, whatever the request asks.</summary>
        public const int CommandTimeoutCeilingSeconds = 600;

        /// <summary>The command timeout the engine uses when the request gives none.</summary>
        public const int CommandTimeoutDefaultSeconds = 30;

        /// <summary>The longest a whole script may run: the engine connection serves one request at a time, so a
        /// script without an end (<c>WAITFOR … GO 10000</c>) would block completion, schema refresh and Cancel.</summary>
        public const int ScriptTimeLimitCeilingSeconds = 3600;

        /// <summary>The per-command timeout the engine applies for a requested <paramref name="requestedSeconds"/>.</summary>
        public static int CommandTimeoutSeconds(int requestedSeconds)
        {
            int seconds = requestedSeconds <= 0 ? CommandTimeoutDefaultSeconds : requestedSeconds;
            return Math.Min(seconds, CommandTimeoutCeilingSeconds);
        }

        /// <summary>
        /// How long a whole script may run, start to last row: the command timeout for each command it runs
        /// (<paramref name="commandRuns"/> = its batches, each as many times as its <c>GO n</c> says), at most
        /// <see cref="ScriptTimeLimitCeilingSeconds"/>. The engine stops the script there and replies.
        /// </summary>
        public static TimeSpan ScriptTimeLimit(int requestedTimeoutSeconds, long commandRuns)
        {
            double seconds = (double)CommandTimeoutSeconds(requestedTimeoutSeconds) * Math.Max(1, commandRuns);
            return TimeSpan.FromSeconds(Math.Min(seconds, ScriptTimeLimitCeilingSeconds));
        }
    }

    /// <summary>Per-column CLR parse hints carried in <see cref="ExecuteResultSet.ClrTypeHints"/>.</summary>
    public static class ClrTypeHint
    {
        public const int String = 0;
        public const int Int64 = 1;
        public const int Double = 2;
        public const int Decimal = 3;
        public const int Bool = 4;
        public const int DateTime = 5;
        public const int DateTimeOffset = 6;
        public const int Guid = 7;
        public const int Binary = 8;
        public const int Variant = 9;

        /// <summary>Spec 042 (R10): a PostgreSQL column read as the server's own text (<c>{1,2,3}</c>, <c>1 day 02:00:00</c>, <c>\xdeadbeef</c>); nothing to parse back.</summary>
        public const int ServerText = 10;

        /// <summary>Spec 042 (R10): a PostgreSQL numeric column (integer types, <c>real</c>, <c>double precision</c>, <c>numeric</c>) as server text; the grid aligns and sorts it as a number.</summary>
        public const int ServerNumber = 11;
    }

    /// <summary>Operation codes for <see cref="CrudEditDto.Op"/>.</summary>
    public static class CrudOp
    {
        public const int Update = 0;
        public const int Insert = 1;
        public const int Delete = 2;
    }
}
