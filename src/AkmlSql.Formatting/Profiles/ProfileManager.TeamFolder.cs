namespace AkmlSql.Formatting.Profiles;

/// <summary>
/// Spec 040 (STY-10, FR-064, research R28) — the shared team style folder: a third folder, between
/// the user's own styles and the built-in ones (names resolve user &gt; team &gt; built-in).
/// <para>
/// The folder is usually a network share, and styles are resolved on the format request path, so
/// every touch of it is bounded: one scan of the folder (existence, file list, changed files, and —
/// for the style list — the write probe) runs on the thread pool with a
/// <see cref="TeamFolderTimeout"/> budget. A folder that misses the budget, or answers slowly that
/// it isn't there, is left alone for <see cref="UnreachableBackoff"/>, so an offline share costs one
/// wait, not one per request. The scan keeps the folder's styles in memory and re-reads only files
/// whose size or time changed, so a style dropped into the folder resolves on the next use.
/// </para>
/// <para>
/// Team styles are read-only unless the folder can be written to (a temp file is created and
/// deleted; a folder or file marked read-only counts as read-only). Writable team styles are edited,
/// renamed and deleted in the team folder itself — that is what sharing them means. Writes to a
/// read-only one are refused with <see cref="TeamReadOnlyMessage"/>.
/// </para>
/// <para>
/// Besides <c>.akmlstyle</c> files the folder may hold SQL Prompt <c>.json</c> styles (as SQL Prompt
/// and the Format Styles window export them); they are listed and format as SQL Prompt styles, and
/// edits to them are written back in SQL Prompt's own format.
/// </para>
/// </summary>
public partial class ProfileManager
{
    public const string SourceBuiltIn = "builtIn";
    public const string SourceUser = "user";
    public const string SourceTeam = "team";

    /// <summary>The budget for one scan of the team folder.</summary>
    internal static readonly TimeSpan TeamFolderTimeout = TimeSpan.FromSeconds(2);

    /// <summary>How long a folder that timed out (or answered slowly) is left alone.</summary>
    internal static readonly TimeSpan UnreachableBackoff = TimeSpan.FromSeconds(30);

    /// <summary>A "not there" answer slower than this is treated like a timeout (offline host).</summary>
    private static readonly TimeSpan SlowAnswer = TimeSpan.FromMilliseconds(200);

    /// <summary>How long a write-probe result is trusted.</summary>
    private static readonly TimeSpan WritableProbeTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a scan of the team folder answers name lookups. Every format request and preview
    /// looks its style up; without this each one listed (and statted) the folder — a network share,
    /// usually — before reaching the built-ins. Writes made here clear it at once.
    /// </summary>
    internal static readonly TimeSpan TeamSnapshotTtl = TimeSpan.FromSeconds(5);

    /// <summary>Test seam: the clock <see cref="TeamSnapshotTtl"/> is measured on.</summary>
    internal Func<DateTime> TeamClock { get; set; } = () => DateTime.UtcNow;

    /// <summary>Test seam: how many times the team folder has been scanned.</summary>
    internal int TeamScanCount => Volatile.Read(ref _teamScanCount);
    private int _teamScanCount;

    /// <summary>The engine's refusal for a write to a read-only team style (contracts/ipc.md).</summary>
    public static string TeamReadOnlyMessage(string name) =>
        $"'{name}' is a team style and can't be changed here — copy it to edit.";

    private readonly object _teamLock = new();
    private TeamSnapshot? _teamSnapshot;
    private string? _unreachablePath;
    private DateTime _unreachableUntilUtc;
    private volatile bool _teamFolderUnavailable;

    /// <summary>
    /// True when the last <see cref="List()"/> found a team folder set but could not reach it (or
    /// it was not a full path). The other styles were still listed.
    /// </summary>
    public bool TeamFolderUnavailable => _teamFolderUnavailable;

    /// <summary>
    /// True when <paramref name="name"/> resolves to a team style (no own style shadows it) that
    /// can't be written to. Probes the folder when the last probe is stale.
    /// </summary>
    public bool IsReadOnlyTeamStyle(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return TryResolveTeamOwner(name, probeWrite: true, out var entry, out var snapshot)
               && IsReadOnly(entry, snapshot);
    }

    /// <summary>
    /// One style file in the team folder, as last read. <see cref="Json"/> is always the
    /// <c>.akmlstyle</c> form; for a SQL Prompt <c>.json</c> style (<see cref="IsSqlPromptFile"/>)
    /// it is the style built from that document, and writes go back as a SQL Prompt document.
    /// </summary>
    private sealed record TeamEntry(string Path, string FileStem, string Json, string? Name,
        DateTime Stamp, long Length, bool FileReadOnly, bool IsSqlPromptFile = false);

