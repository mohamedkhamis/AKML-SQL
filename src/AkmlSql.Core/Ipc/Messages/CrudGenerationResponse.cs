using System.Collections.Generic;
using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// T099: Response containing generated CRUD stored procedure SQL.
    /// Sent Engine -> Shell as MessageType 166 (CrudGenerationResult).
    /// </summary>
    [MessagePackObject]
    public class CrudGenerationResponse
    {
        /// <summary>Whether the generation was successful.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>Error message when <see cref="Success"/> is false.</summary>
        [Key(1)]
        public string? Error { get; set; }

        /// <summary>
        /// List of generated procedure scripts. Each entry contains
        /// the operation type and the full SQL text.
        /// </summary>
        [Key(2)]
        public List<GeneratedProcedure> Procedures { get; set; } = new();
    }

    /// <summary>
    /// Represents a single generated CRUD procedure.
    /// </summary>
    [MessagePackObject]
    public class GeneratedProcedure
    {
        /// <summary>Operation type: "Insert", "Select", "Update", or "Delete".</summary>
        [Key(0)]
        public string Operation { get; set; } = string.Empty;

        /// <summary>The fully qualified procedure name.</summary>
        [Key(1)]
        public string ProcedureName { get; set; } = string.Empty;

        /// <summary>The complete SQL script for this procedure.</summary>
        [Key(2)]
        public string Sql { get; set; } = string.Empty;
    }
}
