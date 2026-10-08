namespace AkmlSql.Core.Config
{
    /// <summary>
    /// Spec 040 (X-02, FR-061, research R27) — every AKML window and form is titled
    /// "AKML SQL – ‹Name›" (an en dash between product and name), so the windows read as one
    /// product next to SSMS's own. Tool-window captions (shown in tabs) and message-box captions
    /// don't use it; see <c>WindowTitleUsageTests</c> in the shell tests for the allow-list.
    /// </summary>
    public static class WindowTitles
    {
        /// <summary>The text between the product name and the window's name: " – " (U+2013).</summary>
        public const string Separator = " – ";

        /// <summary>"AKML SQL – " — what every AKML window title starts with.</summary>
        public const string Prefix = Constants.ProductName + Separator;

        /// <summary>The title for the window called <paramref name="name"/>, e.g. "AKML SQL – Options".</summary>
        public static string For(string name) => Constants.ProductName + Separator + name;
    }
}
