using AkmlSql.Formatter.Commands;
using AkmlSql.Formatter.Output;

namespace AkmlSql.Formatter;

public class Program
{
    private const string Version = "1.0.0";

    public static int Main(string[] args)
    {
        try
        {
            var options = CliOptions.Parse(args);

            // Handle parse errors
            if (options.ErrorMessage != null)
            {
                ConsoleRenderer.WriteError(options.ErrorMessage);
                Console.Error.WriteLine("Run 'akmlsql-format --help' for usage information.");
                return ExitCodes.ParseError;
            }

            // Show help if requested or no arguments
            if (options.ShowHelp || args.Length == 0)
            {
                PrintHelp();
                return ExitCodes.Success;
            }

            // List profiles
            if (options.ListProfiles)
            {
                return new ProfileCommand().Execute(options);
            }

            // Validate that we have a target (file, directory, or stdin)
            if (options.FilePath == null && options.DirectoryPath == null && !options.UseStdin)
            {
                ConsoleRenderer.WriteError("No input specified. Provide a file, directory, or --stdin.");
                Console.Error.WriteLine("Run 'akmlsql-format --help' for usage information.");
                return ExitCodes.ParseError;
            }

            // Validate file exists (early check for single file mode)
            if (options.FilePath != null && !File.Exists(options.FilePath))
            {
                ConsoleRenderer.WriteError($"File not found: {options.FilePath}");
                return ExitCodes.FileNotFound;
            }

            // Validate directory exists
            if (options.DirectoryPath != null && !Directory.Exists(options.DirectoryPath))
            {
                ConsoleRenderer.WriteError($"Directory not found: {options.DirectoryPath}");
                return ExitCodes.FileNotFound;
            }

            // Route to command
            if (options.CheckMode)
            {
                return new CheckCommand().Execute(options);
            }

            if (options.DiffMode)
            {
                return new DiffCommand().Execute(options);
            }

            return new FormatCommand().Execute(options);
        }
        catch (Exception ex)
        {
            ConsoleRenderer.WriteError($"Unexpected error: {ex.Message}");
            return ExitCodes.InternalError;
        }
    }

    private static void PrintHelp()
    {
        Console.Error.WriteLine($"""
            akmlsql-format v{Version} - AKML SQL Formatter CLI

            USAGE:
              akmlsql-format [OPTIONS] [FILE|DIRECTORY]
              akmlsql-format --stdin [OPTIONS]
              cat file.sql | akmlsql-format --stdin --stdout

            ARGUMENTS:
              FILE|DIRECTORY          Path to a .sql file or directory to format

            OPTIONS:
              -f, --file <PATH>       Format a specific .sql file
              -d, --dir <PATH>        Format all .sql files in a directory
              -r, --recursive         Include subdirectories when using --dir
              -p, --profile <NAME>    Profile name to use (default: "Default")
                  --profile-file <PATH> Load profile from a specific .akmlstyle file
              -c, --check             Check formatting without modifying files
                  --diff              Show unified diff of formatting changes
                  --stdin             Read SQL from standard input
                  --stdout            Write formatted output to standard output
                  --report <PATH>     Write JSON report to file
                  --list-profiles     List available formatting profiles
              -j, --parallel <N>      Max parallel files (default: CPU count)
              -h, --help              Show this help message

            EXIT CODES:
              0  Success (all files formatted / no violations)
              1  Formatting violations found (--check / --diff mode)
              2  Parse error (invalid SQL or bad arguments)
              3  File or directory not found
              4  Invalid or missing profile
              5  Internal error

            EXAMPLES:
              akmlsql-format query.sql
              akmlsql-format -d ./src -r -p Compact
              akmlsql-format --check -d ./src -r
              akmlsql-format --diff query.sql
              akmlsql-format --stdin --stdout < query.sql > formatted.sql
              akmlsql-format -d ./src -r --report report.json
              akmlsql-format --list-profiles
            """);
    }
}
