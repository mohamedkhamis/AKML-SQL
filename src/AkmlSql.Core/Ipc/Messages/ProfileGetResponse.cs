using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 033 — ProfileGet result. <see cref="ProfileJson"/> is the stored file text
    /// VERBATIM (no re-serialization), suitable as the shell's merge base for edit-save:
    /// re-serializing would bump <c>metadata.modified</c> and drop unknown fields nested
    /// inside option groups. <see cref="IsBuiltIn"/> is derived from which directory the
    /// name resolved to (built-in dir with no custom shadow) — the JSON's own
    /// <c>isBuiltIn</c> field is untrusted.
    /// </summary>
    [MessagePackObject]
    public class ProfileGetResponse
    {
        /// <summary>False when the name resolves to no stored profile; nothing is created.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>Populated iff <see cref="Success"/> is false.</summary>
        [Key(1)]
        public string? ErrorMessage { get; set; }

        /// <summary>Resolved display name.</summary>
        [Key(2)]
        public string? Name { get; set; }

        /// <summary>Raw stored file text (UTF-8 decoded), verbatim.</summary>
        [Key(3)]
        public string? ProfileJson { get; set; }

        /// <summary>True when the profile is a read-only built-in (no custom shadow exists).</summary>
        [Key(4)]
        public bool IsBuiltIn { get; set; }
    }
}
