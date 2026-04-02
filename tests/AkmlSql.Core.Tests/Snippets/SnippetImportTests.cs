using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Engine.Snippets;
using AkmlSql.Engine.Snippets.Models;
using MessagePack;
using Xunit;

namespace AkmlSql.Core.Tests.Snippets;

public class SnippetImportTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _personalFolder;
    private readonly string _builtInFolder;
    private readonly SnippetRequestHandler _handler;

    public SnippetImportTests()
    {
        var root = Path.Combine(Path.GetTempPath(), $"akml_import_test_{Guid.NewGuid():N}");
        _personalFolder = Path.Combine(root, "personal");
        _builtInFolder = Path.Combine(root, "builtin");
        Directory.CreateDirectory(_personalFolder);
        Directory.CreateDirectory(_builtInFolder);
        _handler = new SnippetRequestHandler(_personalFolder, _builtInFolder);
    }

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_personalFolder)!;
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string MakeSnippetJson(
        string shortcode = "tst",
        string id = "test-id-001",
        string name = "Test Snippet",
        string description = "A test snippet",
        string category = "Test")
    {
        var snippet = new Snippet
        {
            Metadata = new SnippetMetadata
            {
                Id = id,
                Shortcode = shortcode,
                Name = name,
                Description = description,
                Category = category,
                Tags = ["test"],
                Context = ["global"],
                SurroundsWith = false
            },
            Variables = [],
            Body = ["SELECT 1"]
        };
        return JsonSerializer.Serialize(snippet, JsonOptions);
    }

    private static SnippetImportRequest MakeRequest(string fileContent, int sourceFormat = 4, string? newName = null)
    {
        return new SnippetImportRequest
        {
            FileContent = fileContent,
            SourceFormat = sourceFormat,
            NewSnippetName = newName
        };
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ImportSingleAkmlSnippet_Succeeds()
    {
        var json = MakeSnippetJson();
        var request = MakeRequest(json, sourceFormat: 4);

        var response = _handler.HandleImport(request);

        Assert.True(response.Success);
        Assert.Equal(1, response.ImportedCount);
        Assert.Equal(0, response.FailedCount);
        Assert.Single(response.SnippetIds);
    }

    [Fact]
    public void ImportArray_Succeeds()
    {
        var snippet1 = MakeSnippetJson(shortcode: "s1", id: "id-001", name: "Snippet One");
        var snippet2 = MakeSnippetJson(shortcode: "s2", id: "id-002", name: "Snippet Two");
        var arrayJson = $"[{snippet1},{snippet2}]";
        var request = MakeRequest(arrayJson, sourceFormat: 4);

        var response = _handler.HandleImport(request);

        Assert.True(response.Success);
        Assert.Equal(2, response.ImportedCount);
        Assert.Equal(0, response.FailedCount);
        Assert.Equal(2, response.SnippetIds.Length);
    }

    [Fact]
    public void ImportAutoFormat_DetectsAkmlSnippet()
    {
        var json = MakeSnippetJson(shortcode: "auto", id: "auto-id");
        var request = MakeRequest(json, sourceFormat: 0); // Auto

        var response = _handler.HandleImport(request);

        Assert.True(response.Success);
        Assert.Equal(1, response.ImportedCount);
        Assert.Equal(0, response.FailedCount);
        Assert.Single(response.SnippetIds);
    }

    [Fact]
    public void ImportEmptyShortcode_Fails()
    {
        var snippet = new Snippet
        {
            Metadata = new SnippetMetadata
            {
                Id = "no-shortcode-id",
                Shortcode = "",
                Name = "No Shortcode",
                Category = "Test"
            },
            Body = ["SELECT 1"]
        };
        var json = JsonSerializer.Serialize(snippet, JsonOptions);
        var request = MakeRequest(json, sourceFormat: 4);

        var response = _handler.HandleImport(request);

        Assert.False(response.Success);
        Assert.Equal(0, response.ImportedCount);
        Assert.Equal(1, response.FailedCount);
        Assert.NotEmpty(response.FailedDetails);
    }

    [Fact]
    public void ImportInvalidJson_Fails()
    {
        var request = MakeRequest("this is not json at all {{{{", sourceFormat: 4);

        var response = _handler.HandleImport(request);

        Assert.False(response.Success);
    }

    [Fact]
    public void ImportEmptyContent_Fails()
    {
        var request = MakeRequest("", sourceFormat: 4);

        var response = _handler.HandleImport(request);

        Assert.False(response.Success);
        Assert.Equal(1, response.FailedCount);
        Assert.NotEmpty(response.FailedDetails);
    }
}
