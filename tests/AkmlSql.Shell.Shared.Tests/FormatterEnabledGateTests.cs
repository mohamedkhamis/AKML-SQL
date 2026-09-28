#nullable enable
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Formatting;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>Spec 040 (T017, OPT-01) — "Enable SQL formatter" gates the format commands.</summary>
    public class FormatterEnabledGateTests
    {
        [Fact]
        public void No_message_while_the_formatter_is_on()
            => Assert.Null(FormatActionHelper.FormatterDisabledMessage(new FormatterSettings { Enabled = true }));

        [Fact]
        public void A_status_message_when_the_formatter_is_off()
            => Assert.Equal("AKML SQL formatting is off — turn it on in Options › Format › Styles.",
                FormatActionHelper.FormatterDisabledMessage(new FormatterSettings { Enabled = false }));
    }
}
