namespace AkmlSql.Core.Models.Productivity
{
    /// <summary>A user-defined friendly name for a server connection.</summary>
    public class ConnectionAlias
    {
        /// <summary>Actual server name (e.g., "SQL-PROD-EC-01\\INST02").</summary>
        public string ServerName { get; set; } = string.Empty;

        /// <summary>Friendly alias (e.g., "Production East").</summary>
        public string Alias { get; set; } = string.Empty;
    }
}
