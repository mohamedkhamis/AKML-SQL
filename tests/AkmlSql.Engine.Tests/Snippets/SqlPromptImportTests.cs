using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Snippets;
using Xunit;

namespace AkmlSql.Engine.Tests.Snippets;

/// <summary>
/// Spec 030 T042/T043 (FR-032, R7) — <c>.sqlpromptsnippet</c> (SQL Prompt XML) import with token
/// mapping. Mapping assertions exercise <see cref="SqlPromptSnippetParser.ParseXml"/> directly
/// (visible via the engine's <c>InternalsVisibleTo("AkmlSql.Engine.Tests")</c>); one test routes a
/// full import through <see cref="SnippetRequestHandler.HandleImport"/> to cover the wiring + failure
/// path.
///
/// <para><b>ASSUMED XML SCHEMA — validate against a real SQL Prompt file later.</b> SQL Prompt exports
/// the Visual Studio CodeSnippet schema, default namespace
/// <c>http://schemas.microsoft.com/VisualStudio/2005/CodeSnippet</c>:
/// <c>CodeSnippets/CodeSnippet/Header/{Title,Shortcut,Description,Author,SnippetTypes/SnippetType}</c>
/// and <c>CodeSnippet/Snippet/{Declarations/Literal/{ID,Default,ToolTip}, Code (CDATA)}</c>. The parser
/// also tolerates a flat shape (a root snippet element with <c>Title</c>/<c>Code</c> children, no
/// namespace) — both shapes are asserted below.</para>
/// </summary>
public sealed class SqlPromptImportTests : System.IDisposable
{
    private readonly string _personalDir;
    private readonly string _builtInDir;

