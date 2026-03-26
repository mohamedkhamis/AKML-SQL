using System.Text;
using AkmlSql.Core.Ipc.Messages;
using Serilog;

namespace AkmlSql.Engine.Productivity
{
    /// <summary>
    /// T101: Generates "Script As" templates for database objects:
    /// CREATE, INSERT, SELECT, UPDATE, DELETE, MERGE, and BCP command templates.
    /// Uses column metadata from the schema cache.
    /// </summary>
    internal sealed class ScriptAsGenerator
    {
        /// <summary>
        /// Generates a script template for the given object.
        /// </summary>
        /// <param name="request">The Script As request.</param>
        /// <param name="columns">Column metadata for the target object (table/view).</param>
        /// <param name="objectType">The type of object ("TABLE", "VIEW", etc.).</param>
        /// <returns>The generated script response.</returns>
        public ScriptAsResponse Generate(
            ScriptAsRequest request,
            IReadOnlyList<ColumnInfo> columns,
            string objectType)
        {
            try
            {
                var fqn = $"[{request.SchemaName}].[{request.ObjectName}]";

                var sql = request.TemplateType.ToUpperInvariant() switch
                {
                    "CREATE" => GenerateCreate(fqn, columns, objectType),
                    "INSERT" => GenerateInsert(fqn, columns),
                    "SELECT" => GenerateSelect(fqn, columns),
                    "UPDATE" => GenerateUpdate(fqn, columns),
                    "DELETE" => GenerateDelete(fqn, columns),
                    "MERGE"  => GenerateMerge(fqn, columns),
                    "BCP"    => GenerateBcp(request),
                    _ => throw new ArgumentException($"Unknown template type: {request.TemplateType}")
                };

                Log.Information("ScriptAsGenerator: generated {Type} template for {Object}",
                    request.TemplateType, fqn);

                return new ScriptAsResponse
                {
                    Success = true,
                    Sql = sql,
                    TemplateType = request.TemplateType,
                    FullObjectName = fqn
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ScriptAsGenerator: generation failed for {Schema}.{Object}",
                    request.SchemaName, request.ObjectName);
                return new ScriptAsResponse
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private static string GenerateSelect(string fqn, IReadOnlyList<ColumnInfo> columns)
        {
            var sb = new StringBuilder();
            sb.AppendLine("SELECT");

            for (int i = 0; i < columns.Count; i++)
            {
                var comma = i < columns.Count - 1 ? "," : "";
                sb.AppendLine($"    [{columns[i].Name}]{comma}");
            }

            sb.AppendLine($"FROM {fqn}");
            sb.AppendLine("-- WHERE <search conditions>");
            sb.AppendLine("-- ORDER BY <column list>");

            return sb.ToString();
        }

        private static string GenerateInsert(string fqn, IReadOnlyList<ColumnInfo> columns)
        {
            var insertable = columns.Where(c => !c.IsIdentity && !c.IsComputed).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"INSERT INTO {fqn}");
            sb.AppendLine("(");
            for (int i = 0; i < insertable.Count; i++)
            {
                var comma = i < insertable.Count - 1 ? "," : "";
                sb.AppendLine($"    [{insertable[i].Name}]{comma}");
            }
            sb.AppendLine(")");
            sb.AppendLine("VALUES");
            sb.AppendLine("(");
            for (int i = 0; i < insertable.Count; i++)
            {
                var comma = i < insertable.Count - 1 ? "," : "";
                sb.AppendLine($"    <{insertable[i].Name}, {insertable[i].DataType}>{comma}");
            }
            sb.AppendLine(")");

            return sb.ToString();
        }

        private static string GenerateUpdate(string fqn, IReadOnlyList<ColumnInfo> columns)
        {
            var updatable = columns.Where(c => !c.IsIdentity && !c.IsComputed).ToList();
            var pkCols = columns.Where(c => c.IsPrimaryKey).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"UPDATE {fqn}");
            sb.AppendLine("SET");
            for (int i = 0; i < updatable.Count; i++)
            {
                var comma = i < updatable.Count - 1 ? "," : "";
                sb.AppendLine($"    [{updatable[i].Name}] = <{updatable[i].Name}, {updatable[i].DataType}>{comma}");
            }

            if (pkCols.Count > 0)
            {
                sb.Append("WHERE ");
                sb.AppendLine(string.Join(" AND ",
                    pkCols.Select(c => $"[{c.Name}] = <{c.Name}, {c.DataType}>")));
            }
            else
            {
                sb.AppendLine("WHERE <search conditions>");
            }

            return sb.ToString();
        }