    /// <summary>
    /// SQL Prompt style files (<c>.json</c>, as SQL Prompt and AKML export them) a team can drop
    /// into the folder beside <c>.akmlstyle</c> files.
    /// </summary>
    private const string SqlPromptStyleExtension = ".json";

    /// <summary>The team folder as last scanned.</summary>
    private sealed record TeamSnapshot(string Folder, IReadOnlyList<TeamEntry> Entries, bool? Writable, DateTime WritableCheckedUtc)
    {
        /// <summary>When the folder was read (<see cref="TeamClock"/>).</summary>
        public DateTime ScannedUtc { get; init; }
    }

    private static bool IsReadOnly(TeamEntry entry, TeamSnapshot snapshot) =>
        entry.FileReadOnly || snapshot.Writable != true;

    /// <summary>
    /// The configured team folder: null when none is set; empty when one is set but isn't a full
    /// path (reported as unavailable rather than resolved against the engine's own folder).
    /// </summary>
    private string? ConfiguredTeamFolder()
    {
        if (_teamFolderProvider == null) return null;
        string? raw;
        try { raw = _teamFolderProvider(); }
        catch (Exception) { return null; }   // a broken settings read means "no team folder", never a failed list

        var text = raw?.Trim().Trim('"').Trim();
        if (string.IsNullOrEmpty(text)) return null;
        try
        {
            return Path.IsPathFullyQualified(text) ? Path.GetFullPath(text) : string.Empty;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Brings the team snapshot up to date within the time budget. Null when no team folder is set
    /// or it can't be reached; <paramref name="unavailable"/> tells those two apart.
    /// </summary>
    private TeamSnapshot? CurrentTeam(bool probeWrite, out bool unavailable)
    {
        unavailable = false;
        var folder = ConfiguredTeamFolder();
        if (folder == null) return null;
        if (folder.Length == 0) { unavailable = true; return null; }

        TeamSnapshot? previous;
        lock (_teamLock)
        {
            if (string.Equals(_unreachablePath, folder, StringComparison.OrdinalIgnoreCase)
                && DateTime.UtcNow < _unreachableUntilUtc)
            {
                unavailable = true;
                return null;
            }
            previous = _teamSnapshot != null && string.Equals(_teamSnapshot.Folder, folder, StringComparison.OrdinalIgnoreCase)
                ? _teamSnapshot
                : null;
        }

        var needProbe = probeWrite
                        && (previous?.Writable == null || DateTime.UtcNow - previous.WritableCheckedUtc > WritableProbeTtl);

        // A recent scan answers: no listing of the share per format request.
        if (previous != null && !needProbe && TeamClock() - previous.ScannedUtc < TeamSnapshotTtl)
            return previous;

        Interlocked.Increment(ref _teamScanCount);
        var started = DateTime.UtcNow;
        var scan = Task.Run(() => ScanTeamFolder(folder, previous, needProbe));
        TeamSnapshot? fresh = null;
        bool finished;
        try
        {
            finished = scan.Wait(TeamFolderTimeout);
            if (finished) fresh = scan.Result;
        }
        catch (AggregateException)
        {
            finished = true;   // an IO error is an answer: the folder can't be read
        }

        if (!finished)
            _ = scan.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);   // observe it

        lock (_teamLock)
        {
            if (fresh != null)
            {
                fresh = fresh with { ScannedUtc = TeamClock() };
                _teamSnapshot = fresh;
                if (string.Equals(_unreachablePath, folder, StringComparison.OrdinalIgnoreCase)) _unreachablePath = null;
                return fresh;
            }

            _teamSnapshot = null;
            if (!finished || DateTime.UtcNow - started > SlowAnswer)
            {
                _unreachablePath = folder;
                _unreachableUntilUtc = DateTime.UtcNow + UnreachableBackoff;
            }
        }

        unavailable = true;
        return null;
    }

    /// <summary>
    /// Reads the team folder: the style files (re-reading only those whose size or time changed)
    /// and, when asked, whether it can be written to. Null when the folder isn't there.
    /// </summary>
    private static TeamSnapshot? ScanTeamFolder(string folder, TeamSnapshot? previous, bool probeWrite)
    {
        if (!Directory.Exists(folder)) return null;

        var known = previous?.Entries.ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);
        var entries = new List<TeamEntry>();
        var files = Directory.GetFiles(folder, "*" + ProfileExtension)
            .Concat(Directory.GetFiles(folder, "*" + SqlPromptStyleExtension)
                .Where(f => f.EndsWith(SqlPromptStyleExtension, StringComparison.OrdinalIgnoreCase)));   // not "*.jsonx"
        foreach (var file in files)
        {
            try
            {
                var info = new FileInfo(file);
                var stamp = info.LastWriteTimeUtc;
                var length = info.Length;
                var readOnly = info.IsReadOnly;
                if (known != null && known.TryGetValue(file, out var same) && same.Stamp == stamp && same.Length == length)
                {
                    entries.Add(same with { FileReadOnly = readOnly });
                    continue;
                }

                var text = File.ReadAllText(file);
                var stem = Path.GetFileNameWithoutExtension(file);
                if (file.EndsWith(SqlPromptStyleExtension, StringComparison.OrdinalIgnoreCase))
                {
                    if (ReadSqlPromptTeamStyle(text, stem) is { } style)
                        entries.Add(new TeamEntry(file, stem, style.Json, style.Name, stamp, length, readOnly, IsSqlPromptFile: true));
                    continue;
                }

                string? name = null;
                using (var document = System.Text.Json.JsonDocument.Parse(text))
                {
                    if (document.RootElement.TryGetProperty("metadata", out var metadata)
                        && metadata.TryGetProperty("name", out var nameElement)
                        && nameElement.ValueKind == System.Text.Json.JsonValueKind.String)
                        name = nameElement.GetString()?.Trim();
                }
                entries.Add(new TeamEntry(file, stem, text, name, stamp, length, readOnly));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException
                                          or InvalidOperationException or FormatException)
            {
                // An unreadable or broken file is skipped, the way the other folders skip them.
            }
        }

        var writable = previous?.Writable;
        var checkedAt = previous?.WritableCheckedUtc ?? DateTime.MinValue;
        if (probeWrite)
        {
            writable = ProbeWritable(folder);
            checkedAt = DateTime.UtcNow;
        }

        return new TeamSnapshot(folder, entries, writable, checkedAt);
    }

