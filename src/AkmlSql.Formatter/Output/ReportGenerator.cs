using System.Text.Json;
using System.Text.Json.Serialization;

namespace AkmlSql.Formatter.Output;

/// <summary>
/// Generates a JSON report for bulk format operations.
/// </summary>
public class ReportGenerator
{
    private readonly List<FileReport> _files = new();
    private long _totalElapsedMs;

    public void AddFile(string path, bool modified, bool success, string[] diagnostics, long elapsedMs)
    {
        _files.Add(new FileReport
        {
            Path = path,
            Modified = modified,
            Success = success,
            Diagnostics = diagnostics,
            ElapsedMs = elapsedMs
        });
    }

    public void SetTotalElapsed(long elapsedMs)
    {
        _totalElapsedMs = elapsedMs;
    }

    public string Generate()
    {
        var report = new FormatReport
        {
            Timestamp = DateTime.UtcNow,
            TotalFiles = _files.Count,
            ModifiedFiles = _files.Count(f => f.Modified),
            ErrorFiles = _files.Count(f => !f.Success),
            TotalElapsedMs = _totalElapsedMs,
            Files = _files
        };

        return JsonSerializer.Serialize(report, ReportJsonContext.Default.FormatReport);
    }

    public void WriteToFile(string path)
    {
        var json = Generate();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, json);
    }
}

public class FormatReport
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("modifiedFiles")]
    public int ModifiedFiles { get; set; }

    [JsonPropertyName("errorFiles")]
    public int ErrorFiles { get; set; }

    [JsonPropertyName("totalElapsedMs")]
    public long TotalElapsedMs { get; set; }

    [JsonPropertyName("files")]
    public List<FileReport> Files { get; set; } = new();
}

public class FileReport
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("modified")]
    public bool Modified { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("diagnostics")]
    public string[] Diagnostics { get; set; } = [];

    [JsonPropertyName("elapsedMs")]
    public long ElapsedMs { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(FormatReport))]
internal partial class ReportJsonContext : JsonSerializerContext;
