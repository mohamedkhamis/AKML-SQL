namespace AkmlSql.Formatter;

/// <summary>
/// Discovers .sql files from CLI options.
/// </summary>
public static class FileDiscovery
{
    private static readonly string[] SqlExtensions = [".sql"];

    /// <summary>
    /// Returns the list of .sql file paths to process based on CLI options.
    /// </summary>
    public static List<string> GetFiles(CliOptions options)
    {
        var files = new List<string>();

        if (options.FilePath != null)
        {
            files.Add(Path.GetFullPath(options.FilePath));
        }
        else if (options.DirectoryPath != null)
        {
            var searchOption = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var dir = Path.GetFullPath(options.DirectoryPath);

            foreach (var ext in SqlExtensions)
            {
                files.AddRange(Directory.GetFiles(dir, $"*{ext}", searchOption));
            }

            files.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return files;
    }
}
