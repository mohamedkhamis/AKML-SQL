using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>What a <see cref="SessionSuppressionRequest"/> asks the engine to do.</summary>
    public static class SessionSuppressionActions
    {
        /// <summary>Suppress <c>RuleId</c> for the rest of the session.</summary>
        public const int Add = 0;

        /// <summary>Lift the session suppression on <c>RuleId</c>.</summary>
        public const int Remove = 1;

        /// <summary>Lift every session suppression. <c>RuleId</c> is ignored.</summary>
        public const int Clear = 2;

        /// <summary>Change nothing; just return the current list. <c>RuleId</c> is ignored.</summary>
        public const int List = 3;
    }

    /// <summary>
    /// Adds, removes, clears or lists the rules suppressed for the current session — the scope that
    /// lasts until the IDE closes and writes nothing to the script or to disk.
    /// Sent Shell -> Engine as MessageType 36 (SessionSuppression). Pairs with response 136.
    /// </summary>
    [MessagePackObject]
    public class SessionSuppressionRequest
    {
        /// <summary>The rule to add or remove (e.g. "PE001"). Ignored by Clear and List.</summary>
        [Key(0)]
        public string RuleId { get; set; } = string.Empty;

        /// <summary>One of <see cref="SessionSuppressionActions"/>. Defaults to List.</summary>
        [Key(1)]
        public int Action { get; set; } = SessionSuppressionActions.List;
    }
}
