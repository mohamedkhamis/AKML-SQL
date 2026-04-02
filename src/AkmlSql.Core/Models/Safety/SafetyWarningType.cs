namespace AkmlSql.Core.Models.Safety
{
    /// <summary>Categories of execution safety warnings detected by the engine.</summary>
    public enum SafetyWarningType
    {
        ProductionDml = 0,
        ProductionDdl = 1,
        DeleteWithoutWhere = 2,
        UpdateWithoutWhere = 3,
        DropTable = 4,
        DropDatabase = 5,
        TruncateTable = 6
    }
}
