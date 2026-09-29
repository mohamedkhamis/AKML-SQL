namespace AkmlSql.Core.Models.History
{
    /// <summary>Outcome of a SQL execution captured by the history engine.</summary>
    public enum ExecutionStatus
    {
        Success = 0,
        Error = 1,
        Cancelled = 2,

        /// <summary>
        /// Spec 040 (HIS-13): a draft — a query captured (autosave, or a tab closing) without being
        /// run. Listed in History, but never counted as a run.
        /// </summary>
        NotExecuted = 3
    }
}
