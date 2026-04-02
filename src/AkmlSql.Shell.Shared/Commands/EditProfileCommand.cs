using System;
using System.ComponentModel.Design;
using System.IO;
using System.Text;
using AkmlSql.Core;
using AkmlSql.Shell.Shared.Ui;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// T106: Command that opens the Profile Editor dialog for editing formatting profiles.
    /// </summary>
    internal sealed class EditProfileCommand
    {
        private readonly Package _package;

        private EditProfileCommand(Package package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdEditProfile);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static EditProfileCommand Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new EditProfileCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                ProfileEditorViewModel viewModel;

                // Try to load the active profile from disk
                var profilesDir = Path.Combine(Constants.AppDataPath, "profiles");
                var activeProfilePath = FindActiveProfile(profilesDir);

                if (activeProfilePath != null && File.Exists(activeProfilePath))
                {
                    var json = File.ReadAllText(activeProfilePath, Encoding.UTF8);
                    viewModel = new ProfileEditorViewModel(json);
                }
                else
                {
                    viewModel = new ProfileEditorViewModel();
                }

                var dialog = new ProfileEditorDialog(viewModel);
                dialog.ShowModal();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open Profile Editor");
                System.Windows.MessageBox.Show(
                    "Failed to open Profile Editor: " + ex.Message,
                    Constants.ProductName,
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Finds the active profile file. Returns the first .akmlstyle file found,
        /// or null if no profiles exist.
        /// </summary>
        private static string FindActiveProfile(string profilesDir)
        {
            if (!Directory.Exists(profilesDir))
                return null;

            // Look for a config.json to determine active profile name
            try
            {
                var configPath = Constants.ConfigFilePath;
                if (File.Exists(configPath))
                {
                    var configJson = File.ReadAllText(configPath, Encoding.UTF8);
                    // Simple parse — look for "activeProfile" key
                    var key = "\"activeProfile\"";
                    var idx = configJson.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        // Find the value after the colon
                        var colonIdx = configJson.IndexOf(':', idx + key.Length);
                        if (colonIdx >= 0)
                        {
                            var quoteStart = configJson.IndexOf('"', colonIdx + 1);
                            if (quoteStart >= 0)
                            {
                                var quoteEnd = configJson.IndexOf('"', quoteStart + 1);
                                if (quoteEnd > quoteStart)
                                {
                                    var profileName = configJson.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                                    var profilePath = Path.Combine(profilesDir, profileName + ".akmlstyle");
                                    if (File.Exists(profilePath))
                                        return profilePath;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall through to default behavior
            }

            // Fallback: return the first profile found
            var files = Directory.GetFiles(profilesDir, "*.akmlstyle");
            return files.Length > 0 ? files[0] : null;
        }
    }
}
