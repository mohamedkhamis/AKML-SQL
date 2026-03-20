using System.Diagnostics;
using AkmlSql.Formatter.Output;
using AkmlSql.Formatting.Pipeline;

namespace AkmlSql.Formatter.Commands;

/// <summary>
/// Formats SQL files in-place using <see cref="FormatterPipeline"/> and a formatting profile.
/// </summary>
public class FormatCommand
{
    public int Execute(CliOptions options)
    {
        // Load profile
        var profile = ProfileLoader.Load(options, out var profileError);
        if (profile == null)
        {
            ConsoleRenderer.WriteError(profileError!);
            return ExitCodes.InvalidProfile;
        }

        // Handle stdin mode
        if (options.UseStdin)
        {
            return FormatStdin(profile, options.UseStdout);
        }

        // Discover files
        var files = FileDiscovery.GetFiles(options);
        if (files.Count == 0)
        {
            ConsoleRenderer.WriteError("No .sql files found.");
            return ExitCodes.FileNotFound;
        }

        var sw = Stopwatch.StartNew();
        var pipeline = new FormatterPipeline();
        var report = options.ReportPath != null ? new ReportGenerator() : null;

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = options.Parallel };
        int modifiedCount = 0;
        int errorCount = 0;

        Parallel.ForEach(files, parallelOptions, file =>
        {
            if (!File.Exists(file))
            {
                ConsoleRenderer.WriteError($"File not found: {file}");
                Interlocked.Increment(ref errorCount);
                report?.AddFile(file, false, false, ["File not found"], 0);
                return;
            }

            try
            {
                var sql = File.ReadAllText(file);
                var result = pipeline.Format(sql, profile);

                if (!result.Success)
                {
                    var msgs = result.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.Message)
                        .ToArray();
                    ConsoleRenderer.WriteError($"Failed to format {file}: {string.Join("; ", msgs)}");
                    Interlocked.Increment(ref errorCount);
                    report?.AddFile(file, false, false, msgs, result.ElapsedMs);
                    return;
                }

                if (result.WasModified)
                {
                    if (!options.UseStdout)
                    {
                        File.WriteAllText(file, result.FormattedText);
                    }
                    Interlocked.Increment(ref modifiedCount);
                }

                if (options.UseStdout)
                {
                    // In stdout mode, output the formatted text
                    Console.Write(result.FormattedText);
                }
                else
                {
                    ConsoleRenderer.WriteFile(file, result.WasModified);
                }

                var diagMsgs = result.Diagnostics.Select(d => $"[{d.Severity}] {d.Message}").ToArray();
                report?.AddFile(file, result.WasModified, true, diagMsgs, result.ElapsedMs);
            }
            catch (Exception ex)
            {
                ConsoleRenderer.WriteError($"Error processing {file}: {ex.Message}");
                Interlocked.Increment(ref errorCount);
                report?.AddFile(file, false, false, [ex.Message], 0);
            }
        });

        sw.Stop();

        if (!options.UseStdout)
        {
            ConsoleRenderer.WriteSummary(files.Count, modifiedCount, errorCount, sw.ElapsedMilliseconds);
        }

        if (report != null && options.ReportPath != null)
        {
            report.SetTotalElapsed(sw.ElapsedMilliseconds);
            report.WriteToFile(options.ReportPath);
            ConsoleRenderer.WriteInfo($"Report written to {options.ReportPath}");
        }

        return errorCount > 0 ? ExitCodes.ParseError : ExitCodes.Success;
    }

    private static int FormatStdin(AkmlSql.Formatting.Profiles.FormattingProfile profile, bool useStdout)
    {
        try
        {
            var sql = Console.In.ReadToEnd();
            var pipeline = new FormatterPipeline();
            var result = pipeline.Format(sql, profile);

            if (!result.Success)
            {
                var msgs = result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => d.Message);
                ConsoleRenderer.WriteError($"Format failed: {string.Join("; ", msgs)}");
                return ExitCodes.ParseError;
            }

            // Always write to stdout in stdin mode
            Console.Write(result.FormattedText);
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            ConsoleRenderer.WriteError($"Error: {ex.Message}");
            return ExitCodes.InternalError;
        }
    }
}
