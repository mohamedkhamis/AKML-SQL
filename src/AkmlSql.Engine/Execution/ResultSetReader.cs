using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Text;
using AkmlSql.Engine.Schema;
using Microsoft.Data.SqlClient;
using Serilog;

namespace AkmlSql.Engine.Execution
{
    /// <summary>
    /// Spec 030 — Phase 5. Reads a live <see cref="SqlDataReader"/> into <see cref="ExecuteResultSet"/>[]
    /// using SAFE string encoding. Provenance comes from the SAME data reader opened with
    /// <see cref="CommandBehavior.KeyInfo"/> (NO separate SchemaOnly/FMTONLY pass — that would
    /// re-execute the batch and break #temp persistence) via <c>GetColumnSchema()</c>; provenance
    /// extraction is strictly best-effort and a failure simply makes the result set read-only.
    /// </summary>
    public sealed class ResultSetReader
    {
        // Conservative cumulative byte budget across ALL result sets in one ExecuteQueryResult — the
        // whole thing must serialize under the 16 MB FrameProtocol cap, so we stop well below it.
        private const long ByteBudget = 15L * 1024 * 1024;

        // Binary cells larger than this are replaced with a "[binary N bytes]" indicator on the wire.
        private const int BlobIndicatorThreshold = 64 * 1024;

        private readonly DatabaseCache? _dbCache;
        private readonly Func<string?>? _currentDatabase;
        private readonly Func<string, DatabaseCache?>? _cacheFor;

        public ResultSetReader(DatabaseCache? dbCache)
        {
            _dbCache = dbCache;
        }

        /// <summary>
        /// A reader for a script that may change database (<c>USE</c>) between its sets: each set is read
        /// in the database <paramref name="currentDatabase"/> reports when it begins. A set over a two-part
        /// name gets that database as its <see cref="ExecuteResultSet.BaseCatalog"/>, so Apply writes where
        /// the rows came from, and its primary-key cross-check uses that database's schema
        /// (<paramref name="cacheFor"/>, null when not loaded).
        /// </summary>
        public ResultSetReader(Func<string?> currentDatabase, Func<string, DatabaseCache?> cacheFor)
        {
            _currentDatabase = currentDatabase ?? throw new ArgumentNullException(nameof(currentDatabase));
            _cacheFor = cacheFor ?? throw new ArgumentNullException(nameof(cacheFor));
        }

        /// <summary>
        /// Drain the reader into result sets, enforcing the row cap and the cumulative byte budget.
        /// Captures provenance per result set when <paramref name="includeProvenance"/> is set.
        /// </summary>
        public async Task<List<ExecuteResultSet>> ReadAllAsync(
            SqlDataReader reader,
            int maxRows,
            bool includeProvenance,
            CancellationToken ct)
        {
            var sink = new ListSink(ByteBudget);
            await ReadAllAsync(reader, maxRows, includeProvenance, sink, ct).ConfigureAwait(false);
            return sink.Sets;
        }

        /// <summary>
        /// Spec 041 (R17, R19, R23) — drain the reader into <paramref name="sink"/>: each set is announced
        /// (<see cref="IResultSink.BeginSet"/>) before its rows are read, so a statement's row count that
        /// arrives while the set is current lands on it; rows stop at the cap or at the sink's budget, which
        /// one script shares across its batches and messages. A set the sink declines (over its set cap) is
        /// drained without keeping anything.
        /// </summary>
        public async Task ReadAllAsync(
            SqlDataReader reader,
            int maxRows,
            bool includeProvenance,
            IResultSink sink,
            CancellationToken ct)
        {
            do
            {
                // A non-row statement (INSERT/UPDATE/DDL) has FieldCount 0 — skip it as a result set.
                if (reader.FieldCount == 0) continue;

                var set = await ReadOneAsync(reader, maxRows, includeProvenance, sink, ct).ConfigureAwait(false);
                if (set != null) sink.Consume(EstimateSetBytes(set));
            }
            while (await reader.NextResultAsync(ct).ConfigureAwait(false));
        }

