#nullable enable
using System;
using System.ComponentModel.Design;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.VisualStudio.Shell;

namespace AkmlSql.Shell.Shared.Refactoring
{
    /// <summary>
    /// Spec 030 T067 — Inline a stored-procedure call into its (single-query) body (FR-020).
    /// Needs a live connection (the engine fetches the proc body from sys.sql_modules); the helper
    /// supplies the active editor's real session id so the engine can resolve it.
    /// </summary>
    internal sealed class InlineStoredProcedureCommand
    {
        private InlineStoredProcedureCommand(Package package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdInlineStoredProcedure);
            var item  = new OleMenuCommand(Execute, cmdId);
            item.BeforeQueryStatus += (s, _) => { if (s is OleMenuCommand c) { c.Visible = true; c.Enabled = true; } };
            commandService.AddCommand(item);
        }

        public static InlineStoredProcedureCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
            => Instance = new InlineStoredProcedureCommand(package, commandService);

        private void Execute(object sender, EventArgs e)
            => RefactorCommandHelper.RunInlineRefactor((int)RefactorOperationType.InlineStoredProcedure,
                "Inline stored procedure", "Apply");
    }
}
