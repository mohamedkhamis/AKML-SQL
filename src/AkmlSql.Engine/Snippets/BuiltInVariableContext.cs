namespace AkmlSql.Engine.Snippets;

public class BuiltInVariableContext
{
    public string DatabaseName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string SchemaName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ClipboardText { get; set; } = string.Empty;
    public string SelectedText { get; set; } = string.Empty;
}
