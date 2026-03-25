#nullable enable
using System.Collections.Generic;

namespace AkmlSql.Core.Models.Ai
{
    /// <summary>
    /// Tracks privacy transformations applied to SQL before sending to an AI provider.
    /// Used to reverse-map obfuscated identifiers and literals back to originals.
    /// </summary>
    public class PrivacyTransformation
    {
        /// <summary>
        /// Privacy mode: "schemaOnly" (send schema, strip data), "obfuscated" (hash identifiers),
        /// or "full" (send everything as-is).
        /// </summary>
        public string Mode { get; set; } = "schemaOnly";

        /// <summary>Per-session random key used for deterministic obfuscation. Null when mode is "full".</summary>
        public byte[]? SessionKey { get; set; }

        /// <summary>Map from obfuscated literal placeholder to original literal value.</summary>
        public Dictionary<string, string> LiteralMap { get; set; } = new();

        /// <summary>Map from obfuscated identifier to original identifier name.</summary>
        public Dictionary<string, string> IdentifierMap { get; set; } = new();
    }
}
