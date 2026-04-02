using MessagePack;

namespace AkmlSql.Core.Ipc.Messages;

[MessagePackObject]
public class WildcardExpansionResponse
{
    [Key(0)]
    public bool Success { get; set; }

    [Key(1)]
    public WildcardTableGroup[] Tables { get; set; } = [];

    [Key(2)]
    public string? ErrorMessage { get; set; }
}

[MessagePackObject]
public class WildcardTableGroup
{
    /// <summary>Display name for the table header (e.g., "Orders").</summary>
    [Key(0)]
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// Prefix for columns in the expansion text.
    /// Alias if defined, table name if not.
    /// </summary>
    [Key(1)]
    public string Qualifier { get; set; } = string.Empty;

    [Key(2)]
    public WildcardColumn[] Columns { get; set; } = [];
}

[MessagePackObject]
public class WildcardColumn
{
    [Key(0)]
    public string ColumnName { get; set; } = string.Empty;

    /// <summary>Type display string, e.g., "int, NOT NULL, PK".</summary>
    [Key(1)]
    public string TypeDisplay { get; set; } = string.Empty;
}
