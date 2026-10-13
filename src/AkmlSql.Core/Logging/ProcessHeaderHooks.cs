using System;
using System.Globalization;
using System.IO;
using System.Text;
using Serilog.Sinks.File;

namespace AkmlSql.Core.Logging
{
    /// <summary>
    /// Writes "AKML SQL {Version} log file opened by {Process} (pid {Pid})" at the point where the
    /// file sink starts writing, every time it opens a file — not only when the logger starts.
    /// <para>
    /// Every AKML SQL process logs to the same <c>akmlsql-YYYYMMDD.log</c> name, and the sink holds
    /// its file exclusively, so a day's log is spread over <c>_001</c>, <c>_002</c>, … one per
    /// process. On a size or day roll the sink opens the highest-numbered file it can lock, which
    /// can be a file another process wrote earlier and has since closed. Without a line at every
    /// open, the log viewer (<c>LogFileSet</c> in the shell) could not tell whose entries follow,
    /// and labelled a rolled engine's errors with the updater that wrote the file's first line.
    /// </para>
    /// <para>The line uses the same shape as the output template (timestamp, <c>[INF]</c>, message)
    /// so every reader parses it as an ordinary entry. Keep it in step with <c>LogFileSet</c>.</para>
    /// </summary>
    public sealed class ProcessHeaderHooks : FileLifecycleHooks
    {
        private readonly string _processName;
        private readonly int _processId;

        public ProcessHeaderHooks(string processName, int processId)
        {
            _processName = string.IsNullOrWhiteSpace(processName) ? "unknown" : processName;
            _processId = processId;
        }

        /// <summary>The header line (without the line break) as written at <paramref name="now"/>.</summary>
        public static string FormatHeader(DateTimeOffset now, string version, string processName, int processId)
            => now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture) +
               $" [INF] AKML SQL {version} log file opened by {processName} (pid {processId})";

        public override Stream OnFileOpened(string path, Stream underlyingStream, Encoding encoding)
        {
            try
            {
                var bytes = encoding.GetBytes(
                    FormatHeader(DateTimeOffset.Now, Constants.RuntimeVersion, _processName, _processId) +
                    Environment.NewLine);
                underlyingStream.Write(bytes, 0, bytes.Length);
            }
            catch
            {
                // A header must never stop the process from logging.
            }

            return underlyingStream;
        }
    }
}
