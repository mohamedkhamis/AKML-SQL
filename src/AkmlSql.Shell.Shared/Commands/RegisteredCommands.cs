#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace AkmlSql.Shell.Shared.Commands
{
    /// <summary>
    /// Spec 040 (T105) — every command id the SSMS package registers (from the
    /// <c>TryInitCommand</c> list in <c>AkmlSqlPackage</c>). The menu table is tested against it, so a
    /// menu can only point at a command that exists. Keep it in sync when a command is added; a
    /// Debug build of the package logs any registered id missing from here.
    /// </summary>
    internal static class RegisteredCommands
    {
        internal static readonly IReadOnlyCollection<int> Ids = new HashSet<int>(new[]
        {
            CommandIds.CmdAbout,
            CommandIds.CmdCheckUpdate,
            CommandIds.CmdOptions,
            CommandIds.CmdFormatStyles,
            CommandIds.CmdManageCodeAnalysisRules,
            CommandIds.CmdInlineExec,
            CommandIds.CmdInsertToUpdate,
            CommandIds.CmdInlineStoredProcedure,
            CommandIds.CmdScriptAsAlter,
            CommandIds.CmdShowFindInvalidObjects,
            CommandIds.CmdSafeRename,
            CommandIds.CmdToggleCodeAnalysis,
            CommandIds.CmdDisableFormattingForSelection,
            CommandIds.CmdSendFeedback,
            CommandIds.CmdViewLogs,
            CommandIds.CmdRefreshCache,
            CommandIds.CmdRestoreClosedTab,
            CommandIds.CmdCloseUnmodified,
            CommandIds.CmdDuplicateTab,
            CommandIds.CmdPinTab,
            CommandIds.CmdHistoryPanel,
            CommandIds.CmdGridExport,
            CommandIds.CmdGoToDefinition,
            CommandIds.CmdPeekDefinition,
            CommandIds.CmdObjectSearch,
            CommandIds.CmdFindReferences,
            CommandIds.CmdCommandPalette,
            CommandIds.CmdExecuteCurrentStatement,
            CommandIds.CmdExecuteToCursor,
            CommandIds.CmdDocumentOutline,
            CommandIds.CmdNavigateNextStatement,
            CommandIds.CmdNavigatePrevStatement,
            CommandIds.CmdNavigateMatchingPair,
            CommandIds.CmdFormatDocument,
            CommandIds.CmdFormatSelection,
            CommandIds.CmdUnformat,
            CommandIds.CmdAiExplain,
            CommandIds.CmdAiFix,
            CommandIds.CmdAiChatPanel,
            CommandIds.CmdBulkAnalysis,
            CommandIds.CmdSnippetManager,
            CommandIds.CmdSnippetCreateFromSelection,
            CommandIds.CmdSnippetSurroundWith,
            CommandIds.CmdBulkFormat,
            CommandIds.CmdBookmarkToggle,
            CommandIds.CmdBookmarkNext,
            CommandIds.CmdBookmarkPrev,
            // Spec 040 (T106/T107) — Active Style slots and Edit Styles…
            CommandIds.CmdEditStyles,
        }.Concat(Enumerable.Range(CommandIds.CmdActiveStyleSlot0, CommandIds.ActiveStyleSlotCount)));
    }
}
