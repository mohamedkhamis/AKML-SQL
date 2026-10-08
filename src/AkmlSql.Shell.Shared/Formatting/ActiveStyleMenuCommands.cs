#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Commands;
using AkmlSql.Shell.Shared.StatusBar;
using Microsoft.VisualStudio.Shell;
using Serilog;

namespace AkmlSql.Shell.Shared.Formatting
{
    /// <summary>
    /// Spec 040 (T107, STY-08, FR-033) — AKML SQL › Active Style: one checkable item per style
    /// (up to <see cref="SlotCount"/>), so switching the style Format SQL uses takes one click, as in
    /// SQL Prompt, plus "Edit Styles…". The items are fixed VSCT buttons whose text, check mark and
    /// visibility come from <see cref="ActiveStyleCache"/> each time the menu opens.
    /// </summary>
    internal static class ActiveStyleMenuCommands
    {
        internal const int SlotCount = 30;

        /// <summary>What slot <paramref name="slot"/> shows: style N's name, checked when active; hidden past the last style.</summary>
        internal static (string Text, bool Checked, bool Visible) SlotState(
            IReadOnlyList<(string Name, string Source, bool IsActive)> styles, int slot)
            => slot >= 0 && slot < SlotCount && slot < styles.Count
                ? (styles[slot].Name, styles[slot].IsActive, true)
                : ("Style", false, false);

        /// <summary>
        /// Spec 040 (T110) — the query editor's context menu among SSMS's command bars:
        /// "SQL Files Editor Context", else another "SQL … Editor Context", else VS's generic
        /// "Code Window". Null when there is none.
        /// </summary>
        internal static string? PickEditorContextBar(IEnumerable<string> barNames)
        {
            var names = (barNames ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrEmpty(n)).ToList();
            return names.FirstOrDefault(n => string.Equals(n, "SQL Files Editor Context", StringComparison.OrdinalIgnoreCase))
                ?? names.FirstOrDefault(n => n.IndexOf("SQL", StringComparison.OrdinalIgnoreCase) >= 0
                                          && n.IndexOf("Editor Context", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? names.FirstOrDefault(n => string.Equals(n, "Code Window", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Makes slot <paramref name="slot"/>'s style the active one: saves it as
        /// <c>Formatter.ActiveProfile</c>, shows it in the status bar (when that is on), marks it at
        /// once and refreshes the list. False when the slot is empty or the save failed.
        /// </summary>
        internal static bool ActivateSlot(int slot, ActiveStyleCache cache)
        {
            var styles = cache.Styles;
            if (slot < 0 || slot >= SlotCount || slot >= styles.Count) return false;
            var name = styles[slot].Name;

            try
            {
                var settings = ConfigManager.Load();
                settings.Formatter.ActiveProfile = name;
                ConfigManager.Save(settings);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Active Style: could not save '{Name}' as the active style", name);
                return false;
            }

            cache.MarkActive(name);
            try { StatusBarManager.SetActiveProfile(name); }
            catch (Exception ex) { Log.Debug(ex, "Active Style: status bar update failed"); }
            cache.RefreshNow();
            Log.Information("Active Style: '{Name}' is now the active formatting style", name);
            return true;
        }

        public static void Initialize(Package package, OleMenuCommandService commandService)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            if (commandService == null) throw new ArgumentNullException(nameof(commandService));

            for (var i = 0; i < SlotCount; i++)
            {
                var slot = i;
                var command = new OleMenuCommand(
                    (_, _) =>
                    {
                        ThreadHelper.ThrowIfNotOnUIThread();
                        ActivateSlot(slot, ActiveStyleCache.Instance);
                    },
                    new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdActiveStyleSlot0 + slot));
                command.BeforeQueryStatus += (sender, _) =>
                {
                    if (!(sender is OleMenuCommand c)) return;
                    var state = SlotState(ActiveStyleCache.Instance.Styles, slot);
                    c.Text = state.Text.Replace("&", "&&"); // a style name is not an accelerator
                    c.Checked = state.Checked;
                    c.Visible = state.Visible;
                    c.Enabled = state.Visible;
                    // One refresh per menu open (throttled): the first slot asks.
                    if (slot == 0) ActiveStyleCache.Instance.RequestRefresh();
                };
                commandService.AddCommand(command);
            }

            commandService.AddCommand(new MenuCommand(
                (_, _) => FormatStylesCommand.Open(),
                new CommandID(PackageGuids.AkmlSqlCmdSet, CommandIds.CmdEditStyles)));
        }
    }
}
