using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) -- response to <see cref="EngineLogTailRequest"/>.
    /// </summary>
    [MessagePackObject]
    public sealed class EngineLogTailResponse
    {
        /// <summary>The UTF-8 log tail. May be empty when no log file exists yet.</summary>
        [Key(0)] public string LogText { get; set; } = string.Empty;

        /// <summary>Absolute path of the log file (for the browser's manifest.json).</summary>
        [Key(1)] public string SourcePath { get; set; } = string.Empty;

        /// <summary>True when the tail was truncated (the file was larger than MaxBytes).</summary>
        [Key(2)] public bool Truncated { get; set; }
    }
}
