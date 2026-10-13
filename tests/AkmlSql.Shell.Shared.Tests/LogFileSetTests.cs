using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AkmlSql.Core.Logging;
using AkmlSql.Shell.Shared.Dialogs;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// View Logs must show what every AKML SQL process logged. Each process rolls its own
    /// <c>akmlsql-YYYYMMDD_NNN.log</c> (Serilog holds files exclusively), so the viewer that opened
    /// only the highest-numbered file showed the updater's six lines while the engine's failed AI
    /// provider test sat in another file. The fixtures below are the 2026-10-13 layout that hid it.
    /// Header lines come from Core's real <see cref="ProcessHeaderHooks.FormatHeader"/>, so the
    /// writer and the reader cannot drift apart.
    /// </summary>
    public sealed class LogFileSetTests : IDisposable
    {
        private static readonly TimeSpan Cairo = TimeSpan.FromHours(3);
        private readonly string _dir;

        public LogFileSetTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "akml-logfileset-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string Write(string name, params string[] lines)
        {
            var path = Path.Combine(_dir, name);
            File.WriteAllLines(path, lines);
            return path;
        }

        private static string Header(int hour, int minute, int second, string process, int pid)
            => ProcessHeaderHooks.FormatHeader(
                new DateTimeOffset(2026, 10, 13, hour, minute, second, 0, Cairo), "1.26.1013.2300", process, pid);

        // The SSMS extension's file: written by the updater in the morning, reused by SSMS later.
        private string ShellFile() => Write("akmlsql-20261013_006.log",
            Header(19, 31, 22, "AkmlSql.Updater", 100),
            "2026-10-13 19:31:22.205 +03:00 [INF] AKML SQL 1.26.1012.2246 logger initialized",
            "2026-10-13 19:31:22.473 +03:00 [INF] Update check started for v1.26.1012.2246",
            Header(22, 55, 29, "SSMS", 27504),
            "2026-10-13 22:55:29.100 +03:00 [INF] AKML SQL 1.26.1013.1822 logger initialized",
            "2026-10-13 23:06:42.000 +03:00 [DBG] Options dialog closed");

        // The engine's file: the error the user was told to look for.
        private string EngineFile() => Write("akmlsql-20261013_007.log",
            Header(22, 55, 44, "AkmlSql.Engine", 19584),
            "2026-10-13 22:55:44.937 +03:00 [INF] AKML SQL 1.26.1013.1822 logger initialized",
            "2026-10-13 23:00:55.630 +03:00 [DBG] AiProviderTest: provider=gemini, model=gemini-3.7-flash, hasEndpoint=false",
            "2026-10-13 23:00:58.947 +03:00 [ERR] AI provider test failed after 3317ms",
            "Mscc.GenerativeAI.Types.GeminiApiException: The request was not successful. Last API response:",
            "{",
            "  \"error\": { \"code\": 503, \"status\": \"UNAVAILABLE\" }",
            "}",
            "   at AkmlSql.Engine.Ai.AiProviderTestHandler.HandleAsync(RpcMessage message, CancellationToken ct)");

        // The updater's file: the highest number, so the old viewer opened it.
        private string UpdaterFile() => Write("akmlsql-20261013_008.log",
            Header(22, 56, 10, "AkmlSql.Updater", 4242),
            "2026-10-13 22:56:10.820 +03:00 [INF] AKML SQL 1.26.1013.1822 logger initialized",
            "2026-10-13 22:56:12.663 +03:00 [INF] No update available");

        [Theory]
        [InlineData("akmlsql-20261013.log", 2026, 10, 13, 0)]
        [InlineData("akmlsql-20261013_007.log", 2026, 10, 13, 7)]
        [InlineData("AKMLSQL-20261231_120.LOG", 2026, 12, 31, 120)]
        public void TryParseFileName_reads_day_and_sequence(string name, int y, int m, int d, int sequence)
        {
            Assert.True(LogFileSet.TryParseFileName(name, out var day, out var seq));
            Assert.Equal(new DateTime(y, m, d), day);
            Assert.Equal(sequence, seq);
        }

        [Theory]
        [InlineData("akmlsql-.log")]
        [InlineData("akmlsql-20261399.log")]
        [InlineData("akmlsql-web-20261013.log")]
        [InlineData("other-20261013.log")]
        public void TryParseFileName_rejects_other_names(string name)
        {
            Assert.False(LogFileSet.TryParseFileName(name, out _, out _));
        }

        [Fact]
        public void Default_selection_is_the_newest_day_with_every_process_merged()
        {
            Write("akmlsql-20261012_001.log", "2026-10-12 22:46:32.000 +03:00 [INF] yesterday");
            var first = Write("akmlsql-20261013.log", "2026-10-13 09:00:00.000 +03:00 [INF] morning");
            var shell = ShellFile();
            var engine = EngineFile();
            var updater = UpdaterFile();
            Write("akmlsql-notes.log", "unrelated");

            var selections = LogFileSet.ListSelections(_dir);

            Assert.Equal("All logs — 2026-10-13 (4 files)", selections[0].Label);
            Assert.Equal(new[] { updater, engine, shell, first }, selections[0].Paths);

            // Then the day's files one by one, highest sequence first …
            Assert.Equal(
                new[] { "akmlsql-20261013_008.log", "akmlsql-20261013_007.log", "akmlsql-20261013_006.log", "akmlsql-20261013.log" },
                selections.Skip(1).Take(4).Select(s => s.Label));
            // … an older day with a single file gets no "All logs" entry, and odd names go last.
            Assert.Equal("akmlsql-20261012_001.log", selections[5].Label);
            Assert.Equal("akmlsql-notes.log", selections[6].Label);
            Assert.Equal(7, selections.Count);
        }

        [Fact]
        public void ListSelections_on_a_missing_folder_is_empty()
        {
            Assert.Empty(LogFileSet.ListSelections(Path.Combine(_dir, "missing")));
        }

        [Fact]
        public void Merged_view_finds_the_engine_error_and_lists_newest_first()
        {
            var paths = new[] { UpdaterFile(), EngineFile(), ShellFile() };

            var entries = LogFileSet.Read(paths);

            // Newest first, across files.
            var times = entries.Select(e => e.Time!.Value).ToList();
            Assert.Equal(times.OrderByDescending(t => t), times);
            Assert.Equal("Options dialog closed", entries[0].Message);

            // The engine's error is in the merged view, with its exception text attached.
            var error = Assert.Single(entries, e => e.Level == "ERR");
            Assert.Equal("AI provider test failed after 3317ms", error.Message);
            Assert.Equal("Engine", error.Source);
            Assert.Equal("akmlsql-20261013_007.log", error.FileName);
            Assert.Contains("GeminiApiException", error.FullText);
            Assert.Contains("\"code\": 503", error.FullText);
            Assert.EndsWith("HandleAsync(RpcMessage message, CancellationToken ct)", error.FullText);

            Assert.Equal(paths.Sum(p => File.ReadAllLines(p).Count(l => l.StartsWith("2026-"))), entries.Count);
        }

        [Fact]
        public void Source_follows_the_latest_header_in_a_file_reused_by_another_process()
        {
            var entries = LogFileSet.Read(new[] { ShellFile() });

            Assert.Equal("SSMS", entries.Single(e => e.Message == "Options dialog closed").Source);
            Assert.Equal("Updater", entries.Single(e => e.Message.StartsWith("Update check started")).Source);
        }

        [Fact]
        public void A_size_roll_that_appends_to_another_process_file_is_labelled_by_its_own_header()
        {
            // Serilog's size/day roll opens the highest-numbered file it can lock — here the
            // updater's closed _008 — and appends without a "logger initialized" line. The
            // header written on every open is what keeps the engine's entries labelled Engine.
            var path = Write("akmlsql-20261013_008.log",
                Header(22, 56, 10, "AkmlSql.Updater", 4242),
                "2026-10-13 22:56:12.663 +03:00 [INF] No update available",
                Header(23, 40, 1, "AkmlSql.Engine", 19584),
                "2026-10-13 23:40:01.500 +03:00 [ERR] AI provider test failed after 370ms");

            var entries = LogFileSet.Read(new[] { path });

            Assert.Equal("Engine", entries.Single(e => e.Level == "ERR").Source);
            Assert.Equal("Updater", entries.Single(e => e.Message == "No update available").Source);
        }

        [Fact]
        public void Logs_from_before_the_header_existed_fall_back_to_the_file_name()
        {
            var path = Write("akmlsql-20261013_005.log",
                "2026-10-13 19:30:56.010 +03:00 [INF] AKML SQL 1.26.1013.1805 logger initialized",
                "2026-10-13 19:30:56.087 +03:00 [INF] AkmlSql.Engine starting.");

            var entries = LogFileSet.Read(new[] { path });

            Assert.All(entries, e => Assert.Equal("akmlsql-20261013_005.log", e.Source));
        }

        [Fact]
        public void Entries_sharing_a_timestamp_keep_file_order_reversed()
        {
            var path = Write("akmlsql-20261013.log",
                "2026-10-13 22:55:45.085 +03:00 [DBG] first",
                "2026-10-13 22:55:45.085 +03:00 [DBG] second",
                "2026-10-13 22:55:45.085 +03:00 [DBG] third");

            var entries = LogFileSet.Read(new[] { path });

            Assert.Equal(new[] { "third", "second", "first" }, entries.Select(e => e.Message));
        }

        [Fact]
        public void A_file_still_held_open_by_its_writer_is_readable()
        {
            var path = EngineFile();
            // How Serilog's (non-shared) file sink holds its file.
            using (new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                Assert.Contains(LogFileSet.Read(new[] { path }), e => e.Level == "ERR");
            }
        }

        [Fact]
        public void An_unreadable_file_is_reported_and_skipped()
        {
            var good = UpdaterFile();
            var missing = Path.Combine(_dir, "akmlsql-20261013_009.log");
            var failures = new List<string>();

            var entries = LogFileSet.Read(new[] { missing, good }, (p, _) => failures.Add(p));

            Assert.Equal(new[] { missing }, failures);
            Assert.Equal(3, entries.Count);
        }

        [Theory]
        [InlineData("SSMS", "SSMS")]
        [InlineData("Ssms", "SSMS")]
        [InlineData("AkmlSql.Engine", "Engine")]
        [InlineData("AkmlSql.Updater", "Updater")]
        [InlineData("testhost", "testhost")]
        public void ProcessLabel_shortens_the_recorded_process_name(string processName, string label)
        {
            Assert.Equal(label, LogFileSet.ProcessLabel(processName));
        }
    }
}
