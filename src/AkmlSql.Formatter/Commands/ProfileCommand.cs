using AkmlSql.Formatter.Output;
using AkmlSql.Formatting.Profiles;

namespace AkmlSql.Formatter.Commands;

/// <summary>
/// Lists available formatting profiles and displays profile details.
/// </summary>
public class ProfileCommand
{
    public int Execute(CliOptions options)
    {
        try
        {
            var manager = ProfileManager.CreateDefault();
            var profiles = manager.List();

            if (profiles.Count == 0)
            {
                ConsoleRenderer.WriteWarning("No formatting profiles found.");
                ConsoleRenderer.WriteInfo("Profiles are loaded from:");
                ConsoleRenderer.WriteInfo("  Built-in: <install-dir>/profiles/");
                ConsoleRenderer.WriteInfo($"  Custom:   {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AKML SQL", "profiles")}");
                return ExitCodes.Success;
            }

            Console.Error.WriteLine();
            Console.Error.WriteLine("Available formatting profiles:");
            Console.Error.WriteLine();

            // Calculate column widths
            int nameWidth = Math.Max(4, profiles.Max(p => p.Name.Length));
            int typeWidth = 8; // "built-in" or "custom"

            // Header
            Console.ForegroundColor = ConsoleColor.White;
            Console.Error.Write($"  {"Name".PadRight(nameWidth)}  {"Type".PadRight(typeWidth)}  Description");
            Console.ResetColor();
            Console.Error.WriteLine();
            Console.Error.WriteLine($"  {new string('-', nameWidth)}  {new string('-', typeWidth)}  {new string('-', 40)}");

            foreach (var profile in profiles)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Error.Write($"  {profile.Name.PadRight(nameWidth)}");
                Console.ResetColor();

                Console.Error.Write("  ");

                if (profile.IsBuiltIn)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Error.Write("built-in".PadRight(typeWidth));
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Error.Write("custom".PadRight(typeWidth));
                }
                Console.ResetColor();

                Console.Error.Write("  ");
                Console.Error.WriteLine(string.IsNullOrEmpty(profile.Description) ? "(no description)" : profile.Description);
            }

            Console.Error.WriteLine();
            ConsoleRenderer.WriteInfo($"{profiles.Count} profile(s) found.");

            return ExitCodes.Success;
        }
        catch (Exception ex)
        {
            ConsoleRenderer.WriteError($"Failed to list profiles: {ex.Message}");
            return ExitCodes.InternalError;
        }
    }
}
