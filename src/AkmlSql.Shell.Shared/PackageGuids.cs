using System;

namespace AkmlSql.Shell.Shared
{
    public static class PackageGuids
    {
        public const string AkmlSqlPackageString = "A1B2C3D4-1111-2222-3333-444455556666";
        public const string AkmlSqlCmdSetString = "A1B2C3D4-1111-2222-3333-444455557777";

        public static readonly Guid AkmlSqlPackage = new Guid(AkmlSqlPackageString);
        public static readonly Guid AkmlSqlCmdSet = new Guid(AkmlSqlCmdSetString);
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
    }
}