    /// <summary>
    /// A SQL Prompt <c>.json</c> style in the team folder as a stored style (its document kept
    /// whole, so it formats with the SQL Prompt layout) and its name — the file name when the
    /// document has none. Null for a <c>.json</c> file that isn't a SQL Prompt style.
    /// </summary>
    private static (string Json, string Name)? ReadSqlPromptTeamStyle(string text, string fileStem)
    {
        if (!SqlPrompt.SqlPromptStyleDocument.TryParse(text, out var document, out _) || document == null) return null;

        // Any JSON object parses; only one carrying style metadata or SQL Prompt option sections is a style.
        var root = document.Root;
        if (!root.ContainsKey("metadata") && !SqlPrompt.SqlPromptOptionCatalog.Options.Any(o => root.ContainsKey(o.Section)))
            return null;

        if (string.IsNullOrWhiteSpace(document.Name)) document.Name = fileStem;
        var profile = SqlPrompt.SqlPromptStyles.ToProfile(document);
        return (ProfileSerializer.Serialize(profile), profile.Metadata.Name.Trim());
    }

    /// <summary>A style written back to a team SQL Prompt <c>.json</c> file, in SQL Prompt's own form.</summary>
    private static string SqlPromptTeamText(FormattingProfile profile)
    {
        var document = SqlPrompt.SqlPromptStyles.ToDocument(profile);
        document.Minimize();
        return document.ToJson();
    }

    /// <summary>Writes <paramref name="profile"/> over a writable team style, in that file's own format.</summary>
    private void WriteTeamStyle(TeamEntry entry, FormattingProfile profile)
    {
        profile.Metadata.IsBuiltIn = false;
        WriteAtomic(entry.Path, entry.IsSqlPromptFile ? SqlPromptTeamText(profile) : ProfileSerializer.Serialize(profile));
        ForgetTeamSnapshot();
    }

