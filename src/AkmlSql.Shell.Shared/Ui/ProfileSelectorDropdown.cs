using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;

namespace AkmlSql.Shell.Shared.Ui
{
    /// <summary>
    /// Stub for the profile selector dropdown in the toolbar.
    /// Full implementation deferred to a later phase.
    /// </summary>
    internal static class ProfileSelectorDropdown
    {
        public static void Initialize(AsyncPackage package, OleMenuCommandService commandService)
        {
            // Stub: toolbar dropdown for switching formatting profiles.
            // Will be wired to ProfileManager.List() to populate items
            // and ProfileManager.Load() when user selects a profile.
        }
    }
}
