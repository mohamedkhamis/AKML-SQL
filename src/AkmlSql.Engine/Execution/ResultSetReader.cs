using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
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

        public ResultSetReader(DatabaseCache? dbCache)
        {
            _dbCache = dbCache;
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
            var results = new List<ExecuteResultSet>();
            long byteBudgetRemaining = ByteBudget;

            do
            {
                // A non-row statement (INSERT/UPDATE/DDL) has FieldCount 0 — skip it as a result set.
                if (reader.FieldCount == 0) continue;

                var set = await ReadOneAsync(reader, maxRows, includeProvenance, byteBudgetRemaining, ct)
                    .ConfigureAwait(false);
                // Subtract a rough estimate of what this set consumed from the shared budget.
                byteBudgetRemaining -= EstimateSetBytes(set);
                if (byteBudgetRemaining < 0) byteBudgetRemaining = 0;
                results.Add(set);
            }
            while (await reader.NextResultAsync(ct).ConfigureAwait(false));

            return results;
        }

        private async Task<ExecuteResultSet> ReadOneAsync(
            SqlDataReader reader,
            int maxRows,
            bool includeProvenance,
            long byteBudgetRemaining,
            CancellationToken ct)
        {
            int fieldCount = reader.FieldCount;
            var columnNames = new string[fieldCount];
            var columnSqlTypes = new string[fieldCount];
            var clrHints = new int[fieldCount];
            var sqlDbTypes = new SqlDbType[fieldCount];

            for (int i = 0; i < fieldCount; i++)
            {
                columnNames[i] = reader.GetName(i);
                columnSqlTypes[i] = SafeDataTypeName(reader, i);
                sqlDbTypes[i] = ResolveSqlDbType(reader, i);
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
                TryPopulateProvenance(reader, fieldCount, sqlDbTypes, set);
            }

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
                    string? cell = EncodeCell(reader, c, sqlDbTypes[c]);
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
        private void TryPopulateProvenance(SqlDataReader reader, int fieldCount, SqlDbType[] sqlDbTypes, ExecuteResultSet set)
        {
            try
            {
                ReadOnlyCollection<DbColumn> cols = reader.GetColumnSchema();
                var prov = new ColumnProvenanceDto[fieldCount];

                string? baseCatalog = null, baseSchema = null, baseTable = null;
                bool multipleBaseTables = false;
                bool anyRealBaseTable = false;
                bool anyKey = false;

                for (int i = 0; i < fieldCount && i < cols.Count; i++)
                {
                    var col = cols[i];
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
                        Precision = col.NumericPrecision,
                        Scale = col.NumericScale,
                    };
                }

                // Cross-check IsKey columns against the schema cache's declared PRIMARY KEY so the
                // writer prefers the true PK over an arbitrary unique key KeyInfo may pick.
                if (!string.IsNullOrEmpty(baseTable))
                {
                    CrossCheckPrimaryKey(prov, baseSchema, baseTable!);
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

        private void CrossCheckPrimaryKey(ColumnProvenanceDto[] prov, string? schema, string table)
        {
            if (_dbCache == null) return;
            try
            {
                var obj = _dbCache.FindObject(schema ?? string.Empty, table);
                if (obj == null && !string.IsNullOrEmpty(schema))
                {
                    obj = _dbCache.FindObject(string.Empty, table);
                }
                if (obj == null || obj.Columns.Count == 0) return;

                var pkNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in obj.Columns)
                {
                    if (c.IsPrimaryKey) pkNames.Add(c.ColumnName);
                }
                if (pkNames.Count == 0) return;

                foreach (var p in prov)
                {
                    if (!string.IsNullOrEmpty(p.BaseColumnName) && pkNames.Contains(p.BaseColumnName!))
                    {
                        p.IsTruePrimaryKey = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ResultSetReader: PK cross-check failed (non-fatal).");
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
