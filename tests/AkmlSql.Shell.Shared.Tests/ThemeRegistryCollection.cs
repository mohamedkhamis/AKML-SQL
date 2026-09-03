using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 036 — serializes every test class that constructs an <c>AiChatPanel</c> (or any
    /// ThemeRegistry-attached WPF control). xunit runs test classes in parallel by default; two
    /// panels on different STA threads racing <c>ThemeRegistry.EnsureInitialized</c> write the
    /// process-global resource dictionary while the other thread's control owns it
    /// (cross-thread InvalidOperationException).
    /// </summary>
    [CollectionDefinition("AkmlSql ThemeRegistry")]
    public sealed class ThemeRegistryCollection
    {
    }
}
