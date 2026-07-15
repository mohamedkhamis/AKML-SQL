namespace AkmlSql.Engine.Schema.Models;

/// <summary>
/// A linked server registered on the connected instance (a row of <c>sys.servers</c>
/// with <c>is_linked = 1</c>). Surfaced by <c>ObjectProvider</c> as a top-level
/// (four-part-name) completion when the IntelliSense connection scope has
/// <c>IncludeLinkedServers</c> enabled (FR-016).
/// </summary>
public class LinkedServerInfo
{
    /// <summary>The linked-server name — the first part of a <c>server.database.schema.object</c> reference.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional product string (e.g. <c>SQL Server</c>, <c>Oracle</c>); may be null.</summary>
    public string? Product { get; set; }

    /// <summary>Optional OLE DB provider (e.g. <c>SQLNCLI</c>, <c>MSOLEDBSQL</c>); may be null.</summary>
    public string? Provider { get; set; }
}
