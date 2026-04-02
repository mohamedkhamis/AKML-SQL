using Xunit;
using AkmlSql.Engine.Snippets.Models;

namespace AkmlSql.Engine.Tests.Snippets.Models;

public class SnippetModelsTests
{
    // ── Snippet ───────────────────────────────────────────────────────────

    [Fact]
    public void Snippet_Defaults()
    {
        var s = new Snippet();
        Assert.NotNull(s.Metadata);
        Assert.Empty(s.Variables);
        Assert.Empty(s.Body);
    }

    [Fact]
    public void Snippet_Mutations()
    {
        var s = new Snippet
        {
            Variables = [new SnippetVariable { Name = "TableName" }],
            Body = ["SELECT * FROM $TableName$"]
        };
        Assert.Single(s.Variables);
        Assert.Single(s.Body);
        Assert.Equal("TableName", s.Variables[0].Name);
    }

    // ── SnippetMetadata ───────────────────────────────────────────────────

    [Fact]
    public void SnippetMetadata_Defaults()
    {
        var m = new SnippetMetadata();
        Assert.NotEmpty(m.Id); // GUID
        Assert.Equal(string.Empty, m.Shortcode);
        Assert.Equal(string.Empty, m.Name);
        Assert.Equal(string.Empty, m.Description);
        Assert.Equal(string.Empty, m.Author);
        Assert.Equal("1.0", m.Version);
        Assert.Equal("Custom", m.Category);
        Assert.Empty(m.Tags);
        Assert.Single(m.Context);
        Assert.Equal("global", m.Context[0]);
        Assert.False(m.SurroundsWith);
    }

    [Fact]
    public void SnippetMetadata_Mutations()
    {
        var m = new SnippetMetadata
        {
            Id = "my-id",
            Shortcode = "sel",
            Name = "SELECT template",
            Description = "Basic SELECT",
            Author = "Test",
            Version = "2.0",
            Category = "DML",
            Tags = ["select", "query"],
            Context = ["global", "after_from"],
            SurroundsWith = true
        };
        Assert.Equal("my-id", m.Id);
        Assert.Equal("sel", m.Shortcode);
        Assert.Equal("SELECT template", m.Name);
        Assert.Equal("Basic SELECT", m.Description);
        Assert.Equal("Test", m.Author);
        Assert.Equal("2.0", m.Version);
        Assert.Equal("DML", m.Category);
        Assert.Equal(2, m.Tags.Length);
        Assert.Equal(2, m.Context.Length);
        Assert.True(m.SurroundsWith);
    }

    // ── SnippetVariable ───────────────────────────────────────────────────

    [Fact]
    public void SnippetVariable_Defaults()
    {
        var v = new SnippetVariable();
        Assert.Equal(string.Empty, v.Name);
        Assert.Equal(string.Empty, v.Default);
        Assert.Equal(string.Empty, v.Tooltip);
        Assert.Null(v.SchemaAware);
    }

    [Fact]
    public void SnippetVariable_Mutations()
    {
        var v = new SnippetVariable
        {
            Name = "TableName",
            Default = "MyTable",
            Tooltip = "Enter table name",
            SchemaAware = "table"
        };
        Assert.Equal("TableName", v.Name);
        Assert.Equal("MyTable", v.Default);
        Assert.Equal("Enter table name", v.Tooltip);
        Assert.Equal("table", v.SchemaAware);
    }

    // ── SnippetSource ─────────────────────────────────────────────────────

    [Fact]
    public void SnippetSource_Defaults()
    {
        var src = new SnippetSource();
        Assert.Equal((SnippetSourceType)0, src.Type); // enum default is 0 (no named value)
        Assert.Equal(string.Empty, src.Path);
        Assert.False(src.IsWriteable);
        Assert.True(src.IsAvailable);
    }

    [Fact]
    public void SnippetSource_Priority_Personal()
    {
        var src = new SnippetSource { Type = SnippetSourceType.Personal };
        Assert.Equal(1, src.Priority);
    }

    [Fact]
    public void SnippetSource_Priority_Team()
    {
        var src = new SnippetSource { Type = SnippetSourceType.Team };
        Assert.Equal(2, src.Priority);
    }

    [Fact]
    public void SnippetSource_Priority_BuiltIn()
    {
        var src = new SnippetSource { Type = SnippetSourceType.BuiltIn };
        Assert.Equal(3, src.Priority);
    }

    [Fact]
    public void SnippetSourceType_Values()
    {
        Assert.Equal(1, (int)SnippetSourceType.Personal);
        Assert.Equal(2, (int)SnippetSourceType.Team);
        Assert.Equal(3, (int)SnippetSourceType.BuiltIn);
    }
}
