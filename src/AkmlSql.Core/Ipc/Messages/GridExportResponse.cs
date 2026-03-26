using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Response from the engine after exporting grid data to a file.
    /// Sent Engine -> Shell as MessageType 168 (GridExportResult).
    /// </summary>
    [MessagePackObject]
    public class GridExportResponse
    {
        /// <summary>Whether the export completed successfully.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>Absolute path of the generated output file. Null when <see cref="Success"/> is false.</summary>
        [Key(1)]
        public string? OutputPath { get; set; }

        /// <summary>Number of data rows written (excluding header).</summary>
        [Key(2)]
        public int RowCount { get; set; }

        /// <summary>Error message if <see cref="Success"/> is false; otherwise null.</summary>
        [Key(3)]
        public string? Error { get; set; }
    }
}
