using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 033 — serializes every test class that mutates the process-global
    /// <c>AKML_APP_DATA_ROOT</c> environment variable (ConfigManager path redirection).
    /// xunit runs test classes in parallel by default; two classes flipping the same env var
    /// concurrently would leak isolation roots into each other.
    /// </summary>
    [CollectionDefinition("AkmlSql AppData isolation")]
    public sealed class AppDataIsolationCollection
    {
    }
}
