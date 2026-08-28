namespace AkmlSql.Site.Analytics;

/// <summary>One installer file currently present in the downloads folder.</summary>
public sealed record DownloadFileInfo(string Name, long SizeBytes, DateTimeOffset LastWriteUtc);

/// <summary>Directory listing of the downloads folder for the /admin dashboard. Read-only; never throws.</summary>
public static class DownloadsInfo
{
    /// <summary>Lists files (name, size, last write UTC, newest first); empty when the folder is missing/unreadable.</summary>
    public static IReadOnlyList<DownloadFileInfo> List(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return [];
            }

            return new DirectoryInfo(folder)
                .EnumerateFiles()
                .Select(f => new DownloadFileInfo(f.Name, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)))
                .OrderByDescending(f => f.LastWriteUtc)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
