#nullable enable
using System;
using System.ComponentModel.Design;
using AkmlSql.Core.Ipc.Messages;
using Microsoft.VisualStudio.Shell;

namespace AkmlSql.Shell.Shared.Refactoring
{
    /// <summary>Spec 030 T067 — Convert a single-row INSERT…VALUES into an UPDATE (FR-021).</summary>
    internal sealed class InsertToUpdateCommand
    {
        private InsertToUpdateCommand(Package package, OleMenuCommandService commandService)
        {
            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdInsertToUpdate);
            var item  = new OleMenuCommand(Execute, cmdId);
            item.BeforeQueryStatus += (s, _) => { if (s is OleMenuCommand c) { c.Visible = true; c.Enabled = true; } };
            commandService.AddCommand(item);
        }

        public static InsertToUpdateCommand? Instance { get; private set; }

        public static void Initialize(Package package, OleMenuCommandService commandService)
            => Instance = new InsertToUpdateCommand(package, commandService);

        private void Execute(object sender, EventArgs e)
            => RefactorCommandHelper.RunInlineRefactor((int)RefactorOperationType.InsertToUpdate, "INSERT → UPDATE", "Apply");
    }
}
