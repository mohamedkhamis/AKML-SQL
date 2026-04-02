#nullable enable
using System;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Snippets
{
    /// <summary>
    /// Menu command that opens the Snippet Manager dialog.
    /// Bound to <see cref="CommandIds.CmdSnippetManager"/> (0x0800).
    /// </summary>
    internal sealed class SnippetManagerCommand
    {
        public static SnippetManagerCommand? Instance { get; private set; }

        private SnippetManagerCommand(Package package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdSnippetManager);
            var menuItem = new MenuCommand(Execute, cmdId);
            commandService.AddCommand(menuItem);
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new SnippetManagerCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var viewModel = new SnippetManagerViewModel();
                var dialog = new SnippetManagerDialog(viewModel);
                dialog.ShowModal();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "SnippetManagerCommand: failed to open Snippet Manager");
            }
        }
    }
}
