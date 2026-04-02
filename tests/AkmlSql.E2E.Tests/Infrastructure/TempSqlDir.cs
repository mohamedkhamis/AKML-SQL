namespace AkmlSql.E2E.Tests.Infrastructure;

/// <summary>
/// Creates a temporary directory for E2E test files and deletes it on disposal.
/// Usage:
///   using var tmp = new TempSqlDir();
///   var file = tmp.Write("q.sql", "select 1");
///   // file.FullName is the absolute path
/// </summary>
public sealed class TempSqlDir : IDisposable
{
    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"akml_e2e_{Guid.NewGuid():N}");

    public TempSqlDir() => Directory.CreateDirectory(Path);

    /// <summary>Writes <paramref name="content"/> to a file inside the temp dir.</summary>
    public FileInfo Write(string fileName, string content)
    {
        var fullPath = System.IO.Path.Combine(Path, fileName);
        // Create any subdirectories the fileName may contain
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return new FileInfo(fullPath);
    }

    /// <summary>Creates a named sub-directory inside the temp dir.</summary>
    public string SubDir(string name)
    {
        var dir = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Returns the full path of a file that may or may not exist.</summary>
    public string FilePath(string relativeName)
    {
        return System.IO.Path.Combine(Path, relativeName);
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { /* best-effort cleanup */ }
    }
}
