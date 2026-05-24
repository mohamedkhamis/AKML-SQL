using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using AkmlSql.Engine.Snippets.Models;
using Serilog;

namespace AkmlSql.Engine.Snippets;

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Snippet models are simple POCOs preserved by DynamicDependency in Program.cs")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class SnippetLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public List<(Snippet Snippet, SnippetSourceType Source, string? FilePath)> LoadFromSources(IEnumerable<SnippetSource> sources)
    {
        var results = new List<(Snippet, SnippetSourceType, string?)>();
        foreach (var source in sources.Where(s => s.IsAvailable))
        {
            var snippets = LoadFromDirectory(source.Path, source.Type);
            results.AddRange(snippets);
        }
        return results;
    }

    public List<(Snippet Snippet, SnippetSourceType Source, string? FilePath)> LoadFromDirectory(string directoryPath, SnippetSourceType sourceType)
    {
        var results = new List<(Snippet, SnippetSourceType, string?)>();
        if (!Directory.Exists(directoryPath))
        {
            // Expected on fresh install: the personal folder is created on first
            // SaveSnippet, and the built-in folder is only present when the installer
            // ships a snippet pack. Log at Information so it's still visible for
            // diagnostics without looking like a defect.
            Log.Information("Snippet directory not present ({Source}): {Path}", sourceType, directoryPath);
            return results;
        }

        foreach (var file in Directory.EnumerateFiles(directoryPath, "*.akmlsnippet"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var snippet = JsonSerializer.Deserialize<Snippet>(json, JsonOptions);
                if (snippet != null && !string.IsNullOrEmpty(snippet.Metadata.Shortcode))
                {
                    results.Add((snippet, sourceType, file));
                }
                else
                {
                    Log.Warning("Invalid snippet file (missing shortcode): {File}", file);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load snippet file: {File}", file);
            }
        }

        Log.Information("Loaded {Count} snippets from {Path} ({Source})", results.Count, directoryPath, sourceType);
        return results;
    }

    public Snippet? LoadSingle(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathRooted(filePath))
            {
                Log.Warning("LoadSingle rejected non-absolute path: {Path}", filePath);
                return null;
            }
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<Snippet>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load snippet: {Path}", filePath);
            return null;
        }
    }

    public void SaveSnippet(Snippet snippet, string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
        var fileName = $"{snippet.Metadata.Shortcode}.akmlsnippet";
        var filePath = Path.Combine(directoryPath, fileName);
        var json = JsonSerializer.Serialize(snippet, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        File.WriteAllText(filePath, json);
    }

    public bool DeleteSnippet(string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath) || !Path.IsPathRooted(filePath))
            {
                Log.Warning("DeleteSnippet rejected non-absolute path: {Path}", filePath);
                return false;
            }
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete snippet file: {Path}", filePath);
        }
        return false;
    }
}
