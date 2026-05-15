using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 020 US3 (T049) — Shell→Engine request for the canonical Format Styles editor schema.
    /// Pairs with <see cref="StyleEditorSchemaResponse"/>. See
    /// <c>specs/020-sqlprompt-visual-parity/contracts/ipc-style-editor-schema.md</c>.
    /// </summary>
    [MessagePackObject]
    public class StyleEditorSchemaRequest
    {
        /// <summary>
        /// Schema version the shell last received. If it matches the engine's current
        /// schema version, the engine short-circuits and returns an empty body
        /// (<see cref="StyleEditorSchemaResponse.Cached"/> = true).
        /// Null means "I have no cache; send the full schema".
        /// </summary>
        [Key(0)]
        public int? ClientSchemaVersion { get; set; }

        /// <summary>
        /// When true (default), unsupported / AKML-only settings are included so the editor
        /// can render them disabled-with-value per FR-023.
        /// </summary>
        [Key(1)]
        public bool IncludeUnsupported { get; set; } = true;
    }
}
