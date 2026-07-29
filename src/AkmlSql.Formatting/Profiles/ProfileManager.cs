using System.Diagnostics.CodeAnalysis;

namespace AkmlSql.Formatting.Profiles;

/// <summary>
/// Manages loading, saving, listing, and deleting formatting profiles (.akmlstyle files).
/// Built-in profiles come from an install directory; custom profiles live in %AppData%.
/// </summary>
[SuppressMessage("ReSharper", "GrammarMistakeInComment")]
public class ProfileManager
{
    private const string ProfileExtension = ".akmlstyle";

    private readonly string _builtInProfilesPath;
    private readonly string _customProfilesPath;

    /// <summary>
    /// Spec 031 FR-006 — directory where custom (non-built-in) profile files are written.
    /// Exposed so callers can place sibling artifacts next to a saved profile
    /// (e.g. <c>&lt;name&gt;.source.json</c>, the verbatim import source).
    /// </summary>
    public string CustomProfilesPath => _customProfilesPath;

    /// <summary>
    /// Creates a new <see cref="ProfileManager"/>.
    /// </summary>
    /// <param name="builtInProfilesPath">
    /// Directory containing built-in (read-only) profile files shipped with the installer.
    /// </param>
    /// <param name="customProfilesPath">
    /// Directory for user-created/modified profiles (typically %AppData%/AKML SQL/profiles).
    /// </param>
    public ProfileManager(string builtInProfilesPath, string customProfilesPath)
    {
        _builtInProfilesPath = builtInProfilesPath ?? throw new ArgumentNullException(nameof(builtInProfilesPath));
        _customProfilesPath = customProfilesPath ?? throw new ArgumentNullException(nameof(customProfilesPath));
    }

    /// <summary>
    /// Creates a <see cref="ProfileManager"/> using the default paths.
    /// </summary>
    public static ProfileManager CreateDefault()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AKML SQL");

        var customPath = Path.Combine(appData, "profiles");

        // Built-in profiles are next to the assembly
        var assemblyDir = Path.GetDirectoryName(typeof(ProfileManager).Assembly.Location) ?? string.Empty;
        var builtInPath = Path.Combine(assemblyDir, "profiles");

