using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.Data.SqlClient;

namespace AkmlSql.Engine.Execution
{
    /// <summary>
    /// Spec 030 — Phase 5. Builds PARAMETERIZED <see cref="SqlCommand"/>s from a single
    /// <see cref="CrudEditDto"/>. Identifiers are wrapped with <see cref="QuoteName"/> (QUOTENAME
    /// semantics — the only <c>]</c>→<c>]]</c> escaper in the codebase); parameter NAMES are synthetic
    /// (<c>@p0</c>/<c>@k0</c>) because a real BaseColumnName can contain spaces/specials illegal in a
    /// <c>@identifier</c>. Every value is bound as a typed <see cref="SqlParameter"/> via
    /// <see cref="SqlScalarEncoder.Decode"/> — never inlined. UPDATE/DELETE with zero key cells are
    /// REFUSED (no safe WHERE → never a full-table write).
    /// </summary>
    public static class CrudWriteGenerator
    {
        /// <summary>Deliberate hard ceiling for a single CRUD command. Single-row writes finish instantly;
        /// this only bounds a runaway trigger (the ADO default would otherwise apply implicitly).</summary>
        private const int ApplyCommandTimeoutSeconds = 600;

        /// <summary>QUOTENAME-equivalent: wrap an identifier in brackets, doubling any embedded <c>]</c>.</summary>
        public static string QuoteName(string id)
        {
            return "[" + (id ?? string.Empty).Replace("]", "]]") + "]";
        }

