using System.Text.Json;
using System.Text.Json.Serialization;
using AkmlSql.Core.Models.Tabs;
using Serilog;

namespace AkmlSql.Engine.Sessions;

/// <summary>
/// Manages persistence of <see cref="SessionSnapshot"/> JSON files in
/// <c>%AppData%\AKML SQL\sessions\</c>. Each session is stored as a separate
/// file named <c>{SessionId}.json</c>. Writes are atomic (temp file + <c>File.Move</c>).
/// </summary>
public class SessionStorage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _sessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AKML SQL", "sessions");

    /// <summary>
    /// Atomically saves a session snapshot to disk.
    /// Uses temp file + <c>File.Move(overwrite:true)</c> to prevent partial-write corruption.
    /// </summary>
    public async Task SaveAsync(SessionSnapshot snapshot)
    {
        EnsureDirectory();

        var filePath = GetFilePath(snapshot.SessionId);
        var tempPath = filePath + ".tmp";

        try
        {
            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, filePath, overwrite: true);

            Log.Debug("Session snapshot saved: {SessionId} ({TabCount} tabs)",
                snapshot.SessionId, snapshot.Tabs.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save session snapshot {SessionId}", snapshot.SessionId);

            // Clean up temp file on failure
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* Intentional: best-effort cleanup */ }

            throw;
        }
    }

    /// <summary>
    /// Loads all persisted session snapshots from the sessions directory.
    /// Files that fail to deserialize are logged and skipped.
    /// </summary>
    public async Task<List<SessionSnapshot>> LoadAllAsync()
    {
        var sessions = new List<SessionSnapshot>();

        if (!Directory.Exists(_sessionsDir))
        {
            return sessions;
        }

        foreach (var file in Directory.GetFiles(_sessionsDir, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var snapshot = JsonSerializer.Deserialize<SessionSnapshot>(json, JsonOptions);
                if (snapshot != null)
                {
                    sessions.Add(snapshot);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load session file {File}, skipping", file);
            }
        }

        // Return newest first
        sessions.Sort((a, b) => b.CapturedAt.CompareTo(a.CapturedAt));
        return sessions;
    }

    /// <summary>
    /// Deletes the session snapshot file for the given session ID.
    /// </summary>
    public Task DeleteAsync(string sessionId)
    {
        var filePath = GetFilePath(sessionId);
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Log.Debug("Session snapshot deleted: {SessionId}", sessionId);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to delete session file {SessionId}", sessionId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Purges old session files, keeping only the newest <paramref name="maxSessions"/> snapshots.
    /// </summary>
    public async Task PurgeOldSessionsAsync(int maxSessions = 5)
    {
        var sessions = await LoadAllAsync();
        if (sessions.Count <= maxSessions)
        {
            return;
        }

        // sessions is already sorted newest-first by LoadAllAsync
        var toDelete = sessions.Skip(maxSessions);
        foreach (var old in toDelete)
        {
            await DeleteAsync(old.SessionId);
        }

        Log.Information("Purged {Count} old session snapshots (kept {Max})",
            sessions.Count - maxSessions, maxSessions);
    }

    private string GetFilePath(string sessionId)
    {
        // Sanitize the session ID to prevent path traversal
        var safeId = Path.GetFileName(sessionId);
        return Path.Combine(_sessionsDir, safeId + ".json");
    }

    private void EnsureDirectory()
    {
        if (!Directory.Exists(_sessionsDir))
        {
            Directory.CreateDirectory(_sessionsDir);
        }
    }
}
