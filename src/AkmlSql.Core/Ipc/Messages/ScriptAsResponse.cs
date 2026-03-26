using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// T099: Response containing the generated "Script As" SQL template.
    /// Sent Engine -> Shell as MessageType 167 (ScriptAsResult).
    /// </summary>
    [MessagePackObject]
    public class ScriptAsResponse
    {
        /// <summary>Whether the script generation was successful.</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>Error message when <see cref="Success"/> is false.</summary>
        [Key(1)]
        public string? Error { get; set; }

        /// <summary>The generated SQL script text.</summary>
        [Key(2)]
        public string? Sql { get; set; }

        /// <summary>The template type that was generated.</summary>
        [Key(3)]
        public string? TemplateType { get; set; }

        /// <summary>Fully qualified object name.</summary>
        [Key(4)]
        public string? FullObjectName { get; set; }
    }
}
