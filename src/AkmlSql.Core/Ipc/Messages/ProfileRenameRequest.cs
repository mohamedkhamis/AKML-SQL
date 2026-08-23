using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 033 — renames a CUSTOM formatting profile engine-side (built-ins rejected).
    /// The engine rewrites <c>metadata.name</c> and the filename together (List() keys on the
    /// JSON name while Load() resolves by filename — they must never diverge) and moves the
    /// <c>&lt;name&gt;.source.json</c> import sidecar. Case-only renames are allowed.
    /// </summary>
    [MessagePackObject]
    public class ProfileRenameRequest
    {
        /// <summary>Current display name — must resolve to a custom profile.</summary>
        [Key(0)]
        public string OldName { get; set; } = string.Empty;

        /// <summary>Requested new name (engine-side sanitized; collisions rejected).</summary>
        [Key(1)]
        public string NewName { get; set; } = string.Empty;
    }
}
