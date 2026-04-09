using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 014, US13 — response from <see cref="FindUnusedVariablesRequest"/>.
    /// Sent Engine → Shell as <see cref="MessageTypes.FindUnusedVariablesResult"/> (191).
    /// </summary>
    [MessagePackObject]
    public class FindUnusedVariablesResponse
    {
        /// <summary>Status code: 0 = Ok, 1 = ParseError.</summary>
        [Key(0)]
        public int Status { get; set; }

        /// <summary>The set of unused declarations. Empty when nothing was found.</summary>
        [Key(1)]
        public UnusedDeclarationDto[] Unused { get; set; } = [];

        /// <summary>Error message when <see cref="Status"/> is non-zero.</summary>
        [Key(2)]
        public string? ErrorMessage { get; set; }
    }
}
