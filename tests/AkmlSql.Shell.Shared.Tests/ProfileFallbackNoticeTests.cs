using System;
using System.Collections.Generic;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Pins the "your style could not be loaded" notice contract in
    /// <see cref="FormatRequestDispatcher"/>.
    ///
    /// <para>The dispatcher is the shared choke point for EVERY format trigger — Format SQL plus
    /// the format-on-save / on-paste / on-delimiter handlers — so an un-deduped notice would pop a
    /// modal message box on every delimiter keystroke. A missing style is a persistent
    /// configuration problem, so it is announced once per distinct message per session.</para>
    /// </summary>
    public class ProfileFallbackNoticeTests : IDisposable
    {
        private readonly List<string> _shown = new List<string>();

        public ProfileFallbackNoticeTests()
        {
            FormatRequestDispatcher.ResetProfileFallbackWarnings();
            FormatRequestDispatcher.ProfileFallbackNotifierOverride = m => _shown.Add(m);
        }

        public void Dispose()
        {
            FormatRequestDispatcher.ProfileFallbackNotifierOverride = null;
            FormatRequestDispatcher.ResetProfileFallbackWarnings();
        }

        [Fact]
        public void Warning_IsShownOnce_EvenAcrossManyFormats()
        {
            const string warning = "Formatting style 'Khamis Style' could not be loaded...";

            for (var i = 0; i < 25; i++)   // simulates format-on-delimiter typing
                FormatRequestDispatcher.NotifyProfileFallbackOnce(warning);

            Assert.Single(_shown);
            Assert.Equal(warning, _shown[0]);
        }

        [Fact]
        public void DistinctStyles_EachGetTheirOwnNotice()
        {
            FormatRequestDispatcher.NotifyProfileFallbackOnce("style 'A' could not be loaded");
            FormatRequestDispatcher.NotifyProfileFallbackOnce("style 'B' could not be loaded");
            FormatRequestDispatcher.NotifyProfileFallbackOnce("style 'A' could not be loaded");

            Assert.Equal(2, _shown.Count);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NoWarning_ShowsNothing(string? warning)
        {
            // The success path passes null here on every single format — it must stay silent.
            FormatRequestDispatcher.NotifyProfileFallbackOnce(warning);

            Assert.Empty(_shown);
        }
    }
}
