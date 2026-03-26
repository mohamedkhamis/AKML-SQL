namespace AkmlSql.Core.Models.Productivity
{
    /// <summary>Identifies a single SQL statement within a script by its character offsets.</summary>
    public class StatementRange
    {
        /// <summary>Character offset of statement start.</summary>
        public int StartOffset { get; set; }

        /// <summary>Character offset of statement end.</summary>
        public int EndOffset { get; set; }

        /// <summary>1-based start line number.</summary>
        public int StartLine { get; set; }

        /// <summary>1-based end line number.</summary>
        public int EndLine { get; set; }

        /// <summary>Statement type: SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, EXEC, etc.</summary>
        public string StatementType { get; set; } = string.Empty;
    }
}
