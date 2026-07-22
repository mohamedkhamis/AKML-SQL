using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>Spec 033 — ProfileRename result.</summary>
    [MessagePackObject]
    public class ProfileRenameResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>Populated iff <see cref="Success"/> is false.</summary>
        [Key(1)]
        public string? ErrorMessage { get; set; }

        /// <summary>The final (sanitized) name actually persisted.</summary>
        [Key(2)]
        public string? NewName { get; set; }
    }
}
