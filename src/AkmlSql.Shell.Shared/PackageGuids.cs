using System;

namespace AkmlSql.Shell.Shared
{
    public static class PackageGuids
    {
        public const string AkmlSqlPackageString = "A1B2C3D4-1111-2222-3333-444455556666";
        public const string AkmlSqlCmdSetString = "A1B2C3D4-1111-2222-3333-444455557777";

        public static readonly Guid AkmlSqlPackage = new(AkmlSqlPackageString);
        public static readonly Guid AkmlSqlCmdSet = new(AkmlSqlCmdSetString);
    }

    public static class CommandIds
    {
        public const int AkmlSqlMenu = 0x1000;
        public const int AkmlSqlMenuGroup = 0x1020;
        public const int CmdAbout = 0x0100;
        public const int CmdCheckUpdate = 0x0101;
        public const int CmdOptions = 0x0102;
        public const int CmdSendFeedback = 0x0103;
        public const int CmdViewLogs = 0x0104;
        public const int CmdRefreshCache = 0x0105;
        public const int CmdFormatDocument = 0x0200;
        public const int CmdFormatSelection = 0x0201;
        public const int CmdCasingOnly = 0x0210;
        public const int CmdInsertSemicolons = 0x0211;
        public const int CmdRemoveSemicolons = 0x0212;
        public const int CmdExpandWildcards = 0x0213;
        public const int CmdQualifyNames = 0x0214;
        public const int CmdToggleBrackets = 0x0215;
        public const int CmdToggleAs = 0x0216;
        public const int CmdUnformat = 0x021E;
        public const int CmdEditProfile = 0x0220;
        public const int CmdBulkAnalysis = 0x0300;

        // Phase 6 — Lightweight refactoring operations (Format menu)
        public const int CmdExpandInsertColumns    = 0x0217;
        public const int CmdExpandExecParameters   = 0x0218;
        public const int CmdExpandUpdateColumns    = 0x0219;
        public const int CmdConvertOldStyleJoins   = 0x021A;
        public const int CmdAddGroupByColumns      = 0x021B;
        public const int CmdEncapsulateBeginEnd    = 0x021C;
        public const int CmdReplaceDeprecatedSyntax = 0x021D;

        // Phase 6 — Heavyweight refactoring operations
        public const int CmdSafeRename             = 0x0400;
        public const int CmdExtractToCte           = 0x0401;
        public const int CmdExtractToProc          = 0x0402;
        public const int CmdExtractToDerivedTable  = 0x0403;
        public const int CmdEncapsulateAsView      = 0x0404;
        public const int CmdConvertTempToTableVar  = 0x0405;
        public const int CmdConvertTableVarToTemp  = 0x0406;
        public const int CmdParameterizeValues     = 0x0407;

        // Phase 7 — SQL History & Tab Management
        public const int CmdHistoryPanel            = 0x0500;
        public const int CmdRestoreClosedTab        = 0x0501;
        public const int CmdCloseUnmodified         = 0x0502;
        public const int CmdDuplicateTab            = 0x0503;
        public const int CmdPinTab                  = 0x0504;

        // Phase 8 — Productivity Toolkit
        public const int CmdCommandPalette          = 0x0600;
        public const int CmdExecuteCurrentStatement = 0x0601;
        public const int CmdExecuteToCursor         = 0x0602;
        public const int CmdGoToDefinition          = 0x0603;
        public const int CmdPeekDefinition          = 0x0604;
        public const int CmdFindReferences          = 0x0605;
        public const int CmdObjectSearch            = 0x0606;
        public const int CmdNavigateNextStatement   = 0x0607;
        public const int CmdNavigatePrevStatement   = 0x0608;
        public const int CmdNavigateMatchingPair    = 0x0609;
        public const int CmdGridFind                = 0x060A;
        public const int CmdGridExport              = 0x060B;
        public const int CmdCrudGeneration          = 0x060C;
        public const int CmdDocumentOutline         = 0x060D;

        // Phase 9 — AI Assistance
        public const int CmdTextToSql               = 0x0700;
        public const int CmdAiExplain               = 0x0701;
        public const int CmdAiFix                   = 0x0702;
        public const int CmdAiOptimize              = 0x0703;
        public const int CmdAiIndexAnalysis         = 0x0704;
        public const int CmdAiChatPanel             = 0x0705;

        // Phase 10 — SQL Prompt Core Parity
        public const int CmdSnippetManager          = 0x0800;
        public const int CmdBookmarkToggle          = 0x0801;
        public const int CmdBookmarkNext            = 0x0802;
        public const int CmdBookmarkPrev            = 0x0803;
        public const int CmdSplitTable              = 0x0804;

        // Phase 10 — SQL Prompt Parity Closure (spec 019)
        // See specs/019-phase10-parity-closure/contracts/commands.md for the full
        // catalog including which existing commands (CmdCommandPalette, CmdToggleBrackets,
        // CmdExtractToProc, CmdExecuteToCursor, CmdSafeRename, CmdAiChatPanel, CmdAiFix,
        // CmdAiOptimize, CmdAiExplain, CmdAiIndexAnalysis) receive new chord bindings
        // rather than new IDs. Reserved range 0x0916..0x093F.
        public const int CmdColumnPickerOpen             = 0x0900;
        public const int CmdTabWildcardExpand            = 0x0901;
        public const int CmdShowCodeAnalysisIssues       = 0x0902;
        public const int CmdLightbulbApplyFix            = 0x0903;
        public const int CmdLightbulbDisableRule         = 0x0904;
        public const int CmdTabColorAssignServer         = 0x0905;
        public const int CmdTabColorAssignDatabase       = 0x0906;
        public const int CmdTabColorAssignServerGroup    = 0x0907;
        public const int CmdSummarizeScript              = 0x0908;
        public const int CmdScriptAsAlter                = 0x0909;
        public const int CmdSelectInObjectExplorer       = 0x090A;
        public const int CmdFindUnusedVariables          = 0x090B;
        public const int CmdBrowseOpenTabs               = 0x090C;
        public const int CmdShowFindInvalidObjects       = 0x090D;
        public const int CmdInlineStoredProcedure        = 0x090E;
        public const int CmdSmartRename                  = 0x090F;
        public const int CmdExecuteCurrentBatch          = 0x0910;
        public const int CmdToggleSuggestions            = 0x0911;
        public const int CmdCycleCategoryFilterForward   = 0x0912;
        public const int CmdCycleCategoryFilterBackward  = 0x0913;
        public const int CmdDisableFormattingForSelection = 0x0914;
        public const int CmdAiManualGhostText            = 0x0915;

        // Spec 020 US3 T059 — Format Styles editor launcher (takes the next free slot;
        // spec 019's reservation note above is tightened from 0x0916..0x093F to 0x0917..0x093F).
        public const int CmdFormatStyles                 = 0x0916;

        // Spec 030 T053 — Manage Code Analysis Rules dialog launcher.
        public const int CmdManageCodeAnalysisRules      = 0x0917;

        // Spec 030 T067 — refactor ops that lacked an id (InlineStoredProcedure=0x090E and
        // ScriptAsAlter=0x0909 already exist above and are reused).
        public const int CmdInlineExec                   = 0x0918;
        public const int CmdInsertToUpdate               = 0x0919;
    }
}
