namespace AkmlSql.Formatter;

/// <summary>
/// Parsed command-line options for the akmlsql-format CLI.
/// </summary>
public class CliOptions
{
    public string? FilePath { get; set; }
    public string? DirectoryPath { get; set; }
    public bool Recursive { get; set; }
    public string ProfileName { get; set; } = "Default";
    public string? ProfileFile { get; set; }
    public bool CheckMode { get; set; }
    public bool DiffMode { get; set; }
    public bool UseStdin { get; set; }
    public bool UseStdout { get; set; }
    public string? ReportPath { get; set; }
    public bool ListProfiles { get; set; }
    public int Parallel { get; set; } = Environment.ProcessorCount;
    public bool ShowHelp { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Parses command-line arguments into <see cref="CliOptions"/>.
    /// </summary>
    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    return options;

                case "-f":
                case "--file":
                    if (i + 1 < args.Length)
                        options.FilePath = args[++i];
                    else
                    {
                        options.ErrorMessage = $"Missing value for {arg}";
                        return options;
                    }
                    break;

                case "-d":
                case "--dir":
                case "--directory":
                    if (i + 1 < args.Length)
                        options.DirectoryPath = args[++i];
                    else
                    {
                        options.ErrorMessage = $"Missing value for {arg}";
                        return options;
                    }
                    break;

                case "-r":
                case "--recursive":
                    options.Recursive = true;
                    break;

                case "-p":
                case "--profile":
                    if (i + 1 < args.Length)
                        options.ProfileName = args[++i];
                    else
                    {
                        options.ErrorMessage = $"Missing value for {arg}";
                        return options;
                    }
                    break;

                case "--profile-file":
                    if (i + 1 < args.Length)
                        options.ProfileFile = args[++i];
                    else
                    {
                        options.ErrorMessage = $"Missing value for {arg}";
                        return options;
                    }
                    break;

                case "-c":
                case "--check":
                    options.CheckMode = true;
                    break;

                case "--diff":
                    options.DiffMode = true;
                    break;

                case "--stdin":
                    options.UseStdin = true;
                    break;

                case "--stdout":
                    options.UseStdout = true;
                    break;

                case "--report":
                    if (i + 1 < args.Length)
                        options.ReportPath = args[++i];
                    else
                    {
                        options.ErrorMessage = $"Missing value for {arg}";
                        return options;
                    }
                    break;

                case "--list-profiles":
                    options.ListProfiles = true;
                    break;

                case "-j":
                case "--parallel":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var parallel) && parallel > 0)
                        options.Parallel = parallel;
                    else
                    {
                        options.ErrorMessage = $"Invalid value for {arg}; expected a positive integer.";
                        return options;
                    }
                    break;

                default:
                    // Treat unrecognized positional arg as a file path if no file/dir set yet
                    if (!arg.StartsWith('-') && options.FilePath == null && options.DirectoryPath == null)
                    {
                        // Could be a file or directory
                        if (Directory.Exists(arg))
                            options.DirectoryPath = arg;
                        else
                            options.FilePath = arg;
                    }
                    else
                    {
                        options.ErrorMessage = $"Unknown option: {arg}";
                        return options;
                    }
                    break;
            }
        }

        return options;
    }
}
