using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 020 US3 (T049) — Engine→Shell response carrying the Format Styles editor schema.
    /// Pairs with <see cref="StyleEditorSchemaRequest"/>.
    ///
    /// <para>
    /// The schema body is serialized as JSON (<see cref="SchemaJson"/>) rather than typed
    /// MessagePack so the wire contract stays decoupled from the Formatting project's
    /// types (which target .NET 10 and cannot be referenced from <c>AkmlSql.Core</c>'s
    /// netstandard2.0 surface). Shell-side deserializes via <c>System.Text.Json</c>.
    /// </para>
    /// </summary>
    [MessagePackObject]
    public class StyleEditorSchemaResponse
    {
        /// <summary>The engine's current schema version. Always present.</summary>
        [Key(0)]
        public int SchemaVersion { get; set; }

        /// <summary>
        /// Full JSON-serialized <c>FormatSettingSchema</c>. Null when <see cref="Cached"/>
        /// is true (the shell's cached schema matches the engine's current version).
        /// </summary>
        [Key(1)]
        public string? SchemaJson { get; set; }

        /// <summary>
        /// True when the engine short-circuited because <c>ClientSchemaVersion</c> matched.
        /// Shell uses its cached schema in this case.
        /// </summary>
        [Key(2)]
        public bool Cached { get; set; }

        /// <summary>Optional error message; populated only on failure.</summary>
        [Key(3)]
        public string? ErrorMessage { get; set; }
    }
}
