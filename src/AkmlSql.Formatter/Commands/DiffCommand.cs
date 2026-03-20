using System.Diagnostics;
using AkmlSql.Formatter.Output;
using AkmlSql.Formatting.Pipeline;

namespace AkmlSql.Formatter.Commands;

/// <summary>
/// Computes and displays unified diff between original and formatted SQL.
/// </summary>
public class DiffCommand
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
            return DiffStdin(profile);
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
        int violations = 0;
        int errors = 0;

        // Diff is inherently sequential for readable output
        foreach (var file in files)
        {
            if (!File.Exists(file))
            {
                ConsoleRenderer.WriteError($"File not found: {file}");
                errors++;
                continue;
            }

            try
            {
                var sql = File.ReadAllText(file);
                var result = pipeline.Format(sql, profile);

                if (!result.Success)
                {
                    var msgs = result.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.Message);
                    ConsoleRenderer.WriteError($"Failed to format {file}: {string.Join("; ", msgs)}");
                    errors++;
                    continue;
                }

                if (result.WasModified)
                {
                    violations++;
                    var diffLines = UnifiedDiffFormatter.ComputeDiff(sql, result.FormattedText, file);
                    UnifiedDiffFormatter.RenderToConsole(diffLines);
                    Console.WriteLine(); // blank line between files
                }
            }
            catch (Exception ex)
            {
                ConsoleRenderer.WriteError($"Error processing {file}: {ex.Message}");
                errors++;
            }
        }

        sw.Stop();

        ConsoleRenderer.WriteCheckSummary(files.Count, violations, sw.ElapsedMilliseconds);

        if (errors > 0) return ExitCodes.ParseError;
        return violations > 0 ? ExitCodes.Violations : ExitCodes.Success;
    }

    private static int DiffStdin(AkmlSql.Formatting.Profiles.FormattingProfile profile)
    {
        try
        {
            var sql = Console.In.ReadToEnd();
            var pipeline = new FormatterPipeline();
            var result = pipeline.Format(sql, profile);

            if (!result.Success)
            {
                ConsoleRenderer.WriteError("Format failed (parse error).");
                return ExitCodes.ParseError;
            }

            if (result.WasModified)
            {
                var diffLines = UnifiedDiffFormatter.ComputeDiff(sql, result.FormattedText, "<stdin>");
                UnifiedDiffFormatter.RenderToConsole(diffLines);
                return ExitCodes.Violations;
            }

            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            ConsoleRenderer.WriteError($"Error: {ex.Message}");
            return ExitCodes.InternalError;
        }
    }
}
