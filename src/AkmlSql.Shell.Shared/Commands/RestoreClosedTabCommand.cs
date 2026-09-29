#nullable enable
using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Threading.Tasks;
using AkmlSql.Core.Ipc;
using AkmlSql.Core.Ipc.Messages;
using AkmlSql.Core.Models.Tabs;
using AkmlSql.Shell.Shared.History;
using AkmlSql.Shell.Shared.Ipc;
using AkmlSql.Shell.Shared.Tabs;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using Serilog;
using Constants = AkmlSql.Core.Constants;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Restores the most recently closed tab: the newest entry of <see cref="ClosedTabStack"/>
    /// (tabs closed in this session), else — spec 040 (T146, HIS-14) — the newest closed query in
    /// SQL History, so Ctrl+Shift+T also works after SSMS restarts.
    /// <para>
    /// Bound to <see cref="CommandIds.CmdRestoreClosedTab"/> (0x0501).
    /// </para>
    /// </summary>
    internal sealed class RestoreClosedTabCommand
    {
        private readonly ClosedTabRestorer _restorer = new ClosedTabRestorer();

        private RestoreClosedTabCommand(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));

            var cmdId = new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdRestoreClosedTab);
            var menuItem = new OleMenuCommand(Execute, cmdId);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static RestoreClosedTabCommand? Instance { get; private set; }

        /// <summary>
        /// Creates the singleton command instance and registers it with the command service.
        /// Must be called on the UI thread during package initialization.
        /// </summary>
        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            Instance = new RestoreClosedTabCommand(package, commandService);
        }

        /// <summary>Enabled while there is something to reopen: a closed tab, or SQL History.</summary>
        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            if (sender is OleMenuCommand cmd)
            {
                cmd.Enabled = _restorer.CanRestore;
            }
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _ = ExecuteAsync();
        }

        private async Task ExecuteAsync()
        {
            try
            {
                await _restorer.RestoreAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "RestoreClosedTabCommand: failed to restore tab");
                System.Windows.Forms.MessageBox.Show(
                    "Unable to restore the closed tab: " + ex.Message,
                    Constants.ProductName,
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Warning);
            }
        }

        /// <summary>Opens a closed tab's captured text in a new query tab.</summary>
        internal static void OpenClosedTab(ClosedTabEntry entry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            Log.Information("RestoreClosedTabCommand: restoring '{Title}' (closed at {ClosedAt})",
                entry.TabTitle ?? "(untitled)", entry.ClosedAt);

            var dte = (DTE)Package.GetGlobalService(typeof(DTE));
            if (dte == null)
            {
                Log.Error("RestoreClosedTabCommand: DTE service unavailable");
                return;
            }

            dte.ItemOperations.NewFile(
                @"General\Sql File",
                entry.TabTitle ?? "Restored.sql",
                EnvDTE.Constants.vsViewKindCode);

            var activeDoc = dte.ActiveDocument;
            if (activeDoc == null)
            {
                Log.Warning("RestoreClosedTabCommand: no active document after NewFile");
                return;
            }

            if (activeDoc.Object("TextDocument") is not TextDocument textDocument)
            {
                Log.Warning("RestoreClosedTabCommand: new document has no TextDocument; content not restored");
                return;
            }

            var editPoint = textDocument.StartPoint.CreateEditPoint();
            editPoint.Insert(entry.Content);
            textDocument.Selection.StartOfDocument();
        }
    }

    /// <summary>
    /// Spec 040 (T146, HIS-14) — what Ctrl+Shift+T reopens: the newest tab closed in this session
    /// (<see cref="ClosedTabStack"/>); when there is none, the newest closed query in SQL History,
    /// through History's own open path. The history client and the openers are injectable for tests.
    /// </summary>
    internal sealed class ClosedTabRestorer
    {
        private readonly IRpcClientAccessor _rpc;
        private readonly ClosedTabStack _stack;

        public ClosedTabRestorer() : this(EngineRpcClientAccessor.Instance, ClosedTabStack.Instance) { }

        internal ClosedTabRestorer(IRpcClientAccessor rpc, ClosedTabStack stack)
        {
            _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
            _stack = stack ?? throw new ArgumentNullException(nameof(stack));
        }

        internal Action<ClosedTabEntry> OpenClosedTab { get; set; } = RestoreClosedTabCommand.OpenClosedTab;

        internal Action<HistoryEntryDto> OpenFromHistory { get; set; } = entry =>
            HistoryQueryOpener.OpenInNewTab(entry.SqlText, entry.Server, entry.Database, entry.SessionKey, "History.sql");

        internal Action<string> Notify { get; set; } = text => StatusBar.StatusBarManager.ShowTransient(text, 4);

        public bool CanRestore => _stack.Count > 0 || _rpc.IsConnected;

        /// <summary>Reopens one tab; false when there was nothing to reopen.</summary>
        public async Task<bool> RestoreAsync()
        {
            var closed = _stack.Pop();
            if (closed != null)
            {
                OpenClosedTab(closed);
                return true;
            }

            if (!_rpc.IsConnected) return false;

            var search = await _rpc.SendRequestAsync<HistorySearchResponse, HistorySearchRequest>(
                MessageTypes.HistorySearch,
                new HistorySearchRequest { IsOpen = false, Deduplicate = true, Offset = 0, Limit = 1 },
                timeoutMs: 10000);
            var newest = search?.Entries?.FirstOrDefault();
            if (newest == null)
            {
                Notify("There are no closed queries to reopen.");
                return false;
            }

            // The search rows carry only the start of the text.
            var full = await _rpc.SendRequestAsync<HistoryActionResponse, HistoryActionRequest>(
                MessageTypes.HistoryAction,
                new HistoryActionRequest { Action = HistoryActions.GetFullSql, EntryIds = new[] { newest.Id } },
                timeoutMs: 10000);
            if (full?.Success == true && full.FullSqlText != null) newest.SqlText = full.FullSqlText;

            OpenFromHistory(newest);
            Notify($"Restored '{HistoryRowDisplay.DisplayNameFor(newest)}' from SQL History.");
            return true;
        }
    }
}
