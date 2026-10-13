#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AkmlSql.Shell.Shared.Dialogs
{
    /// <summary>One parsed log entry, including any attached exception / stack trace.</summary>
    internal sealed class LogEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        /// <summary>The parsed <see cref="Timestamp"/>; <c>null</c> when it does not parse.</summary>
        public DateTimeOffset? Time { get; set; }
        public string Level     { get; set; } = string.Empty;
        public string Message   { get; set; } = string.Empty;
        /// <summary>Full raw text including any attached exception / stack trace.</summary>
        public string FullText  { get; set; } = string.Empty;
        /// <summary>The process that wrote the entry ("SSMS", "Engine", "Updater"), or the
        /// file name when the file does not say (logs written before the process was recorded).
        /// Taken from the "log file opened by" header Core's <c>ProcessHeaderHooks</c> writes.</summary>
        public string Source    { get; set; } = string.Empty;
        public string FileName  { get; set; } = string.Empty;
    }

    /// <summary>One choice in the viewer's File box: a whole day (every process's file merged) or one file.</summary>
    internal sealed class LogSelection
    {
        public LogSelection(string label, IReadOnlyList<string> paths)
        {
            Label = label;
            Paths = paths;
        }

        public string Label { get; }
        public IReadOnlyList<string> Paths { get; }
        public override string ToString() => Label;
    }

    /// <summary>
    /// The log files behind the log viewer. Every AKML SQL process — the SSMS extension, the
    /// engine, the updater — logs through <c>LoggerFactory</c> to <c>akmlsql-YYYYMMDD.log</c>, but
    /// Serilog's file sink holds its file exclusively, so a second process finding the day's file
    /// locked rolls to <c>akmlsql-YYYYMMDD_001.log</c>, the next to <c>_002</c>, and so on. A day's
    /// log is therefore spread over several files, one per process, and the highest-numbered file
    /// is merely whichever process started last (often the updater). Showing one file at a time
    /// hid the engine's errors — e.g. a failed AI provider test — from a user who opened View Logs.
    /// The default view is the newest day with every file merged by timestamp, newest first.
    /// </summary>
    internal static class LogFileSet
    {
        // akmlsql-20261013.log, akmlsql-20261013_007.log
        private static readonly Regex FileNamePattern = new Regex(
            @"^akmlsql-(\d{8})(?:_(\d+))?\.log$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Matches the Serilog output template:
        // {Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message}
        private static readonly Regex EntryPattern = new Regex(
            @"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+\-]\d{2}:\d{2}) \[([A-Z]{3})\] (.*)$",
            RegexOptions.Compiled);

        // Written by Core's ProcessHeaderHooks each time a process opens a log file:
        // "AKML SQL {Version} log file opened by {Process} (pid {Pid})".
        private static readonly Regex HeaderPattern = new Regex(
            @"^AKML SQL \S+ log file opened by (.+) \(pid \d+\)$",
            RegexOptions.Compiled);

        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz";

        /// <summary>Parses a rolling log file name into its day and roll sequence (0 for the first file).</summary>
        internal static bool TryParseFileName(string fileName, out DateTime day, out int sequence)
        {
            day = default;
            sequence = 0;
            var m = FileNamePattern.Match(fileName ?? string.Empty);
            if (!m.Success) return false;
            if (!DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out day))
                return false;
            if (m.Groups[2].Success &&
                !int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out sequence))
                return false;
            return true;
        }

        /// <summary>
        /// The File box choices, newest day first: each day with more than one file opens with an
        /// "All logs" entry merging them, followed by the day's files (highest sequence first).
        /// Index 0 is the default — the newest day, every process included.
        /// </summary>
        internal static List<LogSelection> ListSelections(string logsPath)
        {
            var result = new List<LogSelection>();
            if (!Directory.Exists(logsPath)) return result;

            var dated = new List<(string Path, DateTime Day, int Sequence)>();
            var undated = new List<string>();
            foreach (var path in Directory.GetFiles(logsPath, "akmlsql*.log"))
            {
                if (TryParseFileName(Path.GetFileName(path), out var day, out var sequence))
                    dated.Add((path, day, sequence));
                else
                    undated.Add(path);
            }

            foreach (var group in dated.GroupBy(f => f.Day).OrderByDescending(g => g.Key))
            {
                var files = group.OrderByDescending(f => f.Sequence).Select(f => f.Path).ToList();
                if (files.Count > 1)
                {
                    result.Add(new LogSelection(
                        $"All logs — {group.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} ({files.Count} files)",
                        files));
                }
                foreach (var file in files)
                    result.Add(new LogSelection(Path.GetFileName(file), new[] { file }));
            }

            foreach (var path in undated.OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase))
                result.Add(new LogSelection(Path.GetFileName(path), new[] { path }));

            return result;
        }

        /// <summary>
        /// Reads every file of <paramref name="paths"/> and merges the entries newest first (by
        /// timestamp; entries that share a timestamp keep their file order, reversed). A file
        /// that cannot be read is reported through <paramref name="onError"/> and skipped.
        /// </summary>
        internal static List<LogEntry> Read(IEnumerable<string> paths, Action<string, Exception>? onError = null)
        {
            var all = new List<LogEntry>();
            foreach (var path in paths)
            {
                try
                {
                    // FileShare.ReadWrite: the writing processes still hold their files open.
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs, Encoding.UTF8);
                    all.AddRange(Parse(sr, Path.GetFileName(path)));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    onError?.Invoke(path, ex);
                }
            }

            // Index-tagged so the sort is stable: List.Sort is not, and a merged day has many
            // entries sharing a millisecond.
            return all
                .Select((entry, index) => (entry, index))
                .OrderByDescending(x => x.entry.Time ?? DateTimeOffset.MinValue)
                .ThenByDescending(x => x.index)
                .Select(x => x.entry)
                .ToList();
        }

        /// <summary>
        /// Parses one file's entries in file order. Lines that do not start an entry (exception
        /// text, stack frames) are attached to the entry before them. Each entry's
        /// <see cref="LogEntry.Source"/> is the process named by the latest "log file opened by"
        /// header above it — after a size or day roll a process can append to a file another
        /// process wrote earlier, and every open writes that header — or the file name when no
        /// header names one (files written by builds before the header existed).
        /// </summary>
        internal static List<LogEntry> Parse(TextReader reader, string fileName)
        {
            var entries = new List<LogEntry>();
            LogEntry? current = null;
            string? source = null;
            var extra = new StringBuilder();

            void Flush()
            {
                if (current == null) return;
                var tail = extra.ToString().TrimEnd();
                current.FullText = string.IsNullOrEmpty(tail)
                    ? $"{current.Timestamp} [{current.Level}] {current.Message}"
                    : $"{current.Timestamp} [{current.Level}] {current.Message}\n{tail}";
                entries.Add(current);
                extra.Clear();
            }

            while (true)
            {
                var line = reader.ReadLine();
                if (line == null)
                {
                    Flush();
                    break;
                }

                var m = EntryPattern.Match(line);
                if (m.Success)
                {
                    Flush();
                    var message = m.Groups[3].Value;
                    var header = HeaderPattern.Match(message);
                    if (header.Success)
                        source = ProcessLabel(header.Groups[1].Value);

                    var timestamp = m.Groups[1].Value;
                    current = new LogEntry
                    {
                        Timestamp = timestamp,
                        Time      = DateTimeOffset.TryParseExact(timestamp, TimestampFormat,
                                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                                        ? time : (DateTimeOffset?)null,
                        Level     = m.Groups[2].Value,
                        Message   = message,
                        Source    = source ?? fileName,
                        FileName  = fileName,
                    };
                }
                else if (current != null)
                {
                    if (extra.Length > 0) extra.AppendLine();
                    extra.Append(line);
                }
            }

            return entries;
        }

        /// <summary>A short label for the process names the header records.</summary>
        internal static string ProcessLabel(string processName)
        {
            var name = (processName ?? string.Empty).Trim();
            if (name.Equals("Ssms", StringComparison.OrdinalIgnoreCase)) return "SSMS";
            const string prefix = "AkmlSql.";
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
                return name.Substring(prefix.Length);
            return name;
        }
    }
}
