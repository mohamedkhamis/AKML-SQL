using System;

namespace AkmlSql.Engine.Execution
{
    /// <summary>
    /// UI scan 2026-11-03 (W3): a cell's text could not be turned into a value of its column's type
    /// (<see cref="SqlScalarEncoder.Decode"/>). Carries the column so the apply result can point the grid at the cell
    /// (<c>CrudEditResult.Column</c>); the message is the converter's own.
    /// </summary>
    public sealed class CrudCellException : InvalidOperationException
    {
        public CrudCellException(string columnName, string message, Exception innerException)
            : base(message, innerException)
        {
            ColumnName = columnName ?? string.Empty;
        }

        /// <summary>The base column whose value could not be converted.</summary>
        public string ColumnName { get; }
    }
}
