#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AkmlSql.Core.Logging
{
    /// <summary>
    /// Removes what identifies a person or their systems from an error report before it leaves the
    /// machine.
    /// <para>
    /// Error reports are on by default, and the settings page promises "no personal data, machine
    /// name or IP". The batch envelope already keeps that promise, but the events inside it are
    /// log text: an error message or stack trace routinely carries <c>C:\Users\&lt;name&gt;\…</c>,
    /// the machine and domain name, a server and database name, or a connection string. Those are
    /// replaced with placeholders here, at the single point every event passes through, so what is
    /// left is the part that helps fix the bug — the failure, the code path, the SQL error number.
    /// </para>
    /// <para>
    /// Deliberately conservative: a placeholder where one wasn't needed costs a little diagnostic
    /// detail; a missed user name is a broken promise.
    /// </para>
    /// </summary>
    internal static class TelemetryScrubber
    {
        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

        /// <summary>Any Windows profile path, whoever's it is: C:\Users\name\… → C:\Users\&lt;user&gt;\….</summary>
        private static readonly Regex ProfilePath = new Regex(
            @"(?<prefix>[A-Z]:[\\/](?:Users|Documents and Settings)[\\/])(?<name>[^\\/:*?""<>|\r\n]+)", Options, Timeout);

        /// <summary>UNC paths: \\server\share → \\&lt;host&gt;\share.</summary>
        private static readonly Regex UncHost = new Regex(
            @"(?<![\w\\])\\\\(?<host>[A-Za-z0-9._$-]+)(?=\\)", Options, Timeout);

        /// <summary>The engine's own connection description: server='x' catalog='y'.</summary>
        private static readonly Regex DescribedTarget = new Regex(
            @"\b(?<key>server|catalog)='[^']*'", Options, Timeout);

        /// <summary>
        /// Key=value pairs whose value names a system or a secret: connection strings, and the
        /// engine's own log shapes (<c>db=Sales</c>, <c>server='x'</c>).
        /// </summary>
        private static readonly Regex ConnectionKeyValue = new Regex(
            @"\b(?<key>Server|Data Source|Address|Addr|Network Address|Initial Catalog|Catalog|Database|db|User ID|UID|User|Password|PWD)\s*=\s*(?<value>""[^""]*""|'[^']*'|[^;,\s]*)",
            Options, Timeout);

        /// <summary>SQL Server's own wording: "Login failed for user 'x'", "database 'y'", "server 'z'".</summary>
        private static readonly Regex SqlQuotedName = new Regex(
            @"\b(?<key>for user|database|server|login)\s+'[^']*'", Options, Timeout);

        private static readonly Regex Email = new Regex(
            @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", Options, Timeout);

        /// <summary>IPv4 with real octets, not part of a longer dotted number (so versions like 1.26.0923.0723 survive).</summary>
        private static readonly Regex IPv4 = new Regex(
            @"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)(?![\d.])", Options, Timeout);

        /// <summary>The names of this machine and account, replaced wherever they appear as a whole word.</summary>
        private static readonly IReadOnlyList<KeyValuePair<Regex, string>> LocalNames = BuildLocalNames();

        /// <summary>Returns <paramref name="text"/> with identifying details replaced; null stays null.</summary>
        public static string? Scrub(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            try
            {
                var result = text!;
                result = ProfilePath.Replace(result, m => m.Groups["prefix"].Value + "<user>");
                result = UncHost.Replace(result, @"\\<host>");
                result = DescribedTarget.Replace(result, m => m.Groups["key"].Value + "='<redacted>'");
                result = ConnectionKeyValue.Replace(result, m => m.Groups["key"].Value + "=" + Redacted(m.Groups["value"].Value));
                result = SqlQuotedName.Replace(result, m => m.Groups["key"].Value + " '<redacted>'");
                result = Email.Replace(result, "<email>");
                result = IPv4.Replace(result, "<ip>");

                foreach (var pair in LocalNames)
                {
                    result = pair.Key.Replace(result, pair.Value);
                }

                return result;
            }
            catch (RegexMatchTimeoutException)
            {
                // A pathological input must not leak: send nothing rather than the raw text.
                return "(error text withheld: could not be checked for personal data)";
            }
        }

        /// <summary>The placeholder, in the same quotes the value had (so a message still reads right).</summary>
        private static string Redacted(string value)
        {
            if (value.Length > 0 && (value[0] == '\'' || value[0] == '"'))
            {
                return value[0] + "<redacted>" + value[0];
            }

            return "<redacted>";
        }

        private static IReadOnlyList<KeyValuePair<Regex, string>> BuildLocalNames()
        {
            var names = new List<KeyValuePair<Regex, string>>();
            Add(names, SafeGet(() => Environment.UserName), "<user>");
            Add(names, SafeGet(() => Environment.MachineName), "<machine>");
            Add(names, SafeGet(() => Environment.UserDomainName), "<domain>");
            return names;
        }

        private static void Add(List<KeyValuePair<Regex, string>> names, string? value, string placeholder)
        {
            // Very short names ("sa", "pc") would turn ordinary words into placeholders; profile
            // paths still catch them where they matter most.
            if (value == null || value.Trim().Length < 3)
            {
                return;
            }

            var pattern = @"(?<![A-Za-z0-9_-])" + Regex.Escape(value.Trim()) + @"(?![A-Za-z0-9_-])";
            names.Add(new KeyValuePair<Regex, string>(new Regex(pattern, Options, Timeout), placeholder));
        }

        private static string? SafeGet(Func<string> read)
        {
            try
            {
                return read();
            }
            catch
            {
                return null;
            }
        }
    }
}