        private async Task<ExecuteResultSet?> ReadOneAsync(
            SqlDataReader reader,
            int maxRows,
            bool includeProvenance,
            IResultSink sink,
            CancellationToken ct)
        {
            int rawFieldCount = reader.FieldCount;

            // Under CommandBehavior.KeyInfo (the provenance path) SQL Server appends HIDDEN key columns
            // to the result set so each row is identifiable — e.g. a SELECT over a view (sys.databases)
            // or a base table whose PRIMARY KEY is not in the SELECT list. They inflate FieldCount and
            // must never reach the grid (a phantom "id" beside the one column the user actually asked
            // for). GetColumnSchema().IsHidden flags them; read it once here and reuse it for provenance.
            // Best-effort: if the schema can't be read we fall back to every column (prior behaviour).
            // Spec 041 (R10): the column schema is read for every set — it carries the declared length,
            // precision and scale the grid shows (nvarchar(50), decimal(10,2), datetime2(3)).
            ReadOnlyCollection<DbColumn>? cols = null;
            try { cols = reader.GetColumnSchema(); } catch { cols = null; }
            // displayOrdinals[displayIndex] = reader ordinal, hidden columns removed. null when nothing
            // is hidden (the common case) → identity mapping with no extra allocation or indirection.
            int[]? displayOrdinals = BuildVisibleOrdinals(cols, rawFieldCount);
            int fieldCount = displayOrdinals?.Length ?? rawFieldCount;

            var columnNames = new string[fieldCount];
            var columnSqlTypes = new string[fieldCount];
            var clrHints = new int[fieldCount];
            var sqlDbTypes = new SqlDbType[fieldCount];

            for (int i = 0; i < fieldCount; i++)
            {
                int ord = displayOrdinals?[i] ?? i;
                columnNames[i] = reader.GetName(ord);
                columnSqlTypes[i] = DeclaredTypeName(reader, cols, ord);
                sqlDbTypes[i] = ResolveSqlDbType(reader, ord);
                clrHints[i] = SqlScalarEncoder.ClrHint(sqlDbTypes[i]);
            }

            var set = new ExecuteResultSet
            {
                ColumnNames = columnNames,
                ColumnSqlTypes = columnSqlTypes,
                ClrTypeHints = clrHints,
            };

            // Provenance FIRST (off the column schema; no rows read yet) — best-effort, never throws out.
            if (includeProvenance)
            {
                string? sourceDatabase = null;
                try { sourceDatabase = NullIfEmpty(_currentDatabase?.Invoke()); }
                catch (Exception ex) { Log.Debug(ex, "ResultSetReader: the connection's database could not be read."); }
                TryPopulateProvenance(cols, displayOrdinals, fieldCount, sqlDbTypes, set, sourceDatabase);
            }

            // Announce the set before its rows: the statement's row count arrives while it is current.
            if (sink.BeginSet(set) < 0)
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false)) { /* over the set cap: drain */ }
                return null;
            }
            long byteBudgetRemaining = sink.BudgetRemaining;

            // Rows.
            var rows = new List<string?[]>();
            long approxBytes = 0;
            bool truncated = false;
            int omitted = 0;

            // Once truncated we keep counting omitted rows for an accurate banner, but only up to a
            // bounded peek so a runaway result set can't make us drain millions of rows just to count
            // them (the cap/timeout are the DoS bound; the banner just needs a representative figure).
            const int MaxOmittedCount = 10_000;

            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (rows.Count >= maxRows || approxBytes >= byteBudgetRemaining)
                {
                    truncated = true;
                    omitted++;
                    if (omitted >= MaxOmittedCount) break; // stop scanning; RowsOmitted is a lower bound.
                    continue;
                }

                var row = new string?[fieldCount];
                for (int c = 0; c < fieldCount; c++)
                {
                    int ord = displayOrdinals?[c] ?? c;
                    string? cell = EncodeCell(reader, ord, sqlDbTypes[c]);
                    row[c] = cell;
                    approxBytes += EstimateCellBytes(cell);
                }
                rows.Add(row);
            }

            set.Rows = rows.ToArray();
            set.Truncated = truncated;
            set.RowsOmitted = omitted;
            return set;
        }

        /// <summary>
        /// Map display column index → reader ordinal, skipping HIDDEN columns — the key columns
        /// <see cref="CommandBehavior.KeyInfo"/> appends so each row is identifiable when the base
        /// table's key isn't in the SELECT list. Returns <c>null</c> when nothing is hidden (the
        /// overwhelmingly common case) so the hot path keeps identity ordinals with no allocation.
        /// A result set whose only key column is hidden is correctly NOT editable: to edit rows the
        /// user must include the key in the SELECT (SSMS-like), rather than the grid silently showing
        /// a phantom key column it added behind their back.
        /// </summary>
        internal static int[]? BuildVisibleOrdinals(ReadOnlyCollection<DbColumn>? cols, int rawFieldCount)
        {
            if (cols == null) return null;

            bool anyHidden = false;
            for (int i = 0; i < rawFieldCount && i < cols.Count; i++)
            {
                if (cols[i].IsHidden == true) { anyHidden = true; break; }
            }
            if (!anyHidden) return null;

            var visible = new List<int>(rawFieldCount);
            for (int i = 0; i < rawFieldCount; i++)
            {
                bool hidden = i < cols.Count && cols[i].IsHidden == true;
                if (!hidden) visible.Add(i);
            }
            return visible.ToArray();
        }

        /// <summary>Spec 041 (N2): SqlClient's 255 "not applicable" precision/scale sentinel becomes null.</summary>
        internal static int? ApplicableNumeric(int? value) => value is int v && v != 255 ? v : null;

        private static string? EncodeCell(SqlDataReader reader, int ordinal, SqlDbType sqlDbType)
        {
            if (reader.IsDBNull(ordinal)) return null;

            // Large BINARY → "[binary N bytes]" indicator (DoS / frame-cap protection).
            if (sqlDbType is SqlDbType.VarBinary or SqlDbType.Binary or SqlDbType.Image or SqlDbType.Timestamp)
            {
                long len = reader.GetBytes(ordinal, 0, null, 0, 0);
                if (len > BlobIndicatorThreshold)
                {
                    return $"[binary {len} bytes]";
                }
            }
            // Large CHARACTER LOB → "[text N chars]" indicator. The cumulative byte budget is only
            // checked BETWEEN rows, so a single nvarchar(max)/text/ntext/xml cell could blow the 16 MB
            // frame on its own. GetChars(…, null, …) returns the length WITHOUT materializing the value.
            else if (sqlDbType is SqlDbType.NVarChar or SqlDbType.VarChar or SqlDbType.Char or SqlDbType.NChar
                              or SqlDbType.Text or SqlDbType.NText or SqlDbType.Xml)
            {
                long clen = -1;
                try { clen = reader.GetChars(ordinal, 0, null, 0, 0); }
                catch { /* xml / unsupported → fall through to the post-materialisation backstop below */ }
                if (clen > BlobIndicatorThreshold)
                {
                    return $"[text {clen} chars]";
                }
            }

            var value = reader.GetValue(ordinal);
            var encoded = SqlScalarEncoder.Encode(value);
            // Backstop: any encoded cell that is still oversized (e.g. an xml cell GetChars couldn't
            // size, or a sql_variant holding a huge string) is replaced with the same indicator.
            if (encoded != null && encoded.Length > BlobIndicatorThreshold)
            {
                return $"[text {encoded.Length} chars]";
            }
            return encoded;
        }

        /// <summary>Estimate a cell's serialized size in BYTES. MessagePack encodes strings as UTF-8,
        /// so a UTF-16 <c>Length*2</c> estimate UNDER-counts non-ASCII (CJK etc.) and could let a
        /// 15 MB-estimated payload reach ~22 MB on the wire and overflow the 16 MB frame.</summary>
        private static long EstimateCellBytes(string? cell)
            => cell == null ? 1 : System.Text.Encoding.UTF8.GetByteCount(cell) + 5; // +5 ≈ MessagePack str header

        /// <summary>
        /// Populate per-column provenance + the CRUD-eligibility predicate from the live reader's
        /// column schema (KeyInfo). Best-effort: any failure leaves the set read-only and never
        /// disturbs the data read.
        /// </summary>
        private void TryPopulateProvenance(
            ReadOnlyCollection<DbColumn>? cols, int[]? displayOrdinals, int fieldCount, SqlDbType[] sqlDbTypes, ExecuteResultSet set,
            string? sourceDatabase)
        {
            if (cols == null)
            {
                set.Provenance = Array.Empty<ColumnProvenanceDto>();
                set.IsEditable = false;
                return;
            }
            try
            {
                var prov = new ColumnProvenanceDto[fieldCount];

                string? baseCatalog = null, baseSchema = null, baseTable = null;
                bool multipleBaseTables = false;
                bool anyRealBaseTable = false;
                bool anyKey = false;

                for (int i = 0; i < fieldCount; i++)
                {
                    int ord = displayOrdinals?[i] ?? i;
                    if (ord >= cols.Count) continue;
                    var col = cols[ord];
                    bool isExpr = col.IsExpression == true;
                    string? bTable = col.BaseTableName;
                    string? bSchema = col.BaseSchemaName;
                    string? bCatalog = NullIfEmpty(GetBaseCatalog(col));

                    // EFFECTIVE base column name: Microsoft.Data.SqlClient only populates BaseColumnName
                    // when the result column is ALIASED (e.g. "Id AS Ident"); for an unaliased column the
                    // result alias (ColumnName) IS the base column name. Verified empirically against
                    // Microsoft.Data.SqlClient 7.x under CommandBehavior.KeyInfo. A computed column has an
                    // empty BaseColumnName AND IsReadOnly=true, so it stays out of SET/INSERT regardless.
                    string? effectiveBaseCol = NullIfEmpty(col.BaseColumnName)
                        ?? (isExpr ? null : NullIfEmpty(col.ColumnName));

                    bool isKey = col.IsKey == true && !string.IsNullOrEmpty(effectiveBaseCol);
                    if (isKey) anyKey = true;

                    if (!isExpr && !string.IsNullOrEmpty(bTable))
                    {
                        anyRealBaseTable = true;
                        if (baseTable == null)
                        {
                            baseTable = bTable;
                            baseSchema = bSchema;
                            baseCatalog = bCatalog;
                        }
                        else if (!string.Equals(baseTable, bTable, StringComparison.OrdinalIgnoreCase)
                              || !string.Equals(baseSchema ?? string.Empty, bSchema ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                        {
                            multipleBaseTables = true; // a join → not editable.
                        }
                    }

                    prov[i] = new ColumnProvenanceDto
                    {
                        Ordinal = i,
                        BaseColumnName = effectiveBaseCol,
                        IsKey = isKey,
                        IsAutoIncrement = col.IsAutoIncrement == true,
                        IsReadOnly = col.IsReadOnly == true,
                        IsExpression = isExpr,
                        AllowDBNull = col.AllowDBNull == true,
                        ProviderType = (int)sqlDbTypes[i],
                        ColumnSize = col.ColumnSize,
                        // Spec 041 (N2): SqlClient reports 255 where precision/scale do not apply (int,
                        // money, date, uniqueidentifier). Forwarded, it became SqlParameter.Scale = 255 on
                        // Apply and broke edits to date and money columns; null means "not applicable".
                        Precision = ApplicableNumeric(col.NumericPrecision),
                        Scale = ApplicableNumeric(col.NumericScale),
                    };
                }

                // Cross-check IsKey columns against the schema cache's declared PRIMARY KEY so the
                // writer prefers the true PK over an arbitrary unique key KeyInfo may pick.
                // A two-part name is read from the database the connection is in when the set begins.
                if (!string.IsNullOrEmpty(baseTable) && baseCatalog == null) baseCatalog = sourceDatabase;

                if (!string.IsNullOrEmpty(baseTable))
                {
                    set.DefaultsKnown = CrossCheckPrimaryKey(SchemaFor(baseCatalog), prov, baseSchema, baseTable!);
                }

                // CRUD-eligibility predicate: single real base table across non-expression cols,
                // a real base table, and >= 1 key column.
                bool editable = anyRealBaseTable && !multipleBaseTables && anyKey && !string.IsNullOrEmpty(baseTable);

                set.Provenance = prov;
                set.IsEditable = editable;
                set.BaseCatalog = baseCatalog;
                set.BaseSchema = baseSchema;
                set.BaseTable = baseTable;
            }
            catch (Exception ex)
            {
                // Best-effort: any provenance failure → read-only set. Never disturbs the data read.
                Log.Debug(ex, "ResultSetReader: provenance extraction failed — result set treated as read-only.");
                set.Provenance = Array.Empty<ColumnProvenanceDto>();
                set.IsEditable = false;
            }
        }

        /// <summary>The schema of <paramref name="catalog"/>; the fixed cache for a single-database reader.</summary>
        private DatabaseCache? SchemaFor(string? catalog)
        {
            if (_cacheFor == null) return _dbCache;
            if (string.IsNullOrEmpty(catalog)) return null;
            try { return _cacheFor(catalog!); }
            catch (Exception ex)
            {
                Log.Debug(ex, "ResultSetReader: no schema cache for {Catalog}.", catalog);
                return null;
            }
        }

        /// <returns>True when the schema had the table's columns, so <see cref="ColumnProvenanceDto.HasDefault"/> is known.</returns>
        private static bool CrossCheckPrimaryKey(DatabaseCache? dbCache, ColumnProvenanceDto[] prov, string? schema, string table)
        {
            if (dbCache == null) return false;
            try
            {
                var obj = dbCache.FindObject(schema ?? string.Empty, table);
                if (obj == null && !string.IsNullOrEmpty(schema))
                {
                    obj = dbCache.FindObject(string.Empty, table);
                }
                if (obj == null || obj.Columns.Count == 0) return false;

                var pkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var defaulted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in obj.Columns)
                {
                    if (c.IsPrimaryKey) pkNames.Add(c.ColumnName);
                    if (c.DefaultValue != null) defaulted.Add(c.ColumnName);   // review WEB-20: the server fills it in
                }
                foreach (var p in prov)
                {
                    if (!string.IsNullOrEmpty(p.BaseColumnName) && defaulted.Contains(p.BaseColumnName!)) p.HasDefault = true;
                }
                if (pkNames.Count == 0) return true;

                foreach (var p in prov)
                {
                    if (!string.IsNullOrEmpty(p.BaseColumnName) && pkNames.Contains(p.BaseColumnName!))
                    {
                        p.IsTruePrimaryKey = true;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ResultSetReader: PK cross-check failed (non-fatal).");
                return false;
            }
        }

        private static string? GetBaseCatalog(DbColumn col)
        {
            // DbColumn has no BaseCatalogName on the base type; Microsoft.Data.SqlClient exposes it via
            // the provider-specific [string] indexer. Try it, swallow if unsupported.
            try { return col["BaseCatalogName"] as string; }
            catch { return null; }
        }

        private static SqlDbType ResolveSqlDbType(SqlDataReader reader, int ordinal)
        {
            try
            {
                // SqlDataReader.GetProviderSpecificFieldType / GetFieldType give the CLR type; the most
                // reliable SqlDbType source is the schema's ProviderType. Fall back to CLR-type inference.
                var clr = reader.GetFieldType(ordinal);
                return SqlScalarEncoder.InferSqlDbType(clr);
            }
            catch
            {
                return SqlDbType.NVarChar;
            }
        }

        private static string SafeDataTypeName(SqlDataReader reader, int ordinal)
        {
            try { return reader.GetDataTypeName(ordinal); }
            catch { return "sql_variant"; }
        }

        /// <summary>Spec 041 (R10) — the declared type (<c>nvarchar(50)</c>, <c>decimal(10,2)</c>,
        /// <c>datetime2(3)</c>) composed from the column schema; the bare name when the schema is missing.</summary>
        private static string DeclaredTypeName(SqlDataReader reader, ReadOnlyCollection<DbColumn>? cols, int ordinal)
        {
            var bare = SafeDataTypeName(reader, ordinal);
            if (cols == null || ordinal >= cols.Count) return bare;
            try
            {
                var col = cols[ordinal];
                var declared = SqlTypeName.Format(string.IsNullOrEmpty(col.DataTypeName) ? bare : col.DataTypeName,
                    col.ColumnSize, col.NumericPrecision, col.NumericScale);
                return string.IsNullOrEmpty(declared) ? bare : declared;
            }
            catch
            {
                return bare;
            }
        }

        /// <summary>The pre-spec-041 behaviour: every set kept, one budget for this reader alone.</summary>
        private sealed class ListSink : IResultSink
        {
            public ListSink(long budget) => BudgetRemaining = budget;

            public List<ExecuteResultSet> Sets { get; } = new();

            public long BudgetRemaining { get; private set; }

            public void Consume(long bytes) => BudgetRemaining = Math.Max(0, BudgetRemaining - Math.Max(0, bytes));

            public int BeginSet(ExecuteResultSet set)
            {
                Sets.Add(set);
                return Sets.Count - 1;
            }
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

        private static long EstimateSetBytes(ExecuteResultSet set)
        {
            long bytes = 0;
            foreach (var row in set.Rows)
            {
                foreach (var cell in row)
                {
                    bytes += EstimateCellBytes(cell);
                }
            }
            return bytes;
        }
    }
}
