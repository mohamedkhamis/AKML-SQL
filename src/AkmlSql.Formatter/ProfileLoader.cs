using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatter;

/// <summary>
/// Loads a formatting profile from CLI options: either from a --profile-file path
/// or by name from the ProfileManager.
/// </summary>
public static class ProfileLoader
{
    /// <summary>
    /// Loads the formatting profile specified by the CLI options.
    /// Returns null and sets errorMessage on failure.
    /// </summary>
    public static FormattingProfile? Load(CliOptions options, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            if (options.ProfileFile != null)
            {
                var fullPath = Path.GetFullPath(options.ProfileFile);
                if (!File.Exists(fullPath))
                {
                    errorMessage = $"Profile file not found: {fullPath}";
                    return null;
                }

                var json = File.ReadAllText(fullPath);
                return ProfileSerializer.Deserialize(json);
            }

            var manager = ProfileManager.CreateDefault();
            return manager.Load(options.ProfileName);
        }
        catch (FileNotFoundException)
        {
            errorMessage = $"Profile '{options.ProfileName}' not found. Use --list-profiles to see available profiles.";
            return null;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to load profile: {ex.Message}";
            return null;
        }
    }
}
