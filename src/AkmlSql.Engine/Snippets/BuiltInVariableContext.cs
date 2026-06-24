namespace AkmlSql.Engine.Snippets;

public class BuiltInVariableContext
{
    public string DatabaseName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ClipboardText { get; set; } = string.Empty;
    public string SelectedText { get; set; } = string.Empty;
    /// <summary>
    /// The SQL login name for the active connection (e.g. SYSTEM_USER / UserID from the
    /// connection string). When empty, $USER$ falls back to <see cref="Environment.UserName"/>.
    /// Populated by the snippet expand handler from the session connection string; empty for
    /// integrated-auth connections (Windows login is surfaced via the fallback).
    /// </summary>
    public string SqlUserName { get; set; } = string.Empty;
}
