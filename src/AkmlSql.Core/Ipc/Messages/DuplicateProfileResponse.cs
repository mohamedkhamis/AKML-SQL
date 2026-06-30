using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>Spec 030 T020 — result of <see cref="DuplicateProfileRequest"/>.</summary>
    [MessagePackObject]
    public class DuplicateProfileResponse
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public string? ErrorMessage { get; set; }

        /// <summary>The name of the created copy (echoes the requested name on success).</summary>
        [Key(2)]
        public string NewName { get; set; } = string.Empty;
    }
}
