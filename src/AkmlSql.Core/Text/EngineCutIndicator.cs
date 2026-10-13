using System.Globalization;
using System.Text.RegularExpressions;

namespace AkmlSql.Core.Text
{
    /// <summary>What kind of value the engine cut from a result cell.</summary>
    public enum EngineCutKind
    {
        None = 0,
        Text = 1,
        Binary = 2,
    }

    /// <summary>
    /// Spec 041 (research R8) — recognises the in-band indicator the engine sends instead of a very
    /// large cell value: <c>[text N chars]</c> for a character LOB and <c>[binary N bytes]</c> for a
    /// binary one. These are the only forms <c>ResultSetReader.EncodeCell</c> emits, so anything else —
    /// including a user value that merely looks similar — is an ordinary value.
    /// </summary>
    public static class EngineCutIndicator
    {
        private static readonly Regex Pattern = new Regex(
            @"^\[(?:(text) (\d+) chars|(binary) (\d+) bytes)\]$",
            RegexOptions.CultureInvariant);

        /// <summary>True when <paramref name="value"/> is exactly an engine-cut indicator.</summary>
        public static bool IsIndicator(string? value) => TryParse(value, out _, out _);

        /// <summary>Parse an engine-cut indicator into its kind and original size (characters for
        /// text, bytes for binary). Returns false, with <see cref="EngineCutKind.None"/> and 0, for
        /// anything else.</summary>
        public static bool TryParse(string? value, out EngineCutKind kind, out long size)
        {
            kind = EngineCutKind.None;
            size = 0;
            // Cheap pre-check: every indicator starts with '[' and ends with ']'.
            if (string.IsNullOrEmpty(value) || value![0] != '[' || value[value.Length - 1] != ']') return false;

            var m = Pattern.Match(value);
            if (!m.Success) return false;

            bool isText = m.Groups[1].Success;
            var digits = isText ? m.Groups[2].Value : m.Groups[4].Value;
            long parsed;
            if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out parsed)) return false;

            kind = isText ? EngineCutKind.Text : EngineCutKind.Binary;
            size = parsed;
            return true;
        }
    }
}
