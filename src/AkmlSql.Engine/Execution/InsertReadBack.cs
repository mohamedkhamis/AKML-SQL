using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.Data.SqlClient;

namespace AkmlSql.Engine.Execution
{
    /// <summary>One column of the unique key a new row is read back by, with the type a table variable declares it as.</summary>
    public readonly record struct ReadBackKeyColumn(string Name, string Declaration, bool IsNullable);

    /// <summary>
    /// Review round 3 (REV-06, REV-09): how a SQL Server insert gives the grid the row as it was stored — identity, defaults,
    /// server-generated keys and whatever a trigger changed. The insert writes the new row's key through
    /// <c>OUTPUT INSERTED.&lt;key&gt; INTO @akml_k</c> (allowed with triggers, unlike a bare OUTPUT), and the same batch then
    /// selects the row by that key, after the triggers ran. The table's metadata is read in the table's own database, never the
    /// session's: a three-part name in another database has its own <c>sys.triggers</c> and <c>sys.indexes</c>.
    /// </summary>
    public sealed class InsertReadBack
    {
        /// <summary>The batch's identity output parameter (<c>SCOPE_IDENTITY()</c>, which a trigger's own inserts do not change).</summary>
        public const string IdentityParameter = "@akml_id";

        private static readonly Regex SafeDeclaration = new(@"^[a-z][a-z0-9_]*(\((max|\d{1,4})(,\d{1,2})?\))?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex SafeCollation = new(@"^[A-Za-z0-9_]+$", RegexOptions.CultureInvariant);

        public InsertReadBack(bool hasEnabledTriggers, IReadOnlyList<ReadBackKeyColumn> key)
        {
            HasEnabledTriggers = hasEnabledTriggers;
            Key = key ?? Array.Empty<ReadBackKeyColumn>();
        }

        /// <summary>The table has an enabled trigger, so a bare <c>OUTPUT INSERTED</c> is refused (and would not show its changes).</summary>
        public bool HasEnabledTriggers { get; }

        /// <summary>The primary key, else the narrowest unfiltered unique index, else the identity column; empty when none can be
        /// declared (a CLR type), and then the row is read back only when no trigger can have changed it.</summary>
        public IReadOnlyList<ReadBackKeyColumn> Key { get; }

        /// <summary>Whether this insert can say what the server stored: by its key, or straight from OUTPUT when nothing can change it after.</summary>
        public bool CanReturnRow => Key.Count > 0 || !HasEnabledTriggers;

        /// <summary>
        /// The target table's enabled triggers and read-back key. <c>OBJECT_ID</c> resolves the quoted three-part name, and the
        /// catalog views are those of that database.
        /// </summary>
        public static async Task<InsertReadBack> LoadAsync(SqlConnection conn, SqlTransaction tx, ApplyChangesRequest req, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = MetadataSql(req.BaseCatalog);
            cmd.Parameters.Add(new SqlParameter("@t", SqlDbType.NVarChar, 4000)
            {
                Value = CrudWriteGenerator.BuildQualifiedTable(req.BaseCatalog, req.BaseSchema, req.BaseTable),
            });

            bool triggers = false;
            var key = new List<ReadBackKeyColumn>();
            bool usable = true;
            await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (await reader.ReadAsync(ct).ConfigureAwait(false)) triggers = reader.GetInt32(0) > 0;
                if (await reader.NextResultAsync(ct).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        var name = reader.GetString(0);
                        var decl = reader.IsDBNull(1) ? null : reader.GetString(1);
                        var collation = reader.IsDBNull(2) ? null : reader.GetString(2);
                        var declaration = Declare(decl, collation);
                        if (declaration == null) usable = false;
                        else key.Add(new ReadBackKeyColumn(name, declaration, reader.GetBoolean(3)));
                    }
                }
            }
            return new InsertReadBack(triggers, usable ? key : Array.Empty<ReadBackKeyColumn>());
        }

        /// <summary>The type text the catalog query printed, checked before it is written into a batch; null when it is not a plain
        /// system type (then the key cannot be held in a table variable).</summary>
        internal static string? Declare(string? type, string? collation)
        {
            if (string.IsNullOrEmpty(type) || !SafeDeclaration.IsMatch(type)) return null;
            if (string.IsNullOrEmpty(collation)) return type;
            // A table variable takes the session database's collation; comparing it to a column of another database's could fail.
            return SafeCollation.IsMatch(collation) ? type + " COLLATE " + collation : null;
        }

