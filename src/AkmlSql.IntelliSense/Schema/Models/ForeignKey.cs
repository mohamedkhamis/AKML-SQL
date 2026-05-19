namespace AkmlSql.Engine.Schema.Models;

public enum FkAction
{
    NoAction,
    Cascade,
    SetNull,
    SetDefault
}

public class ForeignKey
{
    public string FkName { get; set; } = string.Empty;
    public string ParentSchema { get; set; } = string.Empty;
    public string ParentTable { get; set; } = string.Empty;
    public List<string> ParentColumns { get; set; } = [];
    public string ReferencedSchema { get; set; } = string.Empty;
    public string ReferencedTable { get; set; } = string.Empty;
    public List<string> ReferencedColumns { get; set; } = [];
    public bool IsDisabled { get; set; }
    public FkAction DeleteAction { get; set; }
    public FkAction UpdateAction { get; set; }

    public string ParentFullName => $"{ParentSchema}.{ParentTable}";
    public string ReferencedFullName => $"{ReferencedSchema}.{ReferencedTable}";
}
