using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    [MessagePackObject]
    public class ProfileInfo
    {
        [Key(0)]
        public string Name { get; set; } = string.Empty;

        [Key(1)]
        public string? Description { get; set; }

        [Key(2)]
        public string? Author { get; set; }

        [Key(3)]
        public bool IsBuiltIn { get; set; }

        [Key(4)]
        public bool IsActive { get; set; }

        [Key(5)]
        public string? BasedOn { get; set; }

        [Key(6)]
        public string? Modified { get; set; }

        /// <summary>
        /// True when this is a shipped style the user has edited. <see cref="IsBuiltIn"/> is false
        /// for these — the file that resolves really is the custom one — so the two together say
        /// "shipped, and changed", which is the only state Reset acts on.
        /// </summary>
        [Key(7)]
        public bool IsCustomizedBuiltIn { get; set; }

        /// <summary>
        /// True when the style is written in SQL Prompt's model and formats with the SQL Prompt
        /// layout. False on older engines and for styles in AKML's own model.
        /// </summary>
        [Key(8)]
        public bool IsSqlPromptStyle { get; set; }

        /// <summary>
        /// Spec 040 (STY-10) — where the style comes from: <c>"builtIn"</c>, <c>"user"</c> or
        /// <c>"team"</c> (the shared team style folder). Null from older engines.
        /// </summary>
        [Key(9)]
        public string? Source { get; set; }

        /// <summary>
        /// Spec 040 (STY-10) — a team style in a folder that can't be written to. The engine refuses
        /// Save, Rename, Delete and Reset on it; Copy makes the user's own style.
        /// </summary>
        [Key(10)]
        public bool IsReadOnly { get; set; }
    }
}
