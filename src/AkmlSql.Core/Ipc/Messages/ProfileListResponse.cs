using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ProfileListResponse
    {
        [Key(0)]
        public ProfileInfo[] Profiles { get; set; } = [];

        /// <summary>
        /// Spec 040 (STY-10, FR-064) — a team style folder is set but can't be reached (missing,
        /// offline share, or slower than the 2 s budget). The other styles are still listed; the
        /// Format Styles window shows "Team styles unavailable". False from older engines.
        /// </summary>
        [Key(1)]
        public bool TeamFolderUnavailable { get; set; }
    }
}
