using System.Linq;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Schema;
using AkmlSql.Engine.Schema.Models;
using MessagePack;

namespace AkmlSql.Engine.Handlers.Schema
{
    /// <summary>
    /// Spec 021 (web edition) — M5 task T109. Translates a <see cref="DatabaseCache"/>
    /// into a flat MessagePack-serialisable <see cref="SchemaPhasePayload"/> shaped for
    /// the browser. Two entry points so handlers don't have to know which fields to
    /// strip per phase:
    /// <list type="bullet">
    ///   <item><see cref="SerializePhaseA"/> — schemas + object names + types (no columns, no FKs).</item>
    ///   <item><see cref="SerializePhaseB"/> — adds columns + foreign keys.</item>
    /// </list>
    /// The choice of which to call lives in the matching handler.
    /// </summary>
    public static class SchemaPhaseSerializer
    {
        /// <summary>Compute the checksum echoed back to the browser. Mirrors the
        /// <c>SchemaChecksumHandler</c> production wiring so a Phase-A response paired
        /// with a checksum-poll answer line up byte-equal — that pairing is what lets
        /// the browser skip a redundant fetch when the cache hasn't drifted.</summary>
        public static string ComputeChecksum(DatabaseCache cache)
        {
            int objectCount = 0;
            foreach (var schema in cache.Schemas.Values)
            {
                objectCount += schema.Objects.Count;
            }
            return $"{cache.Phase}:{objectCount}";
        }

        public static byte[] SerializePhaseA(DatabaseCache cache, string databaseName)
        {
            var payload = BuildPayload(cache, databaseName, includeColumns: false, includeForeignKeys: false);
            return MessagePackSerializer.Serialize(payload);
        }

        public static byte[] SerializePhaseB(DatabaseCache cache, string databaseName)
        {
            var payload = BuildPayload(cache, databaseName, includeColumns: true, includeForeignKeys: true);
            return MessagePackSerializer.Serialize(payload);
        }

        private static SchemaPhasePayload BuildPayload(
            DatabaseCache cache, string databaseName, bool includeColumns, bool includeForeignKeys)
        {
            var schemas = cache.Schemas.Values
                .OrderBy(s => s.SchemaName, System.StringComparer.OrdinalIgnoreCase)
                .Select(s => new SchemaPhaseSchema
                {
                    Name = s.SchemaName,
                    Objects = s.Objects
                        .OrderBy(o => o.ObjectName, System.StringComparer.OrdinalIgnoreCase)
                        .Select(o => new SchemaPhaseObject
                        {
                            SchemaName = o.SchemaName,
                            ObjectName = o.ObjectName,
                            ObjectType = (int)o.ObjectType,
                            Columns = includeColumns
                                ? o.Columns.Select(MapColumn).ToArray()
                                : System.Array.Empty<SchemaPhaseColumn>(),
                        })
                        .ToArray(),
                })
                .ToArray();

            var fks = includeForeignKeys
                ? cache.ForeignKeys.Select(MapForeignKey).ToArray()
                : System.Array.Empty<SchemaPhaseForeignKey>();

            return new SchemaPhasePayload
            {
                DatabaseName = databaseName,
                Phase = (int)cache.Phase,
                Checksum = ComputeChecksum(cache),
                Schemas = schemas,
                ForeignKeys = fks,
            };
        }

        private static SchemaPhaseColumn MapColumn(Column c) => new()
        {
            Name = c.ColumnName,
            TypeName = c.TypeName,
            IsNullable = c.IsNullable,
            IsPrimaryKey = c.IsPrimaryKey,
        };

        private static SchemaPhaseForeignKey MapForeignKey(ForeignKey fk) => new()
        {
            Name = fk.FkName,
            ParentSchema = fk.ParentSchema,
            ParentTable = fk.ParentTable,
            ParentColumns = fk.ParentColumns.ToArray(),
            ReferencedSchema = fk.ReferencedSchema,
            ReferencedTable = fk.ReferencedTable,
            ReferencedColumns = fk.ReferencedColumns.ToArray(),
        };
    }
}
