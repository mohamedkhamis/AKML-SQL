using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 014, US13 — request to scan a script for declared variables and
    /// procedure/function parameters that are never read.
    /// Sent Shell → Engine as <see cref="MessageTypes.FindUnusedVariables"/> (91).
    /// </summary>
    [MessagePackObject]
    public class FindUnusedVariablesRequest
    {
        /// <summary>Session id of the active editor (used for logging only).</summary>
        [Key(0)]
        public string SessionId { get; set; } = string.Empty;

        /// <summary>Full SQL text of the document to analyse.</summary>
        [Key(1)]
        public string DocumentText { get; set; } = string.Empty;
    }
}
