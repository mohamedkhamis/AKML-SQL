namespace AkmlSql.Engine.Snippets.Models;

public enum SnippetSourceType
{
    Personal = 1,
    Team = 2,
    BuiltIn = 3
}

public class SnippetSource
{
    public SnippetSourceType Type { get; set; }
    public int Priority => (int)Type; // Lower = higher priority
    public string Path { get; set; } = string.Empty;
    public bool IsWriteable { get; set; }
    public bool IsAvailable { get; set; } = true;
}
