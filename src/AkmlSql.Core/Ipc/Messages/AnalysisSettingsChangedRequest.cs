using MessagePack;

namespace AkmlSql.Core.Ipc.Messages
{
    /// <summary>
    /// Notification fired by the shell when the user changes analysis-related settings
    /// in the Options dialog. Has no fields -- the engine reacts by invalidating its
    /// cached <see cref="AkmlSql.Core.Config.AppSettings"/> and <c>.casettings</c> loader.
    /// Placeholder request type used by the spec-021 typed-handler migration
    /// (<c>AnalysisSettingsChangedHandler</c>).
    /// </summary>
    [MessagePackObject]
    public class AnalysisSettingsChangedRequest
    {
        // Empty -- the notification carries no data.
    }
}