        return new ProfileManager(builtInPath, customPath);
    }

    /// <summary>
    /// Loads a profile by name. Searches custom profiles first, then built-in.
    /// </summary>
    /// <param name="name">Profile name (without extension).</param>
    /// <returns>The deserialized profile.</returns>
    /// <exception cref="FileNotFoundException">Thrown when no profile with the given name exists.</exception>
    public FormattingProfile Load(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Re-expressed over TryReadRaw (spec 033 simplify pass) so the custom-first resolution
        // lives in exactly one place — the doc-comment promise of identical semantics is now
        // structural rather than parallel-copy.
        if (!TryReadRaw(name, out var json, out var isBuiltIn))
            throw new FileNotFoundException($"Profile '{name}' not found.", name);

        var profile = ProfileSerializer.Deserialize(json);
        if (isBuiltIn) profile.Metadata.IsBuiltIn = true;
        return profile;
    }

    /// <summary>
    /// Spec 033 (ProfileGet) — reads the stored profile file text VERBATIM, custom-first then
    /// built-in, without deserializing. Serializing a loaded profile bumps
    /// <c>Metadata.Modified</c> and drops unknown fields nested inside option groups, so the
    /// raw text is the only faithful merge base for edit-save flows.
    /// </summary>
    /// <param name="name">Profile display name (same resolution semantics as <see cref="Load"/>).</param>
    /// <param name="json">The exact file text, or empty when not found.</param>
    /// <param name="isBuiltIn">
    /// True only when the name resolved from the built-in directory with no custom shadow —
    /// derived from the resolving directory, never from the JSON's own <c>isBuiltIn</c> field
    /// (a custom file claiming built-in status must stay editable).
    /// </param>
    /// <returns>True when a stored profile with the given name exists.</returns>
    public bool TryReadRaw(string name, out string json, out bool isBuiltIn)
    {
        ArgumentNullException.ThrowIfNull(name);
        json = string.Empty;
        isBuiltIn = false;

        // Tier 1 — exact filename match, custom first (the shadowing precedence both this
        // method and Load() promise). Fast path: one File.Exists per directory.
        var customFile = GetCustomFilePath(name);
        if (File.Exists(customFile))
        {
            json = File.ReadAllText(customFile);
            return true;
        }

        var builtInFile = GetBuiltInFilePath(name);
        if (File.Exists(builtInFile))
        {
            json = File.ReadAllText(builtInFile);
            isBuiltIn = true;
            return true;
        }

        // Tier 2 — resolve by the profile's OWN metadata.name. List() reports metadata names,
        // so a name that came from List() (or from Formatter.ActiveProfile, which the styles
        // editor writes from that list) is the only key callers ever have — but the shipped
        // built-ins use kebab-case FILENAMES with Title-Case metadata names
        // ("khamis-style.akmlstyle" → "Khamis Style"). Tier 1 alone therefore missed every
        // multi-word built-in, and FormatRequestHandler.LoadProfile swallowed the resulting
        // FileNotFoundException into POCO defaults — so "Khamis Style" (the shipped default
        // ActiveProfile) silently never applied. Single-word styles only worked by accident of
        // case-insensitive filesystems. Same custom-first precedence as tier 1.
        if (TryReadByMetadataName(_customProfilesPath, name, out json))
        {
            return true;
        }

        if (TryReadByMetadataName(_builtInProfilesPath, name, out json))
        {
            isBuiltIn = true;
            return true;
        }

        json = string.Empty;
        return false;
    }

    /// <summary>
    /// Scans <paramref name="directory"/> for a profile file whose <c>metadata.name</c> equals
    /// <paramref name="name"/> (case-insensitive), returning its verbatim text. Only reached when
    /// the filename-derived lookup misses, so the per-call cost is bounded by the profile count.
    /// Unreadable/corrupt files are skipped rather than failing the whole resolution.
    /// </summary>
    private static bool TryReadByMetadataName(string directory, string name, out string json)
    {
        json = string.Empty;
        if (!Directory.Exists(directory)) return false;

        foreach (var file in Directory.GetFiles(directory, "*" + ProfileExtension))
        {
            var metadata = TryLoadMetadata(file, isBuiltIn: false);
            if (metadata != null && string.Equals(metadata.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    json = File.ReadAllText(file);
                    return true;
                }
                catch (IOException)
                {
                    return false;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Saves a profile to the custom profiles directory.
    /// Uses atomic write (temp file + rename) to prevent corruption.
    /// Built-in profiles cannot be overwritten.
    /// </summary>
    public void Save(FormattingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var name = profile.Metadata.Name;
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Profile metadata must have a non-empty Name.", nameof(profile));

        // Reject saving over a built-in profile unless a custom override already exists
        if (profile.Metadata.IsBuiltIn)
            throw new InvalidOperationException($"Cannot overwrite built-in profile '{name}'. Duplicate it first.");

        var builtInFile = GetBuiltInFilePath(name);
        if (File.Exists(builtInFile) && !File.Exists(GetCustomFilePath(name)))
            throw new InvalidOperationException(
                $"Cannot overwrite built-in profile '{name}'. Use Duplicate to create a custom copy first.");

        Directory.CreateDirectory(_customProfilesPath);

        var filePath = GetCustomFilePath(name);
        ValidatePathWithinBase(filePath, _customProfilesPath);
        var json = ProfileSerializer.Serialize(profile);

        WriteAtomic(filePath, json);
    }

    /// <summary>
    /// Lists all available profiles (custom + built-in), returning metadata only.
    /// Custom profiles with the same name shadow built-in ones.
    /// </summary>
    public IReadOnlyList<ProfileMetadata> List()
    {
        var profiles = new Dictionary<string, ProfileMetadata>(StringComparer.OrdinalIgnoreCase);

        // Built-in first (will be overridden by custom if same name)
        if (Directory.Exists(_builtInProfilesPath))
        {
            foreach (var file in Directory.GetFiles(_builtInProfilesPath, "*" + ProfileExtension))
            {
                var profile = TryLoadMetadata(file, isBuiltIn: true);
                if (profile != null)
                {
                    profiles[profile.Name] = profile;
                }
            }
        }

        // Custom profiles override built-in
        if (Directory.Exists(_customProfilesPath))
        {
            foreach (var file in Directory.GetFiles(_customProfilesPath, "*" + ProfileExtension))
            {
                var profile = TryLoadMetadata(file, isBuiltIn: false);
                if (profile != null)
                {
                    profiles[profile.Name] = profile;
                }
            }
        }

        return profiles.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Deletes a custom profile by name.
    /// Built-in profiles cannot be deleted.
    /// </summary>
    /// <returns>True if the file was deleted; false if it did not exist.</returns>
    /// <exception cref="InvalidOperationException">Thrown when attempting to delete a built-in profile.</exception>
    public bool Delete(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var customFile = GetCustomFilePath(name);
        ValidatePathWithinBase(customFile, _customProfilesPath);
        if (!File.Exists(customFile))
        {
            // Check if it's built-in
            var builtInFile = GetBuiltInFilePath(name);
            if (File.Exists(builtInFile))
                throw new InvalidOperationException($"Cannot delete built-in profile '{name}'.");

            return false;
        }

        File.Delete(customFile);
        return true;
    }

    /// <summary>
    /// Spec 033 (ProfileRename) — renames a CUSTOM profile: rewrites <c>metadata.name</c>
    /// (+<c>modified</c>) via a raw JsonNode edit (no full round-trip, so unknown nested
    /// fields survive), writes the new file atomically, removes the old one, and moves the
    /// <c>&lt;name&gt;.source.json</c> import sidecar. <c>List()</c> keys on the JSON name
    /// while <c>Load()</c> resolves by filename — this keeps them consistent in one operation.
    /// </summary>
    /// <returns>The final display name persisted in the profile metadata.</returns>
    /// <exception cref="InvalidOperationException">Built-in source, or name collision.</exception>
    /// <exception cref="FileNotFoundException">No custom profile with <paramref name="oldName"/>.</exception>
    public string Rename(string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(oldName);
        ArgumentNullException.ThrowIfNull(newName);

        var finalName = newName.Trim();
        var sanitizedNew = SanitizeFileName(finalName); // throws on empty/hostile
        var sanitizedOld = SanitizeFileName(oldName);

        var oldFile = GetCustomFilePath(oldName);
        ValidatePathWithinBase(oldFile, _customProfilesPath);
        if (!File.Exists(oldFile))
        {
            if (File.Exists(GetBuiltInFilePath(oldName)))
                throw new InvalidOperationException($"Cannot rename built-in profile '{oldName}'. Duplicate it first.");
            throw new FileNotFoundException($"Profile '{oldName}' not found.", oldName);
        }

        // NTFS File.Exists is case-insensitive: a case-only rename would see its own source as
        // the "existing" target — allow it; everything else collides.
        var caseOnly = string.Equals(sanitizedOld, sanitizedNew, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly)
        {
            if (File.Exists(GetCustomFilePath(finalName)))
                throw new InvalidOperationException($"A profile named '{finalName}' already exists.");
            if (File.Exists(GetBuiltInFilePath(finalName)))
                throw new InvalidOperationException($"'{finalName}' is a built-in style name and cannot be used.");
        }

        var newFile = GetCustomFilePath(finalName);
        ValidatePathWithinBase(newFile, _customProfilesPath);

        // Rewrite metadata.name/modified on the RAW text (never Deserialize→Serialize: that
        // bumps nothing we want and drops unknown nested fields).
        var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(oldFile)) as System.Text.Json.Nodes.JsonObject
                   ?? throw new InvalidOperationException($"Profile '{oldName}' is not a JSON object.");
        if (root["metadata"] is not System.Text.Json.Nodes.JsonObject metadata)
        {
            metadata = new System.Text.Json.Nodes.JsonObject();
            root["metadata"] = metadata;
        }
        metadata["name"] = finalName;
        metadata["modified"] = DateTime.UtcNow;
        var updated = root.ToJsonString(IndentedJson);

        if (caseOnly)
        {
            // Direct move fixes the filename casing; then rewrite content atomically in place.
            if (!string.Equals(oldFile, newFile, StringComparison.Ordinal))
                File.Move(oldFile, newFile);
            WriteAtomic(newFile, updated);
        }
        else
        {
            // New name appears complete before the old disappears — a crash in between leaves
            // both files present (recoverable), never zero.
            WriteAtomic(newFile, updated);
            File.Delete(oldFile);
        }

        // Move the verbatim import sidecar so lossless re-import keeps working (spec 031).
        // GetCustomArtifactPath owns the sanitize + ValidatePathWithinBase pairing for both ends.
        var oldSidecar = GetCustomArtifactPath(oldName, ".source.json");
        if (File.Exists(oldSidecar))
        {
            var newSidecar = GetCustomArtifactPath(finalName, ".source.json");
            if (!string.Equals(oldSidecar, newSidecar, StringComparison.Ordinal))
                File.Move(oldSidecar, newSidecar);
        }

        return finalName;
    }

    private static readonly System.Text.Json.JsonSerializerOptions IndentedJson =
        new() { WriteIndented = true };

    /// <summary>
    /// The corruption-prevention idiom this class advertises as a design decision, in ONE
    /// place: write to a sibling temp file, then atomically move over the destination.
    /// </summary>
    private static void WriteAtomic(string path, string text)
    {
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, text);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>
    /// Duplicates an existing profile under a new name.
    /// </summary>
    public FormattingProfile Duplicate(string sourceName, string newName)
    {
        ArgumentNullException.ThrowIfNull(sourceName);
        ArgumentNullException.ThrowIfNull(newName);

        var source = Load(sourceName);

        // Create a fresh copy with new identity
        var json = ProfileSerializer.Serialize(source);
        var copy = ProfileSerializer.Deserialize(json);

        copy.Metadata.Id = Guid.NewGuid().ToString();
        copy.Metadata.Name = newName;
        copy.Metadata.IsBuiltIn = false;
        copy.Metadata.BasedOn = sourceName;
        copy.Metadata.Created = DateTime.UtcNow;
        copy.Metadata.Modified = DateTime.UtcNow;

        Save(copy);
        return copy;
    }

    /// <summary>
    /// Builds a validated path for a sibling artifact of a custom profile (e.g. "&lt;name&gt;.source.json").
    /// Pairs SanitizeFileName with the canonical ValidatePathWithinBase check — the same two-layer
    /// invariant Save/Delete use — so external callers cannot accidentally reintroduce a single-layer write.
    /// </summary>
    public string GetCustomArtifactPath(string profileName, string suffix)
    {
        var path = Path.Combine(_customProfilesPath, SanitizeFileName(profileName) + suffix);
        ValidatePathWithinBase(path, _customProfilesPath);
        return path;
    }

    /// <summary>
    /// Returns all built-in profiles.
    /// </summary>
    public IReadOnlyList<FormattingProfile> GetBuiltIn()
    {
        var results = new List<FormattingProfile>();

        if (!Directory.Exists(_builtInProfilesPath))
            return results;

        foreach (var file in Directory.GetFiles(_builtInProfilesPath, "*" + ProfileExtension))
        {
            try
            {
                var json = File.ReadAllText(file);
                var profile = ProfileSerializer.Deserialize(json);
                profile.Metadata.IsBuiltIn = true;
                results.Add(profile);
            }
            catch
            {
                // Skip malformed built-in profiles
            }
        }

        return results;
    }

    /// <summary>
    /// Exports a profile to the specified destination path as a .akmlstyle file.
    /// </summary>
    /// <param name="profileName">Name of the profile to export.</param>
    /// <param name="destinationPath">Full file path where the .akmlstyle file will be written.</param>
    public void Export(string profileName, string destinationPath)
    {
        ArgumentNullException.ThrowIfNull(profileName);
        ArgumentNullException.ThrowIfNull(destinationPath);

        var profile = Load(profileName);
        var json = ProfileSerializer.Serialize(profile);

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        WriteAtomic(destinationPath, json);
    }

    /// <summary>
    /// Imports a profile from an .akmlstyle file.
    /// Assigns a new GUID and saves it under the given name (or the name inside the file).
    /// </summary>
    /// <param name="sourcePath">Full path to the .akmlstyle file to import.</param>
    /// <param name="newName">
    /// Optional new name for the imported profile. If null, the name from the file is used.
    /// </param>
    /// <returns>The imported profile.</returns>
    public FormattingProfile Import(string sourcePath, string? newName = null)
    {
        ArgumentNullException.ThrowIfNull(sourcePath);

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException($"Import source file not found: '{sourcePath}'", sourcePath);

        var json = File.ReadAllText(sourcePath);
        var profile = ProfileSerializer.Deserialize(json);

        // Assign new identity
        profile.Metadata.Id = Guid.NewGuid().ToString();
        profile.Metadata.IsBuiltIn = false;
        profile.Metadata.Created = DateTime.UtcNow;
        profile.Metadata.Modified = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(newName))
        {
            profile.Metadata.BasedOn = profile.Metadata.Name;
            profile.Metadata.Name = newName;
        }

        Save(profile);
        return profile;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private string GetCustomFilePath(string name)
    {
        return Path.Combine(_customProfilesPath, SanitizeFileName(name) + ProfileExtension);
    }

    private string GetBuiltInFilePath(string name)
    {
        return Path.Combine(_builtInProfilesPath, SanitizeFileName(name) + ProfileExtension);
    }

    private static ProfileMetadata? TryLoadMetadata(string filePath, bool isBuiltIn)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var profile = ProfileSerializer.Deserialize(json);
            profile.Metadata.IsBuiltIn = isBuiltIn;
            return profile.Metadata;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Removes invalid filename characters from a profile name and prevents path traversal.
    /// Public so callers can derive sibling artifact filenames (e.g. Spec 031's
    /// <c>&lt;name&gt;.source.json</c>) using the exact same sanitization the profile file itself uses.
    /// </summary>
    public static string SanitizeFileName(string name)
    {
        // Strip directory separators and path traversal sequences first
        var stripped = name.Replace("..", "").Replace("/", "").Replace("\\", "");
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(stripped.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c));
        var result = sanitized.Trim();
        if (string.IsNullOrWhiteSpace(result))
            throw new ArgumentException("Profile name results in an empty filename after sanitization.", nameof(name));
        return result;
    }

    /// <summary>
    /// Validates that a resolved file path stays within the expected base directory.
    /// Prevents path traversal attacks.
    /// </summary>
    private static void ValidatePathWithinBase(string filePath, string baseDirectory)
    {
        var fullPath = Path.GetFullPath(filePath);
        var fullBase = Path.GetFullPath(baseDirectory);
        if (!fullPath.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"Path '{fullPath}' is outside the allowed directory '{fullBase}'.");
    }
}
