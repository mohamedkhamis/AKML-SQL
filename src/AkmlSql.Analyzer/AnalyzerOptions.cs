using AkmlSql.Core.Models.Analysis;

namespace AkmlSql.Analyzer;

/// <summary>Strongly-typed CLI options parsed from <c>string[] args</c>.</summary>
internal sealed class AnalyzerOptions
{
    public string?       File           { get; private set; }
    public string?       Directory      { get; private set; }
    public bool          Recursive      { get; private set; }
    public string?       ReportPath     { get; private set; }
    public string        Format         { get; private set; } = "text";
    public DiagnosticSeverity MinSeverity { get; private set; } = DiagnosticSeverity.Information;
    public HashSet<string>? OnlyRules    { get; private set; }
    public HashSet<string>? ExcludeRules { get; private set; }
    public string?       SettingsPath   { get; private set; }
    public bool          Check          { get; private set; }
    public bool          Version        { get; private set; }
    public bool          Help           { get; private set; }

    private AnalyzerOptions() { }

    /// <summary>
    /// Parses <paramref name="args"/> into an <see cref="AnalyzerOptions"/> instance.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when arguments are invalid or required values are missing.</exception>
    public static AnalyzerOptions Parse(string[] args)
    {
        var opts = new AnalyzerOptions();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            switch (arg.ToLowerInvariant())
            {
                case "--help":     opts.Help    = true; return opts;
                case "--version":  opts.Version = true; return opts;
                case "--recursive": opts.Recursive = true; break;
                case "--check":     opts.Check     = true; break;

                case "--file":
                    opts.File = GetNextRequired(args, ref i, "--file");
                    break;

                case "--directory":
                    opts.Directory = GetNextRequired(args, ref i, "--directory");
                    break;

                case "--report":
                    opts.ReportPath = GetNextRequired(args, ref i, "--report");
                    break;

                case "--format":
                {
                    var fmt = GetNextRequired(args, ref i, "--format").ToLowerInvariant();
                    if (fmt != "text" && fmt != "json")
                        throw new ArgumentException($"--format must be 'text' or 'json', got '{fmt}'");
                    opts.Format = fmt;
                    break;
                }

                case "--severity":
                {
                    var sev = GetNextRequired(args, ref i, "--severity").ToLowerInvariant();
                    opts.MinSeverity = sev switch
                    {
                        "error"       => DiagnosticSeverity.Error,
                        "warning"     => DiagnosticSeverity.Warning,
                        "information" => DiagnosticSeverity.Information,
                        "hint"        => DiagnosticSeverity.Hint,
                        _             => throw new ArgumentException($"--severity must be error|warning|information|hint, got '{sev}'")
                    };
                    break;
                }

                case "--rules":
                    opts.OnlyRules = SplitRuleList(GetNextRequired(args, ref i, "--rules"));
                    break;

                case "--exclude-rules":
                    opts.ExcludeRules = SplitRuleList(GetNextRequired(args, ref i, "--exclude-rules"));
                    break;

                case "--settings":
                    opts.SettingsPath = GetNextRequired(args, ref i, "--settings");
                    break;

                default:
                    throw new ArgumentException($"Unknown argument: {arg}");
            }
        }

        return opts;
    }

    /// <summary>Returns true and sets error if the options are invalid for analysis.</summary>
    public bool TryValidate(out string error)
    {
        if (Help || Version) { error = string.Empty; return true; }

        if (string.IsNullOrEmpty(File) && string.IsNullOrEmpty(Directory))
        {
            error = "Either --file or --directory is required.";
            return false;
        }

        if (!string.IsNullOrEmpty(File) && !System.IO.File.Exists(File))
        {
            error = $"File not found: {File}";
            return false;
        }

        if (!string.IsNullOrEmpty(Directory) && !System.IO.Directory.Exists(Directory))
        {
            error = $"Directory not found: {Directory}";
            return false;
        }

        if (!string.IsNullOrEmpty(SettingsPath) && !System.IO.File.Exists(SettingsPath))
        {
            error = $"Settings file not found: {SettingsPath}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string GetNextRequired(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{flag} requires a value");
        return args[++i];
    }

    private static HashSet<string> SplitRuleList(string s)
    {
        return new HashSet<string>(s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
    }
}
