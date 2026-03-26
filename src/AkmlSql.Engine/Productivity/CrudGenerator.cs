#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AkmlSql.Core.Ipc.Messages;
using Serilog;

namespace AkmlSql.Engine.Productivity
{
    /// <summary>
    /// T100: Generates four CRUD stored procedures (Insert, Select, Update, Delete) from
    /// table metadata. Uses column information from the schema cache to build parameterized
    /// procedure bodies.
    /// </summary>
    internal sealed class CrudGenerator
    {
        /// <summary>
        /// Generates CRUD procedures for the given table using the provided column metadata.
        /// </summary>
        /// <param name="request">The CRUD generation request.</param>
        /// <param name="columns">Column metadata for the target table.</param>
        /// <param name="primaryKeyColumns">Names of the primary key columns.</param>
        /// <returns>A response containing the generated procedures.</returns>
        public CrudGenerationResponse Generate(
            CrudGenerationRequest request,
            IReadOnlyList<ColumnInfo> columns,
            IReadOnlyList<string> primaryKeyColumns)
        {
            try
            {
                if (columns.Count == 0)
                {
                    return new CrudGenerationResponse
                    {
                        Success = false,
                        Error = $"No columns found for [{request.SchemaName}].[{request.TableName}]"
                    };
                }

                var response = new CrudGenerationResponse { Success = true };
                var ops = request.Operations.ToUpperInvariant();
                var fqn = $"[{request.SchemaName}].[{request.TableName}]";

                if (ops.Contains("R"))
                    response.Procedures.Add(GenerateSelect(request, columns, primaryKeyColumns, fqn));

                if (ops.Contains("C"))
                    response.Procedures.Add(GenerateInsert(request, columns, primaryKeyColumns, fqn));

                if (ops.Contains("U"))
                    response.Procedures.Add(GenerateUpdate(request, columns, primaryKeyColumns, fqn));

                if (ops.Contains("D"))
                    response.Procedures.Add(GenerateDelete(request, columns, primaryKeyColumns, fqn));

                Log.Information("CrudGenerator: generated {Count} procedures for {Table}",
                    response.Procedures.Count, fqn);

                return response;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CrudGenerator: generation failed for {Schema}.{Table}",
                    request.SchemaName, request.TableName);
                return new CrudGenerationResponse
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private GeneratedProcedure GenerateSelect(
            CrudGenerationRequest request, IReadOnlyList<ColumnInfo> columns,
            IReadOnlyList<string> pkColumns, string fqn)
        {
            var procName = FormatProcName(request, "Select");
            var sb = new StringBuilder();

            AppendHeader(sb, procName, request.UseCreateOrAlter);

            // Parameters: PK columns only
            if (pkColumns.Count > 0)
            {
                foreach (var pk in pkColumns)
                {
                    var col = columns.FirstOrDefault(c =>
                        c.Name.Equals(pk, StringComparison.OrdinalIgnoreCase));
                    if (col != null)
                        sb.AppendLine($"    @{col.Name} {col.DataType},");
                }
                // Remove trailing comma
                RemoveTrailingComma(sb);
            }

            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");

            if (request.IncludeSetNocount)
                sb.AppendLine("    SET NOCOUNT ON;");

            sb.AppendLine();
            sb.Append("    SELECT ");
            sb.AppendLine(string.Join(", ", columns.Select(c => $"[{c.Name}]")));
            sb.AppendLine($"    FROM {fqn}");

            if (pkColumns.Count > 0)
            {
                sb.Append("    WHERE ");
                sb.AppendLine(string.Join(" AND ",
                    pkColumns.Select(pk => $"[{pk}] = @{pk}")));
            }

            sb.AppendLine("END");
            sb.AppendLine("GO");

            return new GeneratedProcedure
            {
                Operation = "Select",
                ProcedureName = procName,
                Sql = sb.ToString()
            };
        }

        private GeneratedProcedure GenerateInsert(
            CrudGenerationRequest request, IReadOnlyList<ColumnInfo> columns,
            IReadOnlyList<string> pkColumns, string fqn)
        {
            var procName = FormatProcName(request, "Insert");
            var sb = new StringBuilder();

            AppendHeader(sb, procName, request.UseCreateOrAlter);

            // Parameters: all non-identity, non-computed columns
            var insertableColumns = columns
                .Where(c => !c.IsIdentity && !c.IsComputed)
                .ToList();

            foreach (var col in insertableColumns)
            {
                sb.AppendLine($"    @{col.Name} {col.DataType},");
            }
            RemoveTrailingComma(sb);

            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");

            if (request.IncludeSetNocount)
                sb.AppendLine("    SET NOCOUNT ON;");

            sb.AppendLine();
            sb.Append("    INSERT INTO ");
            sb.AppendLine(fqn);
            sb.Append("    (");
            sb.Append(string.Join(", ", insertableColumns.Select(c => $"[{c.Name}]")));
            sb.AppendLine(")");
            sb.Append("    VALUES (");
            sb.Append(string.Join(", ", insertableColumns.Select(c => $"@{c.Name}")));
            sb.AppendLine(");");

            // Return the identity value if there's an identity column
            var identityCol = columns.FirstOrDefault(c => c.IsIdentity);
            if (identityCol != null)
            {
                sb.AppendLine();
                sb.AppendLine("    SELECT SCOPE_IDENTITY() AS NewId;");
            }

            sb.AppendLine("END");
            sb.AppendLine("GO");

            return new GeneratedProcedure
            {
                Operation = "Insert",
                ProcedureName = procName,
                Sql = sb.ToString()
            };
        }

        private GeneratedProcedure GenerateUpdate(
            CrudGenerationRequest request, IReadOnlyList<ColumnInfo> columns,
            IReadOnlyList<string> pkColumns, string fqn)
        {
            var procName = FormatProcName(request, "Update");
            var sb = new StringBuilder();

            AppendHeader(sb, procName, request.UseCreateOrAlter);

            // Parameters: all non-computed columns
            var updatableColumns = columns
                .Where(c => !c.IsComputed)
                .ToList();

            foreach (var col in updatableColumns)
            {
                sb.AppendLine($"    @{col.Name} {col.DataType},");
            }
            RemoveTrailingComma(sb);

            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");

            if (request.IncludeSetNocount)
                sb.AppendLine("    SET NOCOUNT ON;");

            sb.AppendLine();
            sb.AppendLine($"    UPDATE {fqn}");
            sb.Append("    SET ");

            var setColumns = updatableColumns
                .Where(c => !pkColumns.Contains(c.Name, StringComparer.OrdinalIgnoreCase)
                             && !c.IsIdentity)
                .ToList();

            sb.AppendLine(string.Join(",\n        ",
                setColumns.Select(c => $"[{c.Name}] = @{c.Name}")));

            if (pkColumns.Count > 0)
            {
                sb.Append("    WHERE ");
                sb.AppendLine(string.Join(" AND ",
                    pkColumns.Select(pk => $"[{pk}] = @{pk}")));
            }
            else
            {
                sb.AppendLine("    -- WARNING: No primary key defined. Add a WHERE clause before executing.");
                sb.AppendLine("    WHERE 1 = 0 -- Safety: prevents full-table update");
            }

            sb.AppendLine("END");
            sb.AppendLine("GO");

            return new GeneratedProcedure
            {
                Operation = "Update",
                ProcedureName = procName,
                Sql = sb.ToString()
            };
        }

        private GeneratedProcedure GenerateDelete(
            CrudGenerationRequest request, IReadOnlyList<ColumnInfo> columns,
            IReadOnlyList<string> pkColumns, string fqn)
        {
            var procName = FormatProcName(request, "Delete");
            var sb = new StringBuilder();

            AppendHeader(sb, procName, request.UseCreateOrAlter);

            // Parameters: PK columns with actual data types from column metadata
            foreach (var pk in pkColumns)
            {
                var col = columns.FirstOrDefault(c =>
                    c.Name.Equals(pk, StringComparison.OrdinalIgnoreCase));
                var dataType = col?.DataType ?? "INT";
                sb.AppendLine($"    @{pk} {dataType},");
            }
            RemoveTrailingComma(sb);

            sb.AppendLine("AS");
            sb.AppendLine("BEGIN");

            if (request.IncludeSetNocount)
                sb.AppendLine("    SET NOCOUNT ON;");

            sb.AppendLine();
            sb.AppendLine($"    DELETE FROM {fqn}");

            if (pkColumns.Count > 0)
            {
                sb.Append("    WHERE ");
                sb.AppendLine(string.Join(" AND ",
                    pkColumns.Select(pk => $"[{pk}] = @{pk}")));
            }
            else
            {
                sb.AppendLine("    -- WARNING: No primary key defined. Add a WHERE clause before executing.");
                sb.AppendLine("    WHERE 1 = 0 -- Safety: prevents full-table delete");
            }

            sb.AppendLine("END");
            sb.AppendLine("GO");

            return new GeneratedProcedure
            {
                Operation = "Delete",
                ProcedureName = procName,
                Sql = sb.ToString()
            };
        }

        private static void AppendHeader(StringBuilder sb, string procName, bool useCreateOrAlter)
        {
            if (useCreateOrAlter)
            {
                sb.AppendLine($"CREATE OR ALTER PROCEDURE [{procName}]");
            }
            else
            {
                sb.AppendLine($"CREATE PROCEDURE [{procName}]");
            }
        }

        private static string FormatProcName(CrudGenerationRequest request, string operation)
        {
            return request.NamingPattern
                .Replace("{schema}", request.SchemaName)
                .Replace("{table}", request.TableName)
                .Replace("{operation}", operation);
        }

        private static void RemoveTrailingComma(StringBuilder sb)
        {
            // Scan backwards from end to find the last comma, skipping whitespace/newlines
            for (int i = sb.Length - 1; i >= 0; i--)
            {
                var c = sb[i];
                if (c == ',')
                {
                    sb.Remove(i, 1);
                    return;
                }
                // Only skip whitespace characters when searching for the trailing comma
                if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
                    return; // Hit a non-whitespace, non-comma char — no trailing comma
            }
        }
    }

    /// <summary>
    /// Simplified column metadata used by the CRUD generator.
    /// Populated from the schema cache.
    /// </summary>
    internal sealed class ColumnInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DataType { get; set; } = "NVARCHAR(MAX)";
        public bool IsNullable { get; set; }
        public bool IsIdentity { get; set; }
        public bool IsComputed { get; set; }
        public bool IsPrimaryKey { get; set; }
    }
}
