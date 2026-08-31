namespace AkmlSql.Core.Models.Analysis
{
    /// <summary>
    /// How far a "stop reporting this rule" action reaches. Ordered narrowest to widest as the
    /// quick-fix menu presents them. Values are carried over IPC as ints
    /// (<c>FixActionInfo.SuppressScopeCode</c>), so existing members keep their numbers.
    /// </summary>
    public enum SuppressScope
    {
        /// <summary>One line, via <c>-- akml-disable-line RULE</c> appended to it.</summary>
        Line   = 0,

        /// <summary>
        /// The whole script, via <c>-- akml-disable RULE</c> inserted at the top of the document.
        /// Travels with the file, so it survives a restart and reaches anyone who opens it.
        /// </summary>
        File   = 1,

        /// <summary>Every file, persisted to <c>config.json codeAnalysis.ruleOverrides</c>.</summary>
        Global = 2,

        /// <summary>
        /// Every file, but only until the IDE is closed. Held in engine memory
        /// (<c>SessionSuppressionStore</c>) — nothing is written to the script or to disk.
        /// </summary>
        Session = 3
    }
}
