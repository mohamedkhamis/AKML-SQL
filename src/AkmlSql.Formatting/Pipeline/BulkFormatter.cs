using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatting.Pipeline;

/// <summary>Options controlling a bulk format operation across multiple files.</summary>
public class BulkFormatOptions
{
    /// <summary>Name of the formatting profile to apply to all files.</summary>
    public string ProfileName { get; set; } = "Default";

    /// <summary>Write <c>.bak</c> backup files alongside each modified file.</summary>
    public bool CreateBackups { get; set; } = true;

    /// <summary>Skip files that fail to parse rather than aborting the whole operation.</summary>
    public bool SkipParseErrors { get; set; } = true;

    /// <summary>Honour <c>-- noformat</c> / <c>-- endnoformat</c> regions in each file.</summary>
    public bool RespectNoformat { get; set; } = true;

    /// <summary>When <c>true</c>, compute results without writing any files to disk.</summary>
    public bool PreviewOnly { get; set; }

    /// <summary>Maximum number of files formatted in parallel. Defaults to processor count.</summary>
    public int MaxParallelism { get; set; } = Environment.ProcessorCount;
}

/// <summary>Formatting result for a single file within a bulk operation.</summary>
public class FileFormatResult
{
    /// <summary>Absolute path of the file that was processed.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Outcome status for this file.</summary>
    public FileFormatStatus Status { get; set; }

    /// <summary>Number of lines that changed. <c>0</c> when the file was already formatted.</summary>
    public int LinesChanged { get; set; }

    /// <summary>Error description when <see cref="Status"/> is <c>Error</c> or <c>ParseError</c>.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Elapsed time in milliseconds for this file.</summary>
    public long ElapsedMs { get; set; }
}

/// <summary>Outcome of formatting a single file during bulk formatting.</summary>
public enum FileFormatStatus
{
    Formatted = 0,
    AlreadyFormatted = 1,
    ParseError = 2,
    Skipped = 3,
    Error = 4
}

public class BulkFormatReport
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string ProfileName { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int FormattedCount { get; set; }
    public int AlreadyFormattedCount { get; set; }
    public int ParseErrorCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public int TotalLinesChanged { get; set; }
    public long ElapsedMs { get; set; }
    public List<FileFormatResult> Details { get; set; } = [];

    /// <summary>
    /// Serializes this report to indented JSON for saving to disk.
    /// </summary>
    public string ToJson()
    {
        return JsonSerializer.Serialize(this, BulkFormatReportJsonContext.Default.BulkFormatReport);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BulkFormatReport))]
internal partial class BulkFormatReportJsonContext : JsonSerializerContext;

/// <summary>
/// Applies <see cref="FormatterPipeline"/> to a collection of SQL files in parallel.
/// Respects <see cref="BulkFormatOptions.MaxParallelism"/> and supports cooperative cancellation.
/// Produces a <see cref="BulkFormatReport"/> summarising per-file outcomes.
/// </summary>
public class BulkFormatter
{
    private readonly FormatterPipeline _pipeline = new();

    /// <summary>
    /// Formats all files in <paramref name="filePaths"/> concurrently using <paramref name="profile"/>.
    /// Reports progress via <paramref name="progress"/> and supports cancellation via <paramref name="ct"/>.
    /// </summary>
    public async Task<BulkFormatReport> FormatFilesAsync(
        IReadOnlyList<string> filePaths,
        FormattingProfile profile,
        BulkFormatOptions options,
        IProgress<(int completed, int total, string currentFile)>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var results = new ConcurrentBag<FileFormatResult>();
        int completed = 0;

        try
        {
            await Parallel.ForEachAsync(filePaths,
                new ParallelOptions { MaxDegreeOfParallelism = options.MaxParallelism, CancellationToken = ct },
                async (filePath, token) =>
                {
                    var result = await FormatFileAsync(filePath, profile, options, token);
                    results.Add(result);
                    var c = Interlocked.Increment(ref completed);
                    progress?.Report((c, filePaths.Count, filePath));
                });
        }
        catch (OperationCanceledException)
        {
            // Cancellation — partial results are already in the ConcurrentBag
        }

        sw.Stop();
        var details = results.OrderBy(r => r.FilePath).ToList();

        return new BulkFormatReport
        {
            ProfileName = profile.Metadata.Name,
            TotalFiles = filePaths.Count,
            FormattedCount = details.Count(r => r.Status == FileFormatStatus.Formatted),
            AlreadyFormattedCount = details.Count(r => r.Status == FileFormatStatus.AlreadyFormatted),
            ParseErrorCount = details.Count(r => r.Status == FileFormatStatus.ParseError),
            SkippedCount = details.Count(r => r.Status == FileFormatStatus.Skipped),
            ErrorCount = details.Count(r => r.Status == FileFormatStatus.Error),
            TotalLinesChanged = details.Sum(r => r.LinesChanged),
            ElapsedMs = sw.ElapsedMilliseconds,
            Details = details
        };
    }

