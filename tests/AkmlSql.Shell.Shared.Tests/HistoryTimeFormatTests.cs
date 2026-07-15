using System;
using System.Globalization;
using AkmlSql.Shell.Shared.History;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Pins the single absolute-timestamp format used across the SQL History tool window
    /// (report §3 rec #2 — the three regions must read consistently). The format is the culture's
    /// short-date pattern + 24-hour time, so it is locale-aware rather than a hard-coded ISO string.
    /// </summary>
    public class HistoryTimeFormatTests
    {
        [Fact]
        public void Absolute_UsesCultureShortDate_Plus24HourTime()
        {
            var dt = new DateTime(2025, 12, 29, 11, 8, 0);
            Assert.Equal("2025-12-29 11:08", HistoryTimeFormat.Absolute(dt, CultureInfo.GetCultureInfo("sv-SE"))); // yyyy-MM-dd
            Assert.Equal("12/29/2025 11:08", HistoryTimeFormat.Absolute(dt, CultureInfo.GetCultureInfo("en-US"))); // M/d/yyyy
            Assert.Equal("29/12/2025 11:08", HistoryTimeFormat.Absolute(dt, CultureInfo.GetCultureInfo("en-GB"))); // dd/MM/yyyy
        }
    }
}
