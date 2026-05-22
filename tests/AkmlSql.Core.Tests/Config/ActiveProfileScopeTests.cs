using System.Linq;
using Xunit;
using AkmlSql.Core.Config;

namespace AkmlSql.Core.Tests.Config
{
    /// <summary>
    /// Spec 020 T043 / FR-027b — structural drift-guard pinning that
    /// <see cref="FormatterSettings.ActiveProfile"/> stays a single global string,
    /// shared across SSMS 20/21/22 + VS 2019/22/26.
    ///
    /// <para>
    /// A runtime test that mutates and reads back the value would be tautological —
    /// the deferral note in <c>specs/020-sqlprompt-visual-parity/tasks.md</c> made
    /// exactly that point. The risk the spec actually wants to guard against is
    /// silent design drift toward a per-host mechanism (a `Dictionary&lt;HostType, string&gt;`,
    /// a plural `ActiveProfiles`, a sibling `ActiveProfilePerHost`). This file pins
    /// those structural invariants; if anyone changes the design, this test breaks
    /// deliberately and the change is reviewed.
    /// </para>
    /// </summary>
    public class ActiveProfileScopeTests
    {
        [Fact]
        public void ActiveProfile_IsSingleString()
        {
            var prop = typeof(FormatterSettings).GetProperty(nameof(FormatterSettings.ActiveProfile));

            Assert.NotNull(prop);
            Assert.Equal(typeof(string), prop!.PropertyType);
        }

        [Fact]
        public void FormatterSettings_HasNoPluralActiveProfileForm()
        {
            // If a future change introduces per-host scope, the natural property names would be
            // one of these. They MUST NOT exist on FormatterSettings — the active profile is
            // a single global value (FR-027b).
            var propNames = typeof(FormatterSettings)
                .GetProperties()
                .Select(p => p.Name)
                .ToList();

            Assert.DoesNotContain("ActiveProfiles", propNames);
            Assert.DoesNotContain("ActiveProfilePerHost", propNames);
            Assert.DoesNotContain("ActiveProfileByHost", propNames);
            Assert.DoesNotContain("HostActiveProfiles", propNames);
        }

        [Fact]
        public void ActiveProfile_DefaultIsNonEmptyString()
        {
            // Guard the contract that the default is a usable profile name, not null/empty —
            // ProfileManager.Load relies on a non-empty name.
            var settings = new FormatterSettings();

            Assert.False(string.IsNullOrWhiteSpace(settings.ActiveProfile),
                "FormatterSettings.ActiveProfile must default to a non-empty profile name (ProfileManager.Load throws otherwise).");
        }
    }
}
