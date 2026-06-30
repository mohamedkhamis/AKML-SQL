using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 030 T020 — server-side duplicate of a stored formatting profile (the engine owns the
    /// profile store, so a faithful copy of a profile's persisted values is a name-based engine
    /// operation, not a shell working-values save). Backs the Format Styles editor New (duplicate
    /// the built-in default) and Copy (duplicate the selected profile) buttons.
    /// </summary>
    [MessagePackObject]
    public class DuplicateProfileRequest
    {
        /// <summary>Name of the existing profile to copy from (built-in or custom).</summary>
        [Key(0)]
        public string SourceName { get; set; } = string.Empty;

        /// <summary>Name for the new copy (must be unique; the shell pre-computes a unique name).</summary>
        [Key(1)]
        public string NewName { get; set; } = string.Empty;
    }
}
