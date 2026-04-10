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
        TruncateTable = 6,

        // Spec 014, US1 — new detection patterns (FR-002, FR-003)

        /// <summary>MERGE without a WHEN MATCHED filter.</summary>
        MergeWithoutFilter = 7,
        /// <summary>DELETE or UPDATE inside an INNER JOIN with no WHERE clause.</summary>
        DmlInsideJoinWithoutWhere = 8,
        /// <summary>Unsafe DML found inside CREATE/ALTER PROCEDURE or TRIGGER body.</summary>
        UnsafeDmlInProcOrTrigger = 9
    }
}
