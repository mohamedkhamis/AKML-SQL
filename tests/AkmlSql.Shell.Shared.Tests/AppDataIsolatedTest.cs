#nullable enable
using System;
using System.IO;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Base for test classes that redirect <c>AKML_APP_DATA_ROOT</c> to a fresh temp root
    /// (ConfigManager isolation): sets the env var in the ctor, restores + best-effort-deletes
    /// on dispose. Pair with <c>[Collection("AkmlSql AppData isolation")]</c> so classes
    /// mutating the process-global env var never run concurrently.
    /// </summary>
    public abstract class AppDataIsolatedTest : IDisposable
    {
        private const string AppDataRootEnvVar = "AKML_APP_DATA_ROOT";
        private readonly string? _priorRoot;

        /// <summary>
        /// Process-wide serialization for AKML_APP_DATA_ROOT redirection. The variable is
        /// process-global, and redirecting classes live in DIFFERENT xunit collections
        /// ("AkmlSql AppData isolation" vs "AkmlSql ThemeRegistry") which run CONCURRENTLY —
        /// a racing Dispose once restored the real path while another class was mid-write, so
        /// fixture agents landed in the developer's REAL config.json (and live-panel tests saw
        /// an empty config and flaked). Every redirect mechanism (this base AND scoped helpers
        /// like AiChatAgentPickerTests' AppDataRedirect) must hold this lock for the whole
        /// redirect lifetime. SemaphoreSlim (not Monitor): xunit may Dispose a test on a
        /// different thread than the ctor after an async test, and Monitor would deadlock there.
        /// </summary>
        internal static readonly System.Threading.SemaphoreSlim EnvVarLock = new(1, 1);

        /// <summary>This class's private, empty AppData root.</summary>
        protected string TempRoot { get; }

        protected AppDataIsolatedTest(string tempDirPrefix)
        {
            EnvVarLock.Wait();
            _priorRoot = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
            TempRoot = Path.Combine(Path.GetTempPath(), tempDirPrefix + Guid.NewGuid());
            Environment.SetEnvironmentVariable(AppDataRootEnvVar, TempRoot);
        }

        public virtual void Dispose()
        {
            try
            {
                Environment.SetEnvironmentVariable(AppDataRootEnvVar, _priorRoot);
                try { if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            finally
            {
                EnvVarLock.Release();
            }
        }
    }
}