    private async Task<FileFormatResult> FormatFileAsync(
        string filePath, FormattingProfile profile, BulkFormatOptions options, CancellationToken ct)
    {
        var fSw = Stopwatch.StartNew();
        try
        {
            if (!File.Exists(filePath))
                return new FileFormatResult
                {
                    FilePath = filePath,
                    Status = FileFormatStatus.Skipped,
                    ErrorMessage = "File not found",
                    ElapsedMs = fSw.ElapsedMilliseconds
                };

            var attr = File.GetAttributes(filePath);
            if (attr.HasFlag(FileAttributes.ReadOnly))
                return new FileFormatResult
                {
                    FilePath = filePath,
                    Status = FileFormatStatus.Skipped,
                    ErrorMessage = "Read-only",
                    ElapsedMs = fSw.ElapsedMilliseconds
                };

            var originalText = await File.ReadAllTextAsync(filePath, ct);

            // Check for noformat whole-file marker
            if (options.RespectNoformat)
            {
                var scanner = new NoformatScanner();
                var regions = scanner.Scan(originalText);
                // If the entire file is in a noformat region, skip it
                if (regions is [{ StartOffset: 0 }] && regions[0].EndOffset >= originalText.Length)
                    return new FileFormatResult
                    {
                        FilePath = filePath,
                        Status = FileFormatStatus.Skipped,
                        ErrorMessage = "File marked as noformat",
                        ElapsedMs = fSw.ElapsedMilliseconds
                    };
            }

            var result = _pipeline.Format(originalText, profile);

            if (!result.Success)
            {
                if (options.SkipParseErrors)
                    return new FileFormatResult
                    {
                        FilePath = filePath,
                        Status = FileFormatStatus.ParseError,
                        ErrorMessage = "Parse failed",
                        ElapsedMs = fSw.ElapsedMilliseconds
                    };

                return new FileFormatResult
                {
                    FilePath = filePath,
                    Status = FileFormatStatus.Error,
                    ErrorMessage = "Parse failed",
                    ElapsedMs = fSw.ElapsedMilliseconds
                };
            }

            if (!result.WasModified)
                return new FileFormatResult
                {
                    FilePath = filePath,
                    Status = FileFormatStatus.AlreadyFormatted,
                    ElapsedMs = fSw.ElapsedMilliseconds
                };

            if (!options.PreviewOnly)
            {
                if (options.CreateBackups)
                    File.Copy(filePath, filePath + ".bak", overwrite: true);

                await File.WriteAllTextAsync(filePath, result.FormattedText, ct);
            }

            var linesChanged = CountDifferentLines(originalText, result.FormattedText);
            return new FileFormatResult
            {
                FilePath = filePath,
                Status = FileFormatStatus.Formatted,
                LinesChanged = linesChanged,
                ElapsedMs = fSw.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException)
        {
            return new FileFormatResult
            {
                FilePath = filePath,
                Status = FileFormatStatus.Skipped,
                ErrorMessage = "Cancelled",
                ElapsedMs = fSw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            return new FileFormatResult
            {
                FilePath = filePath,
                Status = FileFormatStatus.Error,
                ErrorMessage = ex.Message,
                ElapsedMs = fSw.ElapsedMilliseconds
            };
        }
    }

    private static int CountDifferentLines(string a, string b)
    {
        var aLines = a.Split('\n');
        var bLines = b.Split('\n');
        int diff = Math.Abs(aLines.Length - bLines.Length);
        for (int i = 0; i < Math.Min(aLines.Length, bLines.Length); i++)
            if (aLines[i] != bLines[i]) diff++;
        return diff;
    }
}
