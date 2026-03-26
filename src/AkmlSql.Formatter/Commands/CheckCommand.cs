using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using AkmlSql.Formatter.Output;
using AkmlSql.Formatting.Pipeline;

namespace AkmlSql.Formatter.Commands;

/// <summary>
/// Validates formatting without modifying files.
/// Returns exit code 0 (all formatted) or 1 (violations found).
/// </summary>
[SuppressMessage("ReSharper", "NullableWarningSuppressionIsUsed")]
public class CheckCommand
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
            return CheckStdin(profile);
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
        int violationCount = 0;
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
                    ConsoleRenderer.WriteError($"Failed to check {file}: {string.Join("; ", msgs)}");
                    Interlocked.Increment(ref errorCount);
                    report?.AddFile(file, false, false, msgs, result.ElapsedMs);
                    return;
                }

                bool isFormatted = !result.WasModified;
                ConsoleRenderer.WriteCheckResult(file, isFormatted);

                if (!isFormatted)
                {
                    Interlocked.Increment(ref violationCount);
                }

                report?.AddFile(file, result.WasModified, true,
                    result.Diagnostics.Select(d => $"[{d.Severity}] {d.Message}").ToArray(),
                    result.ElapsedMs);
            }
            catch (Exception ex)
            {
                ConsoleRenderer.WriteError($"Error checking {file}: {ex.Message}");
                Interlocked.Increment(ref errorCount);
                report?.AddFile(file, false, false, [ex.Message], 0);
            }
        });

        var violations = violationCount;
        var errors = errorCount;

        sw.Stop();

        ConsoleRenderer.WriteCheckSummary(files.Count, violations, sw.ElapsedMilliseconds);

        if (report != null && options.ReportPath != null)
        {
            report.SetTotalElapsed(sw.ElapsedMilliseconds);
            report.WriteToFile(options.ReportPath);
            ConsoleRenderer.WriteInfo($"Report written to {options.ReportPath}");
        }

        if (errors > 0) return ExitCodes.ParseError;
        return violations > 0 ? ExitCodes.Violations : ExitCodes.Success;
    }

    private static int CheckStdin(Formatting.Profiles.FormattingProfile profile)
    {
        try
        {
            var sql = Console.In.ReadToEnd();
            var pipeline = new FormatterPipeline();
            var result = pipeline.Format(sql, profile);

            if (!result.Success)
            {
                ConsoleRenderer.WriteError("Format check failed (parse error).");
                return ExitCodes.ParseError;
            }

            if (result.WasModified)
            {
                ConsoleRenderer.WriteCheckResult("<stdin>", false);
                return ExitCodes.Violations;
            }

            ConsoleRenderer.WriteCheckResult("<stdin>", true);
            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            ConsoleRenderer.WriteError($"Error: {ex.Message}");
            return ExitCodes.InternalError;
        }
    }
}