        internal static string MetadataSql(string? catalog)
        {
            var c = string.IsNullOrEmpty(catalog) ? string.Empty : CrudWriteGenerator.QuoteName(catalog!) + ".";
            return
$@"DECLARE @id int = OBJECT_ID(@t);
SELECT COUNT(*) FROM {c}sys.triggers WHERE parent_id = @id AND is_disabled = 0;
DECLARE @ix int = (SELECT TOP (1) i.index_id FROM {c}sys.indexes AS i
    WHERE i.object_id = @id AND i.is_unique = 1 AND i.has_filter = 0 AND i.is_disabled = 0 AND i.is_hypothetical = 0
    ORDER BY i.is_primary_key DESC,
             (SELECT COUNT(*) FROM {c}sys.index_columns AS x WHERE x.object_id = @id AND x.index_id = i.index_id AND x.key_ordinal > 0),
             i.index_id);
SELECT col.name,
       CASE
           WHEN tn.n IS NULL THEN NULL
           WHEN tn.n IN (N'varchar', N'char', N'varbinary', N'binary')
               THEN tn.n + N'(' + CASE WHEN col.max_length = -1 THEN N'max' ELSE CAST(col.max_length AS nvarchar(10)) END + N')'
           WHEN tn.n IN (N'nvarchar', N'nchar')
               THEN tn.n + N'(' + CASE WHEN col.max_length = -1 THEN N'max' ELSE CAST(col.max_length / 2 AS nvarchar(10)) END + N')'
           WHEN tn.n IN (N'decimal', N'numeric')
               THEN tn.n + N'(' + CAST(col.precision AS nvarchar(10)) + N',' + CAST(col.scale AS nvarchar(10)) + N')'
           WHEN tn.n IN (N'datetime2', N'time', N'datetimeoffset') THEN tn.n + N'(' + CAST(col.scale AS nvarchar(10)) + N')'
           WHEN tn.n IN (N'timestamp', N'rowversion') THEN N'binary(8)'
           ELSE tn.n
       END,
       col.collation_name,
       col.is_nullable
FROM {c}sys.columns AS col
JOIN {c}sys.types AS tu ON tu.user_type_id = col.user_type_id
LEFT JOIN {c}sys.types AS tb ON tb.user_type_id = col.system_type_id
CROSS APPLY (SELECT CASE WHEN tu.is_user_defined = 0 THEN tu.name WHEN tu.is_assembly_type = 0 THEN tb.name END AS n) AS tn
LEFT JOIN {c}sys.index_columns AS ic
    ON ic.object_id = @id AND ic.index_id = @ix AND ic.column_id = col.column_id AND ic.key_ordinal > 0
WHERE col.object_id = @id AND (ic.column_id IS NOT NULL OR (@ix IS NULL AND col.is_identity = 1))
ORDER BY ic.key_ordinal;";
        }

        /// <summary>
        /// <c>DECLARE @akml_k …; INSERT … OUTPUT INSERTED.&lt;key&gt; INTO @akml_k …; SET @akml_id = SCOPE_IDENTITY(); SELECT
        /// &lt;columns&gt; FROM &lt;table&gt; WHERE &lt;key&gt; IN @akml_k;</c> — <paramref name="insert"/> is the INSERT up to where the
        /// OUTPUT clause goes, <paramref name="rest"/> what follows it (<c>VALUES (…)</c> or <c>DEFAULT VALUES</c>).
        /// </summary>
        internal string BuildKeyedInsert(string table, string insert, string rest, IReadOnlyList<string> returnColumns)
        {
            var sb = new StringBuilder();
            sb.Append("DECLARE @akml_k TABLE (");
            for (int i = 0; i < Key.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("[k").Append(i).Append("] ").Append(Key[i].Declaration);
            }
            sb.Append("); ");
            sb.Append(insert)
              .Append(" OUTPUT ").Append(string.Join(", ", Key.Select(k => "INSERTED." + CrudWriteGenerator.QuoteName(k.Name))))
              .Append(" INTO @akml_k (").Append(string.Join(", ", Key.Select((_, i) => "[k" + i + "]"))).Append(')')
              .Append(rest).Append("; ");
            sb.Append("SET ").Append(IdentityParameter).Append(" = SCOPE_IDENTITY(); ");
            sb.Append("SELECT ").Append(string.Join(", ", returnColumns.Select(c => "akml_t." + CrudWriteGenerator.QuoteName(c))))
              .Append(" FROM ").Append(table).Append(" AS akml_t WHERE EXISTS (SELECT 1 FROM @akml_k AS akml_k WHERE ");
            for (int i = 0; i < Key.Count; i++)
            {
                if (i > 0) sb.Append(" AND ");
                var column = "akml_t." + CrudWriteGenerator.QuoteName(Key[i].Name);
                var held = "akml_k.[k" + i + "]";
                sb.Append(Key[i].IsNullable
                    ? "(" + column + " = " + held + " OR (" + column + " IS NULL AND " + held + " IS NULL))"
                    : column + " = " + held);
            }
            sb.Append(");");
            return sb.ToString();
        }
    }
}
