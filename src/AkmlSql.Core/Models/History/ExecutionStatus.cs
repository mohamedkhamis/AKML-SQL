namespace AkmlSql.Core.Models.History
{
    /// <summary>Outcome of a SQL execution captured by the history engine.</summary>
    public enum ExecutionStatus
    {
        Success = 0,
        Error = 1,
        Cancelled = 2
    }
}
