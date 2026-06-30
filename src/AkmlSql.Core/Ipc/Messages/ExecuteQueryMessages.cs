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

        [Key(9)]
        public int? Precision { get; set; }

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

        /// <summary>SQL type names, e.g. "int", "nvarchar(50)".</summary>
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
    }

    /// <summary>Engine → Browser: the full execute outcome (NEVER null — a missing frame hangs the bridge).</summary>
    [MessagePackObject]
    public sealed class ExecuteQueryResult
    {
        [Key(0)]
        public string QueryId { get; set; } = string.Empty;

        /// <summary>See <see cref="ExecuteStatus"/>: 0 Ok, 1 Error, 2 Cancelled, 3 TimedOut, 4 NoConnection.</summary>
        [Key(1)]
        public int Status { get; set; }

        [Key(2)]
        public string? ErrorMessage { get; set; }

        [Key(3)]
        public ExecuteResultSet[] ResultSets { get; set; } = System.Array.Empty<ExecuteResultSet>();

        /// <summary>PRINT / low-severity RAISERROR / rows-affected message lines.</summary>
        [Key(4)]
        public string[] Messages { get; set; } = System.Array.Empty<string>();

        [Key(5)]
        public long ElapsedMs { get; set; }

        [Key(6)]
        public int TotalRowsAffected { get; set; }

        /// <summary>True when a prior #temp/SET/USE state was lost because the connection was reopened
        /// (the SSMS-like persistence guarantee was broken; surfaced, not silently swallowed).</summary>
        [Key(7)]
        public bool ConnectionWasReset { get; set; }
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
    }

    /// <summary>Operation codes for <see cref="CrudEditDto.Op"/>.</summary>
    public static class CrudOp
    {
        public const int Update = 0;
        public const int Insert = 1;
        public const int Delete = 2;
    }
}
