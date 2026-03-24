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
    }
}
