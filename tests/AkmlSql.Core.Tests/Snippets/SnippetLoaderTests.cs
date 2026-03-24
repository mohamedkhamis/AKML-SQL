using System;
using System.IO;
using System.Text.Json;
using AkmlSql.Engine.Snippets;
using AkmlSql.Engine.Snippets.Models;
using Xunit;

namespace AkmlSql.Core.Tests.Snippets;

public class SnippetLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnippetLoader _loader = new();

    public SnippetLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"akml_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string WriteSnippetFile(string shortcode, string? id = null)
    {
        var snippet = new Snippet
        {
            Metadata = new SnippetMetadata
            {
                Id = id ?? Guid.NewGuid().ToString(),
                Shortcode = shortcode,
                Name = $"Test {shortcode}",
                Category = "Test"
            },
            Body = [$"SELECT -- {shortcode}"]
        };
        var filePath = Path.Combine(_tempDir, $"{shortcode}.akmlsnippet");
        File.WriteAllText(filePath, JsonSerializer.Serialize(snippet, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
        return filePath;
    }

    // ── LoadSingle ────────────────────────────────────────────────────────────

    [Fact]
    public void LoadSingle_ReturnsSnippet_WhenFileIsValid()
    {
        var filePath = WriteSnippetFile("sel");
        var snippet = _loader.LoadSingle(filePath);
        Assert.NotNull(snippet);
        Assert.Equal("sel", snippet.Metadata.Shortcode);
    }

    [Fact]
    public void LoadSingle_ReturnsNull_WhenPathIsRelative()
    {
        Assert.Null(_loader.LoadSingle("relative/path/file.akmlsnippet"));
    }

    [Fact]
    public void LoadSingle_ReturnsNull_WhenPathIsEmpty()
    {
        Assert.Null(_loader.LoadSingle(""));
    }

    [Fact]
    public void LoadSingle_ReturnsNull_WhenPathIsWhitespace()
    {
        Assert.Null(_loader.LoadSingle("   "));
    }

    [Fact]
    public void LoadSingle_ReturnsNull_WhenFileDoesNotExist()
    {
        var missing = Path.Combine(_tempDir, "nonexistent.akmlsnippet");
        Assert.Null(_loader.LoadSingle(missing));
    }

    [Fact]
    public void LoadSingle_ReturnsNull_WhenJsonIsInvalid()
    {
        var filePath = Path.Combine(_tempDir, "bad.akmlsnippet");
        File.WriteAllText(filePath, "{ invalid json !!!");
        Assert.Null(_loader.LoadSingle(filePath));
    }

    // ── LoadFromDirectory ─────────────────────────────────────────────────────

    [Fact]
    public void LoadFromDirectory_ReturnsSnippets_WhenDirectoryContainsFiles()
    {
        WriteSnippetFile("sel");
        WriteSnippetFile("ins");

        var results = _loader.LoadFromDirectory(_tempDir, SnippetSourceType.Personal);
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void LoadFromDirectory_ReturnsEmpty_WhenDirectoryDoesNotExist()
    {
        var missing = Path.Combine(_tempDir, "no_such_dir");
        var results = _loader.LoadFromDirectory(missing, SnippetSourceType.Personal);
        Assert.Empty(results);
    }

    [Fact]
    public void LoadFromDirectory_SkipsFilesWithMissingShortcode()
    {
        // Write a snippet with no shortcode
        var snippet = new Snippet { Metadata = new SnippetMetadata { Shortcode = "" } };
        var filePath = Path.Combine(_tempDir, "noshortcode.akmlsnippet");
        File.WriteAllText(filePath, JsonSerializer.Serialize(snippet,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

        WriteSnippetFile("valid");

        var results = _loader.LoadFromDirectory(_tempDir, SnippetSourceType.Personal);
        Assert.Single(results); // only the valid one
    }

    [Fact]
    public void LoadFromDirectory_SetsCorrectSourceType()
    {
        WriteSnippetFile("sel");
        var results = _loader.LoadFromDirectory(_tempDir, SnippetSourceType.Team);
        Assert.All(results, r => Assert.Equal(SnippetSourceType.Team, r.Source));
    }

    [Fact]
    public void LoadFromDirectory_TracksFilePaths()
    {
        WriteSnippetFile("sel");
        var results = _loader.LoadFromDirectory(_tempDir, SnippetSourceType.Personal);
        Assert.All(results, r => Assert.NotNull(r.FilePath));
    }

    // ── SaveSnippet ───────────────────────────────────────────────────────────

    [Fact]
    public void SaveSnippet_WritesFileAndCanBeReadBack()
    {
        var snippet = new Snippet
        {
            Metadata = new SnippetMetadata
            {
                Id = "test-id",
                Shortcode = "mysnip",
                Name = "My Snippet",
                Category = "Test"
            },
            Body = ["SELECT 1"]
        };

        _loader.SaveSnippet(snippet, _tempDir);

        var expectedPath = Path.Combine(_tempDir, "mysnip.akmlsnippet");
        Assert.True(File.Exists(expectedPath));

        var loaded = _loader.LoadSingle(expectedPath);
        Assert.NotNull(loaded);
        Assert.Equal("mysnip", loaded.Metadata.Shortcode);
        Assert.Equal("My Snippet", loaded.Metadata.Name);
    }

    [Fact]
    public void SaveSnippet_CreatesDirectoryIfAbsent()
    {
        var subDir = Path.Combine(_tempDir, "new_subdir");
        var snippet = new Snippet { Metadata = new SnippetMetadata { Shortcode = "x" } };
        _loader.SaveSnippet(snippet, subDir);
        Assert.True(Directory.Exists(subDir));
    }

    // ── DeleteSnippet ─────────────────────────────────────────────────────────

    [Fact]
    public void DeleteSnippet_ReturnsTrueAndDeletesFile()
    {
        var filePath = WriteSnippetFile("del");
        Assert.True(File.Exists(filePath));

        var result = _loader.DeleteSnippet(filePath);
        Assert.True(result);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public void DeleteSnippet_ReturnsFalse_WhenFileDoesNotExist()
    {
        var missing = Path.Combine(_tempDir, "missing.akmlsnippet");
        Assert.False(_loader.DeleteSnippet(missing));
    }

    [Fact]
    public void DeleteSnippet_ReturnsFalse_WhenPathIsRelative()
    {
        Assert.False(_loader.DeleteSnippet("relative/path.akmlsnippet"));
    }

    [Fact]
    public void DeleteSnippet_ReturnsFalse_WhenPathIsEmpty()
    {
        Assert.False(_loader.DeleteSnippet(""));
    }
}
