namespace AkmlSql.Core.Models.Productivity
{
    /// <summary>Supported export formats for results grid data.</summary>
    public enum GridExportFormat
    {
        Csv = 0,
        Tsv = 1,
        Json = 2,
        Xml = 3,
        Xlsx = 4,
        Html = 5,
        SqlInsert = 6,
        Markdown = 7
    }
}
