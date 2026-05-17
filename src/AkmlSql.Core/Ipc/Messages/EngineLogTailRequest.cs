using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Spec 021 (web edition) -- diagnostics export extension. Browser asks the
    /// engine for the last <see cref="MaxBytes"/> bytes of <c>engine.log</c> so it
    /// can append the slice to the diagnostics ZIP under <c>engine.log</c>.
    /// Capability-gated on <c>diagnostics.engine-log-tail.v1</c>.
    /// </summary>
    [MessagePackObject]
    public sealed class EngineLogTailRequest
    {
        /// <summary>Upper bound on how much log to return. The engine enforces a hard cap (1 MB).</summary>
        [Key(0)] public int MaxBytes { get; set; } = 256 * 1024;   // 256 KB default
    }
}