        private static string GenerateDelete(string fqn, IReadOnlyList<ColumnInfo> columns)
        {
            var pkCols = columns.Where(c => c.IsPrimaryKey).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"DELETE FROM {fqn}");

            if (pkCols.Count > 0)
            {
                sb.Append("WHERE ");
                sb.AppendLine(string.Join(" AND ",
                    pkCols.Select(c => $"[{c.Name}] = <{c.Name}, {c.DataType}>")));
            }
            else
            {
                sb.AppendLine("WHERE <search conditions>");
            }

            return sb.ToString();
        }

        private static string GenerateCreate(string fqn, IReadOnlyList<ColumnInfo> columns, string objectType)
        {
            if (objectType.Equals("VIEW", StringComparison.OrdinalIgnoreCase))
            {
                return $"-- Script As CREATE for view {fqn}\n-- Use sp_helptext '{fqn}' to retrieve the original definition\n";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"CREATE TABLE {fqn}");
            sb.AppendLine("(");

            for (int i = 0; i < columns.Count; i++)
            {
                var col = columns[i];
                var comma = i < columns.Count - 1 ? "," : "";
                var nullable = col.IsNullable ? "NULL" : "NOT NULL";
                var identity = col.IsIdentity ? " IDENTITY(1,1)" : "";
                sb.AppendLine($"    [{col.Name}] {col.DataType}{identity} {nullable}{comma}");
            }

            // Primary key constraint
            var pkCols = columns.Where(c => c.IsPrimaryKey).ToList();
            if (pkCols.Count > 0)
            {
                sb.AppendLine($"    ,CONSTRAINT [PK_{fqn.Replace("[", "").Replace("]", "").Replace(".", "_")}]");
                sb.AppendLine($"        PRIMARY KEY CLUSTERED ({string.Join(", ", pkCols.Select(c => $"[{c.Name}]"))})");
            }

            sb.AppendLine(")");

            return sb.ToString();
        }

        private static string GenerateMerge(string fqn, IReadOnlyList<ColumnInfo> columns)
        {
            var pkCols = columns.Where(c => c.IsPrimaryKey).ToList();
            var nonPkCols = columns.Where(c => !c.IsPrimaryKey && !c.IsIdentity && !c.IsComputed).ToList();
            var insertable = columns.Where(c => !c.IsIdentity && !c.IsComputed).ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"MERGE {fqn} AS target");
            sb.AppendLine("USING (");
            sb.AppendLine("    SELECT");
            for (int i = 0; i < columns.Count; i++)
            {
                var comma = i < columns.Count - 1 ? "," : "";
                sb.AppendLine($"        <{columns[i].Name}, {columns[i].DataType}> AS [{columns[i].Name}]{comma}");
            }
            sb.AppendLine(") AS source");

            if (pkCols.Count > 0)
            {
                sb.Append("ON (");
                sb.Append(string.Join(" AND ",
                    pkCols.Select(c => $"target.[{c.Name}] = source.[{c.Name}]")));
                sb.AppendLine(")");
            }
            else
            {
                sb.AppendLine("ON (<join conditions>)");
            }

            // WHEN MATCHED
            if (nonPkCols.Count > 0)
            {
                sb.AppendLine("WHEN MATCHED THEN");
                sb.AppendLine("    UPDATE SET");
                for (int i = 0; i < nonPkCols.Count; i++)
                {
                    var comma = i < nonPkCols.Count - 1 ? "," : "";
                    sb.AppendLine($"        target.[{nonPkCols[i].Name}] = source.[{nonPkCols[i].Name}]{comma}");
                }
            }

            // WHEN NOT MATCHED
            sb.AppendLine("WHEN NOT MATCHED THEN");
            sb.Append("    INSERT (");
            sb.Append(string.Join(", ", insertable.Select(c => $"[{c.Name}]")));
            sb.AppendLine(")");
            sb.Append("    VALUES (");
            sb.Append(string.Join(", ", insertable.Select(c => $"source.[{c.Name}]")));
            sb.AppendLine(")");
            sb.AppendLine(";");

            return sb.ToString();
        }

        private static string GenerateBcp(ScriptAsRequest request)
        {
            var fqn = $"{request.SchemaName}.{request.ObjectName}";
            var sb = new StringBuilder();
            sb.AppendLine($"-- BCP export command for {fqn}");
            sb.AppendLine($"-- bcp \"{fqn}\" out \"{request.ObjectName}.dat\" -c -t\",\" -T -S <ServerName>");
            sb.AppendLine();
            sb.AppendLine($"-- BCP import command for {fqn}");
            sb.AppendLine($"-- bcp \"{fqn}\" in \"{request.ObjectName}.dat\" -c -t\",\" -T -S <ServerName>");
            return sb.ToString();
        }
    }
}