    public SqlPromptImportTests()
    {
        _personalDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"akml_spi_personal_{System.Guid.NewGuid():N}");
        _builtInDir  = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"akml_spi_builtin_{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(_personalDir);
        System.IO.Directory.CreateDirectory(_builtInDir);
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_personalDir, true); } catch { }
        try { System.IO.Directory.Delete(_builtInDir, true); } catch { }
    }

    // A realistic .sqlpromptsnippet fixture: VS CodeSnippet schema with the default namespace, a
    // declared Literal variable, a CDATA body, and SQL Prompt placeholder tokens to be mapped.
    private const string NestedXml = """
<?xml version="1.0" encoding="utf-8"?>
<CodeSnippets xmlns="http://schemas.microsoft.com/VisualStudio/2005/CodeSnippet">
  <CodeSnippet Format="1.0.0">
    <Header>
      <Title>Select from table in database</Title>
      <Shortcut>ssfdb</Shortcut>
      <Description>Selects rows from a table, scoped to a database.</Description>
      <Author>Red Gate</Author>
      <SnippetTypes>
        <SnippetType>Expansion</SnippetType>
      </SnippetTypes>
    </Header>
    <Snippet>
      <Declarations>
        <Literal>
          <ID>tableName</ID>
          <ToolTip>The table to query</ToolTip>
          <Default>dbo.MyTable</Default>
        </Literal>
      </Declarations>
      <Code Language="SQL"><![CDATA[USE $DBNAME$;
SELECT *
FROM $tableName$
WHERE notes = $PASTE$ $CURSOR$]]></Code>
    </Snippet>
  </CodeSnippet>
</CodeSnippets>
""";

    // ── Mapping / parsing (direct ParseXml) ──────────────────────────────────

    [Fact]
    public void ParseXml_MapsTokens_DbnameToDatabase_AndPasteToClipboard()
    {
        var snippets = SqlPromptSnippetParser.ParseXml(NestedXml);

        var snippet = Assert.Single(snippets);
        var body = string.Join("\n", snippet.Body);

        Assert.Contains("$DATABASE$", body);        // $DBNAME$ → $DATABASE$
        Assert.Contains("$CLIPBOARD$", body);       // $PASTE$  → $CLIPBOARD$
        Assert.DoesNotContain("$DBNAME$", body);
        Assert.DoesNotContain("$PASTE$", body);
    }

    [Fact]
    public void ParseXml_DerivesShortcode_FromShortcut()
    {
        var snippets = SqlPromptSnippetParser.ParseXml(NestedXml);

        var snippet = Assert.Single(snippets);
        Assert.Equal("ssfdb", snippet.Metadata.Shortcode);
        Assert.Equal("Select from table in database", snippet.Metadata.Name);
        Assert.Equal("Selects rows from a table, scoped to a database.", snippet.Metadata.Description);
    }

    [Fact]
    public void ParseXml_PreservesBodyAndCursorMarker()
    {
        var snippets = SqlPromptSnippetParser.ParseXml(NestedXml);

        var snippet = Assert.Single(snippets);
        var body = string.Join("\n", snippet.Body);

        // Untouched tokens (including the declared Literal variable) survive verbatim.
        Assert.Contains("$CURSOR$", body);
        Assert.Contains("$tableName$", body);
        Assert.Contains("SELECT *", body);
        Assert.Contains("FROM $tableName$", body);
        // CDATA body split into lines (leading/trailing blank lines trimmed, interior preserved).
        Assert.Equal("USE $DATABASE$;", snippet.Body[0]);
    }

    [Fact]
    public void ParseXml_PreservesDeclaredVariables()
    {
        var snippets = SqlPromptSnippetParser.ParseXml(NestedXml);

        var snippet = Assert.Single(snippets);
        var variable = Assert.Single(snippet.Variables);

        Assert.Equal("tableName", variable.Name);
        Assert.Equal("dbo.MyTable", variable.Default);
        Assert.Equal("The table to query", variable.Tooltip);
    }

    [Fact]
    public void ParseXml_MapsSelectionRangeTokens_PreservingAkmlForm()
    {
        // SQL Prompt may emit either underscore or non-underscore variants; both → the AKML form
        // that the engine's HandleExpand recognises as live selection markers (spec 030 T040/T047).
        const string xml = """
<Snippet>
  <Title>wrap</Title>
  <Shortcut>wrp</Shortcut>
  <Code>BEGIN $SELECTION_START$body$SELECTION_END$ END $CURSOR$</Code>
</Snippet>
""";
        var snippets = SqlPromptSnippetParser.ParseXml(xml);

        var snippet = Assert.Single(snippets);
        var body = string.Join("\n", snippet.Body);

        Assert.Contains("$SELECTIONSTART$", body);
        Assert.Contains("$SELECTIONEND$", body);
        Assert.DoesNotContain("$SELECTION_START$", body);
        Assert.DoesNotContain("$SELECTION_END$", body);
        Assert.Contains("$CURSOR$", body); // $CURSOR$ left as-is
    }

    [Fact]
    public void ParseXml_FlatShape_NoNamespace_Parses()
    {
        // Tolerant fallback: a flat <Snippet> with no <CodeSnippet> wrapper and no namespace.
        const string xml = """
<Snippet>
  <Title>quick select</Title>
  <Description>flat shape</Description>
  <Code>SELECT $CURSOR$</Code>
</Snippet>
""";
        var snippets = SqlPromptSnippetParser.ParseXml(xml);

        var snippet = Assert.Single(snippets);
        Assert.Equal("quick", snippet.Metadata.Shortcode);  // derived from first word of Title
        Assert.Equal("SELECT $CURSOR$", string.Join("\n", snippet.Body));
    }

    [Fact]
    public void ParseXml_MultipleSnippets_AllParsed()
    {
        const string xml = """
<CodeSnippets xmlns="http://schemas.microsoft.com/VisualStudio/2005/CodeSnippet">
  <CodeSnippet>
    <Header><Title>One</Title><Shortcut>one</Shortcut></Header>
    <Snippet><Code>SELECT 1</Code></Snippet>
  </CodeSnippet>
  <CodeSnippet>
    <Header><Title>Two</Title><Shortcut>two</Shortcut></Header>
    <Snippet><Code>SELECT 2</Code></Snippet>
  </CodeSnippet>
</CodeSnippets>
""";
        var snippets = SqlPromptSnippetParser.ParseXml(xml);

        Assert.Equal(2, snippets.Count);
        Assert.Contains(snippets, s => s.Metadata.Shortcode == "one");
        Assert.Contains(snippets, s => s.Metadata.Shortcode == "two");
    }

    [Fact]
    public void ParseXml_UnparseableInput_ReturnsEmptyList()
    {
        Assert.Empty(SqlPromptSnippetParser.ParseXml("this is not xml at all <<<<"));
        Assert.Empty(SqlPromptSnippetParser.ParseXml(""));
    }

    // ── Integration through HandleImport (wiring + failure path) ──────────────

    [Fact]
    public void HandleImport_SqlPromptXml_ImportsSnippet()
    {
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir);

        var resp = handler.HandleImport(new SnippetImportRequest
        {
            FileContent = NestedXml,
            SourceFormat = 1 // SqlPromptXml
        });

        Assert.True(resp.Success);
        Assert.Equal(1, resp.ImportedCount);
        Assert.Equal(0, resp.FailedCount);

        // The mapped tokens are now REAL built-in vars: expanding with a DB + clipboard context resolves
        // them (proving $DBNAME$→$DATABASE$ and $PASTE$→$CLIPBOARD$ — an unmapped $DBNAME$ would not resolve).
        var expand = handler.HandleExpand(
            new SnippetExpandRequest { Shortcode = "ssfdb", ClipboardText = "CLIP" }, databaseName: "MyDb");
        Assert.True(expand.Success);
        Assert.Contains("MyDb", expand.ExpandedText);
        Assert.Contains("CLIP", expand.ExpandedText);
    }

    [Fact]
    public void HandleImport_SqlPromptXml_AutoDetect_ImportsSnippet()
    {
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir);

        // SourceFormat 0 (Auto): the JSON probe throws/returns null on XML, so the SQL Prompt branch wins.
        var resp = handler.HandleImport(new SnippetImportRequest
        {
            FileContent = NestedXml,
            SourceFormat = 0
        });

        Assert.True(resp.Success);
        Assert.Equal(1, resp.ImportedCount);
    }

    [Fact]
    public void HandleImport_SqlPromptXml_Unparseable_Fails()
    {
        var handler = new SnippetRequestHandler(_personalDir, _builtInDir);

        var resp = handler.HandleImport(new SnippetImportRequest
        {
            FileContent = "not xml {{{ <<<",
            SourceFormat = 1
        });

        Assert.False(resp.Success);
        Assert.Equal(0, resp.ImportedCount);
        Assert.NotEmpty(resp.FailedDetails);
    }
}
