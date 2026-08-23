using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 033 — Format Styles editor load-on-select. Requests one stored profile's raw
    /// .akmlstyle file text by display name (resolved custom-first then built-in,
    /// case-insensitively, matching <c>ProfileManager.Load</c> semantics).
    /// </summary>
    [MessagePackObject]
    public class ProfileGetRequest
    {
        /// <summary>Display name of the profile to read.</summary>
        [Key(0)]
        public string Name { get; set; } = string.Empty;
    }
}
