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

        /// <summary>This class's private, empty AppData root.</summary>
        protected string TempRoot { get; }

        protected AppDataIsolatedTest(string tempDirPrefix)
        {
            _priorRoot = Environment.GetEnvironmentVariable(AppDataRootEnvVar);
            TempRoot = Path.Combine(Path.GetTempPath(), tempDirPrefix + Guid.NewGuid());
            Environment.SetEnvironmentVariable(AppDataRootEnvVar, TempRoot);
        }

        public virtual void Dispose()
        {
            Environment.SetEnvironmentVariable(AppDataRootEnvVar, _priorRoot);
            try { if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