        /// <summary>
        /// Build the three-part (or two-part) quoted table reference: [catalog].[schema].[table].
        /// A catalog is included only when non-empty so the command stays valid on the current DB.
        /// </summary>
        public static string BuildQualifiedTable(string? catalog, string schema, string table)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(catalog))
            {
                sb.Append(QuoteName(catalog!)).Append('.');
            }
            // Schema may be empty (default schema) — emit it only when present.
            if (!string.IsNullOrEmpty(schema))
            {
                sb.Append(QuoteName(schema)).Append('.');
            }
            sb.Append(QuoteName(table));
            return sb.ToString();
        }

        /// <summary>
        /// Build a parameterized <see cref="SqlCommand"/> for one edit, attached to
        /// <paramref name="connection"/> and (optionally) <paramref name="transaction"/>. The caller
        /// owns disposal of the returned command.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown for a keyless UPDATE/DELETE, an empty INSERT, or an empty table name — the
        /// handler converts this into a per-edit error rather than emitting unsafe SQL.
        /// </exception>
        public static SqlCommand BuildCommand(
            ApplyChangesRequest req,
            CrudEditDto edit,
            SqlConnection connection,
            SqlTransaction? transaction)
        {
            if (string.IsNullOrWhiteSpace(req.BaseTable))
                throw new InvalidOperationException("Cannot build a CRUD command without a base table name.");

            var table = BuildQualifiedTable(req.BaseCatalog, req.BaseSchema, req.BaseTable);
            var cmd = connection.CreateCommand();
            cmd.CommandTimeout = ApplyCommandTimeoutSeconds;
            if (transaction != null) cmd.Transaction = transaction;

            switch (edit.Op)
            {
                case CrudOp.Update:
                    BuildUpdate(cmd, table, edit);
                    break;
                case CrudOp.Insert:
                    BuildInsert(cmd, table, edit);
                    break;
                case CrudOp.Delete:
                    BuildDelete(cmd, table, edit);
                    break;
                default:
                    cmd.Dispose();
                    throw new InvalidOperationException($"Unknown CRUD op {edit.Op}.");
            }

            return cmd;
        }

        private static void BuildUpdate(SqlCommand cmd, string table, CrudEditDto edit)
        {
            if (edit.KeyCells.Length == 0)
                throw new InvalidOperationException("Refusing to UPDATE without a key column (would update the whole table).");
            if (edit.SetCells.Length == 0)
                throw new InvalidOperationException("UPDATE has no columns to set.");

            var sb = new StringBuilder();
            sb.Append("UPDATE ").Append(table).Append(" SET ");

            for (int i = 0; i < edit.SetCells.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                var pName = "@p" + i;
                sb.Append(QuoteName(edit.SetCells[i].BaseColumnName)).Append(" = ").Append(pName);
                cmd.Parameters.Add(MakeParameter(pName, edit.SetCells[i]));
            }

            AppendWhereKeys(sb, cmd, edit.KeyCells);
            // SELECT @@ROWCOUNT so the handler reads the affected count via ExecuteScalar. Unlike
            // ExecuteNonQuery's return value, @@ROWCOUNT is NOT suppressed by SET NOCOUNT ON — which a
            // prior batch can leave active on the persistent session, making ExecuteNonQuery return -1.
            sb.Append("; SELECT @@ROWCOUNT;");
            cmd.CommandText = sb.ToString();
        }

        private static void BuildInsert(SqlCommand cmd, string table, CrudEditDto edit)
        {
            if (edit.SetCells.Length == 0)
                throw new InvalidOperationException("INSERT has no column values.");

            var cols = new StringBuilder();
            var vals = new StringBuilder();
            for (int i = 0; i < edit.SetCells.Length; i++)
            {
                if (i > 0) { cols.Append(", "); vals.Append(", "); }
                var pName = "@p" + i;
                cols.Append(QuoteName(edit.SetCells[i].BaseColumnName));
                vals.Append(pName);
                cmd.Parameters.Add(MakeParameter(pName, edit.SetCells[i]));
            }

            // SELECT SCOPE_IDENTITY() lets the handler echo the new identity back to the grid.
            cmd.CommandText = "INSERT INTO " + table + " (" + cols + ") VALUES (" + vals + "); SELECT SCOPE_IDENTITY();";
        }

        private static void BuildDelete(SqlCommand cmd, string table, CrudEditDto edit)
        {
            if (edit.KeyCells.Length == 0)
                throw new InvalidOperationException("Refusing to DELETE without a key column (would delete the whole table).");

            var sb = new StringBuilder();
            sb.Append("DELETE FROM ").Append(table);
            AppendWhereKeys(sb, cmd, edit.KeyCells);
            sb.Append("; SELECT @@ROWCOUNT;");   // see BuildUpdate — read count via ExecuteScalar, NOCOUNT-safe.
            cmd.CommandText = sb.ToString();
        }

        private static void AppendWhereKeys(StringBuilder sb, SqlCommand cmd, IReadOnlyList<CrudCellDto> keys)
        {
            sb.Append(" WHERE ");
            for (int i = 0; i < keys.Count; i++)
            {
                if (i > 0) sb.Append(" AND ");
                var kName = "@k" + i;
                // A NULL key cell must use IS NULL, not = @kN (which never matches NULL).
                if (keys[i].Value is null)
                {
                    sb.Append(QuoteName(keys[i].BaseColumnName)).Append(" IS NULL");
                }
                else
                {
                    sb.Append(QuoteName(keys[i].BaseColumnName)).Append(" = ").Append(kName);
                    cmd.Parameters.Add(MakeParameter(kName, keys[i]));
                }
            }
        }

        private static SqlParameter MakeParameter(string name, CrudCellDto cell)
        {
            // ProviderType is fully client-supplied (the bridge peer is a WASM browser). Reject a
            // bogus/tampered enum value with a clear per-edit error rather than casting to an undefined
            // SqlDbType (which yields an unpredictable parameter and a confusing driver exception).
            if (!Enum.IsDefined(typeof(SqlDbType), cell.ProviderType))
                throw new InvalidOperationException(
                    $"Column '{cell.BaseColumnName}' carried an invalid SQL type code ({cell.ProviderType}).");

            var sqlDbType = (SqlDbType)cell.ProviderType;
            var p = new SqlParameter(name, sqlDbType)
            {
                Value = SqlScalarEncoder.Decode(cell.Value, sqlDbType),
            };

            // Do NOT pin Size for length-bearing types: Microsoft.Data.SqlClient SILENTLY TRUNCATES the
            // value to a client-echoed column width. Leaving Size unset sizes the parameter to the actual
            // value, so SQL Server raises a real truncation error if it overflows the column instead.
            bool lengthBearing = sqlDbType is SqlDbType.NVarChar or SqlDbType.VarChar or SqlDbType.VarBinary
                                          or SqlDbType.Char or SqlDbType.NChar or SqlDbType.Binary
                                          or SqlDbType.Text or SqlDbType.NText or SqlDbType.Image or SqlDbType.Xml;
            if (!lengthBearing && cell.Size is int sz && sz > 0) p.Size = sz;
            if (cell.Precision is int pr && pr > 0) p.Precision = (byte)Math.Min(pr, byte.MaxValue);
            if (cell.Scale is int sc && sc >= 0) p.Scale = (byte)Math.Min(sc, byte.MaxValue);
            return p;
        }
    }
}
