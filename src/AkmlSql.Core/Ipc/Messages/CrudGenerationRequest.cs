#nullable enable
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// T099: Request to generate CRUD stored procedures for a given table.
    /// Sent Shell -> Engine as MessageType 66 (CrudGeneration).
    /// </summary>
    [MessagePackObject]
    public class CrudGenerationRequest
    {
        /// <summary>Session identifier for the active connection.</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Schema name of the target table (e.g. "dbo").</summary>
        [Key(1)]
        public string SchemaName { get; set; } = "dbo";

        /// <summary>Name of the target table.</summary>
        [Key(2)]
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// Which CRUD operations to generate. Combination of flags:
        /// "C" = Create (INSERT), "R" = Read (SELECT), "U" = Update, "D" = Delete.
        /// Default: "CRUD" (all four).
        /// </summary>
        [Key(3)]
        public string Operations { get; set; } = "CRUD";

        /// <summary>
        /// Naming pattern for generated procedures.
        /// Tokens: {schema}, {table}, {operation}.
        /// Default: "usp_{table}_{operation}".
        /// </summary>
        [Key(4)]
        public string NamingPattern { get; set; } = "usp_{table}_{operation}";

        /// <summary>When true, wraps each procedure in IF NOT EXISTS / CREATE OR ALTER.</summary>
        [Key(5)]
        public bool UseCreateOrAlter { get; set; } = true;

        /// <summary>When true, includes SET NOCOUNT ON at the top of each procedure.</summary>
        [Key(6)]
        public bool IncludeSetNocount { get; set; } = true;
    }
}
