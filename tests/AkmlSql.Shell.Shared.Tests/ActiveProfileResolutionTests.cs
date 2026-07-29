#nullable enable
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Pins the contract behind "I picked a style, closed the editor, and Format SQL didn't change".
    ///
    /// <para>Root cause: <c>FormatDocumentCommand</c> and <c>FormatSelectionCommand</c> built their
    /// <c>FormatRequest</c> WITHOUT <c>ProfileName</c>, so the engine received null and formatted
    /// with <c>new FormattingProfile()</c> — POCO defaults — no matter which style was active. The
    /// one place that did set it (<c>FormatRequestDispatcher</c>) was never constructed anywhere,
    /// so the correct behaviour existed only as dead code.</para>
    ///
    /// <para>The resolver must read config FRESH on every call: activating a style writes
    /// <c>Formatter.ActiveProfile</c> to disk, and the very next format has to see it — a cached
    /// snapshot is what makes "close the editor, format, nothing happens" feel like the fix didn't
    /// work (the same stale-snapshot trap as the AI consent flag).</para>
    /// </summary>
    [Collection("AkmlSql AppData isolation")]
    public class ActiveProfileResolutionTests : AppDataIsolatedTest
    {
        public ActiveProfileResolutionTests() : base("akmlsql-activeprofile-") { }

        [Fact]
        public void Resolves_TheConfiguredActiveStyle()
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = "Compact";
            ConfigManager.Save(settings);

            Assert.Equal("Compact", FormatActionHelper.ResolveActiveProfileName());
        }

        [Fact]
        public void PicksUpAChangeWithoutRestart()
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = "Compact";
            ConfigManager.Save(settings);
            Assert.Equal("Compact", FormatActionHelper.ResolveActiveProfileName());

            // Simulates Set Active in the styles editor, then formatting again in the same session.
            settings.Formatter.ActiveProfile = "Khamis Style";
            ConfigManager.Save(settings);

            Assert.Equal("Khamis Style", FormatActionHelper.ResolveActiveProfileName());
        }

        [Fact]
        public void FallsBackToTheShippedDefault_WhenConfigCarriesNoActiveStyle()
        {
            var settings = ConfigManager.Load();
            settings.Formatter.ActiveProfile = string.Empty;
            ConfigManager.Save(settings);

            // Single source for the default: the FormatterSettings initializer, not a literal here.
            Assert.Equal(new FormatterSettings().ActiveProfile, FormatActionHelper.ResolveActiveProfileName());
        }

        [Fact]
        public void NeverReturnsNullOrEmpty()
        {
            // The engine treats a null/empty ProfileName as "defaults by design" and reports no
            // fallback warning, which would silently reintroduce the original bug.
            var name = FormatActionHelper.ResolveActiveProfileName();

            Assert.False(string.IsNullOrWhiteSpace(name));
        }
    }
}
