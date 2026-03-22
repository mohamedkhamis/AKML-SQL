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
    }
}
