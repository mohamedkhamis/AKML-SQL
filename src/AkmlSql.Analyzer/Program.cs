using AkmlSql.Core;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace AkmlSql.Analyzer;

/// <summary>
/// Entry point for the AkmlSql.Analyzer CLI tool.
/// Exit codes: 0 = clean, 1 = violations found (--check), 2 = error.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        AnalyzerOptions opts;
        try
        {
            opts = AnalyzerOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine("Run with --help for usage.");
            return 2;
        }

        if (opts.Help)
        {
            PrintHelp();
            return 0;
        }

        if (opts.Version)
        {
            Console.WriteLine($"AkmlSql.Analyzer {Constants.Version}");
            return 0;
        }

        if (!opts.TryValidate(out var validationError))
        {
            Console.Error.WriteLine($"Error: {validationError}");
            return 2;
        }

        try
        {
            return RunAnalysis(opts);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Analysis cancelled.");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 2;
        }
    }

    private static int RunAnalysis(AnalyzerOptions opts)
    {
        var analyzer = new BatchFileAnalyzer(opts.SettingsPath);
        var ct       = CancellationToken.None;

        var results = new List<(string FilePath, IReadOnlyList<Engine.Analysis.AnalysisDiagnostic> Diagnostics)>();

        if (!string.IsNullOrEmpty(opts.File))
        {
            var diags = analyzer.AnalyzeFile(opts.File!, opts, ct);
            results.Add((opts.File!, diags));
        }
        else
        {
            foreach (var r in analyzer.AnalyzeDirectory(opts.Directory!, opts.Recursive, opts, ct))
                results.Add(r);
        }

        var inputLabel = opts.File ?? opts.Directory ?? string.Empty;
        var writer     = new ReportWriter(opts);
        var hasViolations = writer.WriteReport(results, inputLabel);

        if (opts.Check && hasViolations)
            return 1;

        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"""
            AkmlSql.Analyzer {Constants.Version} — Static SQL code analysis

            Usage:
              AkmlSql.Analyzer.exe [options]

            Input (one required):
              --file <path>              Analyze a single .sql file
              --directory <path>         Analyze all .sql files in a directory
              --recursive                (with --directory) Include subdirectories

            Output:
              --report <path>            Write JSON report to file
              --format text|json         Output format for stdout (default: text)

            Filtering:
              --severity error|warning|information|hint
                                         Minimum severity to report (default: information)
              --rules PE001,BP004,...    Only run specified rules
              --exclude-rules PE008,...  Skip specified rules

            Configuration:
              --settings <path>          Path to a .casettings JSON file

            CI/CD:
              --check                    Exit 1 if violations found at --severity level

            Misc:
              --version                  Print version and exit
              --help                     Print this help and exit
            """);
    }
}
