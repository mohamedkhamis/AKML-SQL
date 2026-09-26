using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ProfileSaveRequest
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;

        [Key(1)]
        public string ProfileJson { get; set; } = string.Empty;

        [Key(2)]
        public string? Description { get; set; }

        [Key(3)]
        public string? BasedOn { get; set; }

        /// <summary>
        /// The save creates a NEW style: the engine refuses a built-in's name and a name already
        /// taken, instead of overwriting (spec 039 FR-008). An edit leaves it false. Engines
        /// without this key ignore it; a caller that needs the guard also checks names itself.
        /// </summary>
        [Key(4)]
        public bool CreateOnly { get; set; }
    }
}