    /// <summary>
    /// Whether styles can be written to <paramref name="folder"/>: a folder marked read-only counts
    /// as read-only (Windows would still let a file be created in it), otherwise a temp file is
    /// created and deleted.
    /// </summary>
    private static bool ProbeWritable(string folder)
    {
        try
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReadOnly) != 0) return false;
            var probe = Path.Combine(folder, ".akml-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    /// <summary>The team style called <paramref name="name"/>: by file name first, then by its stored name.</summary>
    private static TeamEntry? FindTeamEntry(TeamSnapshot snapshot, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return null;
        string? stem = null;
        try { stem = SanitizeFileName(trimmed); }
        catch (ArgumentException) { }

        if (stem != null)
        {
            var byFile = snapshot.Entries.FirstOrDefault(e => string.Equals(e.FileStem, stem, StringComparison.OrdinalIgnoreCase));
            if (byFile != null) return byFile;
        }
        return snapshot.Entries.FirstOrDefault(e => string.Equals(e.Name, trimmed, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The team tier of name resolution: the team style called <paramref name="name"/>, unless one
    /// of the user's own styles carries that name (user &gt; team).
    /// </summary>
    private bool TryResolveTeamOwner(string name, bool probeWrite, out TeamEntry entry, out TeamSnapshot snapshot)
    {
        entry = null!;
        snapshot = null!;
        var team = CurrentTeam(probeWrite, out _);
        if (team == null) return false;
        var found = FindTeamEntry(team, name);
        if (found == null) return false;

        // The user's own style shadows the team's, whether it matches by file name or stored name.
        if (File.Exists(GetCustomFilePath(name)) || TryReadByMetadataName(_customProfilesPath, name, out _, out _))
            return false;

        entry = found;
        snapshot = team;
        return true;
    }

    /// <summary>
    /// Refuses a write to a read-only team style; returns the writable team style the write should
    /// go to, or null when <paramref name="name"/> isn't a team style.
    /// </summary>
    private TeamEntry? TeamWriteTarget(string name)
    {
        if (!TryResolveTeamOwner(name, probeWrite: true, out var entry, out var snapshot)) return null;
        if (IsReadOnly(entry, snapshot)) throw new InvalidOperationException(TeamReadOnlyMessage(name.Trim()));
        return entry;
    }

    /// <summary>True when a team style (shadowed or not) already uses <paramref name="name"/>.</summary>
    private bool TeamHasName(string name)
    {
        var team = CurrentTeam(probeWrite: false, out _);
        return team != null && FindTeamEntry(team, name) != null;
    }

    /// <summary>Drops the team snapshot after a write, so the next use re-reads the folder.</summary>
    private void ForgetTeamSnapshot()
    {
        lock (_teamLock) _teamSnapshot = null;
    }

    /// <summary>The team folder's styles as list entries (fresh objects; callers may change them).</summary>
    private IEnumerable<ProfileMetadata> TeamListEntries(TeamSnapshot snapshot)
    {
        foreach (var entry in snapshot.Entries)
        {
            ProfileMetadata metadata;
            try
            {
                var profile = ProfileSerializer.Deserialize(entry.Json);
                metadata = profile.Metadata;
                metadata.IsSqlPromptStyle = profile.SqlPrompt is not null;
            }
            catch (Exception)
            {
                continue;
            }

            metadata.IsBuiltIn = false;
            metadata.Source = SourceTeam;
            metadata.IsReadOnly = IsReadOnly(entry, snapshot);
            yield return metadata;
        }
    }

    /// <summary>Renames a writable team style inside the team folder (same raw edit as a user style).</summary>
    private string RenameTeamStyle(TeamEntry entry, string oldName, string finalName)
    {
        var folder = Path.GetDirectoryName(entry.Path)!;
        var sanitizedNew = SanitizeFileName(finalName);
        var caseOnly = string.Equals(entry.FileStem, sanitizedNew, StringComparison.OrdinalIgnoreCase)
                       || string.Equals(oldName.Trim(), finalName, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly)
        {
            if (File.Exists(GetCustomFilePath(finalName)) || TryReadByMetadataName(_customProfilesPath, finalName, out _, out _))
                throw new InvalidOperationException($"A profile named '{finalName}' already exists.");
            if (HasBuiltIn(finalName))
                throw new InvalidOperationException($"'{finalName}' is a built-in style name and cannot be used.");
            if (TeamHasName(finalName))
                throw new InvalidOperationException($"A team style named '{finalName}' already exists.");
        }

        // A team file keeps its format: a SQL Prompt .json style stays one.
        var newFile = Path.Combine(folder, sanitizedNew + (entry.IsSqlPromptFile ? SqlPromptStyleExtension : ProfileExtension));
        ValidatePathWithinBase(newFile, folder);

        string updated;
        if (entry.IsSqlPromptFile)
        {
            var document = SqlPrompt.SqlPromptStyleDocument.Parse(File.ReadAllText(entry.Path));
            document.Name = finalName;
            updated = document.ToJson();
        }
        else
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(entry.Path)) as System.Text.Json.Nodes.JsonObject
                       ?? throw new InvalidOperationException($"Profile '{oldName}' is not a JSON object.");
            if (root["metadata"] is not System.Text.Json.Nodes.JsonObject metadata)
            {
                metadata = new System.Text.Json.Nodes.JsonObject();
                root["metadata"] = metadata;
            }
            metadata["name"] = finalName;
            metadata["modified"] = DateTime.UtcNow;
            updated = root.ToJsonString(IndentedJson);
        }

        if (string.Equals(entry.Path, newFile, StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(entry.Path, newFile, StringComparison.Ordinal)) File.Move(entry.Path, newFile);
            WriteAtomic(newFile, updated);
        }
        else
        {
            WriteAtomic(newFile, updated);
            File.Delete(entry.Path);
        }

        ForgetTeamSnapshot();
        return finalName;
    }
}
