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

        var customFile = GetCustomFilePath(name);
        if (File.Exists(customFile))
        {
            var json = File.ReadAllText(customFile);
            return ProfileSerializer.Deserialize(json);
        }

        var builtInFile = GetBuiltInFilePath(name);
        if (File.Exists(builtInFile))
        {
            var json = File.ReadAllText(builtInFile);
            var profile = ProfileSerializer.Deserialize(json);
            profile.Metadata.IsBuiltIn = true;
            return profile;
        }

        throw new FileNotFoundException($"Profile '{name}' not found.", name);
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

        // Atomic write: write to temp then rename
        var tempPath = filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);
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

        // Atomic write
        var tempPath = destinationPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, destinationPath, overwrite: true);
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
    /// </summary>
    private static string SanitizeFileName(string name)
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
