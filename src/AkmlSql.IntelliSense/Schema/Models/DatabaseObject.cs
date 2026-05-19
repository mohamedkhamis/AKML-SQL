namespace AkmlSql.Engine.Schema.Models;

public enum DbObjectType
{
    Table,
    View,
    Procedure,
    ScalarFunction,
    TableFunction,
    InlineFunction,
    Synonym,
    Sequence
}

public class DatabaseObject
{
    public int ObjectId { get; set; }
    public string SchemaName { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public DbObjectType ObjectType { get; set; }
    public DateTime ModifyDate { get; set; }
    public long ApproxRowCount { get; set; }
    public string? Description { get; set; }
    public List<Column> Columns { get; set; } = [];
    public List<Parameter> Parameters { get; set; } = [];
    public List<Index> Indexes { get; set; } = [];
    public bool ColumnsLoaded { get; set; }

    public string FullName => $"{SchemaName}.{ObjectName}";
}

public class Column
{
    public int ColumnId { get; set; }
    public string ColumnName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public int MaxLength { get; set; }
    public int Precision { get; set; }
    public int Scale { get; set; }
    public bool IsNullable { get; set; }
    public bool IsIdentity { get; set; }
    public bool IsComputed { get; set; }
    public string? ComputedDefinition { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsPrimaryKey { get; set; }
    public string? Description { get; set; }

    public string TypeDisplay
    {
        get
        {
            return TypeName.ToLowerInvariant() switch
            {
                "nvarchar" or "varchar" or "char" or "nchar" or "binary" or "varbinary"
                    => MaxLength == -1 ? $"{TypeName}(MAX)" : $"{TypeName}({MaxLength})",
                "decimal" or "numeric"
                    => $"{TypeName}({Precision},{Scale})",
                _ => TypeName
            };
        }
    }
}

public class Parameter
{
    public int ParameterId { get; set; }
    public string ParameterName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public int MaxLength { get; set; }
    public int Precision { get; set; }
    public int Scale { get; set; }
    public bool IsOutput { get; set; }
    public bool HasDefault { get; set; }
    public object? DefaultValue { get; set; }
}

public class Index
{
    public string IndexName { get; set; } = string.Empty;
    public IndexType IndexType { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsUnique { get; set; }
    public List<IndexColumn> Columns { get; set; } = [];
}

public enum IndexType
{
    Clustered,
    Nonclustered,
    Heap,
    Columnstore
}

public class IndexColumn
{
    public string ColumnName { get; set; } = string.Empty;
    public bool IsDescending { get; set; }
}
