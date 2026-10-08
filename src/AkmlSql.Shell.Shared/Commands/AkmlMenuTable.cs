#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// One item of the AKML SQL menu (spec 040 T180, contracts/ui.md §2): a command, by id, or a
    /// submenu holding commands.
    /// </summary>
    internal sealed class AkmlMenuEntry
    {
        private static readonly IReadOnlyList<AkmlMenuEntry> NoChildren = Array.Empty<AkmlMenuEntry>();

        private AkmlMenuEntry(string? caption, int commandId, IReadOnlyList<AkmlMenuEntry>? children, bool beginGroup, bool aiOnly)
        {
            Caption = caption;
            CommandId = commandId;
            Children = children ?? NoChildren;
            IsSubmenu = children != null;
            BeginGroup = beginGroup;
            AiOnly = aiOnly;
        }

        /// <summary>
        /// The text shown. Null for a command whose text changes at run time (the Active Style
        /// slots show their style's name): its control keeps the command's own text.
        /// </summary>
        internal string? Caption { get; }

        /// <summary>The command's id in the AKML command set; 0 for a submenu.</summary>
        internal int CommandId { get; }

        internal bool IsSubmenu { get; }

        /// <summary>A submenu's items, in order; empty for a command.</summary>
        internal IReadOnlyList<AkmlMenuEntry> Children { get; }

        /// <summary>A separator goes above this item.</summary>
        internal bool BeginGroup { get; }

        /// <summary>Shown only while AI is enabled (the AI submenu, as the AI commands are today).</summary>
        internal bool AiOnly { get; }

        internal static AkmlMenuEntry Command(int commandId, string? caption, bool beginGroup = false) =>
            new AkmlMenuEntry(caption, commandId, null, beginGroup, aiOnly: false);

        internal static AkmlMenuEntry Submenu(string caption, IEnumerable<AkmlMenuEntry> children, bool beginGroup = false, bool aiOnly = false) =>
            new AkmlMenuEntry(caption, 0, children.ToList(), beginGroup, aiOnly);

        public override string ToString() => IsSubmenu ? Caption + " ▸" : Caption ?? $"0x{CommandId:X4}";
    }

    /// <summary>
    /// Spec 040 (T180, X-01, FR-060, FR-034, SC-015) — the AKML SQL menu, declared once.
    /// <c>AkmlSqlPackage.EnsureTopLevelMenu</c> builds the DTE menu SSMS 22 shows from
    /// <see cref="Entries"/> (and the SQL editor's context menu from <see cref="EditorContextEntries"/>);
    /// the VSCT groups mirror the same structure. Tested by <c>AkmlMenuTableTests</c>: every command
    /// here must be in <see cref="RegisteredCommands.Ids"/>.
    /// <para>
    /// Not placed, because nothing handles them yet (recorded as follow-ups): Text to SQL, AI Optimize,
    /// AI Index Analysis, Generate CRUD Procedures, Find in Results Grid and Split Table.
    /// </para>
    /// <para>Change <see cref="Marker"/> whenever this table changes, so an installed menu is rebuilt.</para>
    /// </summary>
    internal static class AkmlMenuTable
    {
        /// <summary>The top-level menu's caption.</summary>
        internal const string MenuCaption = "AKML SQL";

        /// <summary>The version marker kept in the menu popup's <c>Tag</c>.</summary>
        internal const string Marker = "akml-menu-v2";

        private const string Ellipsis = "…";

        private static AkmlMenuEntry Cmd(int id, string caption, bool beginGroup = false) =>
            AkmlMenuEntry.Command(id, caption, beginGroup);

        /// <summary>
        /// Active Style ▸ — the 30 style slots (their text is the style name; empty slots hide
        /// themselves) and Edit Styles…. Shared by the AKML SQL menu and the editor context menu.
        /// </summary>
        internal static readonly AkmlMenuEntry ActiveStyle = AkmlMenuEntry.Submenu("Active Style",
            Enumerable.Range(CommandIds.CmdActiveStyleSlot0, CommandIds.ActiveStyleSlotCount)
                .Select(id => AkmlMenuEntry.Command(id, null))
                .Concat(new[] { Cmd(CommandIds.CmdEditStyles, "Edit Styles" + Ellipsis, beginGroup: true) }));

        /// <summary>The AKML SQL menu's 12 top-level entries, in order (contracts/ui.md §2).</summary>
        internal static readonly IReadOnlyList<AkmlMenuEntry> Entries = new[]
        {
            // Format with the active style
            Cmd(CommandIds.CmdFormatDocument, "Format Document"),
            Cmd(CommandIds.CmdFormatSelection, "Format Selection"),
            ActiveStyle,
            AkmlMenuEntry.Submenu("Formatting", new[]
            {
                Cmd(CommandIds.CmdUnformat, "Unformat Document"),
                Cmd(CommandIds.CmdFormatStyles, "Edit Formatting Styles" + Ellipsis),
                Cmd(CommandIds.CmdBulkFormat, "Bulk Format" + Ellipsis),
                Cmd(CommandIds.CmdDisableFormattingForSelection, "Disable Formatting for Selection"),
            }),

            // Code
            AkmlMenuEntry.Submenu("Refactor", new[]
            {
                Cmd(CommandIds.CmdSafeRename, "Smart Rename"),
                Cmd(CommandIds.CmdScriptAsAlter, "Script as ALTER"),
                Cmd(CommandIds.CmdInlineExec, "Inline EXEC"),
                Cmd(CommandIds.CmdInlineStoredProcedure, "Inline Stored Procedure"),
                Cmd(CommandIds.CmdInsertToUpdate, "Convert INSERT to UPDATE"),
                Cmd(CommandIds.CmdShowFindInvalidObjects, "Find Invalid Objects"),
            }, beginGroup: true),
            AkmlMenuEntry.Submenu("Navigate", new[]
            {
                Cmd(CommandIds.CmdGoToDefinition, "Go To Definition"),
                Cmd(CommandIds.CmdPeekDefinition, "Peek Definition"),
                Cmd(CommandIds.CmdFindReferences, "Find All References"),
                Cmd(CommandIds.CmdObjectSearch, "Object Search"),
                Cmd(CommandIds.CmdNavigateNextStatement, "Next Statement"),
                Cmd(CommandIds.CmdNavigatePrevStatement, "Previous Statement"),
                Cmd(CommandIds.CmdNavigateMatchingPair, "Matching Pair"),
                Cmd(CommandIds.CmdDocumentOutline, "Document Outline"),
                Cmd(CommandIds.CmdBookmarkToggle, "Toggle Bookmark", beginGroup: true),
                Cmd(CommandIds.CmdBookmarkNext, "Next Bookmark"),
                Cmd(CommandIds.CmdBookmarkPrev, "Previous Bookmark"),
            }),

            // Tabs and history
            Cmd(CommandIds.CmdHistoryPanel, "SQL History", beginGroup: true),
            AkmlMenuEntry.Submenu("Tabs", new[]
            {
                Cmd(CommandIds.CmdRestoreClosedTab, "Restore Closed Tab"),
                Cmd(CommandIds.CmdCloseUnmodified, "Close All Unmodified"),
                Cmd(CommandIds.CmdDuplicateTab, "Duplicate Tab"),
                Cmd(CommandIds.CmdPinTab, "Pin Tab"),
            }),

            // AI and tools
            AkmlMenuEntry.Submenu("AI", new[]
            {
                Cmd(CommandIds.CmdAiChatPanel, "Chat Panel"),
                Cmd(CommandIds.CmdAiExplain, "Explain SQL"),
                Cmd(CommandIds.CmdAiFix, "Fix SQL"),
            }, beginGroup: true, aiOnly: true),
            AkmlMenuEntry.Submenu("Tools", new[]
            {
                Cmd(CommandIds.CmdCommandPalette, "Command Palette"),
                Cmd(CommandIds.CmdExecuteCurrentStatement, "Execute Current Statement"),
                Cmd(CommandIds.CmdExecuteToCursor, "Execute to Cursor"),
                Cmd(CommandIds.CmdSnippetManager, "Snippet Manager", beginGroup: true),
                Cmd(CommandIds.CmdSnippetSurroundWith, "Surround Selection With Snippet" + Ellipsis),
                Cmd(CommandIds.CmdManageCodeAnalysisRules, "Manage Code Analysis Rules" + Ellipsis, beginGroup: true),
                Cmd(CommandIds.CmdToggleCodeAnalysis, "Toggle Code Analysis"),
                Cmd(CommandIds.CmdGridExport, "Export Results Grid", beginGroup: true),
            }),

            // Settings and help
            Cmd(CommandIds.CmdOptions, "Options" + Ellipsis, beginGroup: true),
            AkmlMenuEntry.Submenu("Help", new[]
            {
                Cmd(CommandIds.CmdSendFeedback, "Send Feedback"),
                Cmd(CommandIds.CmdViewLogs, "View Logs"),
                Cmd(CommandIds.CmdRefreshCache, "Refresh Schema Cache"),
                Cmd(CommandIds.CmdCheckUpdate, "Check for Updates", beginGroup: true),
                Cmd(CommandIds.CmdAbout, "About AKML SQL"),
            }),
        };

        /// <summary>
        /// What the SQL editor's context menu gets (FR-034): Format Document, then Active Style ▸.
        /// </summary>
        internal static readonly IReadOnlyList<AkmlMenuEntry> EditorContextEntries = new[]
        {
            Cmd(CommandIds.CmdFormatDocument, "Format Document", beginGroup: true),
            ActiveStyle,
        };

        /// <summary>Every command id in <paramref name="entries"/> and their submenus, in menu order.</summary>
        internal static IEnumerable<int> AllCommandIds(IEnumerable<AkmlMenuEntry> entries) =>
            entries.SelectMany(e => e.IsSubmenu ? AllCommandIds(e.Children) : new[] { e.CommandId });

        /// <summary>
        /// A menu caption as compared: no accelerator ampersands, nothing from a tab on (the shortcut
        /// text), trimmed.
        /// </summary>
        internal static string NormalizeCaption(string? caption)
        {
            if (string.IsNullOrEmpty(caption)) return string.Empty;
            var text = caption!;
            var tab = text.IndexOf('\t');
            if (tab >= 0) text = text.Substring(0, tab);
            return text.Replace("&", string.Empty).Trim();
        }

        private static bool SameCaption(string? caption, string? wanted) =>
            string.Equals(NormalizeCaption(caption), NormalizeCaption(wanted), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The menu bar's controls (caption and tag, in order) → what to do: keep the first "AKML SQL"
        /// popup carrying <see cref="Marker"/>; remove every other "AKML SQL" (older menus, without
        /// the marker, and duplicates); build a new menu when none is kept.
        /// </summary>
        internal static AkmlControlPlan PlanMenuBar(IReadOnlyList<(string? Caption, string? Tag)> controls)
        {
            var keep = -1;
            var remove = new List<int>();
            for (var i = 0; i < controls.Count; i++)
            {
                if (!SameCaption(controls[i].Caption, MenuCaption)) continue;
                if (keep < 0 && string.Equals(controls[i].Tag, Marker, StringComparison.Ordinal)) keep = i;
                else remove.Add(i);
            }
            return new AkmlControlPlan(keep, remove, reveal: false);
        }

        /// <summary>
        /// The editor context menu's controls (caption and visibility, in order) → one plan per
        /// <see cref="EditorContextEntries"/> item, so the menu ends up with exactly one of each.
        /// Format Document is a persistent control (<c>Command.AddControl</c>), so it is still there
        /// on the next SSMS start and must not be added again. Keeps the first visible copy (else the
        /// first, revealed) and removes the rest.
        /// </summary>
        internal static IReadOnlyList<AkmlControlPlan> PlanEditorContext(IReadOnlyList<(string? Caption, bool Visible)> controls)
        {
            return EditorContextEntries.Select(entry =>
            {
                var matches = Enumerable.Range(0, controls.Count)
                    .Where(i => SameCaption(controls[i].Caption, entry.Caption))
                    .ToList();
                if (matches.Count == 0) return new AkmlControlPlan(-1, Array.Empty<int>(), reveal: false);

                var keep = matches.Where(i => controls[i].Visible).DefaultIfEmpty(matches[0]).First();
                return new AkmlControlPlan(keep, matches.Where(i => i != keep).ToList(), reveal: !controls[keep].Visible);
            }).ToList();
        }
    }

    /// <summary>What the menu builder does with the controls a bar already holds for one item.</summary>
    internal sealed class AkmlControlPlan
    {
        internal AkmlControlPlan(int keep, IReadOnlyList<int> remove, bool reveal)
        {
            Keep = keep;
            Remove = remove;
            Reveal = reveal;
        }

        /// <summary>The index of the control to keep; -1 when there is none.</summary>
        internal int Keep { get; }

        /// <summary>The indexes of the controls to delete (or hide, when deleting fails).</summary>
        internal IReadOnlyList<int> Remove { get; }

        /// <summary>The kept control is hidden: show it.</summary>
        internal bool Reveal { get; }

        /// <summary>Nothing to keep: add the item.</summary>
        internal bool Add => Keep < 0;
    }
}
