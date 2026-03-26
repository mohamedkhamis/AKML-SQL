using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Request to export grid data to a file (e.g., .xlsx via ClosedXML on the engine side).
    /// Sent Shell -> Engine as MessageType 68 (GridExport).
    /// </summary>
    [MessagePackObject]
    public class GridExportRequest
    {
        /// <summary>Absolute path for the output file.</summary>
        [Key(0)]
        public string OutputPath { get; set; } = string.Empty;

        /// <summary>Column header names.</summary>
        [Key(1)]
        public string[] ColumnHeaders { get; set; } = [];

        /// <summary>Row data — each element is an array of cell values (same length as ColumnHeaders).</summary>
        [Key(2)]
        public string[][] Rows { get; set; } = [];

        /// <summary>Column SQL type names (e.g., "int", "nvarchar", "datetime"). Used for type-appropriate formatting.</summary>
        [Key(3)]
        public string[] ColumnTypes { get; set; } = [];

        /// <summary>Export format as int. Maps to <see cref="AkmlSql.Core.Models.Productivity.GridExportFormat"/>.</summary>
        [Key(4)]
        public int Format { get; set; }
    }
}
